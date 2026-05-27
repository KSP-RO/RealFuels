using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ClickThroughFix;

// Propellant configuration window for ModuleFuelTanks.
// Replaces TankWindow.cs; redirect the PAW toggle that previously called
// TankWindow to ShowGUI / HideGUI on this class instead.

namespace RealFuels.Tanks
{
    // ── Quick-fill preset ────────────────────────────────────────────────────
    // Wraps a FuelInfo so the draw loop has ready-to-display strings.
    internal class QuickFillPreset
    {
        public readonly string EngineName;    // e.g. "RD-253"
        public readonly string CombinedLabel; // e.g. "LqdHydrogen (75%) / LqdOxygen (25%)"
        public readonly FuelInfo Info;          // original RF object

        // Unique key for per-preset state dictionaries (fill % buffer, etc.)
        public string Key => EngineName + "|" + CombinedLabel;

        // Per-propellant target percentages (physical volume fractions within the mix).
        // e.g. [ ("LqdHydrogen", 75.0), ("LqdOxygen", 25.0) ]
        public readonly List<(string name, double targetPct)> PropTargets;

        public QuickFillPreset(FuelInfo fi)
        {
            Info = fi;
            EngineName = fi.title ?? "Engine";

            // propellantVolumeMults value = volumePerUnit = 1/utilization
            // fi.efficiency               = sum[ratio * volumePerUnit]
            //
            // Fill fraction for each resource is its share of PHYSICAL volume, not
            // its share of flow-rate ratio.  Using raw ratios gives wrong results for
            // high-utilization resources (e.g. Oxygen util=200).
            //
            //   vol_frac = ratio * volumePerUnit / efficiency
            var props = fi.propellantVolumeMults.ToList();   // List<KVP<Propellant,double>>

            // Volume-fraction percentage for each propellant (same basis FuelInfo.Label uses)
            PropTargets = props
                .Select(kv => (kv.Key.name, kv.Key.ratio * kv.Value / fi.efficiency * 100d))
                .ToList();

            // Combined label embeds each propellant's volume percentage inline.
            CombinedLabel = string.Join(" / ", props.Select(kv =>
                kv.Key.displayName + " (" +
                (kv.Key.ratio * kv.Value / fi.efficiency * 100d).ToString("F1") + "%)"));
        }
    }

    // Colour-coded status of a preset's propellant ratio vs what is in the tank.
    internal enum RatioStatus { NotLoaded, Partial, Good, Warn, Bad }

