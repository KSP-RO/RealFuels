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
                _instance._editBuf.Clear();
                _instance._pctBuf.Clear();
                _instance._pendingNotify = false;
                _instance._reserveVolume = 0d;
                _instance._lockedAmounts.Clear();
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
            _instance._visible = false;
            _instance._module = null;
            EditorLogic.fetch?.Unlock(LockID);
        }

        /// <summary>Close if the window is currently showing this module.</summary>
        public static void HideGUIForModule(ModuleFuelTanks module)
        {
            if (_instance?._module == module) HideGUI();
        }

        // ── Constants ────────────────────────────────────────────────────────
        private const string LockID = "RFWindowLock";
        private const int WindowID = 0x52465748; // "RFWH" — unique across KSP addons
        private const float WindowW = 480f;   // +18px for wider vol field (F4), +2px extra margin

        // Life Support — daily resource requirements per crew member in RF units/day.
        // Physical litres consumed = rfUnits / tank.utilization (e.g. Oxygen util = 200,
        // so 591.84 RF units ÷ 200 = 2.9592 L of physical tank space per crew per day).
        // Source: USI Life Support / TAC-LS consensus values used in RP-1.
        private const double LsFoodPerCrewDay = 5.84928;
        private const double LsWaterPerCrewDay = 3.87072;
        private const double LsOxygenPerCrewDay = 591.84;

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
        private Rect _winRect = new Rect(120, 80, WindowW, 10);
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
        private GUIStyle _sQfEngine, _sQfName, _sQfRatio, _sQfRatioHighUtil, _sQfMain;
        private GUIStyle _sQfStatusGood, _sQfStatusWarn, _sQfStatusBad, _sQfStatusDim;
        private GUIStyle _sAvailFull;
        private GUIStyle _sFooter, _sFooterDanger;
        private GUIStyle _sMassLbl, _sMassVal;
        private GUIStyle _sMassRow;
        private GUIStyle _sSearch, _sSearchPlaceholder, _sSearchClear;
        private GUIStyle _sLsCommit, _sLsHeaderLbl;
        private GUIStyle _sBtnLock, _sBtnLockOn;  // lock toggle: dim when off, amber when on

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

            // Keep the ClickThruBlocker rect in sync with the window every frame.
            _ctbRect = _winRect;

            // Editor cursor lock: prevent part-picking while mouse is over window.
            _overWindow = _winRect.Contains(Event.current.mousePosition);
            if (_overWindow) EditorLogic.fetch?.Lock(true, true, true, LockID);
            else EditorLogic.fetch?.Unlock(LockID);

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

            GUI.DragWindow(new Rect(0, 0, WindowW, 64));
        }

        // ── Header ───────────────────────────────────────────────────────────

        private void DrawHeader()
        {
            Rect r = GUILayoutUtility.GetRect(WindowW, 64);
            GUI.DrawTexture(r, _txHeader);
            GUI.DrawTexture(new Rect(r.x + 50, r.yMax - 2, r.width - 100, 2), _txAccent);
            GUI.Label(new Rect(r.x + 16, r.y + 10, r.width - 32, 22),
                _module.part.partInfo.title.ToUpper(), _sTitle);
            GUI.Label(new Rect(r.x + 16, r.y + 36, r.width - 32, 20),
                (_module.type ?? "PROPELLANT CONFIGURATION").ToUpper(), _sSubtitle);
        }

        // ── Volume bar ───────────────────────────────────────────────────────

        private void DrawVolumeBar(double remaining, double total, float usedFrac)
        {
            Rect panel = GUILayoutUtility.GetRect(WindowW, 56);   // extra 10 px for text headroom
            GUI.DrawTexture(panel, _txDark);

            float px = panel.x + 16, pw = panel.width - 32;

            GUI.Label(new Rect(px, panel.y + 8, 110, 20), "TANK VOLUME", _sSectionLbl);

            var remStyle = new GUIStyle(_sSectionLbl)
            {
                fontStyle = FontStyle.Normal,
                fontSize = 12,
            };
            remStyle.normal.textColor = C("#72e0a0");   // 7.2:1 on dark bg
            GUI.Label(new Rect(px + 112, panel.y + 7, pw - 112, 20),   // height 20 avoids clip
                remaining.ToString("F1") + " / " + total.ToString("F1") + " L remaining",
                remStyle);

            Rect track = new Rect(px, panel.y + 38, pw, 8);   // bar moved down to match taller panel
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

            Rect panel = GUILayoutUtility.GetRect(WindowW, 46);
            GUI.DrawTexture(panel, _txDark);

            float cw = panel.width / 3f;
            MassCell(new Rect(panel.x, panel.y, cw, panel.height),
                "WET MASS", wet.ToString("F3") + " t");
            MassCell(new Rect(panel.x + cw, panel.y, cw, panel.height),
                "DRY MASS", dry.ToString("F3") + " t");
            MassCell(new Rect(panel.x + cw * 2, panel.y, cw, panel.height),
                "PROPELLANT", (wet - dry).ToString("F3") + " t");

            GUI.DrawTexture(new Rect(panel.x + cw, panel.y + 6, 1, panel.height - 12), _txDivider);
            GUI.DrawTexture(new Rect(panel.x + cw * 2, panel.y + 6, 1, panel.height - 12), _txDivider);
            GUI.DrawTexture(new Rect(panel.x, panel.yMax - 1, panel.width, 1), _txDivider);
        }

        private void MassCell(Rect r, string label, string value)
        {
            GUI.Label(new Rect(r.x + 16, r.y + 6, r.width - 20, 16), label, _sMassLbl);
            GUI.Label(new Rect(r.x + 16, r.y + 22, r.width - 20, 20), value, _sMassVal);
        }

        // ── Section header ───────────────────────────────────────────────────

        private void DrawSectionHeader(string label, string badge)
        {
            Rect r = GUILayoutUtility.GetRect(WindowW, 26);
            GUI.Label(new Rect(r.x + 16, r.y + 5, 80, 16), label, _sSectionLbl);

            float lineW = badge != null ? r.width - 210 : r.width - 116;
            GUI.DrawTexture(new Rect(r.x + 100, r.y + 12, lineW, 1), _txDivider);

            if (badge != null)
                GUI.Label(new Rect(r.x + r.width - 106, r.y + 4, 90, 18), badge, _sCountBadge);
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

            Rect r = GUILayoutUtility.GetRect(WindowW, 44);
            GUI.DrawTexture(r, _txRow);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), _txDivider);

            float cx = r.x + 16f;

            // ── Name ──
            GUI.Label(new Rect(cx, r.y + 11f, 80f, 22f), tank.name, _sPropName);
            cx += 84f;

            // ── Percentage field ──
            GUI.DrawTexture(new Rect(cx, r.y + 8f, 44f, 26f), _txInput);
            string newPct = GUI.TextField(
                new Rect(cx, r.y + 8f, 44f, 26f), _pctBuf[tank.name], _sField);
            if (newPct != _pctBuf[tank.name])
                _pctBuf[tank.name] = newPct;
            cx += 46f;

            GUI.Label(new Rect(cx, r.y + 13f, 14f, 18f), "%", _sUnitLbl);
            cx += 16f;

            // ── Volume field (RF units — maxAmount, not physical litres) ──
            // Wider than before (90px): high-utilization values like Oxygen's 591.84
            // need more room.  No "L" suffix — these are game resource units, not litres.
            GUI.DrawTexture(new Rect(cx, r.y + 8f, 90f, 26f), _txInput);
            string newVol = GUI.TextField(
                new Rect(cx, r.y + 8f, 90f, 26f), _editBuf[tank.name], _sField);
            if (newVol != _editBuf[tank.name])
                _editBuf[tank.name] = newVol;
            cx += 94f;  // 90 field + 4 gap

            // ── Apply ✓ (green) ──
            if (GUI.Button(new Rect(cx, r.y + 8f, 24f, 28f), "✓", _sBtnApply))
            {
                // Prefer percentage if it differs from the live value; else use volume.
                if (double.TryParse(_pctBuf[tank.name], out double pctIn) &&
                    Math.Abs(pctIn - pctOfTotal) > 0.05d)
                    ApplyPct(tank, pctIn);
                else
                    ApplyVolume(tank);
            }
            cx += 28f;

            // ── Remove ✕ (red) ──
            if (GUI.Button(new Rect(cx, r.y + 8f, 24f, 28f), "✕", _sBtnRemove))
                RemoveTank(tank);
            cx += 28f;

            // ── Lock toggle ──
            // Closed padlock (amber) = amount snapshotted; re-asserted on any external change.
            // Open padlock   (dim)   = participates in normal scaling.
            bool isLocked = _lockedAmounts.ContainsKey(tank.name);
            Rect lockR = new Rect(cx, r.y + 8f, 24f, 28f);

            // Hover highlight (same background used by all other small buttons)
            if (lockR.Contains(Event.current.mousePosition))
                GUI.DrawTexture(lockR, _txBtnHov);

            // Icon — tinted amber when locked, dim when unlocked, using the same
            // colour values already baked into the button styles.
            GUI.color = isLocked ? _sBtnLockOn.normal.textColor
                                 : _sBtnLock.normal.textColor;
            GUI.DrawTexture(
                new Rect(lockR.x + 5f, lockR.y + 5f, 14f, 18f),
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
            cx += 28f;

            // ── Half 1/2 ──
            if (GUI.Button(new Rect(cx, r.y + 8f, 24f, 28f), "1/2", _sBtnHalf))
                HalfTank(tank);
            cx += 28f;

            // ── Double 2× ──
            if (GUI.Button(new Rect(cx, r.y + 8f, 24f, 28f), "2×", _sBtnHalf))
                DoubleTank(tank);
            cx += 28f;

            // ── Mass ──
            GUI.Label(new Rect(cx, r.y + 11f, 68f, 22f),
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

            Rect r = GUILayoutUtility.GetRect(WindowW, 44f);
            GUI.DrawTexture(r, _txReserveRow);
            // Thin blue top border ties reserve back to the tank system visually
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1), _txAccent);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), _txDivider);

            float cx = r.x + 16f;

            // Name (italic style to distinguish from real resources)
            GUI.Label(new Rect(cx, r.y + 11f, 108f, 22f), "RESERVE", _sPropNameReserve);
            cx += 112f;

            // Percentage field
            GUI.DrawTexture(new Rect(cx, r.y + 8f, 44f, 26f), _txInput);
            string newPct = GUI.TextField(
                new Rect(cx, r.y + 8f, 44f, 26f), _pctBuf[ReserveKey], _sField);
            if (newPct != _pctBuf[ReserveKey]) _pctBuf[ReserveKey] = newPct;
            cx += 46f;

            GUI.Label(new Rect(cx, r.y + 13f, 14f, 18f), "%", _sUnitLbl);
            cx += 16f;

            // Volume field
            GUI.DrawTexture(new Rect(cx, r.y + 8f, 76f, 26f), _txInput);
            string newVol = GUI.TextField(
                new Rect(cx, r.y + 8f, 76f, 26f), _editBuf[ReserveKey], _sField);
            if (newVol != _editBuf[ReserveKey]) _editBuf[ReserveKey] = newVol;
            cx += 78f;

            GUI.Label(new Rect(cx, r.y + 13f, 14f, 18f), "L", _sUnitLbl);
            cx += 16f;

            // Apply ✓
            if (GUI.Button(new Rect(cx, r.y + 8f, 24f, 28f), "✓", _sBtnApply))
                ApplyReserve();
            cx += 28f;

            // Clear ✕ — zeros out reserve (row stays, just at 0)
            if (GUI.Button(new Rect(cx, r.y + 8f, 24f, 28f), "✕", _sBtnRemove))
            {
                _reserveVolume = 0d;
                _editBuf[ReserveKey] = "0.0000";
                _pctBuf[ReserveKey] = "0.00";
            }
            cx += 28f;

            // 1/2
            if (GUI.Button(new Rect(cx, r.y + 8f, 24f, 28f), "1/2", _sBtnHalf))
            {
                _reserveVolume = _reserveVolume * 0.5d;
                _editBuf[ReserveKey] = _reserveVolume.ToString("F4");
                _pctBuf[ReserveKey] = capacity > 0d
                    ? (_reserveVolume / capacity * 100d).ToString("F2") : "0.00";
            }
            cx += 28f;

            // 2×
            if (GUI.Button(new Rect(cx, r.y + 8f, 24f, 28f), "2×", _sBtnHalf))
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

            float rowH = 34f;

            // ── Scroll height: remaining space inside the fixed-height window ──
            //   Header          54
            //   Volume bar      46
            //   Mass row        46
            //   CURRENT header  26
            //   Current rows     n × 44
            //   Space             4
            //   AVAILABLE header 26
            //   Search bar       30
            //   Quick Fill       34 + presets × 50  (or 0 if no presets)
            //   Footer           38
            //   Window chrome    12  (GUILayout internal padding)

            int nActive = _module.tanksDict.Values.Count(t => t.maxAmount > 0d);
            float qfPanelH = _presets.Count > 0 ? 34f + _presets.Count * 50f + 10f : 0f;
            // Life Support panel: only present when the tank type supports Food.
            // 36px header; when expanded adds inputs(44) + 3 rows(34ea) + commit(38)
            FuelTank _lsFoodCheck;
            bool lsAvail = _module.tanksDict.TryGetValue("Food", out _lsFoodCheck) && _lsFoodCheck.canHave;
            float lsH = lsAvail ? 36f + (_lifeSupportExpanded ? 44f + 3f * 34f + 38f : 0f) : 0f;
            float fixedH = 64 + 56 + 46 + 26 + (nActive * 44f) + 44 + 4 + 44 + 26 + 30 + qfPanelH + lsH + 38 + 12;
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
                Rect dr = GUILayoutUtility.GetRect(WindowW, 10f);
                GUI.DrawTexture(new Rect(dr.x, dr.y + 5, dr.width, 1), _txDivider);
            }

            foreach (var t in others) DrawAvailRow(t);

            // Empty-state message when nothing matches
            if (available.Count == 0)
            {
                Rect er = GUILayoutUtility.GetRect(WindowW, 34f);
                var es = new GUIStyle(_sSectionLbl) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(er, q.Length > 0 ? "No matches for \"" + _searchQuery + "\"" : "All resources active", es);
            }

            GUILayout.EndScrollView();
        }

        private void DrawSearchBar()
        {
            // Full-width bar sitting flush between the section header and the scroll list.
            Rect bar = GUILayoutUtility.GetRect(WindowW, 30f);
            GUI.DrawTexture(bar, _txDark);
            GUI.DrawTexture(new Rect(bar.x, bar.yMax - 1, bar.width, 1), _txDivider);

            float clearW = _searchQuery.Length > 0 ? 24f : 0f;
            float fieldX = bar.x + 16f;
            float fieldW = bar.width - 32f - clearW;

            // Text field
            GUI.DrawTexture(new Rect(fieldX - 2, bar.y + 5f, fieldW + 4, 20f), _txInput);
            GUI.SetNextControlName("RFSearch");
            string newQ = GUI.TextField(
                new Rect(fieldX, bar.y + 6f, fieldW, 18f),
                _searchQuery, _sSearch);

            // Placeholder text when empty and unfocused
            if (_searchQuery.Length == 0 && GUI.GetNameOfFocusedControl() != "RFSearch")
                GUI.Label(new Rect(fieldX, bar.y + 6f, fieldW, 18f),
                    "Search propellants…", _sSearchPlaceholder);

            if (newQ != _searchQuery)
            {
                _searchQuery = newQ;
                _availScroll = Vector2.zero;  // reset scroll on new query
            }

            // ✕ clear button — only visible when there is text
            if (_searchQuery.Length > 0)
            {
                if (GUI.Button(new Rect(bar.xMax - 16f - clearW, bar.y + 5f, clearW, 20f),
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
            Rect r = GUILayoutUtility.GetRect(WindowW, 34f);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), _txAvailRowDivider);

            // Right-side fill controls:
            // [kg/L 70][gap 6][pct field 44][% 16][gap 8][liters 64][gap 8][+ADD 54][rMargin 10]
            const float kgLW = 70f;
            const float kgLGap = 6f;
            const float pctFieldW = 44f;
            const float pctLblW = 16f;
            const float litGap = 8f;
            const float litW = 64f;
            const float btnGap = 8f;
            const float btnW = 54f;
            const float rMargin = 10f;
            const float rightW = kgLW + kgLGap + pctFieldW + pctLblW + litGap + litW + btnGap + btnW + rMargin;

            float cx = r.x + 14;

            // Star toggle
            if (GUI.Button(new Rect(cx, r.y + 5, 24, 24), "★",
                           isFav ? _sStarOn : _sStar))
            {
                if (isFav) Favourites.Remove(tank.name);
                else Favourites.Add(tank.name);
                SaveFavorites();
            }
            cx += 30;

            // Name — fills the remaining left space before the right-side controls
            float nameW = r.xMax - cx - rightW;
            GUI.Label(new Rect(cx, r.y + 6, nameW, 22), tank.name, _sAvailName);

            // kg/L — stored density of this resource accounting for RF utilization.
            // Formula: (density t/u × 1000 kg/t × utilization u/L) ÷ volume L/u
            var resDef = PartResourceLibrary.Instance.GetDefinition(tank.name);
            if (resDef != null)
            {
                double kgPerLitre = (resDef.density * 1000.0 * tank.utilization)
                                    / Math.Max(resDef.volume, 0.001);
                float kgLX = r.xMax - rMargin - btnW - btnGap - litW - litGap
                             - pctLblW - pctFieldW - kgLGap - kgLW;
                GUI.Label(new Rect(kgLX, r.y + 6, kgLW, 22),
                    kgPerLitre.ToString("G3") + " kg/L", _sQfRatio);
            }

            // Right-side fill controls, anchored from the right edge
            float rx = r.xMax - rMargin - btnW - btnGap - litW - litGap - pctLblW - pctFieldW;

            double physAvail = Math.Max(0d, _module.AvailableVolume - _reserveVolume);
            bool canAdd = physAvail >= 0.001d;

            // Fill percentage input field
            if (!_availFillPctBuf.ContainsKey(tank.name)) _availFillPctBuf[tank.name] = "100";
            GUI.DrawTexture(new Rect(rx, r.y + 5f, pctFieldW, 24f), _txInput);
            string newPct = GUI.TextField(
                new Rect(rx, r.y + 5f, pctFieldW, 24f),
                _availFillPctBuf[tank.name], _sField);
            if (newPct != _availFillPctBuf[tank.name]) _availFillPctBuf[tank.name] = newPct;
            rx += pctFieldW;

            GUI.Label(new Rect(rx, r.y + 5, pctLblW, 24), "%", _sUnitLbl);
            rx += pctLblW + litGap;

            // Computed fill amount — updates live as the percentage changes.
            // Displayed in RF units (physAvail * utilization), which equals physical litres
            // for util=1 resources but is larger for compressed gases (util > 1).
            // Resources with utilization > 1 get bold green text to signal that the tank
            // can store significantly more RF units than its physical litre capacity.
            double fillPct = 100d;
            double.TryParse(_availFillPctBuf[tank.name], out fillPct);
            double fillFrac = Math.Max(0d, Math.Min(100d, fillPct)) / 100d;
            double fillAmount = physAvail * fillFrac * tank.utilization;
            GUIStyle litreStyle = tank.utilization > 1.0 ? _sQfRatioHighUtil : _sQfRatio;
            GUI.Label(new Rect(rx, r.y + 5, litW, 24),
                fillAmount.ToString("F1") + " L", litreStyle);
            rx += litW + btnGap;

            // +ADD button (or FULL indicator when tank is at capacity)
            if (canAdd && GUI.Button(new Rect(rx, r.y + 5, btnW, 24), "+ADD", _sBtnAdd))
                AddTank(tank, fillFrac);
            else if (!canAdd)
                GUI.Label(new Rect(rx, r.y + 5, btnW, 24), "FULL", _sAvailFull);
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

            // Each row is now 46px (top line 26px + status line 20px).
            float rowH = 46f;
            float panelH = 14f + 10f + _presets.Count * (rowH + 4f) + 10f;
            Rect panel = GUILayoutUtility.GetRect(WindowW, panelH);
            GUI.DrawTexture(panel, _txDark);
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 1), _txDivider);
            GUI.Label(new Rect(panel.x + 16, panel.y + 8, panel.width - 32, 16),
                "⚡  QUICK FILL — CONSUMER RATIOS", _sSectionLbl);

            float ry = panel.y + 28f;
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
            float topH = 26f;
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

            // ── Invisible click area covers name only (not the input fields) ──
            Rect fillRect = nameRect;
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
            Rect hdr = GUILayoutUtility.GetRect(WindowW, 44f);
            GUI.DrawTexture(hdr, _txLsHeaderBg);
            if (hdr.Contains(Event.current.mousePosition))
                GUI.DrawTexture(hdr, _txLsHov);
            GUI.Label(new Rect(hdr.x + 16, hdr.y, hdr.width - 32, hdr.height),
                (_lifeSupportExpanded ? "▼" : "▶") + "  LIFE SUPPORT PLANNER", _sLsHeaderLbl);
            if (GUI.Button(hdr, GUIContent.none, GUIStyle.none))
                _lifeSupportExpanded = !_lifeSupportExpanded;

            if (!_lifeSupportExpanded) return;

            // ── Input row ────────────────────────────────────────────────────
            Rect inputs = GUILayoutUtility.GetRect(WindowW, 44f);
            GUI.DrawTexture(inputs, _txDark);
            GUI.DrawTexture(new Rect(inputs.x, inputs.yMax - 1, inputs.width, 1), _txDivider);

            float cx = inputs.x + 16f;

            GUI.Label(new Rect(cx, inputs.y + 13f, 38f, 18f), "Crew", _sUnitLbl);
            cx += 42f;
            GUI.DrawTexture(new Rect(cx, inputs.y + 8f, 38f, 26f), _txInput);
            string nc = GUI.TextField(
                new Rect(cx, inputs.y + 8f, 38f, 26f), _lsCrewBuf, _sField);
            if (nc != _lsCrewBuf) _lsCrewBuf = nc;
            cx += 46f;

            cx += 18f; // gap between crew and days

            GUI.Label(new Rect(cx, inputs.y + 13f, 38f, 18f), "Days", _sUnitLbl);
            cx += 42f;
            GUI.DrawTexture(new Rect(cx, inputs.y + 8f, 54f, 26f), _txInput);
            string nd = GUI.TextField(
                new Rect(cx, inputs.y + 8f, 54f, 26f), _lsDaysBuf, _sField);
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
            Rect commit = GUILayoutUtility.GetRect(WindowW, 38f);
            GUI.DrawTexture(commit, _txDark);
            GUI.DrawTexture(new Rect(commit.x, commit.y, commit.width, 1), _txDivider);
            GUI.DrawTexture(new Rect(commit.x, commit.yMax - 1, commit.width, 1), _txDivider);

            float btnW = 120f;
            Rect btn = new Rect(commit.x + (commit.width - btnW) * 0.5f,
                                  commit.y + 5f, btnW, 28f);
            if (GUI.Button(btn, "✓  ADD TO TANK", _sLsCommit))
                CommitLifeSupport(food, water, oxygen);
        }

        private void DrawLsResultRow(string resourceName, double litres)
        {
            Rect r = GUILayoutUtility.GetRect(WindowW, 34f);
            GUI.DrawTexture(r, _txRow);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), _txDivider);

            GUI.Label(new Rect(r.x + 16f, r.y + 7f, 110f, 20f), resourceName, _sPropName);
            // Amount right-aligned to match the mass column style
            GUI.Label(new Rect(r.x + 130f, r.y + 7f, r.width - 146f, 20f),
                litres.ToString("F4") + " L", _sMassRow);
        }

        /// <summary>
        /// Add calculated Life Support volumes to the tank. Each resource is added up to the reserve-respecting available space at that
        /// moment, so Food is added first, then Water, then Oxygen — if the tank is nearly full only the resources that fit will be added.
        /// </summary>
        private void CommitLifeSupport(double food, double water, double oxygen)
        {
            bool any = false;
            if (food > 0.0001d) { CommitLsResource("Food", food); any = true; }
            if (water > 0.0001d) { CommitLsResource("Water", water); any = true; }
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

        // ── Footer ───────────────────────────────────────────────────────────

        private void DrawFooter()
        {
            Rect r = GUILayoutUtility.GetRect(WindowW, 38f);
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
            Rect panel = GUILayoutUtility.GetRect(WindowW, 44f);
            GUI.DrawTexture(panel, _txDark);
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 1), _txDivider);
            GUI.DrawTexture(new Rect(panel.x, panel.yMax - 1, panel.width, 1), _txDivider);

            float px = panel.x + 16f;
            float pw = panel.width - 32f;    // usable width between padding
            float bw = (pw - 3f * 4f) / 4f; // four equal buttons, three 4px gaps
            float by = panel.y + 8f;
            float bh = 28f;
            float gap = 4f;

            if (GUI.Button(new Rect(px, by, bw, bh), "1/4", _sBtnScale)) ScaleAll(0.25d);
            if (GUI.Button(new Rect(px + (bw + gap), by, bw, bh), "1/2", _sBtnScale)) ScaleAll(0.5d);
            if (GUI.Button(new Rect(px + 2f * (bw + gap), by, bw, bh), "2×", _sBtnScale)) ScaleAll(2.0d);
            if (GUI.Button(new Rect(px + 3f * (bw + gap), by, bw, bh), "FILL", _sBtnScale)) ScaleAll(1e9d);
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
            double fill = avail * Math.Max(0d, Math.Min(1d, fillFrac));
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

            _sTitle = Lbl(14, FontStyle.Bold, cText);
            _sSubtitle = Lbl(12, FontStyle.Bold, cTextSec);

            _sSectionLbl = Lbl(10, FontStyle.Bold, cTextSec);
            _sSectionLbl.normal.textColor = cTextSec;

            _sCountBadge = Lbl(10, FontStyle.Bold, cBlue);
            _sCountBadge.alignment = TextAnchor.MiddleCenter;

            _sMassLbl = Lbl(10, FontStyle.Normal, cTextSec);
            _sMassVal = Lbl(13, FontStyle.Bold, cText);
            _sMassRow = Lbl(11, FontStyle.Normal, cTextSec);
            _sMassRow.alignment = TextAnchor.MiddleRight;

            _sPropName = Lbl(13, FontStyle.Bold, cText);
            _sPropName.alignment = TextAnchor.MiddleLeft;

            // Reserve row name — italic to visually signal it's a virtual/reserved entry
            _sPropNameReserve = Lbl(13, FontStyle.Italic, cTextSec);
            _sPropNameReserve.alignment = TextAnchor.MiddleLeft;

            _sPctLbl = Lbl(11, FontStyle.Normal, cBlue);
            _sPctLbl.alignment = TextAnchor.MiddleRight;

            _sUnitLbl = Lbl(11, FontStyle.Normal, cTextSec);
            _sUnitLbl.alignment = TextAnchor.MiddleLeft;

            _sField = Sty(GUI.skin.textField);
            _sField.fontSize = 12;
            _sField.alignment = TextAnchor.MiddleRight;
            _sField.normal.textColor = cBlueT;
            _sField.focused.textColor = cBlueT;
            _sField.normal.background = _txInput;
            _sField.focused.background = _txInput;
            _sField.hover.background = _txInput;

            _sBtnApply = Btn(C("#72e0a0"), cBlueT, _txBorder, _txBtnHov, 14, 24, 28); // green ✓
            _sBtnRemove = Btn(cRedT, cRedT, _txBorder, _txBtnRed, 14, 24, 28); // red ✕
            _sBtnHalf = Btn(cTextSec, cText, _txBorder, _txBtnHov, 11, 24, 28); // 1/2 / 2×
            // Life Support commit — same green as ✓ but no fixed width (explicit Rect controls size)
            _sLsCommit = Btn(C("#72e0a0"), cBlueT, _txBorder, _txBtnHov, 13, -1, 28);
            _sLsCommit.fontStyle = FontStyle.Bold;

            // Life Support header label — dark forest green on the bright green header background.
            // #0f1a12 gives ~12:1 contrast against #72e0a0.
            _sLsHeaderLbl = Lbl(12, FontStyle.Bold, C("#0f1a12"));
            _sLsHeaderLbl.normal.textColor = C("#0f1a12");

            // Lock toggle — ○ (unlocked, dim) / ● (locked, amber)
            _sBtnLock = Btn(cTextSec, cGold, _txBorder, _txBtnHov, 14, 24, 28);
            _sBtnLockOn = Btn(cGold, cGold, _txBorder, _txBtnHov, 14, 24, 28);
            _sBtnLockOn.fontStyle = FontStyle.Bold;
            _sBtnHalf.fontStyle = FontStyle.Bold;
            _sBtnScale = Btn(cTextSec, cText, _txBorder, _txBtnHov, 11, -1, 28); // bulk scale
            _sBtnScale.fontStyle = FontStyle.Bold;

            _sStar = Btn(cTextSec, cGold, _txClear, _txClear, 16, 24, 24);  // was cTextDim
            _sStarOn = Btn(cGold, new Color(cGold.r, cGold.g, cGold.b, 0.7f), _txClear, _txClear, 16, 24, 24);

            _sAvailName = Lbl(13, FontStyle.Bold, cText);
            _sAvailName.alignment = TextAnchor.MiddleLeft;

            _sFavBadge = Lbl(9, FontStyle.Bold, cGold);
            _sFavBadge.alignment = TextAnchor.MiddleCenter;

            _sBtnAdd = Btn(cBlue, cBlueT, _txAddNorm, _txAddHov, 11, 54, 24);
            _sBtnAdd.fontStyle = FontStyle.Bold;

            _sQfEngine = Lbl(10, FontStyle.Bold, cBlue);
            _sQfEngine.alignment = TextAnchor.MiddleCenter;

            _sQfName = Lbl(12, FontStyle.Bold, cText);
            _sQfName.alignment = TextAnchor.MiddleLeft;

            _sQfRatio = Lbl(11, FontStyle.Normal, cTextSec);
            _sQfRatio.alignment = TextAnchor.MiddleRight;

            // Variant used when the resource has utilization > 1 (e.g. gases stored at
            // high pressure): bold green to signal the litre figure is especially favourable.
            _sQfRatioHighUtil = Lbl(11, FontStyle.Bold, C("#72e0a0"));
            _sQfRatioHighUtil.alignment = TextAnchor.MiddleRight;

            _sQfMain = Btn(cTextSec, cText, _txQfNorm, _txQfHov, 12, -1, 32);
            _sQfMain.alignment = TextAnchor.MiddleLeft;
            _sQfMain.padding = new RectOffset(8, 8, 0, 0);

            // Quick Fill status line styles
            _sQfStatusGood = Lbl(10, FontStyle.Bold, C("#72e0a0")); // green  — 7.2:1 (was #4caf7d @ 4.5:1)
            _sQfStatusWarn = Lbl(10, FontStyle.Bold, C("#f8c848")); // amber  — 7.4:1 (was #f0b830 @ 6.5:1)
            _sQfStatusBad = Lbl(10, FontStyle.Bold, C("#ffb8b8")); // red    — 7.4:1 (was #e05858 @ 3.3:1)
            _sQfStatusDim = Lbl(10, FontStyle.Normal, cTextSec);

            // "FULL" indicator in the Available list — amber, bold, centred so it
            // sits at the same visual baseline as the pct field and liters readout.
            _sAvailFull = Lbl(10, FontStyle.Bold, C("#f8c848"));
            _sAvailFull.alignment = TextAnchor.MiddleCenter;

            // Footer label styles — backgrounds are drawn manually in DrawFooter so
            // that the invisible GUIStyle.none hit buttons work reliably.
            _sFooter = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _sFooter.normal.textColor = cText;

            _sFooterDanger = new GUIStyle(_sFooter);
            _sFooterDanger.normal.textColor = cRedT;

            // Search bar
            _sSearch = Sty(GUI.skin.textField);
            _sSearch.fontSize = 12;
            _sSearch.alignment = TextAnchor.MiddleLeft;
            _sSearch.normal.textColor = cText;
            _sSearch.focused.textColor = cText;
            _sSearch.normal.background = _txClear;  // background drawn manually
            _sSearch.focused.background = _txClear;
            _sSearch.hover.background = _txClear;
            _sSearch.padding = new RectOffset(2, 2, 0, 0);

            _sSearchPlaceholder = Lbl(12, FontStyle.Normal, cTextSec);
            _sSearchPlaceholder.alignment = TextAnchor.MiddleLeft;

            _sSearchClear = Btn(cTextDim, cRedT, _txClear, _txClear, 12, 24, 20);
            _sSearchClear.fontSize = 11;
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
                _txLsHeaderBg, _txLsHov
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
    }

    // ── PAW hook ─────────────────────────────────────────────────────────────
    // Added to every part that has ModuleFuelTanks via the companion MM patch.
    // Keeps RealFuelsWindow.cs self-contained — no edits to ModuleFuelTanks.cs.

    public class RealFuelsWindowPAW : PartModule
    {
        private ModuleFuelTanks _mft;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            _mft = part.FindModuleImplementing<ModuleFuelTanks>();

            // Only show the PAW button in the editor.
            bool hasModule = _mft != null;
            Events[nameof(ToggleTankWindow)].active = hasModule;
            Events[nameof(ToggleTankWindow)].guiActiveEditor = hasModule;
            Events[nameof(ToggleTankWindow)].guiActive = false;   // not in flight
        }

        [KSPEvent(guiName = "Configure Propellants",
                  guiActiveEditor = true,
                  guiActive = false,
                  category = "Fuel")]
        public void ToggleTankWindow()
        {
            if (_mft == null) return;
            RealFuelsWindow.ToggleGUI(_mft);
        }

        private void OnDestroy()
        {
            // Close the window if this part is removed from the ship.
            if (_mft != null)
                RealFuelsWindow.HideGUIForModule(_mft);
        }
    }
}