    // ── Main window ─────────────────────────────────────────────────────────
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class RealFuelsWindow : MonoBehaviour
    {
        // ── Singleton / static API ───────────────────────────────────────────
        private static RealFuelsWindow _instance;

        // Per-part lock cache keyed by part.persistentId.
        // Populated whenever the window hides or switches to a different module so
        // that locks survive closing and reopening the window for the same part.
        private static readonly Dictionary<uint, Dictionary<string, double>> _lockCache =
            new Dictionary<uint, Dictionary<string, double>>();

        /// <summary>Open (or switch to) the tank window for a given module.</summary>
        public static void ShowGUI(ModuleFuelTanks module)
        {
            if (_instance == null) return;

            // Switching to a different module: discard stale edit buffers and cancel
            // any deferred notification that belongs to the old module.  Firing
            // onEditorShipModified for the new module before the player has done
            // anything can cause RF to call ConfigureFor internally, consuming IDs.
            if (_instance._module != module)
            {
                // Save current locks before discarding them so they survive a
                // close/reopen cycle for the same part.
                if (_instance._module != null && _instance._lockedAmounts.Count > 0)
                    _lockCache[_instance._module.part.persistentId] =
                        new Dictionary<string, double>(_instance._lockedAmounts);

                _instance._editBuf.Clear();
                _instance._pctBuf.Clear();
                _instance._pendingNotify = false;
                _instance._reserveVolume = 0d;
                _instance._lockedAmounts.Clear();

                // Restore any locks that were saved for the incoming module.
                if (_lockCache.TryGetValue(module.part.persistentId, out var saved))
                    foreach (var kv in saved)
                        _instance._lockedAmounts[kv.Key] = kv.Value;
            }

            _instance._module = module;
            _instance._visible = true;
            _instance.RebuildPresets();
        }

        /// <summary>Toggle: close if already showing this module, open otherwise.</summary>
        public static void ToggleGUI(ModuleFuelTanks module)
        {
            if (_instance == null) return;
            if (_instance._visible && _instance._module == module) HideGUI();
            else ShowGUI(module);
        }

        /// <summary>Close the window.</summary>
        public static void HideGUI()
        {
            if (_instance == null) return;

            // Persist locks for the current part so they survive a close/reopen cycle.
            if (_instance._module != null && _instance._lockedAmounts.Count > 0)
                _lockCache[_instance._module.part.persistentId] =
                    new Dictionary<string, double>(_instance._lockedAmounts);

            _instance._visible = false;
            _instance._module = null;
            EditorLogic.fetch?.Unlock(LockID);
        }

        /// <summary>Close if the window is currently showing this module or any of its symmetry counterparts.</summary>
        public static void HideGUIForModule(ModuleFuelTanks module)
        {
            if (_instance == null || !_instance._visible) return;

            // Direct match — the window is showing this exact module.
            if (_instance._module == module) { HideGUI(); return; }

            // Symmetry match — the window is showing a different member of the same
            // symmetry group.  ModuleFuelTanks calls HideGUIForModule(this) on every
            // counterpart when the PAW dismisses, so we must recognise those calls too.
            if (_instance._module != null &&
                _instance._module.part.symmetryCounterparts.Contains(module.part))
                HideGUI();
        }

        /// <summary>
        /// Returns true if the named resource is currently locked in the RealFuels window
        /// for the given module.  Checks the live lock table if the window is open for this
        /// module, otherwise falls back to the persisted lock cache.
        /// Called by ModuleFuelTanks to respect UI locks during tank-type carry-over.
        /// </summary>
        public static bool IsLockedResource(ModuleFuelTanks module, string name)
        {
            if (_instance == null) return false;

            // Window is open and showing exactly this module — use live table.
            if (_instance._module == module)
                return _instance._lockedAmounts.ContainsKey(name);

            // Window is closed (or showing another module) — fall back to persisted cache.
            if (_lockCache.TryGetValue(module.part.persistentId, out var cached))
                return cached.ContainsKey(name);

            return false;
        }

        // ── Constants ────────────────────────────────────────────────────────
        private const string LockID = "RFWindowLock";
        private const int WindowID         = 0x52465748; // "RFWH" — unique across KSP addons
        private const int SettingsWindowID = 0x52465753; // "RFWS"
        private const int LsPopupWindowID  = 0x52465750; // "RFWP"
        private const float MinWindowW = 480f;
        private const float MaxWindowW = 720f;
        private const float MinScale   = 0.7f;
        private const float MaxScale   = 1.5f;

        // WindowW behaves like the old const but reads the player-configured value.
        // All existing Draw* code that references WindowW continues to work unchanged.
        private float WindowW => _windowW;

        // User-configurable display settings — persisted to PluginData, static so they
        // survive part/module switches within the same editor session.
        private static float _windowW = MinWindowW;
        private static float _uiScale = 1.0f;

        // Scale a font size; always clamps to a readable minimum.
        private int FS(int size) => Mathf.Max(7, Mathf.RoundToInt(size * _uiScale));
        // Row-Height scaling: proportionally scale pixel heights with _uiScale.
        // Used for GetRect heights, button/field heights inside rows, and y-offsets.
        private float RH(float h) => Mathf.Round(h * _uiScale);

        // Life Support — daily resource requirements per crew member in RF units/day.
        // Physical litres consumed = rfUnits / tank.utilization (e.g. Oxygen util = 200,
        // so 591.84 RF units ÷ 200 = 2.9592 L of physical tank space per crew per day).
        // Source: USI Life Support / TAC-LS consensus values used in RP-1.
        private const double LsFoodPerCrewDay = 5.84928;
        private const double LsWaterPerCrewDay = 3.87072;
        private const double LsOxygenPerCrewDay = 591.84;

        // ── Settings ─────────────────────────────────────────────────────────
        // Persisted to PluginData/RealFuels/RealFuelsWindowSettings.cfg.
        private bool _settingsOpen;
        private Rect _settingsWinRect = new Rect(0, 0, 280f, 0);

        private static readonly string SettingsPath =
            System.IO.Path.Combine(KSPUtil.ApplicationRootPath,
                "GameData", "RealFuels", "PluginData", "RealFuelsWindowSettings.cfg");

        private static void LoadSettings()
        {
            if (!System.IO.File.Exists(SettingsPath)) return;
            try
            {
                ConfigNode root = ConfigNode.Load(SettingsPath);
                if (root?.GetNode("SETTINGS") is ConfigNode node)
                {
                    if (float.TryParse(node.GetValue("windowWidth"), out float w))
                        _windowW = Mathf.Clamp(w, MinWindowW, MaxWindowW);
                    if (float.TryParse(node.GetValue("uiScale"), out float s))
                        _uiScale = Mathf.Clamp(s, MinScale, MaxScale);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RFWindow] Could not load settings: {ex.Message}");
            }
        }

        private static void SaveSettings()
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath));
                ConfigNode root = new ConfigNode();
                ConfigNode node = root.AddNode("SETTINGS");
                node.AddValue("windowWidth", _windowW.ToString("F0"));
                node.AddValue("uiScale",     _uiScale.ToString("F2"));
                root.Save(SettingsPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RFWindow] Could not save settings: {ex.Message}");
            }
        }

        // ── Favourites ───────────────────────────────────────────────────────
        // Persisted to PluginData/RealFuels/RealFuelsWindowFavorites.cfg so
        // they survive across game sessions.
        private static readonly HashSet<string> Favourites = new HashSet<string>();
        private static readonly string FavouritesPath =
            System.IO.Path.Combine(KSPUtil.ApplicationRootPath,
                "GameData", "RealFuels", "PluginData", "RealFuelsWindowFavorites.cfg");

        private static void LoadFavorites()
        {
            Favourites.Clear();
            if (!System.IO.File.Exists(FavouritesPath)) return;
            try
            {
                ConfigNode root = ConfigNode.Load(FavouritesPath);
                if (root?.GetNode("FAVOURITES") is ConfigNode node)
                    foreach (ConfigNode.Value v in node.values)
                        Favourites.Add(v.name);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RFWindow] Could not load favourites: {ex.Message}");
            }
        }

        private static void SaveFavorites()
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FavouritesPath));
                ConfigNode root = new ConfigNode();
                ConfigNode node = root.AddNode("FAVOURITES");
                foreach (string name in Favourites)
                    node.AddValue(name, "true");
                root.Save(FavouritesPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RFWindow] Could not save favourites: {ex.Message}");
            }
        }

        // ── Instance fields ─────────────────────────────────────────────────
        private ModuleFuelTanks _module;
        private bool _visible;
        private Rect _winRect = new Rect(120, 80, MinWindowW, 10);
        private float _targetH = 600f;   // computed each frame in OnGUI
        private Vector2 _availScroll;
        private bool _overWindow;

        // Deferred editor notification: fire onEditorShipModified from Update(),
        // not from within OnGUI, to avoid corrupting Unity's UI layout pool.
        private bool _pendingNotify;

        // Tracks the previously focused IMGUI control so we can detect when focus
        // moves to a new text field and auto-select its contents.
        private int _lastKeyboardControl;

        // Last drawn window rect, kept in sync each frame so the ClickThruBlocker
        // always covers exactly the current window area.
        private Rect _ctbRect;

        // Edit buffers: resource name → string the player is currently typing.
        // Separate from tank.amount so partial input (e.g. "1.") isn't clamped.
        private readonly Dictionary<string, string> _editBuf =
            new Dictionary<string, string>();

        // Percentage edit buffers: resource name → pct string (0-100, no % sign).
        // Percentage is always relative to the FULL tank volume, not to filled volume.
        private readonly Dictionary<string, string> _pctBuf =
            new Dictionary<string, string>();

        // Available-list search
        private string _searchQuery = "";

        // Reserve — reserved empty volume (L).  Not a real RF resource; lives only in
        // this window.  Resets to 0 when a different module is opened.
        private double _reserveVolume = 0d;

        // Life Support planner state — persists across module switches (crew & days
        // are mission-level parameters, not tank-specific).
        private bool _lifeSupportExpanded = false;
        private string _lsCrewBuf = "1";
        private string _lsDaysBuf = "1.0";

        // Life Support "not enough space" popup.
        private bool _lsPopupOpen;
        private string _lsPopupMsg = "";
        private Rect _lsPopupRect = new Rect(0, 0, 340f, 0);

        // Cached quick-fill presets; rebuilt whenever the window opens or the
        // vessel's engine list changes (via onEditorShipModified).
        private List<QuickFillPreset> _presets = new List<QuickFillPreset>();

        // Per-preset fill percentage buffer (keyed by QuickFillPreset.Key).
        // Persists across preset rebuilds so the player's entered value is not lost
        // when an unrelated ship change triggers a rebuild.  Defaults to "100".
        private readonly Dictionary<string, string> _qfFillPctBuf =
            new Dictionary<string, string>();

        // Per-resource fill percentage buffer for the Available list (keyed by tank.name).
        // Mirrors _qfFillPctBuf so each resource remembers its own fill percentage.
        private readonly Dictionary<string, string> _availFillPctBuf =
            new Dictionary<string, string>();

        // Per-resource amount buffer for the Available list (keyed by tank.name).
        // Kept in sync with _availFillPctBuf: editing one recalculates the other.
        // Stores RF units (same basis as the rest of the window).
        private readonly Dictionary<string, string> _availAmountBuf =
            new Dictionary<string, string>();

        // Locked resources: maps resource name → the maxAmount (RF units) that was
        // snapshotted when the player hit the lock toggle.  Update() polls every
        // frame and re-asserts the stored value if RF changed it externally (e.g.
        // because a procedural part resized the tank).  The stored value is also
        // used by ScaleAll / SetVolumeAndScaleOthers to shield locked tanks from
        // indirect scaling.  Cleared when a different module is opened.
        private readonly Dictionary<string, double> _lockedAmounts =
            new Dictionary<string, double>();

        // ── Style / texture cache ────────────────────────────────────────────
        private bool _stylesReady;
        private Texture2D _txBg, _txHeader, _txDark, _txRow;
        private Texture2D _txBarTrack, _txBarBlue, _txPctBlue;
        private Texture2D _txInput, _txBorder, _txBtnHov, _txBtnRed;
        private Texture2D _txAddNorm, _txAddHov;
        private Texture2D _txQfNorm, _txQfHov, _txClear;
        private Texture2D _txFooter, _txFooterHov, _txFooterRedHov;
        private Texture2D _txDivider, _txAccent;
        private Texture2D _txAvailRowDivider, _txQfTagBg;
        private Texture2D _txReserveRow, _txBarReserve;
        private Texture2D _txIconLockClosed, _txIconLockOpen; // programmatic padlock icons
        private Texture2D _txLsHeaderBg, _txLsHov;            // Life Support header colours

        private GUIStyle _sWindow, _sTitle, _sSubtitle;
        private GUIStyle _sSectionLbl, _sCountBadge;
        private GUIStyle _sPropName, _sPropNameReserve, _sPctLbl, _sField, _sUnitLbl;
        private GUIStyle _sBtnApply, _sBtnRemove, _sBtnHalf;
        private GUIStyle _sBtnScale;
        private GUIStyle _sStar, _sStarOn, _sAvailName, _sFavBadge, _sBtnAdd;
        private GUIStyle _sFieldHighUtil; // amount field variant: bold green text for util > 1
        private GUIStyle _sQfEngine, _sQfName, _sQfRatio, _sQfRatioHighUtil, _sQfMain;
        private GUIStyle _sQfStatusGood, _sQfStatusWarn, _sQfStatusBad, _sQfStatusDim;
        private GUIStyle _sAvailFull;
        private GUIStyle _sFooter, _sFooterDanger;
        private GUIStyle _sMassLbl, _sMassVal;
        private GUIStyle _sMassRow;
        private GUIStyle _sSearch, _sSearchPlaceholder, _sSearchClear;
        private GUIStyle _sLsCommit, _sLsHeaderLbl;
        private GUIStyle _sBtnLock, _sBtnLockOn;  // lock toggle: dim when off, amber when on
        private GUIStyle _sGearBtn;         // settings toggle in header
        private GUIStyle _sSettingsValue;   // slider value readout
        private GUIStyle _sSettingsClose;   // Close button in settings window
        private Texture2D _txSettingsIcon;  // programmatic three-sliders icon
        private GUIStyle _sTooltip;         // floating tooltip label

        // Tooltip text set each frame by SetTooltip(); rendered at end of DrawWindow.
        private string _tooltipText = "";

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void Awake()
        {
            if (_instance != null) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            GameEvents.onEditorShipModified.Add(OnShipModified);
            LoadSettings();
            LoadFavorites();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            GameEvents.onEditorShipModified.Remove(OnShipModified);
            EditorLogic.fetch?.Unlock(LockID);
            DestroyTextures();
        }

        private void OnShipModified(ShipConstruct _)
        {
            if (!_visible || _module == null) return;

            // Rebuild quick-fill presets when engines are added/removed.
            RebuildPresets();

            // Refresh all edit/pct buffers from the current RF state so that
            // unlocked resources show their updated amounts after any external
            // change (tank resize, B9 switch, symmetry, etc.).
            SyncEditBuffers();

            // Re-assert locked amounts on top — SyncEditBuffers just wrote the
            // RF-rescaled (wrong) values for locked resources; ReassertLocks
            // overwrites those with the snapshotted values and corrects the %.
            if (_lockedAmounts.Count > 0)
                ReassertLocks();
        }

        // ── Update: deferred editor notification ─────────────────────────────

        private void Update()
        {
            if (_pendingNotify && _module != null)
            {
                _pendingNotify = false;
                if (EditorLogic.fetch?.ship != null)
                    GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }
        }

        /// <summary>
        /// Called from OnShipModified — event-driven, not polled.
        /// RF has already rescaled resources by the time we get here; this writes
        /// the snapshotted locked values back and syncs the pct buffers so the
        /// percentage readout immediately reflects the new tank capacity.
        /// If the tank shrank past what all locked resources can fit, they are
        /// scaled proportionally and their snapshots are updated.
        /// If any maxAmount was actually changed, _pendingNotify queues one
        /// more event so the editor reflects the corrected mass/cost.
        /// </summary>
        private void ReassertLocks()
        {
            double capacity = _module.volume;
            bool anyReasserted = false;

            // ── Collect all valid locked entries ─────────────────────────────
            var entries = new List<(FuelTank tank, string key, double lockedAmt)>();
            foreach (var kv in _lockedAmounts)
            {
                FuelTank t;
                if (_module.tanksDict.TryGetValue(kv.Key, out t))
                    entries.Add((t, kv.Key, kv.Value));
            }
            if (entries.Count == 0) return;

            // ── Total locked physical volume ──────────────────────────────────
            // If the tank shrank so much that even the locked resources alone
            // don't fit, scale them all proportionally (their ratio is preserved).
            double totalLockedLitres = entries.Sum(e =>
                e.tank.utilization > 0f
                    ? e.lockedAmt / (double)e.tank.utilization
                    : e.lockedAmt);

            double shrinkScale = (totalLockedLitres > capacity && totalLockedLitres > 0d)
                ? capacity / totalLockedLitres
                : 1d;

            // ── Re-assert each locked resource ────────────────────────────────
            foreach (var (tank, key, lockedAmt) in entries)
            {
                double lockedLitres = tank.utilization > 0f
                    ? lockedAmt / (double)tank.utilization
                    : lockedAmt;

                double targetLitres = lockedLitres * shrinkScale;
                double targetAmt = targetLitres * tank.utilization;

                // If we had to shrink, update the stored lock value so we don't
                // keep fighting against a now-impossible target.
                if (shrinkScale < 1d - 1e-9)
                    _lockedAmounts[key] = targetAmt;

                // Re-assert if RF silently changed the value
                if (Math.Abs(tank.maxAmount - targetAmt) > 0.0001d)
                {
                    tank.maxAmount = targetAmt;
                    tank.amount = tank.fillable ? tank.maxAmount : 0d;
                    anyReasserted = true;
                }

                // Always keep edit and pct buffers current for locked resources.
                // This is what makes the percentage update immediately when the
                // tank is resized — same physical litres, different capacity → new %.
                _editBuf[key] = targetAmt.ToString("F4");
                double pct = capacity > 0d ? (targetLitres / capacity) * 100d : 0d;
                _pctBuf[key] = pct.ToString("F2");
            }

            if (anyReasserted)
                _pendingNotify = true;   // tell the editor the part changed
        }

        // ── Main OnGUI ───────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_visible || _module == null) return;
            EnsureStyles();

            // Window height: fill from the window's current top edge to near the
            // bottom of the screen, matching the old TankWindow's Screen.height - 365
            // behaviour but relative to wherever the player has dragged the window.
            _targetH = Mathf.Max(200f, Screen.height - _winRect.y - 70f);

            _winRect = ClickThruBlocker.GUILayoutWindow(WindowID, _winRect, DrawWindow,
                                        GUIContent.none, _sWindow,
                                        GUILayout.Width(WindowW),
                                        GUILayout.Height(_targetH));

            // ── Settings window — drawn as a separate draggable window ────────
            if (_settingsOpen)
            {
                _settingsWinRect = ClickThruBlocker.GUILayoutWindow(SettingsWindowID,
                    _settingsWinRect, DrawSettingsWindow, GUIContent.none, _sWindow,
                    GUILayout.Width(280f));

                // Lock editor while mouse is over the settings window too.
                if (_settingsWinRect.Contains(Event.current.mousePosition))
                    EditorLogic.fetch?.Lock(true, true, true, LockID);
            }

            // ── Life Support "not enough space" popup ─────────────────────────
            if (_lsPopupOpen)
            {
                _lsPopupRect = ClickThruBlocker.GUILayoutWindow(LsPopupWindowID,
                    _lsPopupRect, DrawLsPopup, GUIContent.none, _sWindow,
                    GUILayout.Width(Mathf.Round(340f * _uiScale)));

                if (_lsPopupRect.Contains(Event.current.mousePosition))
                    EditorLogic.fetch?.Lock(true, true, true, LockID);
            }

            // Keep the ClickThruBlocker rect in sync with the window every frame.
            _ctbRect = _winRect;

            // Editor cursor lock: prevent part-picking while mouse is over window.
            _overWindow = _winRect.Contains(Event.current.mousePosition);
            if (_overWindow) EditorLogic.fetch?.Lock(true, true, true, LockID);
            else if (!_settingsOpen || !_settingsWinRect.Contains(Event.current.mousePosition))
                EditorLogic.fetch?.Unlock(LockID);

            // Auto-select all text when a text field gains focus.
            // GUIUtility.keyboardControl holds the control ID of the focused field;
            // when it changes to a new non-zero value a text field was just clicked.
            int kc = GUIUtility.keyboardControl;
            if (kc != _lastKeyboardControl)
            {
                _lastKeyboardControl = kc;
                if (kc != 0)
                {
                    TextEditor editor = GUIUtility.GetStateObject(
                        typeof(TextEditor), kc) as TextEditor;
                    editor?.SelectAll();
                }
            }
        }

        // ── Window contents ──────────────────────────────────────────────────

        private void DrawWindow(int id)
        {
            _tooltipText = "";  // reset every pass so only the current hover is shown

            // Live snapshot from RF each frame — no stale cache.
            var activeTanks = ActiveTanks();
            double filled = activeTanks.Sum(t => t.Volume);           // litres
            double capacity = _module.volume;                            // litres
            double rem = Math.Max(0d, capacity - filled);          // physical empty space
            double remForFill = Math.Max(0d, rem - _reserveVolume);        // space available for QF
            float usedFrac = Mathf.Clamp01((float)(filled / capacity));

            DrawHeader();
            DrawVolumeBar(rem, capacity, usedFrac);
            DrawMassRow();
            DrawSectionHeader("CURRENT",
                activeTanks.Count + " resource" + (activeTanks.Count != 1 ? "s" : ""));

            foreach (var tank in activeTanks)
                DrawCurrentRow(tank, filled);
            DrawReserveRow();   // always-present reserved-space row

            GUILayout.Space(4);
            DrawScaleAllBar();
            int availCount = _module.tanksDict.Values.Count(t => t.maxAmount <= 0d && t.canHave);
            DrawSectionHeader("AVAILABLE",
                availCount + " resource" + (availCount != 1 ? "s" : ""));
            DrawAvailableList();
            DrawQuickFill(remForFill);  // Quick Fill only fills non-reserved space
            DrawLifeSupport();
            DrawFooter();
            DrawTooltip();  // render floating tooltip on top of all other content

            GUI.DragWindow(new Rect(0, 0, WindowW, 64));
        }

        // ── Header ───────────────────────────────────────────────────────────

        private void DrawHeader()
        {
            Rect r = GUILayoutUtility.GetRect(WindowW, RH(64));
            GUI.DrawTexture(r, _txHeader);
            GUI.DrawTexture(new Rect(r.x + 50, r.yMax - 2, r.width - 100, 2), _txAccent);

            // Leave 48px on the right for the gear button.
            float titleH    = RH(22);
            float subtitleH = RH(20);
            float titleY    = r.y + RH(10);
            float subtitleY = titleY + titleH + RH(4);
            GUI.Label(new Rect(r.x + 16, titleY, r.width - 64, titleH),
                _module.part.partInfo.title.ToUpper(), _sTitle);
            GUI.Label(new Rect(r.x + 16, subtitleY, r.width - 64, subtitleH),
                (_module.type ?? "PROPELLANT CONFIGURATION").ToUpper(), _sSubtitle);

            // Settings toggle button — top-right of header, vertically centred.
            float gearSz = RH(30);
            Rect gearR = new Rect(r.xMax - 44f, r.y + (r.height - gearSz) * 0.5f, gearSz, gearSz);
            if (GUI.Button(gearR, GUIContent.none, _sGearBtn))
            {
                _settingsOpen = !_settingsOpen;
                if (_settingsOpen)
                {
                    // Position settings window to the right of the main window.
                    _settingsWinRect = new Rect(
                        _winRect.xMax + 8f,
                        _winRect.y,
                        280f, 0f);
                }
            }
            SetTooltip(gearR, "Window settings — adjust width and font scale.");
            // Draw the settings icon texture — tinted gold when active.
            if (_txSettingsIcon != null)
            {
                float iconSz = RH(16);
                GUI.color = _settingsOpen ? new Color(1f, 0.84f, 0f) : new Color(0.78f, 0.85f, 1f);
                GUI.DrawTexture(new Rect(gearR.x + (gearSz - iconSz) * 0.5f,
                                        gearR.y + (gearSz - iconSz) * 0.5f,
                                        iconSz, iconSz),
                    _txSettingsIcon, ScaleMode.StretchToFill);
                GUI.color = Color.white;
            }
        }

        // ── Settings window (separate draggable window) ───────────────────────
        // Changes apply live — the main window reacts in real time as sliders move.
        // Settings are saved to disk when the player closes this window.

        private void DrawSettingsWindow(int id)
        {
            const float W = 280f;
            const float px = 14f;
            const float pw = W - 28f;

            // ── Title bar ─────────────────────────────────────────────────────
            Rect hdr = GUILayoutUtility.GetRect(W, 36f);
            GUI.DrawTexture(hdr, _txHeader);
            GUI.DrawTexture(new Rect(hdr.x, hdr.yMax - 1, hdr.width, 1), _txAccent);
            GUI.Label(new Rect(hdr.x + px, hdr.y + 8f, W - 60f, 20f),
                "WINDOW SETTINGS", _sSectionLbl);

            // X close button (top-right)
            if (GUI.Button(new Rect(hdr.xMax - 32f, hdr.y + 6f, 24f, 24f), "✕", _sBtnRemove))
            {
                SaveSettings();
                _settingsOpen = false;
            }

            GUILayout.Space(6);

            // ── Width slider ──────────────────────────────────────────────────
            Rect widthPanel = GUILayoutUtility.GetRect(W, 52f);
            GUI.DrawTexture(widthPanel, _txDark);
            GUI.DrawTexture(new Rect(widthPanel.x, widthPanel.yMax - 1, widthPanel.width, 1), _txDivider);

            GUI.Label(new Rect(widthPanel.x + px, widthPanel.y + 6f, 120f, 16f),
                "WINDOW WIDTH", _sSectionLbl);
            GUI.Label(new Rect(widthPanel.xMax - px - 68f, widthPanel.y + 6f, 68f, 16f),
                _windowW.ToString("F0") + " px", _sSettingsValue);

            float newW = GUI.HorizontalSlider(
                new Rect(widthPanel.x + px, widthPanel.y + 28f, pw, 14f),
                _windowW, MinWindowW, MaxWindowW,
                HighLogic.Skin.horizontalSlider, HighLogic.Skin.horizontalSliderThumb);
            if (!Mathf.Approximately(newW, _windowW))
                _windowW = Mathf.Round(newW);   // snap to whole pixels

            GUILayout.Space(4);

            // ── Scale slider ──────────────────────────────────────────────────
            Rect scalePanel = GUILayoutUtility.GetRect(W, 52f);
            GUI.DrawTexture(scalePanel, _txDark);
            GUI.DrawTexture(new Rect(scalePanel.x, scalePanel.yMax - 1, scalePanel.width, 1), _txDivider);

            GUI.Label(new Rect(scalePanel.x + px, scalePanel.y + 6f, 120f, 16f),
                "UI SCALE", _sSectionLbl);
            GUI.Label(new Rect(scalePanel.xMax - px - 68f, scalePanel.y + 6f, 68f, 16f),
                _uiScale.ToString("F2") + "×", _sSettingsValue);

            float newS = GUI.HorizontalSlider(
                new Rect(scalePanel.x + px, scalePanel.y + 28f, pw, 14f),
                _uiScale, MinScale, MaxScale,
                HighLogic.Skin.horizontalSlider, HighLogic.Skin.horizontalSliderThumb);
            if (!Mathf.Approximately(newS, _uiScale))
            {
                _uiScale = (float)Math.Round(newS, 2);
                _stylesReady = false;   // rebuild fonts at new scale next frame
            }

            GUILayout.Space(4);

            // ── Note ─────────────────────────────────────────────────────────
            Rect notePanel = GUILayoutUtility.GetRect(W, 28f);
            GUI.DrawTexture(notePanel, _txDark);
            var noteStyle = new GUIStyle(_sSettingsValue)
                { alignment = TextAnchor.MiddleCenter, fontSize = FS(9), wordWrap = true };
            GUI.Label(new Rect(notePanel.x + px, notePanel.y + 2f, pw, 24f),
                "Scale affects font sizes and row heights.", noteStyle);

            GUILayout.Space(6);

            // ── Reset / Close ─────────────────────────────────────────────────
            Rect btnRow = GUILayoutUtility.GetRect(W, 44f);
            GUI.DrawTexture(btnRow, _txDark);
            GUI.DrawTexture(new Rect(btnRow.x, btnRow.y, btnRow.width, 1), _txDivider);

            float bw = (btnRow.width - 44f) * 0.5f;
            float bx = btnRow.x + px;
            float by = btnRow.y + 8f;

            if (GUI.Button(new Rect(bx, by, bw, 28f), "RESET", _sSettingsClose))
            {
                _windowW = MinWindowW;
                _uiScale = 1.0f;
                _stylesReady = false;
                SaveSettings();
            }
            if (GUI.Button(new Rect(bx + bw + 16f, by, bw, 28f), "CLOSE", _sSettingsClose))
            {
                SaveSettings();
                _settingsOpen = false;
            }

            GUI.DragWindow(new Rect(0, 0, W, 36f));
        }

        // ── Volume bar ───────────────────────────────────────────────────────

        private void DrawVolumeBar(double remaining, double total, float usedFrac)
        {
            Rect panel = GUILayoutUtility.GetRect(WindowW, RH(56));
            GUI.DrawTexture(panel, _txDark);

            float px = panel.x + 16, pw = panel.width - 32;

            float lblH  = RH(20);
            float lblY  = panel.y + RH(8);
            float barH  = RH(8);
            float barY  = lblY + lblH + RH(10);
            GUI.Label(new Rect(px, lblY, 110, lblH), "TANK VOLUME", _sSectionLbl);

            var remStyle = new GUIStyle(_sSectionLbl)
            {
                fontStyle = FontStyle.Normal,
                fontSize  = FS(12),
            };
            remStyle.normal.textColor = C("#72e0a0");   // 7.2:1 on dark bg
            GUI.Label(new Rect(px + 112, lblY, pw - 112, lblH),
                remaining.ToString("F1") + " / " + total.ToString("F1") + " L remaining",
                remStyle);

            Rect track = new Rect(px, barY, pw, barH);
            GUI.DrawTexture(track, _txBarTrack);
            if (usedFrac > 0f)
                GUI.DrawTexture(
                    new Rect(track.x, track.y, track.width * usedFrac, track.height),
                    _txBarBlue);
            // Rserve segment — amber fill immediately after the propellant fill
            float reserveFrac = (total > 0d) ? Mathf.Clamp01((float)(_reserveVolume / total)) : 0f;
            if (reserveFrac > 0f)
                GUI.DrawTexture(
                    new Rect(track.x + track.width * usedFrac, track.y,
                             track.width * reserveFrac, track.height),
                    _txBarReserve);
        }

        // ── Mass row ─────────────────────────────────────────────────────────

        private void DrawMassRow()
        {
            // RF calculates wet mass via IPartMassModifier; read from part directly.
            float wet = _module.part.mass + _module.part.GetResourceMass();
            float dry = _module.part.mass; // part.mass is dry in editor context

            Rect panel = GUILayoutUtility.GetRect(WindowW, RH(46));
            GUI.DrawTexture(panel, _txDark);

            float cw = panel.width / 3f;
            MassCell(new Rect(panel.x, panel.y, cw, panel.height),
                "WET MASS", wet.ToString("F3") + " t");
            MassCell(new Rect(panel.x + cw, panel.y, cw, panel.height),
                "DRY MASS", dry.ToString("F3") + " t");
            MassCell(new Rect(panel.x + cw * 2, panel.y, cw, panel.height),
                "PROPELLANT", (wet - dry).ToString("F3") + " t");

            float divPad = RH(6);
            GUI.DrawTexture(new Rect(panel.x + cw,       panel.y + divPad, 1, panel.height - divPad * 2), _txDivider);
            GUI.DrawTexture(new Rect(panel.x + cw * 2,   panel.y + divPad, 1, panel.height - divPad * 2), _txDivider);
            GUI.DrawTexture(new Rect(panel.x, panel.yMax - 1, panel.width, 1), _txDivider);
        }

        private void MassCell(Rect r, string label, string value)
        {
            float lblH = RH(16);
            float valH = RH(20);
            // Stack label + value centred within the cell.
            float totalH  = lblH + RH(4) + valH;
            float startY  = r.y + (r.height - totalH) * 0.5f;
            GUI.Label(new Rect(r.x + 16, startY,           r.width - 20, lblH), label, _sMassLbl);
            GUI.Label(new Rect(r.x + 16, startY + lblH + RH(4), r.width - 20, valH), value, _sMassVal);
        }

        // ── Section header ───────────────────────────────────────────────────

        private void DrawSectionHeader(string label, string badge)
        {
            Rect r = GUILayoutUtility.GetRect(WindowW, RH(26));
            float lblH = RH(16);
            float lblY = r.y + (r.height - lblH) * 0.5f;
            GUI.Label(new Rect(r.x + 16, lblY, 80, lblH), label, _sSectionLbl);

            float lineW = badge != null ? r.width - 210 : r.width - 116;
            GUI.DrawTexture(new Rect(r.x + 100, r.y + r.height * 0.5f, lineW, 1), _txDivider);

            if (badge != null)
            {
                float badgeH = RH(18);
                float badgeY = r.y + (r.height - badgeH) * 0.5f;
                GUI.Label(new Rect(r.x + r.width - 106, badgeY, 90, badgeH), badge, _sCountBadge);
            }
        }

        // ── Current row ──────────────────────────────────────────────────────
        // Edit field shows tank.maxAmount (RF units), NOT physical litres.
        // This matches the old TankWindow and means high-utilization gases like
        // Oxygen (util=200) show "591.84" (the game unit quantity) rather than
        // "2.9592 L" (the physical space consumed).  ApplyVolume converts back.
        //
        // Layout (480px window, 16px padding each side = 448px usable):
        //   Name 80 | pct_field 44 + "%" 14 | vol_field 90 | ✓ 24 | ✕ 24 | 🔒 24 | 1/2 24 | 2× 24 | mass 68
        //   advances: 84+46+16+94+28+28+28+28+28+68 = 448 (start 16 → end 464 = 480−16) ✓

        private void DrawCurrentRow(FuelTank tank, double totalFilled)
        {
            double capacity = _module.volume;

            // Percentage of the FULL tank (not of filled volume) — still in physical space terms.
            double pctOfTotal = capacity > 0d ? (tank.Volume / capacity) * 100d : 0d;

            // Ensure edit buffers exist.
            // Volume field stores RF units (maxAmount) so the player sees game-native quantities.
            // Percentage field stores physical-space % so it matches the volume bar.
            if (!_editBuf.ContainsKey(tank.name))
                _editBuf[tank.name] = tank.maxAmount.ToString("F4");
            if (!_pctBuf.ContainsKey(tank.name))
                _pctBuf[tank.name] = pctOfTotal.ToString("F2");

            Rect r = GUILayoutUtility.GetRect(WindowW, RH(44));
            GUI.DrawTexture(r, _txRow);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), _txDivider);

            // Scaled element heights and vertical offsets within the row.
            float fh  = RH(26);  // text-field height
            float bh  = RH(28);  // small-button height
            float lh  = RH(22);  // label height
            float fyO = r.y + (r.height - fh) * 0.5f;   // field y (vertically centred)
            float byO = r.y + (r.height - bh) * 0.5f;   // button y
            float lyO = r.y + (r.height - lh) * 0.5f;   // label y

            float cx = r.x + 16f;

            // ── Name — width absorbs any extra space when window is wider than 480px ──
            // Fixed controls after the name slot consume 364px + 16px right margin = 380px.
            // At 480px: nameW = 80px.  At 600px: nameW = 200px — no truncation.
            float nameW = Mathf.Max(40f, r.width - 396f);
            GUI.Label(new Rect(cx, lyO, nameW, lh), tank.name, _sPropName);
            cx += nameW + 4f;

            // ── Percentage field ──
            GUI.DrawTexture(new Rect(cx, fyO, 44f, fh), _txInput);
            string newPct = GUI.TextField(
                new Rect(cx, fyO, 44f, fh), _pctBuf[tank.name], _sField);
            if (newPct != _pctBuf[tank.name])
                _pctBuf[tank.name] = newPct;
            SetTooltip(new Rect(cx, fyO, 44f, fh),
                "Percentage of total tank capacity.\nEdit and press ✓ to apply.");
            cx += 46f;

            GUI.Label(new Rect(cx, lyO, 14f, lh), "%", _sUnitLbl);
            cx += 16f;

            // ── Volume field (RF units — maxAmount, not physical litres) ──
            // Wider than before (90px): high-utilization values like Oxygen's 591.84
            // need more room.  No "L" suffix — these are game resource units, not litres.
            GUI.DrawTexture(new Rect(cx, fyO, 90f, fh), _txInput);
            string newVol = GUI.TextField(
                new Rect(cx, fyO, 90f, fh), _editBuf[tank.name], _sField);
            if (newVol != _editBuf[tank.name])
                _editBuf[tank.name] = newVol;
            if (tank.utilization > 1.0f)
                SetTooltip(new Rect(cx, fyO, 90f, fh),
                    $"RF game units — not physical litres.\n{tank.name} has utilization {tank.utilization:F0}×:\n1 RF unit = {(1.0 / tank.utilization):F4} L of tank space.");
            cx += 94f;  // 90 field + 4 gap

            // ── Apply ✓ (green) ──
            if (GUI.Button(new Rect(cx, byO, 24f, bh), "✓", _sBtnApply))
            {
                // Prefer percentage if it differs from the live value; else use volume.
                if (double.TryParse(_pctBuf[tank.name], out double pctIn) &&
                    Math.Abs(pctIn - pctOfTotal) > 0.05d)
                    ApplyPct(tank, pctIn);
                else
                    ApplyVolume(tank);
            }
            SetTooltip(new Rect(cx, byO, 24f, bh),
                "Apply the typed value.\n% field takes priority if edited;\notherwise the volume field is used.\nOther resources scale down only if needed.");
            cx += 28f;

            // ── Remove ✕ (red) ──
            if (GUI.Button(new Rect(cx, byO, 24f, bh), "✕", _sBtnRemove))
                RemoveTank(tank);
            cx += 28f;

            // ── Lock toggle ──
            // Closed padlock (amber) = amount snapshotted; re-asserted on any external change.
            // Open padlock   (dim)   = participates in normal scaling.
            bool isLocked = _lockedAmounts.ContainsKey(tank.name);
            Rect lockR = new Rect(cx, byO, 24f, bh);

            // Hover highlight (same background used by all other small buttons)
            if (lockR.Contains(Event.current.mousePosition))
                GUI.DrawTexture(lockR, _txBtnHov);

            // Icon — tinted amber when locked, dim when unlocked, using the same
            // colour values already baked into the button styles.
            GUI.color = isLocked ? _sBtnLockOn.normal.textColor
                                 : _sBtnLock.normal.textColor;
            GUI.DrawTexture(
                new Rect(lockR.x + (lockR.width - RH(14)) * 0.5f, lockR.y + (lockR.height - RH(18)) * 0.5f, RH(14), RH(18)),
                isLocked ? _txIconLockClosed : _txIconLockOpen,
                ScaleMode.StretchToFill);
            GUI.color = Color.white;  // restore before any further draws

            // Invisible hit-box for click detection
            if (GUI.Button(lockR, GUIContent.none, GUIStyle.none))
            {
                if (isLocked)
                    _lockedAmounts.Remove(tank.name);
                else
                    _lockedAmounts[tank.name] = tank.maxAmount;  // snapshot current RF units
            }
            SetTooltip(lockR, isLocked
                ? "Locked — amount is snapshotted.\nScale All and tank resize will not change this resource.\nClick to unlock."
                : "Lock this resource's amount.\nWhile locked, Scale All and tank resize\nwill not affect it.\nClick to lock.");
            cx += 28f;

            // ── Half 1/2 ──
            if (GUI.Button(new Rect(cx, byO, 24f, bh), "1/2", _sBtnHalf))
                HalfTank(tank);
            SetTooltip(new Rect(cx, byO, 24f, bh), "Halve this resource's volume.");
            cx += 28f;

            // ── Double 2× ──
            if (GUI.Button(new Rect(cx, byO, 24f, bh), "2×", _sBtnHalf))
                DoubleTank(tank);
            SetTooltip(new Rect(cx, byO, 24f, bh),
                "Double this resource's volume.\nCapped at available space; reserve is respected.");
            cx += 28f;

            // ── Mass ──
            GUI.Label(new Rect(cx, lyO, 68f, lh),
                (tank.maxAmount * tank.density).ToString("F3") + " t", _sMassRow);
        }

        // ── Reserve row ───────────────────────────────────────────────────────
        // An always-present virtual row that reserves empty volume in the tank.
        // Nothing is written to RF — _reserveVolume is a window-side reservation only.

        private const string ReserveKey = "__reserve__";

        private void DrawReserveRow()
        {
            double capacity = _module.volume;
            double ullPct = capacity > 0d ? (_reserveVolume / capacity) * 100d : 0d;

            if (!_editBuf.ContainsKey(ReserveKey))
                _editBuf[ReserveKey] = _reserveVolume.ToString("F4");
            if (!_pctBuf.ContainsKey(ReserveKey))
                _pctBuf[ReserveKey] = ullPct.ToString("F2");

            Rect r = GUILayoutUtility.GetRect(WindowW, RH(44));
            GUI.DrawTexture(r, _txReserveRow);
            // Thin blue top border ties reserve back to the tank system visually
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1), _txAccent);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), _txDivider);

            float fh  = RH(26);
            float bh  = RH(28);
            float lh  = RH(22);
            float fyO = r.y + (r.height - fh) * 0.5f;
            float byO = r.y + (r.height - bh) * 0.5f;
            float lyO = r.y + (r.height - lh) * 0.5f;

            float cx = r.x + 16f;

            // Name (italic style to distinguish from real resources)
            GUI.Label(new Rect(cx, lyO, 108f, lh), "RESERVE", _sPropNameReserve);
            SetTooltip(new Rect(cx, lyO, 108f, lh),
                "Reserve empty space in the tank.\n\nQuick Fill and Scale All will not fill past this point. Nothing is written to RF — this is a window-side soft cap.\n\nUseful for ullage gas or keeping room for future propellant additions.");
            cx += 112f;

            // Percentage field
            GUI.DrawTexture(new Rect(cx, fyO, 44f, fh), _txInput);
            string newPct = GUI.TextField(
                new Rect(cx, fyO, 44f, fh), _pctBuf[ReserveKey], _sField);
            if (newPct != _pctBuf[ReserveKey]) _pctBuf[ReserveKey] = newPct;
            cx += 46f;

            GUI.Label(new Rect(cx, lyO, 14f, lh), "%", _sUnitLbl);
            cx += 16f;

            // Volume field
            GUI.DrawTexture(new Rect(cx, fyO, 76f, fh), _txInput);
            string newVol = GUI.TextField(
                new Rect(cx, fyO, 76f, fh), _editBuf[ReserveKey], _sField);
            if (newVol != _editBuf[ReserveKey]) _editBuf[ReserveKey] = newVol;
            cx += 78f;

            GUI.Label(new Rect(cx, lyO, 14f, lh), "L", _sUnitLbl);
            cx += 16f;

            // Apply ✓
            if (GUI.Button(new Rect(cx, byO, 24f, bh), "✓", _sBtnApply))
                ApplyReserve();
            cx += 28f;

            // Clear ✕ — zeros out reserve (row stays, just at 0)
            if (GUI.Button(new Rect(cx, byO, 24f, bh), "✕", _sBtnRemove))
            {
                _reserveVolume = 0d;
                _editBuf[ReserveKey] = "0.0000";
                _pctBuf[ReserveKey] = "0.00";
            }
            cx += 28f;

            // 1/2
            if (GUI.Button(new Rect(cx, byO, 24f, bh), "1/2", _sBtnHalf))
            {
                _reserveVolume = _reserveVolume * 0.5d;
                _editBuf[ReserveKey] = _reserveVolume.ToString("F4");
                _pctBuf[ReserveKey] = capacity > 0d
                    ? (_reserveVolume / capacity * 100d).ToString("F2") : "0.00";
            }
            cx += 28f;

            // 2×
            if (GUI.Button(new Rect(cx, byO, 24f, bh), "2×", _sBtnHalf))
            {
                double maxUll = Math.Max(0d, _module.AvailableVolume);
                _reserveVolume = Math.Min(_reserveVolume * 2d, maxUll);
                _editBuf[ReserveKey] = _reserveVolume.ToString("F4");
                _pctBuf[ReserveKey] = capacity > 0d
                    ? (_reserveVolume / capacity * 100d).ToString("F2") : "0.00";
            }
        }

        /// <summary>
        /// Parse the reserve edit buffers and update _reserveVolume.
        /// Prefers the percentage field if it changed; otherwise uses the volume field.
        /// Caps the result at the physically available empty space.
        /// </summary>
        private void ApplyReserve()
        {
            double capacity = _module.volume;
            double physical = Math.Max(0d, capacity - ActiveTanks().Sum(t => t.Volume));

            double currentPct = capacity > 0d ? (_reserveVolume / capacity) * 100d : 0d;

            if (double.TryParse(_pctBuf[ReserveKey], out double pctIn) &&
                Math.Abs(pctIn - currentPct) > 0.05d)
            {
                _reserveVolume = Math.Max(0d, Math.Min(pctIn / 100d * capacity, physical));
            }
            else if (double.TryParse(_editBuf[ReserveKey], out double volIn))
            {
                _reserveVolume = Math.Max(0d, Math.Min(volIn, physical));
            }

            _editBuf[ReserveKey] = _reserveVolume.ToString("F4");
            _pctBuf[ReserveKey] = capacity > 0d
                ? (_reserveVolume / capacity * 100d).ToString("F2") : "0.00";
        }

        // ── Available list ───────────────────────────────────────────────────

        private void DrawAvailableList()
        {
            DrawSearchBar();

            // Available = tanks in the RF list with maxAmount == 0 that can be added,
            // further filtered by the current search query (case-insensitive).
            string q = _searchQuery.Trim().ToLowerInvariant();

            var available = _module.tanksDict.Values
                .Where(t => t.maxAmount <= 0d && t.canHave &&
                            (q.Length == 0 || t.name.ToLowerInvariant().Contains(q)))
                .OrderBy(t => t.name)
                .ToList();

            var favs = available.Where(t => Favourites.Contains(t.name)).ToList();
            var others = available.Where(t => !Favourites.Contains(t.name)).ToList();

            float rowH = RH(34);

            // ── Scroll height: remaining space inside the fixed-height window ──
            //   Header          RH(64)
            //   Volume bar      RH(56)
            //   Mass row        RH(46)
            //   CURRENT header  RH(26)
            //   Current rows     n × RH(44)
            //   Reserve row     RH(44)
            //   Space             4
            //   Scale-all bar   RH(44)
            //   AVAILABLE header RH(26)
            //   Search bar      RH(30)
            //   Quick Fill       RH(34) + presets × RH(50)  (or 0 if no presets)
            //   Footer          RH(38)
            //   Window chrome    12  (GUILayout internal padding)

            int nActive = _module.tanksDict.Values.Count(t => t.maxAmount > 0d);
            float qfPanelH = _presets.Count > 0 ? RH(34) + _presets.Count * RH(50) + 10f : 0f;
            // Life Support panel: only present when the tank type supports Food.
            // header RH(44); when expanded adds inputs(RH(44)) + 3 rows(RH(34)ea) + commit(RH(38))
            FuelTank _lsFoodCheck;
            bool lsAvail = _module.tanksDict.TryGetValue("Food", out _lsFoodCheck) && _lsFoodCheck.canHave;
            float lsH = lsAvail ? RH(36) + (_lifeSupportExpanded ? RH(44) + 3f * RH(34) + RH(38) : 0f) : 0f;
            float fixedH = RH(64) + RH(56) + RH(46) + RH(26) + (nActive * RH(44)) + RH(44) + 4 + RH(44) + RH(26) + RH(30) + qfPanelH + lsH + RH(38) + 12;
            float scrollH = Mathf.Max(rowH, _targetH - fixedH);

            float contentH = (available.Count + (favs.Count > 0 && others.Count > 0 ? 1 : 0)) * rowH + 4f;
            float listH = Mathf.Min(contentH, scrollH);

            _availScroll = GUILayout.BeginScrollView(
                _availScroll, false, false,
                GUIStyle.none, GUI.skin.verticalScrollbar,
                GUILayout.Height(listH));

            foreach (var t in favs) DrawAvailRow(t);

            if (favs.Count > 0 && others.Count > 0)
            {
                Rect dr = GUILayoutUtility.GetRect(WindowW, RH(10));
                GUI.DrawTexture(new Rect(dr.x, dr.y + 5, dr.width, 1), _txDivider);
            }

            foreach (var t in others) DrawAvailRow(t);

            // Empty-state message when nothing matches
            if (available.Count == 0)
            {
                Rect er = GUILayoutUtility.GetRect(WindowW, RH(34));
                var es = new GUIStyle(_sSectionLbl) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(er, q.Length > 0 ? "No matches for \"" + _searchQuery + "\"" : "All resources active", es);
            }

            GUILayout.EndScrollView();
        }

        private void DrawSearchBar()
        {
            // Full-width bar sitting flush between the section header and the scroll list.
            Rect bar = GUILayoutUtility.GetRect(WindowW, RH(30));
            GUI.DrawTexture(bar, _txDark);
            GUI.DrawTexture(new Rect(bar.x, bar.yMax - 1, bar.width, 1), _txDivider);

            float clearW = _searchQuery.Length > 0 ? 24f : 0f;
            float fieldX = bar.x + 16f;
            float fieldW = bar.width - 32f - clearW;
            float sfh = RH(20);  // search field height
            float sfyO = bar.y + (bar.height - sfh) * 0.5f;

            // Text field
            GUI.DrawTexture(new Rect(fieldX - 2, sfyO - 1, fieldW + 4, sfh + 2), _txInput);
            GUI.SetNextControlName("RFSearch");
            string newQ = GUI.TextField(
                new Rect(fieldX, sfyO, fieldW, sfh),
                _searchQuery, _sSearch);

            // Placeholder text when empty and unfocused
            if (_searchQuery.Length == 0 && GUI.GetNameOfFocusedControl() != "RFSearch")
                GUI.Label(new Rect(fieldX, sfyO, fieldW, sfh),
                    "Search propellants…", _sSearchPlaceholder);

            if (newQ != _searchQuery)
            {
                _searchQuery = newQ;
                _availScroll = Vector2.zero;  // reset scroll on new query
            }

            // ✕ clear button — only visible when there is text
            if (_searchQuery.Length > 0)
            {
                float cbh = RH(20);
                float cby = bar.y + (bar.height - cbh) * 0.5f;
                if (GUI.Button(new Rect(bar.xMax - 16f - clearW, cby, clearW, cbh),
                               "✕", _sSearchClear))
                {
                    _searchQuery = "";
                    _availScroll = Vector2.zero;
                    GUI.FocusControl(null);
                }
            }
        }

        private void DrawAvailRow(FuelTank tank)
        {
            bool isFav = Favourites.Contains(tank.name);
            Rect r = GUILayoutUtility.GetRect(WindowW, RH(34));
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), _txAvailRowDivider);
            float abh = RH(24);  // +ADD button height
            float afh = RH(24);  // input field height in avail row
            float aly = r.y + (r.height - RH(22)) * 0.5f;    // label y (vertically centred)
            float afy = r.y + (r.height - afh) * 0.5f;       // field y
            float aby = r.y + (r.height - abh) * 0.5f;       // button y
            float asby = r.y + (r.height - RH(24)) * 0.5f;   // star button y

            // Right-side controls:
            // [kg/L 70][gap 6][pct field 44][% 16][gap 6][amt field 64][L 14][gap 6][+ADD 54][rMargin 10]
            const float kgLW = 70f;
            const float kgLGap = 6f;
            const float pctFieldW = 44f;
            const float pctLblW = 16f;
            const float amtGap = 6f;
            const float amtFieldW = 64f;
            const float amtLblW = 14f;
            const float btnGap = 6f;
            const float btnW = 54f;
            const float rMargin = 10f;
            const float rightW = kgLW + kgLGap + pctFieldW + pctLblW + amtGap
                                    + amtFieldW + amtLblW + btnGap + btnW + rMargin;

            float cx = r.x + 14;

            // Star toggle
            if (GUI.Button(new Rect(cx, asby, 24, RH(24)), "★",
                           isFav ? _sStarOn : _sStar))
            {
                if (isFav) Favourites.Remove(tank.name);
                else Favourites.Add(tank.name);
                SaveFavorites();
            }
            SetTooltip(new Rect(cx, asby, 24, RH(24)), "Favourite — pins to the top of the list.");
            cx += 30;

            // Name — fills the remaining left space before the right-side controls
            float nameW = r.xMax - cx - rightW;
            GUI.Label(new Rect(cx, aly, nameW, RH(22)), tank.name, _sAvailName);

            // kg/L — stored density accounting for RF utilization.
            // Formula: (density t/u × 1000 kg/t × utilization u/L) ÷ volume L/u
            var resDef = PartResourceLibrary.Instance.GetDefinition(tank.name);
            if (resDef != null)
            {
                double kgPerLitre = (resDef.density * 1000.0 * tank.utilization)
                                    / Math.Max(resDef.volume, 0.001);
                float kgLX = r.xMax - rMargin - btnW - btnGap - amtLblW - amtFieldW
                             - amtGap - pctLblW - pctFieldW - kgLGap - kgLW;
                GUI.Label(new Rect(kgLX, aly, kgLW, RH(22)),
                    kgPerLitre.ToString("G3") + " kg/L", _sQfRatio);
                if (tank.utilization > 1.0f)
                    SetTooltip(new Rect(kgLX, aly, kgLW, RH(22)),
                        $"{tank.name} has utilization {tank.utilization:F0}×.\nHigh-pressure storage: {tank.utilization:F0} RF units fit in 1 L.\nThis is why the kg/L figure is elevated vs. base density.");
            }

            double physAvail = Math.Max(0d, _module.AvailableVolume - _reserveVolume);
            // Percentage fields are relative to the total tank capacity so that setting
            // two resources to 5% each always yields 5% of the total for each — even
            // when clicked sequentially.  The actual add is still capped at physAvail.
            double maxRfUnits = _module.volume * tank.utilization; // RF units at 100% of total capacity
            bool canAdd = physAvail >= 0.001d;

            // Initialise pct buffer on first appearance
            if (!_availFillPctBuf.ContainsKey(tank.name)) _availFillPctBuf[tank.name] = "100";

            // Initialise amount buffer from current pct on first appearance
            if (!_availAmountBuf.ContainsKey(tank.name))
            {
                double initPct;
                double.TryParse(_availFillPctBuf[tank.name], out initPct);
                _availAmountBuf[tank.name] =
                    (maxRfUnits * Math.Max(0d, Math.Min(100d, initPct)) / 100d).ToString("F1");
            }

            string oldPctText = _availFillPctBuf[tank.name];
            string oldAmtText = _availAmountBuf[tank.name];

            // ── Percentage input field ────────────────────────────────────────
            float rx = r.xMax - rMargin - btnW - btnGap - amtLblW - amtFieldW
                       - amtGap - pctLblW - pctFieldW;

            GUI.SetNextControlName("availPct_" + tank.name);
            GUI.DrawTexture(new Rect(rx, afy, pctFieldW, afh), _txInput);
            string newPct = GUI.TextField(
                new Rect(rx, afy, pctFieldW, afh), oldPctText, _sField);
            rx += pctFieldW;

            GUI.Label(new Rect(rx, aly, pctLblW, RH(22)), "%", _sUnitLbl);
            rx += pctLblW + amtGap;

            // ── Amount input field ────────────────────────────────────────────
            // High-utilization resources get the bold-green field style so the
            // visual signal is consistent with what was previously a read-only label.
            GUIStyle amtStyle = tank.utilization > 1.0 ? _sFieldHighUtil : _sField;

            GUI.SetNextControlName("availAmt_" + tank.name);
            GUI.DrawTexture(new Rect(rx, afy, amtFieldW, afh), _txInput);
            string newAmt = GUI.TextField(
                new Rect(rx, afy, amtFieldW, afh), oldAmtText, amtStyle);
            rx += amtFieldW;

            GUI.Label(new Rect(rx, aly, amtLblW, RH(22)), "L", _sUnitLbl);
            rx += amtLblW + btnGap;

            // ── Sync: whichever field changed drives the other ────────────────
            // Amount field changed — back-compute percentage
            if (newAmt != oldAmtText)
            {
                _availAmountBuf[tank.name] = newAmt;
                double amt;
                if (double.TryParse(newAmt, out amt) && maxRfUnits > 0.001d)
                    _availFillPctBuf[tank.name] =
                        Math.Min(100d, amt / maxRfUnits * 100d).ToString("F1");
            }
            // Percentage field changed — recompute amount
            else if (newPct != oldPctText)
            {
                _availFillPctBuf[tank.name] = newPct;
                double pct;
                double.TryParse(newPct, out pct);
                _availAmountBuf[tank.name] =
                    (maxRfUnits * Math.Max(0d, Math.Min(100d, pct)) / 100d).ToString("F1");
            }
            // Neither changed — passive sync so amount reflects current physAvail
            // (e.g. after another resource is added).  Skip if amount field is focused
            // so we don't clobber text the player is actively editing.
            else if (GUI.GetNameOfFocusedControl() != "availAmt_" + tank.name)
            {
                double pct;
                double.TryParse(_availFillPctBuf[tank.name], out pct);
                _availAmountBuf[tank.name] =
                    (maxRfUnits * Math.Max(0d, Math.Min(100d, pct)) / 100d).ToString("F1");
            }

            // fillFrac is always derived from the pct buffer (source of truth)
            double fillPct;
            double.TryParse(_availFillPctBuf[tank.name], out fillPct);
            double fillFrac = Math.Max(0d, Math.Min(100d, fillPct)) / 100d;

            // ── +ADD / FULL ───────────────────────────────────────────────────
            if (canAdd && GUI.Button(new Rect(rx, aby, btnW, abh), "+ADD", _sBtnAdd))
                AddTank(tank, fillFrac);
            else if (!canAdd)
                GUI.Label(new Rect(rx, aby, btnW, abh), "FULL", _sAvailFull);
            if (canAdd)
                SetTooltip(new Rect(rx, aby, btnW, abh),
                    "Add this resource at the specified % of total tank capacity.\nCapped at currently available space.");
        }

        // ── Quick Fill ratio status ──────────────────────────────────────────

        private struct RatioResult
        {
            public RatioStatus Status;
            public string ActualLabel;  // e.g. "41.8% / 58.2%"
            public string Icon;         // ✓ ⚠ ✗ or ""
        }

        /// <summary>
        /// Computes how closely the active tank contents match this preset's ratio.
        /// The ratio is evaluated within just the preset's own propellants —
        /// other propellants in the tank don't affect the reading.
        /// </summary>
        private RatioResult GetRatioStatus(QuickFillPreset p)
        {
            var active = ActiveTanks();

            // Find loaded volumes for each target propellant.
            var loaded = p.PropTargets
                .Select(pt => active.FirstOrDefault(t => t.name == pt.name))
                .ToList();

            int loadedCount = loaded.Count(t => t != null);

            if (loadedCount == 0)
                return new RatioResult
                {
                    Status = RatioStatus.NotLoaded,
                    ActualLabel = "not loaded",
                    Icon = ""
                };

            if (loadedCount < p.PropTargets.Count)
            {
                var missing = p.PropTargets
                    .Where((pt, i) => loaded[i] == null)
                    .Select(pt => pt.name);
                return new RatioResult
                {
                    Status = RatioStatus.Partial,
                    ActualLabel = "missing: " + string.Join(", ", missing),
                    Icon = "⚠"
                };
            }

            // All propellants present — compute ratio within just this group.
            double mixTotal = loaded.Sum(t => t.Volume);
            if (mixTotal <= 0d)
                return new RatioResult
                {
                    Status = RatioStatus.NotLoaded,
                    ActualLabel = "all empty",
                    Icon = ""
                };

            double maxDev = 0d;
            var parts = new List<string>();
            for (int i = 0; i < p.PropTargets.Count; i++)
            {
                double actual = (loaded[i].Volume / mixTotal) * 100d;
                double target = p.PropTargets[i].targetPct;
                maxDev = Math.Max(maxDev, Math.Abs(actual - target));
                parts.Add(actual.ToString("F1") + "%");
            }

            string label = string.Join(" / ", parts);

            if (maxDev < 1.0d) return new RatioResult { Status = RatioStatus.Good, ActualLabel = label, Icon = "✓" };
            if (maxDev < 5.0d) return new RatioResult { Status = RatioStatus.Warn, ActualLabel = label, Icon = "⚠" };
            return new RatioResult { Status = RatioStatus.Bad, ActualLabel = label, Icon = "✗" };
        }

        // ── Quick Fill ───────────────────────────────────────────────────────

        private void DrawQuickFill(double remaining)
        {
            if (_presets.Count == 0) return;

            // Each row: top line + status line; total height scales with UI scale.
            float rowH = RH(46);
            float panelH = 14f + 10f + _presets.Count * (rowH + 4f) + 10f;
            Rect panel = GUILayoutUtility.GetRect(WindowW, panelH);
            GUI.DrawTexture(panel, _txDark);
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 1), _txDivider);
            float qfLblH = RH(16);
            float qfLblY = panel.y + RH(8);
            GUI.Label(new Rect(panel.x + 16, qfLblY, panel.width - 32, qfLblH),
                "⚡  QUICK FILL — CONSUMER RATIOS", _sSectionLbl);

            float ry = qfLblY + qfLblH + RH(4);
            foreach (var p in _presets)
            {
                DrawQuickFillRow(new Rect(panel.x + 16, ry, panel.width - 32, rowH), p, remaining);
                ry += rowH + 4f;
            }
        }

        private void DrawQuickFillRow(Rect r, QuickFillPreset p, double remaining)
        {
            GUI.DrawTexture(r, _txQfNorm);

            float tagW = 80f;
            float topH = RH(26);
            float botH = r.height - topH;

            // Right-side fill controls: [pct field 44][% 16][gap 6][liters 64] = 130px
            const float pctFieldW = 44f;
            const float pctLblW = 16f;
            const float litGap = 6f;
            const float litW = 64f;
            const float rightW = pctFieldW + pctLblW + litGap + litW;

            float mainW = r.width - tagW - 8f - rightW;  // 8px gap after tag

            // ── Engine tag (full height, left column) ──────────────────────
            Rect tagRect = new Rect(r.x, r.y, tagW, r.height);
            GUI.DrawTexture(tagRect, _txQfTagBg);
            GUI.DrawTexture(new Rect(tagRect.xMax - 1, r.y + 4, 1, r.height - 8), _txDivider);
            GUI.Label(tagRect, p.EngineName, _sQfEngine);

            // ── Combined fuels + percentages label (top line, middle) ──────
            Rect nameRect = new Rect(r.x + tagW + 8f, r.y, mainW, topH);
            GUI.Label(nameRect, p.CombinedLabel, _sQfName);

            // ── Click area: entire row except the pct input field and liters readout ──
            // The full row height is used so the status line at the bottom is also
            // clickable — the pct field and liters are excluded by being outside the
            // rect horizontally (they sit to the right of tagW + gap + mainW).
            Rect fillRect = new Rect(r.x, r.y, tagW + 8f + mainW, r.height);
            if (GUI.Button(fillRect, GUIContent.none, GUIStyle.none))
            {
                if (!_qfFillPctBuf.TryGetValue(p.Key, out string pctStr)) pctStr = "100";
                double pctParsed = 100d;
                double.TryParse(pctStr, out pctParsed);
                double fillFrac = Math.Max(0d, Math.Min(100d, pctParsed)) / 100d;
                DoFillRemaining(p, remaining * fillFrac);
            }
            if (fillRect.Contains(Event.current.mousePosition))
                GUI.DrawTexture(fillRect, _txBtnHov);

            // ── Fill percentage input field (top line, right) ───────────────
            if (!_qfFillPctBuf.ContainsKey(p.Key)) _qfFillPctBuf[p.Key] = "100";

            float rx = r.x + tagW + 8f + mainW;
            GUI.DrawTexture(new Rect(rx, r.y + 1f, pctFieldW, topH - 2f), _txInput);
            string newPct = GUI.TextField(
                new Rect(rx, r.y + 1f, pctFieldW, topH - 2f),
                _qfFillPctBuf[p.Key], _sField);
            if (newPct != _qfFillPctBuf[p.Key]) _qfFillPctBuf[p.Key] = newPct;
            SetTooltip(new Rect(rx, r.y + 1f, pctFieldW, topH - 2f),
                "Percentage of currently available non-reserved space to fill.\n100% fills every free litre with this engine's ratio.");
            rx += pctFieldW;

            GUI.Label(new Rect(rx, r.y, pctLblW, topH), "%", _sUnitLbl);
            rx += pctLblW + litGap;

            // ── Computed fill volume (read-only, updates live as pct changes) ─
            double fillPct = 100d;
            double.TryParse(_qfFillPctBuf[p.Key], out fillPct);
            double fillLitres = remaining * Math.Max(0d, Math.Min(100d, fillPct)) / 100d;
            GUI.Label(new Rect(rx, r.y, litW, topH),
                fillLitres.ToString("F1") + " L", _sQfRatio);

            // ── Status line (bottom) ────────────────────────────────────────
            var rs = GetRatioStatus(p);

            GUI.Label(new Rect(r.x + tagW + 8f, r.y + topH, 30f, botH), "now:", _sQfStatusDim);

            GUIStyle statusStyle;
            if (rs.Status == RatioStatus.Good) statusStyle = _sQfStatusGood;
            else if (rs.Status == RatioStatus.Warn) statusStyle = _sQfStatusWarn;
            else if (rs.Status == RatioStatus.Bad) statusStyle = _sQfStatusBad;
            else statusStyle = _sQfStatusDim;

            GUI.Label(new Rect(r.x + tagW + 40f, r.y + topH, r.width - tagW - 44f, botH),
                rs.ActualLabel, statusStyle);
            SetTooltip(new Rect(r.x + tagW + 8f, r.y + topH, r.width - tagW - 8f, botH),
                "Ratio match for propellants currently in the tank:\n  ✓ Within 1% of target\n  ⚠ Within 5% of target\n  ✗ More than 5% off target\n\nOnly this engine's own propellants are compared.");

            if (rs.Icon.Length > 0)
                GUI.Label(new Rect(r.xMax - 26f, r.y + topH, 22f, botH), rs.Icon, statusStyle);
        }

        // ── Life Support planner ─────────────────────────────────────────────

        private void DrawLifeSupport()
        {
            // Only show the planner when this tank type can actually hold Food.
            FuelTank foodCheck;
            if (!_module.tanksDict.TryGetValue("Food", out foodCheck) || !foodCheck.canHave)
                return;

            // ── Toggle header ────────────────────────────────────────────────
            Rect hdr = GUILayoutUtility.GetRect(WindowW, RH(36));
            GUI.DrawTexture(hdr, _txLsHeaderBg);
            if (hdr.Contains(Event.current.mousePosition))
                GUI.DrawTexture(hdr, _txLsHov);
            GUI.Label(new Rect(hdr.x + 16, hdr.y, hdr.width - 32, hdr.height),
                (_lifeSupportExpanded ? "▼" : "▶") + "  LIFE SUPPORT PLANNER", _sLsHeaderLbl);
            if (GUI.Button(hdr, GUIContent.none, GUIStyle.none))
                _lifeSupportExpanded = !_lifeSupportExpanded;
            SetTooltip(hdr,
                "Life Support Planner\n\nCalculates tank volume needed for Food, Water, and Oxygen based on crew size and mission duration.\n\nValues are added manually using the ADD TO TANK button.");

            if (!_lifeSupportExpanded) return;

            // ── Input row ────────────────────────────────────────────────────
            Rect inputs = GUILayoutUtility.GetRect(WindowW, RH(44));
            GUI.DrawTexture(inputs, _txDark);
            GUI.DrawTexture(new Rect(inputs.x, inputs.yMax - 1, inputs.width, 1), _txDivider);

            float cx = inputs.x + 16f;
            float lsfh = RH(26);  // field height
            float lslh = RH(18);  // label height
            float lsfyO = inputs.y + (inputs.height - lsfh) * 0.5f;
            float lslyO = inputs.y + (inputs.height - lslh) * 0.5f;

            GUI.Label(new Rect(cx, lslyO, 38f, lslh), "Crew", _sUnitLbl);
            cx += 42f;
            GUI.DrawTexture(new Rect(cx, lsfyO, 38f, lsfh), _txInput);
            string nc = GUI.TextField(
                new Rect(cx, lsfyO, 38f, lsfh), _lsCrewBuf, _sField);
            if (nc != _lsCrewBuf) _lsCrewBuf = nc;
            cx += 46f;

            cx += 18f; // gap between crew and days

            GUI.Label(new Rect(cx, lslyO, 38f, lslh), "Days", _sUnitLbl);
            cx += 42f;
            GUI.DrawTexture(new Rect(cx, lsfyO, 54f, lsfh), _txInput);
            string nd = GUI.TextField(
                new Rect(cx, lsfyO, 54f, lsfh), _lsDaysBuf, _sField);
            if (nd != _lsDaysBuf) _lsDaysBuf = nd;

            // ── Parse inputs ─────────────────────────────────────────────────
            double crewD = 0d, days = 0d;
            double.TryParse(_lsCrewBuf, out crewD);
            double.TryParse(_lsDaysBuf, out days);
            double crew = Math.Floor(Math.Max(0d, crewD));  // integer crew count
            days = Math.Max(0d, days);

            // RF units required per resource.
            double rfFood = crew * days * LsFoodPerCrewDay;
            double rfWater = crew * days * LsWaterPerCrewDay;
            double rfOxygen = crew * days * LsOxygenPerCrewDay;

            // Convert RF units → physical litres using each resource's utilization.
            // utilization stores the multiplier (e.g. Food≈1, Water≈1, Oxygen=200),
            // so physicalLitres = rfUnits / utilization.
            FuelTank tF, tW, tO;
            double utilFood = (_module.tanksDict.TryGetValue("Food", out tF) && tF != null && tF.utilization > 0f) ? tF.utilization : 1d;
            double utilWater = (_module.tanksDict.TryGetValue("Water", out tW) && tW != null && tW.utilization > 0f) ? tW.utilization : 1d;
            double utilOxygen = (_module.tanksDict.TryGetValue("Oxygen", out tO) && tO != null && tO.utilization > 0f) ? tO.utilization : 1d;

            double food = rfFood / utilFood;
            double water = rfWater / utilWater;
            double oxygen = rfOxygen / utilOxygen;

            // ── Result rows ──────────────────────────────────────────────────
            DrawLsResultRow("Food", food);
            DrawLsResultRow("Water", water);
            DrawLsResultRow("Oxygen", oxygen);

            // ── Commit row ───────────────────────────────────────────────────
            Rect commit = GUILayoutUtility.GetRect(WindowW, RH(38));
            GUI.DrawTexture(commit, _txDark);
            GUI.DrawTexture(new Rect(commit.x, commit.y, commit.width, 1), _txDivider);
            GUI.DrawTexture(new Rect(commit.x, commit.yMax - 1, commit.width, 1), _txDivider);

            // Button width tracks the scaled text so it never clips at any font scale.
            float btnW = Mathf.Max(RH(120f), _sLsCommit.CalcSize(new GUIContent("✓  ADD TO TANK")).x + RH(20f));
            float commitBh = RH(28);
            Rect btn = new Rect(commit.x + (commit.width - btnW) * 0.5f,
                                  commit.y + (commit.height - commitBh) * 0.5f, btnW, commitBh);
            if (GUI.Button(btn, "✓  ADD TO TANK", _sLsCommit))
                CommitLifeSupport(food, water, oxygen);
            SetTooltip(btn,
                "Add the calculated Food, Water, and Oxygen volumes to the tank.\n\nIf the total exceeds available space, nothing is added and an error is shown.");
        }

        private void DrawLsResultRow(string resourceName, double litres)
        {
            Rect r = GUILayoutUtility.GetRect(WindowW, RH(34));
            GUI.DrawTexture(r, _txRow);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), _txDivider);
            float lh = RH(20);
            float lyO = r.y + (r.height - lh) * 0.5f;

            GUI.Label(new Rect(r.x + 16f, lyO, 110f, lh), resourceName, _sPropName);
            // Amount right-aligned to match the mass column style
            GUI.Label(new Rect(r.x + 130f, lyO, r.width - 146f, lh),
                litres.ToString("F4") + " L", _sMassRow);
        }

        /// <summary>
        /// Add calculated Life Support volumes to the tank. Each resource is added up to the reserve-respecting available space at that
        /// moment, so Food is added first, then Water, then Oxygen — if the tank is nearly full only the resources that fit will be added.
        /// </summary>
        private void CommitLifeSupport(double food, double water, double oxygen)
        {
            // Pre-check total space before touching anything.  AvailableVolume can
            // produce stale reads between sequential CommitLsResource calls (the
            // PartResource system may defer accounting until the next event), so we
            // must guard up-front rather than relying on per-call capping.
            double avail = Math.Max(0d, _module.AvailableVolume - _reserveVolume);
            double totalNeeded = food + water + oxygen;

            if (totalNeeded > avail + 0.0001d)
            {
                _lsPopupMsg =
                    $"Not enough space in the tank.\n\n" +
                    $"Required:  {totalNeeded:F2} L\n" +
                    $"Available: {avail:F2} L\n\n" +
                    $"Reduce crew count, mission duration, or free up tank volume.";
                _lsPopupOpen = true;
                // Centre the popup over the main window (dimensions scale with _uiScale).
                float popW = Mathf.Round(340f * _uiScale);
                float popH = Mathf.Round(220f * _uiScale);  // generous estimate; GUILayout auto-fits height
                _lsPopupRect = new Rect(
                    _winRect.x + (_winRect.width  - popW) * 0.5f,
                    _winRect.y + (_winRect.height - popH) * 0.5f,
                    popW, 0f);
                return;
            }

            bool any = false;
            if (food   > 0.0001d) { CommitLsResource("Food",   food);   any = true; }
            if (water  > 0.0001d) { CommitLsResource("Water",  water);  any = true; }
            if (oxygen > 0.0001d) { CommitLsResource("Oxygen", oxygen); any = true; }
            if (!any) return;

            SyncEditBuffers();
            _module.MarkWindowDirty();
            NotifyEditor();
        }

        /// <summary>
        /// Add <paramref name="litres"/> of <paramref name="name"/> to the tank. Activates the resource if it isn't already loaded.
        /// Capped by available volume (minus reserve).
        /// </summary>
        private void CommitLsResource(string name, double litres)
        {
            FuelTank tank;
            if (!_module.tanksDict.TryGetValue(name, out tank)) return;
            if (!tank.canHave || !tank.fillable) return;

            double avail = Math.Max(0d, _module.AvailableVolume - _reserveVolume);
            double toAdd = Math.Min(litres, avail);
            if (toAdd < 0.0001d) return;

            double newVol = tank.Volume + toAdd;
            tank.maxAmount = newVol * tank.utilization;
            tank.amount = tank.maxAmount;
        }

        // ── Life Support "not enough space" popup ────────────────────────────

        private void DrawLsPopup(int id)
        {
            float W  = Mathf.Round(340f * _uiScale);   // matches GUILayout.Width(…) above
            float px = RH(16f);

            // ── Title bar ────────────────────────────────────────────────────
            float popupHdrH = RH(44f);
            Rect hdr = GUILayoutUtility.GetRect(W, popupHdrH);
            GUI.DrawTexture(hdr, _txHeader);
            GUI.DrawTexture(new Rect(hdr.x, hdr.yMax - 1, hdr.width, 1), _txAccent);
            float hdrLblH = RH(20f);
            GUI.Label(new Rect(hdr.x + px, hdr.y + (hdr.height - hdrLblH) * 0.5f, W - 40f, hdrLblH),
                "LIFE SUPPORT — NOT ENOUGH SPACE", _sSectionLbl);

            GUILayout.Space(6f);

            // ── Message ───────────────────────────────────────────────────────
            float popupMsgH = RH(96f);
            Rect msg = GUILayoutUtility.GetRect(W, popupMsgH);
            GUI.DrawTexture(msg, _txDark);
            var msgStyle = new GUIStyle(_sSectionLbl)
            {
                fontStyle = FontStyle.Normal,
                fontSize  = FS(11),
                wordWrap  = true,
                alignment = TextAnchor.UpperLeft,
            };
            msgStyle.normal.textColor = new Color(0.78f, 0.71f, 0.71f, 1f); // soft red-white
            GUI.Label(new Rect(msg.x + px, msg.y + RH(10f), W - px * 2f, msg.height - RH(14f)),
                _lsPopupMsg, msgStyle);

            GUILayout.Space(4f);

            // ── OK button ─────────────────────────────────────────────────────
            float popupBtnRowH = RH(42f);
            Rect btnRow = GUILayoutUtility.GetRect(W, popupBtnRowH);
            GUI.DrawTexture(btnRow, _txDark);
            GUI.DrawTexture(new Rect(btnRow.x, btnRow.y, btnRow.width, 1), _txDivider);
            float bw  = Mathf.Max(RH(80f), _sBtnApply.CalcSize(new GUIContent("OK")).x + RH(20f));
            float bh  = RH(28f);
            float by  = btnRow.y + (btnRow.height - bh) * 0.5f;
            if (GUI.Button(
                new Rect(btnRow.x + (btnRow.width - bw) * 0.5f, by, bw, bh),
                "OK", _sBtnApply))
                _lsPopupOpen = false;

            GUILayout.Space(4f);

            GUI.DragWindow(new Rect(0, 0, W, popupHdrH));
        }

        // ── Footer ───────────────────────────────────────────────────────────

        private void DrawFooter()
        {
            Rect r = GUILayoutUtility.GetRect(WindowW, RH(38));
            GUI.DrawTexture(r, _txFooter);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1), _txDivider);

            if (r.Contains(Event.current.mousePosition))
                GUI.DrawTexture(r, _txFooterRedHov);

            GUI.Label(r, "REMOVE ALL", _sFooterDanger);

            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                RemoveAll();
        }

        // ── Scale-all bar ────────────────────────────────────────────────────

        private void DrawScaleAllBar()
        {
            Rect panel = GUILayoutUtility.GetRect(WindowW, RH(44));
            GUI.DrawTexture(panel, _txDark);
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 1), _txDivider);
            GUI.DrawTexture(new Rect(panel.x, panel.yMax - 1, panel.width, 1), _txDivider);

            float px = panel.x + 16f;
            float pw = panel.width - 32f;    // usable width between padding
            float bw = (pw - 3f * 4f) / 4f; // four equal buttons, three 4px gaps
            float bh = RH(28);
            float by = panel.y + (panel.height - bh) * 0.5f;
            float gap = 4f;

            if (GUI.Button(new Rect(px, by, bw, bh), "1/4", _sBtnScale)) ScaleAll(0.25d);
            SetTooltip(new Rect(px, by, bw, bh),
                "Set all unlocked resources to ¼ of their current volume.");
            if (GUI.Button(new Rect(px + (bw + gap), by, bw, bh), "1/2", _sBtnScale)) ScaleAll(0.5d);
            SetTooltip(new Rect(px + (bw + gap), by, bw, bh),
                "Set all unlocked resources to ½ of their current volume.");
            if (GUI.Button(new Rect(px + 2f * (bw + gap), by, bw, bh), "2×", _sBtnScale)) ScaleAll(2.0d);
            SetTooltip(new Rect(px + 2f * (bw + gap), by, bw, bh),
                "Double the volume of all unlocked resources, capped at available space.");
            if (GUI.Button(new Rect(px + 3f * (bw + gap), by, bw, bh), "FILL", _sBtnScale)) ScaleAll(1e9d);
            SetTooltip(new Rect(px + 3f * (bw + gap), by, bw, bh),
                "Fill all unlocked resources proportionally to capacity.\nLocked resources are untouched.\nReserve space is respected.");
        }

        // ── RF write-back actions ────────────────────────────────────────────

        /// <summary>
        /// Parse the percentage buffer, convert to litres, apply, and scale others.
        /// Percentage is always relative to the full tank volume.
        /// </summary>
        private void ApplyPct(FuelTank tank, double pctIn)
        {
            double capacity = _module.volume;
            double newLitres = Math.Max(0d, Math.Min(capacity, pctIn / 100d * capacity));
            SetVolumeAndScaleOthers(tank, newLitres);  // MarkWindowDirty called inside
        }

        /// <summary>
        /// Parse the RF-units edit buffer, convert to physical litres, apply, and
        /// scale others if needed.  The edit field stores maxAmount (RF units) so
        /// that players see the same game-native quantity that engine configs and life
        /// support mods use.  Internally we always work in physical litres.
        /// </summary>
        private void ApplyVolume(FuelTank tank)
        {
            if (!_editBuf.TryGetValue(tank.name, out string buf)) return;
            if (!double.TryParse(buf, out double rfUnits)) return;
            rfUnits = Math.Max(0d, rfUnits);
            // Convert RF units → physical litres (utilization is the multiplier).
            double litres = tank.utilization > 0f
                ? rfUnits / (double)tank.utilization
                : rfUnits;
            litres = Math.Max(0d, Math.Min(_module.volume, litres));
            SetVolumeAndScaleOthers(tank, litres);  // MarkWindowDirty called inside
        }

        /// <summary>
        /// Core write-back: set this tank's volume then proportionally shrink other
        /// active tanks only as much as necessary so the total never exceeds capacity.
        /// Locked tanks are shielded from proportional shrinking; they only shrink as
        /// a last resort when the new value + all locked amounts together exceed capacity.
        /// </summary>
        private void SetVolumeAndScaleOthers(FuelTank tank, double newLitres)
        {
            double capacity = _module.volume;
            double remaining = capacity - newLitres;   // space left for all others

            var others = ActiveTanks().Where(t => t.name != tank.name).ToList();
            var lockedOthers = others.Where(t => _lockedAmounts.ContainsKey(t.name)).ToList();
            var unlockedOthers = others.Where(t => !_lockedAmounts.ContainsKey(t.name)).ToList();

            double lockedSum = lockedOthers.Sum(t => t.Volume);
            double unlockedSum = unlockedOthers.Sum(t => t.Volume);

            if (lockedSum + unlockedSum > remaining)
            {
                double spaceForUnlocked = remaining - lockedSum;

                if (spaceForUnlocked < 0d)
                {
                    // Even locked resources alone overflow — scale everything proportionally.
                    double totalOther = lockedSum + unlockedSum;
                    if (totalOther > 0d)
                    {
                        double scale = Math.Max(0d, remaining) / totalOther;
                        foreach (var o in others)
                            o.maxAmount = o.Volume * scale * o.utilization;
                    }
                }
                else if (unlockedSum > 0d)
                {
                    // Locked tanks stay; scale only the unlocked portion down.
                    double scale = spaceForUnlocked / unlockedSum;
                    foreach (var o in unlockedOthers)
                        o.maxAmount = o.Volume * scale * o.utilization;
                }
                // (If unlockedSum == 0 and spaceForUnlocked >= 0, locked fit exactly — no change.)
            }

            // Write the target tank last (RF setter may trigger events).
            tank.maxAmount = newLitres * tank.utilization;
            tank.amount = tank.fillable ? tank.maxAmount : 0d;

            SyncEditBuffers();
            _module.MarkWindowDirty();
            NotifyEditor();
        }

        /// <summary>Zero out the tank — RF's maxAmount setter removes the PartResource.</summary>
        private void RemoveTank(FuelTank tank)
        {
            tank.maxAmount = 0d;  // Calls DeleteTank() internally; resource is gone after this
            _editBuf.Remove(tank.name);
            _pctBuf.Remove(tank.name);
            _lockedAmounts.Remove(tank.name);  // removing an active resource always clears its lock
            _module.MarkWindowDirty();
            NotifyEditor();
        }

        /// <summary>
        /// Activate a previously-empty tank, filling <paramref name="fillFrac"/> of the
        /// remaining available volume (1.0 = 100%).  Defaults to full fill.
        /// </summary>
        private void AddTank(FuelTank tank, double fillFrac = 1.0)
        {
            double avail = Math.Max(0d, _module.AvailableVolume - _reserveVolume);
            if (avail < 0.001d) return;
            // fillFrac is relative to total tank capacity (same basis as the pct field in
            // DrawAvailRow), so two resources set to 5% each always contribute 5% of the
            // total regardless of add order.  Cap at physAvail to prevent overfill.
            double fill = Math.Min(_module.volume * Math.Max(0d, Math.Min(1d, fillFrac)), avail);
            tank.maxAmount = fill * tank.utilization;
            tank.amount = tank.fillable ? tank.maxAmount : 0d;
            // Store RF units in the edit buffer — consistent with DrawCurrentRow display.
            _editBuf[tank.name] = tank.maxAmount.ToString("F4");
            _module.MarkWindowDirty();
            NotifyEditor();
        }

        /// <summary>
        /// Fills the remaining tank volume with this engine's propellant ratios.
        /// Activates any preset propellants not yet in the tank, then distributes
        /// the available space proportionally across all preset propellants.
        /// </summary>
        private void DoFillRemaining(QuickFillPreset p, double remaining)
        {
            if (remaining < 0.1d) return;

            // Collect ALL propellants this preset wants that this tank type can hold.
            var targets = new List<KeyValuePair<FuelTank, double>>();
            double sum = 0d;
            foreach (var pt in p.PropTargets)
            {
                FuelTank tank;
                if (!_module.tanksDict.TryGetValue(pt.name, out tank)) continue;
                if (!tank.canHave || !tank.fillable) continue;
                targets.Add(new KeyValuePair<FuelTank, double>(tank, pt.targetPct));
                sum += pt.targetPct;
            }

            if (targets.Count == 0 || sum <= 0d) return;

            // Distribute remaining volume among preset propellants by their ratio.
            foreach (var kv in targets)
            {
                double addVol = remaining * (kv.Value / sum);
                kv.Key.maxAmount = (kv.Key.Volume + addVol) * kv.Key.utilization;
                kv.Key.amount = kv.Key.maxAmount;
            }

            SyncEditBuffers();
            _module.MarkWindowDirty();
            NotifyEditor();
        }

        /// <summary>Zero every active tank without consuming new resource ID slots.</summary>
        private void RemoveAll()
        {
            foreach (var tank in ActiveTanks())
                tank.maxAmount = 0d;   // DeleteTank — slot freed, no new slot consumed
            _editBuf.Clear();
            _pctBuf.Clear();
            _module.MarkWindowDirty();
            NotifyEditor();
        }

        /// <summary>Remove half the volume from a single active tank.</summary>
        private void HalfTank(FuelTank tank)
        {
            tank.maxAmount = (tank.Volume * 0.5d) * tank.utilization;
            tank.amount = tank.fillable ? tank.maxAmount : 0d;
            SyncEditBuffers();
            _module.MarkWindowDirty();
            NotifyEditor();
        }

        /// <summary>Double a single tank's volume, capped by available space (respects reserve).</summary>
        private void DoubleTank(FuelTank tank)
        {
            double maxAllowed = tank.Volume + Math.Max(0d, _module.AvailableVolume - _reserveVolume);
            double newVol = Math.Min(tank.Volume * 2d, maxAllowed);
            tank.maxAmount = newVol * tank.utilization;
            tank.amount = tank.fillable ? tank.maxAmount : 0d;
            SyncEditBuffers();
            _module.MarkWindowDirty();
            NotifyEditor();
        }

        /// <summary>
        /// Scale active tanks by <paramref name="factor"/>, respecting the lock toggles.
        /// Locked tanks keep their current volume unless the effective capacity is too
        /// small to contain even the locked portion alone — in that case everything is
        /// scaled proportionally (the "tank got too small" fallback).
        /// Pass 1e9 for FULL (any value large enough to always hit the cap).
        /// </summary>
        private void ScaleAll(double factor)
        {
            var active = ActiveTanks();
            if (active.Count == 0) return;

            // Effective capacity respects reserve — FULL fills to capacity − reserve, not 100%.
            double effectiveCap = Math.Max(0d, _module.volume - _reserveVolume);

            var locked = active.Where(t => _lockedAmounts.ContainsKey(t.name)).ToList();
            var unlocked = active.Where(t => !_lockedAmounts.ContainsKey(t.name)).ToList();

            double lockedVol = locked.Sum(t => t.Volume);
            double unlockedVol = unlocked.Sum(t => t.Volume);

            if (lockedVol >= effectiveCap)
            {
                // Tank is too small even for the locked portion alone — scale everything
                // proportionally so at least the ratio between resources is preserved.
                double total = lockedVol + unlockedVol;
                if (total <= 0d) return;
                foreach (var tank in active)
                {
                    double newVol = effectiveCap * (tank.Volume / total);
                    tank.maxAmount = newVol * tank.utilization;
                    tank.amount = tank.fillable ? tank.maxAmount : 0d;
                }
            }
            else
            {
                // Locked tanks are untouched; scale only the unlocked ones.
                double remaining = effectiveCap - lockedVol;

                if (unlocked.Count == 0 || unlockedVol <= 0d)
                {
                    // Nothing to scale.
                }
                else
                {
                    double target = Math.Min(unlockedVol * factor, remaining);
                    target = Math.Max(0d, target);
                    foreach (var tank in unlocked)
                    {
                        double newVol = target * (tank.Volume / unlockedVol);
                        tank.maxAmount = newVol * tank.utilization;
                        tank.amount = tank.fillable ? tank.maxAmount : 0d;
                    }
                }
            }

            SyncEditBuffers();
            _module.MarkWindowDirty();
            NotifyEditor();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>Tanks that are currently active (maxAmount > 0).</summary>
        private List<FuelTank> ActiveTanks() =>
            _module.tanksDict.Values.Where(t => t.maxAmount > 0d).ToList();

        /// <summary>
        /// Rebuild quick-fill presets from the engines that reference this tank.
        /// RF populates module.usedBy whenever an engine on the vessel wants to
        /// draw from this tank.
        /// </summary>
        private void RebuildPresets()
        {
            _presets.Clear();
            if (_module?.usedBy == null) return;

            // Deduplicate by engine title so twin engines don't show twice.
            var seen = new HashSet<string>();
            foreach (var fi in _module.usedBy.Values)
            {
                if (fi == null || !fi.valid) continue;
                if (!seen.Add(fi.title ?? "")) continue;
                if (fi.propellantVolumeMults.Count < 1) continue;
                _presets.Add(new QuickFillPreset(fi));
            }
        }

        /// <summary>
        /// After RF writes new amounts (e.g. after ConfigureFor), pull the
        /// updated volumes and percentages back into the edit buffers.
        /// </summary>
        private void SyncEditBuffers()
        {
            double capacity = _module.volume;
            foreach (var tank in _module.tanksDict.Values.Where(t => t.maxAmount > 0d))
            {
                // Edit field stores RF units (maxAmount) — same units the player typed.
                _editBuf[tank.name] = tank.maxAmount.ToString("F4");
                // Pct field stores physical-space percentage so it aligns with the volume bar.
                double pct = capacity > 0d ? (tank.Volume / capacity) * 100d : 0d;
                _pctBuf[tank.name] = pct.ToString("F2");
            }
        }

        /// <summary>
        /// Queue an onEditorShipModified notification to fire next Update() frame.
        /// Firing it directly from OnGUI can corrupt Unity's UI layout pool.
        /// </summary>
        private void NotifyEditor()
        {
            _pendingNotify = true;
        }

        // ── Tooltip helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Set the pending tooltip text if the mouse is currently inside <paramref name="r"/>.
        /// Call once per control, after drawing it.  The last caller whose rect contains the
        /// mouse pointer wins, which naturally gives topmost/last-drawn controls priority.
        /// </summary>
        private void SetTooltip(Rect r, string text)
        {
            if (r.Contains(Event.current.mousePosition))
                _tooltipText = text;
        }

        /// <summary>
        /// Render a floating tooltip near the cursor using whatever text was set this frame
        /// by <see cref="SetTooltip"/>.  Called at the very end of DrawWindow so it draws
        /// on top of every other element.  Automatically clamps to the window bounds.
        /// </summary>
        private void DrawTooltip()
        {
            if (string.IsNullOrEmpty(_tooltipText) || _sTooltip == null) return;

            GUIContent content = new GUIContent(_tooltipText);
            float maxW = RH(280);
            float w = Mathf.Min(maxW, _sTooltip.CalcSize(content).x + 16f);
            float h = _sTooltip.CalcHeight(content, w - 12f) + 14f;

            Vector2 mp = Event.current.mousePosition;
            float tx = mp.x + 16f;
            float ty = mp.y + 16f;

            // Flip to keep within the window bounds.
            if (tx + w > WindowW - 4f) tx = mp.x - w - 4f;
            if (ty + h > _targetH - 4f) ty = mp.y - h - 4f;
            tx = Mathf.Clamp(tx, 2f, WindowW - w - 2f);
            ty = Mathf.Clamp(ty, 2f, _targetH - h - 2f);

            Rect tip = new Rect(tx, ty, w, h);

            GUI.DrawTexture(tip, _txDark);
            // Accent-colour border on all four sides.
            GUI.DrawTexture(new Rect(tip.x,         tip.y,         tip.width, 1f), _txAccent);
            GUI.DrawTexture(new Rect(tip.x,         tip.yMax - 1f, tip.width, 1f), _txAccent);
            GUI.DrawTexture(new Rect(tip.x,         tip.y,         1f, tip.height), _txAccent);
            GUI.DrawTexture(new Rect(tip.xMax - 1f, tip.y,         1f, tip.height), _txAccent);

            GUI.Label(new Rect(tip.x + 6f, tip.y + 6f, w - 12f, h - 12f), _tooltipText, _sTooltip);
        }

        // ── Style / texture initialisation ───────────────────────────────────

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _txBg = T(C("#191e2a"));
            _txHeader = T(C("#252d40"));
            _txDark = T(C("#161b26"));
            _txRow = T(C("#1e2430"));
            _txBarTrack = T(C("#0d1018"));
            _txBarBlue = T(C("#2a5fa8"));
            _txPctBlue = T(C("#4a7cc7"));
            _txInput = T(C("#0d1018"));
            _txBorder = T(C("#2d3547"));
            _txBtnHov = T(new Color(0.290f, 0.486f, 0.780f, 0.15f));
            _txBtnRed = T(new Color(0.780f, 0.290f, 0.290f, 0.15f));
            _txAddNorm = T(new Color(0.290f, 0.486f, 0.780f, 0.08f));
            _txAddHov = T(new Color(0.290f, 0.486f, 0.780f, 0.22f));
            _txQfNorm = T(C("#1a2236"));
            _txQfHov = T(C("#1e2a40"));
            _txClear = T(new Color(0, 0, 0, 0));
            _txFooter = T(C("#131720"));
            _txFooterHov = T(new Color(1, 1, 1, 0.10f));
            _txFooterRedHov = T(new Color(0.780f, 0.290f, 0.290f, 0.18f));
            _txDivider = T(C("#252d40"));
            _txAccent = T(C("#4a7cc7"));
            _txAvailRowDivider = T(C("#1a2030"));
            _txQfTagBg = T(new Color(0.290f, 0.486f, 0.780f, 0.10f));
            // Amber-gold tones for reserve (reserved empty space) — warm contrast against
            // the cool blue-grey of regular propellant rows and the blue accent bar.
            _txReserveRow = T(C("#1e1a0a")); // very dark amber row background
            _txBarReserve = T(C("#7a6418")); // medium amber fill for volume bar

            // Life Support header — solid green background, dark overlay on hover
            _txLsHeaderBg = T(C("#72e0a0"));
            _txLsHov = T(new Color(0f, 0f, 0f, 0.12f));

            // Padlock icons — white on transparent, tinted at draw time via GUI.color.
            _txIconLockClosed = MakeLockIcon(closed: true);
            _txIconLockOpen = MakeLockIcon(closed: false);

            // Text colours — all target ≥ 7:1 contrast on the darkest background (#161b26).
            var cText = C("#c8d0e0");   // primary text
            var cTextSec = C("#c0ccde");   // secondary text
            var cTextDim = C("#c0ccde");   // alias of cTextSec
            var cBlue = C("#a8ccf4");   // blue tint
            var cBlueT = C("#c4e0ff");   // light blue highlight
            var cGold = C("#f8c848");   // gold / amber
            var cRedT = C("#ffb8b8");   // danger / warning

            _sWindow = Sty(GUI.skin.window);
            _sWindow.padding = new RectOffset(0, 0, 0, 0);
            _sWindow.border = new RectOffset(2, 2, 2, 2);
            // contentOffset.y defaults to ~17 (title-bar offset) on GUI.skin.window.
            // Without zeroing it the content area is 17px shorter than _winRect.height,
            // which clips the footer and silently swallows its click events.
            _sWindow.contentOffset = Vector2.zero;
            SetBg(_sWindow, _txBg);

            _sTitle = Lbl(FS(14), FontStyle.Bold, cText);
            _sSubtitle = Lbl(FS(12), FontStyle.Bold, cTextSec);

            _sSectionLbl = Lbl(FS(10), FontStyle.Bold, cTextSec);
            _sSectionLbl.normal.textColor = cTextSec;

            _sCountBadge = Lbl(FS(10), FontStyle.Bold, cBlue);
            _sCountBadge.alignment = TextAnchor.MiddleCenter;

            _sMassLbl = Lbl(FS(10), FontStyle.Normal, cTextSec);
            _sMassVal = Lbl(FS(13), FontStyle.Bold, cText);
            _sMassRow = Lbl(FS(11), FontStyle.Normal, cTextSec);
            _sMassRow.alignment = TextAnchor.MiddleRight;

            _sPropName = Lbl(FS(13), FontStyle.Bold, cText);
            _sPropName.alignment = TextAnchor.MiddleLeft;

            // Reserve row name — italic to visually signal it's a virtual/reserved entry
            _sPropNameReserve = Lbl(FS(13), FontStyle.Italic, cTextSec);
            _sPropNameReserve.alignment = TextAnchor.MiddleLeft;

            _sPctLbl = Lbl(FS(11), FontStyle.Normal, cBlue);
            _sPctLbl.alignment = TextAnchor.MiddleRight;

            _sUnitLbl = Lbl(FS(11), FontStyle.Normal, cTextSec);
            _sUnitLbl.alignment = TextAnchor.MiddleLeft;

            _sField = Sty(GUI.skin.textField);
            _sField.fontSize = FS(12);
            _sField.alignment = TextAnchor.MiddleRight;
            _sField.normal.textColor = cBlueT;
            _sField.focused.textColor = cBlueT;

            // Amount field variant for high-utilization resources — bold green text
            // matches the visual signal used for the kg/L label on the same row.
            _sFieldHighUtil = Sty(GUI.skin.textField);
            _sFieldHighUtil.fontSize = FS(12);
            _sFieldHighUtil.fontStyle = FontStyle.Bold;
            _sFieldHighUtil.alignment = TextAnchor.MiddleRight;
            _sFieldHighUtil.normal.textColor = C("#72e0a0");
            _sFieldHighUtil.focused.textColor = C("#72e0a0");
            _sField.normal.background = _txInput;
            _sField.focused.background = _txInput;
            _sField.hover.background = _txInput;

            _sBtnApply = Btn(C("#72e0a0"), cBlueT, _txBorder, _txBtnHov, FS(14), 24, 28); // green ✓
            _sBtnRemove = Btn(cRedT, cRedT, _txBorder, _txBtnRed, FS(14), 24, 28); // red ✕
            _sBtnHalf = Btn(cTextSec, cText, _txBorder, _txBtnHov, FS(11), 24, 28); // 1/2 / 2×
            // Life Support commit — same green as ✓ but no fixed width (explicit Rect controls size)
            _sLsCommit = Btn(C("#72e0a0"), cBlueT, _txBorder, _txBtnHov, FS(14), -1, 28);
            _sLsCommit.fontStyle = FontStyle.Bold;

            // Life Support header label — dark forest green on the bright green header background.
            // #0f1a12 gives ~12:1 contrast against #72e0a0.
            _sLsHeaderLbl = Lbl(FS(14), FontStyle.Bold, C("#0f1a12"));
            _sLsHeaderLbl.normal.textColor = C("#0f1a12");

            // Lock toggle — ○ (unlocked, dim) / ● (locked, amber)
            _sBtnLock = Btn(cTextSec, cGold, _txBorder, _txBtnHov, FS(14), 24, 28);
            _sBtnLockOn = Btn(cGold, cGold, _txBorder, _txBtnHov, FS(14), 24, 28);
            _sBtnLockOn.fontStyle = FontStyle.Bold;
            _sBtnHalf.fontStyle = FontStyle.Bold;
            _sBtnScale = Btn(cTextSec, cText, _txBorder, _txBtnHov, FS(11), -1, 28); // bulk scale
            _sBtnScale.fontStyle = FontStyle.Bold;

            _sStar = Btn(cTextSec, cGold, _txClear, _txClear, FS(16), 24, 24);
            _sStarOn = Btn(cGold, new Color(cGold.r, cGold.g, cGold.b, 0.7f), _txClear, _txClear, FS(16), 24, 24);

            _sAvailName = Lbl(FS(13), FontStyle.Bold, cText);
            _sAvailName.alignment = TextAnchor.MiddleLeft;

            _sFavBadge = Lbl(FS(9), FontStyle.Bold, cGold);
            _sFavBadge.alignment = TextAnchor.MiddleCenter;

            _sBtnAdd = Btn(cBlue, cBlueT, _txAddNorm, _txAddHov, FS(11), 54, 24);
            _sBtnAdd.fontStyle = FontStyle.Bold;

            _sQfEngine = Lbl(FS(10), FontStyle.Bold, cBlue);
            _sQfEngine.alignment = TextAnchor.MiddleCenter;

            _sQfName = Lbl(FS(12), FontStyle.Bold, cText);
            _sQfName.alignment = TextAnchor.MiddleLeft;

            _sQfRatio = Lbl(FS(11), FontStyle.Normal, cTextSec);
            _sQfRatio.alignment = TextAnchor.MiddleRight;

            // Variant used when the resource has utilization > 1 (e.g. gases stored at
            // high pressure): bold green to signal the litre figure is especially favourable.
            _sQfRatioHighUtil = Lbl(FS(11), FontStyle.Bold, C("#72e0a0"));
            _sQfRatioHighUtil.alignment = TextAnchor.MiddleRight;

            _sQfMain = Btn(cTextSec, cText, _txQfNorm, _txQfHov, FS(12), -1, 32);
            _sQfMain.alignment = TextAnchor.MiddleLeft;
            _sQfMain.padding = new RectOffset(8, 8, 0, 0);

            // Quick Fill status line styles
            _sQfStatusGood = Lbl(FS(10), FontStyle.Bold, C("#72e0a0"));
            _sQfStatusWarn = Lbl(FS(10), FontStyle.Bold, C("#f8c848"));
            _sQfStatusBad  = Lbl(FS(10), FontStyle.Bold, C("#ffb8b8"));
            _sQfStatusDim  = Lbl(FS(10), FontStyle.Normal, cTextSec);

            // "FULL" indicator in the Available list — amber, bold, centred so it
            // sits at the same visual baseline as the pct field and liters readout.
            _sAvailFull = Lbl(FS(10), FontStyle.Bold, C("#f8c848"));
            _sAvailFull.alignment = TextAnchor.MiddleCenter;

            // Footer label styles — backgrounds are drawn manually in DrawFooter so
            // that the invisible GUIStyle.none hit buttons work reliably.
            _sFooter = new GUIStyle(GUI.skin.label)
            {
                fontSize = FS(12),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _sFooter.normal.textColor = cText;

            _sFooterDanger = new GUIStyle(_sFooter);
            _sFooterDanger.normal.textColor = cRedT;

            // Search bar
            _sSearch = Sty(GUI.skin.textField);
            _sSearch.fontSize = FS(12);
            _sSearch.alignment = TextAnchor.MiddleLeft;
            _sSearch.normal.textColor = cText;
            _sSearch.focused.textColor = cText;
            _sSearch.normal.background = _txClear;  // background drawn manually
            _sSearch.focused.background = _txClear;
            _sSearch.hover.background = _txClear;
            _sSearch.padding = new RectOffset(2, 2, 0, 0);

            _sSearchPlaceholder = Lbl(FS(12), FontStyle.Normal, cTextSec);
            _sSearchPlaceholder.alignment = TextAnchor.MiddleLeft;

            _sSearchClear = Btn(cTextDim, cRedT, _txClear, _txClear, FS(12), 24, 20);
            _sSearchClear.fontSize = FS(11);

            // ── Settings panel styles ──────────────────────────────────────────
            _sGearBtn = new GUIStyle(GUI.skin.button)
            {
                fontSize    = FS(16),
                fontStyle   = FontStyle.Normal,
                alignment   = TextAnchor.MiddleCenter,
                fixedWidth  = 30f,
                fixedHeight = 30f,
            };
            _sGearBtn.normal.background  = _txClear;
            _sGearBtn.hover.background   = _txBtnHov;
            _sGearBtn.active.background  = _txBtnHov;
            _sGearBtn.normal.textColor   = C("#c8d0e0");
            _sGearBtn.hover.textColor    = Color.white;
            _sGearBtn.active.textColor   = Color.white;

            _sSettingsValue = Lbl(FS(11), FontStyle.Bold, C("#a8ccf4"));
            _sSettingsValue.alignment = TextAnchor.MiddleRight;

            _sSettingsClose = Btn(C("#c8d0e0"), Color.white, _txBorder, _txBtnHov, FS(12), -1, 28);

            _txSettingsIcon = MakeSettingsIcon();

            _sTooltip = new GUIStyle(GUI.skin.label)
            {
                fontSize  = FS(14),
                fontStyle = FontStyle.Normal,
                wordWrap  = true,
                alignment = TextAnchor.UpperLeft,
            };
            _sTooltip.normal.textColor = C("#c8d0e0");
        }

        private void DestroyTextures()
        {
            var textures = new[] {
                _txBg, _txHeader, _txDark, _txRow, _txBarTrack, _txBarBlue,
                _txPctBlue, _txInput, _txBorder, _txBtnHov, _txBtnRed,
                _txAddNorm, _txAddHov, _txQfNorm, _txQfHov,
                _txClear, _txFooter, _txFooterHov, _txFooterRedHov,
                _txDivider, _txAccent,
                _txAvailRowDivider, _txQfTagBg,
                _txReserveRow, _txBarReserve,
                _txIconLockClosed, _txIconLockOpen,
                _txLsHeaderBg, _txLsHov,
                _txSettingsIcon
            };
            foreach (var t in textures) if (t != null) Destroy(t);
        }

        // ── Style / texture micro-helpers ────────────────────────────────────

        private static Texture2D T(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            t.SetPixel(0, 0, c); t.Apply(); return t;
        }

        private static Color C(string hex)
        {
            hex = hex.TrimStart('#');
            float r = Convert.ToInt32(hex.Substring(0, 2), 16) / 255f;
            float g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255f;
            float b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
            float a = hex.Length >= 8 ? Convert.ToInt32(hex.Substring(6, 2), 16) / 255f : 1f;
            return new Color(r, g, b, a);
        }

        private static GUIStyle Sty(GUIStyle src) => new GUIStyle(src);

        private static void SetBg(GUIStyle s, Texture2D tx)
        { s.normal.background = tx; s.onNormal.background = tx; }

        private static GUIStyle Lbl(int size, FontStyle style, Color col)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = style };
            s.normal.textColor = col;
            return s;
        }

        private static GUIStyle Btn(Color norm, Color hov,
                                    Texture2D normBg, Texture2D hovBg,
                                    int size, int w, int h)
        {
            var s = new GUIStyle(GUI.skin.button)
            {
                fontSize = size,
                alignment = TextAnchor.MiddleCenter,
            };
            if (w > 0) s.fixedWidth = w;
            if (h > 0) s.fixedHeight = h;
            s.normal.textColor = norm;
            s.hover.textColor = hov;
            s.active.textColor = hov;
            s.normal.background = normBg;
            s.hover.background = hovBg;
            s.active.background = hovBg;
            return s;
        }

        /// <summary>
        /// Generates a small padlock icon as a white-on-transparent Texture2D.
        /// Tint it at draw time with GUI.color to match any colour scheme.
        /// Layout (12 × 16, texture y=0 is bottom):
        ///   Rows  0–7  : rectangular body (full width)
        ///   Rows  7–13 : shackle side walls (2 px thick each side)
        ///   Rows 13–15 : arch top connecting both walls
        ///   Open variant: right wall starts 3 rows higher (shackle unclipped).
        /// </summary>
        private static Texture2D MakeLockIcon(bool closed)
        {
            const int W = 12, H = 16;
            var px = new Color[W * H]; // all transparent by default

            void P(int x, int y)
            {
                if (x >= 0 && x < W && y >= 0 && y < H)
                    px[y * W + x] = Color.white;
            }

            // ── Body (bottom 8 rows) ─────────────────────────────────────
            for (int x = 0; x < W; x++)
                for (int y = 0; y <= 7; y++)
                    P(x, y);

            // ── Shackle left wall (always from body top) ─────────────────
            for (int y = 7; y <= 13; y++) { P(1, y); P(2, y); P(3, y); }

            // ── Shackle right wall ────────────────────────────────────────
            // closed → same height as left (shackle fully seated in body)
            // open   → starts 3 rows higher (shackle pulled out on one side)
            int rStart = closed ? 7 : 10;
            for (int y = rStart; y <= 13; y++) { P(8, y); P(9, y); P(10, y); }

            // ── Arch top (connects both walls) ───────────────────────────
            for (int x = 1; x <= 10; x++) { P(x, 13); P(x, 14); P(x, 15); }

            var tex = new Texture2D(W, H, TextureFormat.ARGB32, false);
            tex.filterMode = FilterMode.Point;   // keep pixels crisp, no blur
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Generates a 16×16 "three sliders" settings icon as a white-on-transparent
        /// Texture2D.  Tint at draw time via GUI.color.
        /// Layout (y=0 is bottom row):
        ///   Row  2– 3 : full-width bar; knob (4×4 square) at left (x 1–4)
        ///   Row  7– 8 : full-width bar; knob at centre (x 6–9)
        ///   Row 12–13 : full-width bar; knob at right (x 11–14)
        /// </summary>
        private static Texture2D MakeSettingsIcon()
        {
            const int W = 16, H = 16;
            var px = new Color[W * H];

            void Bar(int y0, int y1, int x0 = 0, int x1 = W - 1)
            {
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                        if (x >= 0 && x < W && y >= 0 && y < H)
                            px[y * W + x] = Color.white;
            }

            // Three horizontal bars with square knobs at different x positions
            Bar(2, 3);  Bar(1, 4, 1, 4);    // top bar + left knob
            Bar(7, 8);  Bar(6, 9, 6, 9);    // middle bar + centre knob
            Bar(12, 13); Bar(11, 14, 11, 14); // bottom bar + right knob

            var tex = new Texture2D(W, H, TextureFormat.ARGB32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode   = TextureWrapMode.Clamp;
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
