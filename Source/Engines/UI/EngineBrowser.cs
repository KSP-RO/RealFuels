using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using ClickThroughFix;
using KSP.UI.Screens;

namespace RealFuels
{
    /// <summary>
    /// Editor window listing every engine config of every part, with search, filters,
    /// sortable columns, entry-cost purchase and a Pick button that spawns the part
    /// on the cursor with the chosen config applied.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class EngineBrowser : MonoBehaviour
    {
        private enum Col
        {
            Family, Config, Type, Thrust, ThrustSL, MinThrottle, IspVac, IspSL, Mass, TwrVac, TwrSL, StructFactor, Gimbal, Igns, Ullage, PFed,
            Propellants, Store, Rated, Tested, IgnRel, DU0, DUMax, Tech, Spec, Cost, Entry, Pick
        }
        private const int ColCount = 28;

        private static readonly string[] ColNames = {
            "Engine Family", "Config", "Type", "Thrust (Vac)", "Thrust (SL)", "Min%", "ISP (Vac)", "ISP (SL)", "Mass",
            "TWR (Vac)", "TWR (SL)", "Tank SF", "Gimbal", "Igns", "Ullg", "PFed",
            "Propellants", "Store", "Rated", "Tested", "Ign%", "0-DU", "Max-DU", "Tech", "Spec", "Cost", "Entry Cost", ""
        };

        private static readonly string[] ColTips = {
            "Part title (xN = engines/chambers in one part)", "Engine configuration", "Engine type",
            "Maximum thrust in vacuum", "Maximum thrust at sea level (vacuum thrust scaled by ISP)", "Minimum throttle",
            "Vacuum specific impulse", "Sea-level specific impulse", "Engine mass",
            "Vacuum thrust-to-weight ratio (1 g)", "Sea-level thrust-to-weight ratio (1 g)",
            "Tank structural factor: tank dry mass / (tank + propellant) for this engine's propellant mix, in the selected tank " +
                "type and material at max utilization (Best researched = lightest researched material). Pressure-fed engines use " +
                "the highly pressurized version. Lower is better.",
            "Gimbal range", "Ignitions (Gnd = ground-lit only)",
            "Requires ullage", "Pressure-fed", "Propellants", "Storable: no cryogenic (boil-off) propellants",
            "Rated burn time (continuous / cumulative)", "Tested burn time", "Ignition reliability (0 data / max data)",
            "Cycle reliability at 0 data", "Cycle reliability at max data", "Tech node required", "Specification level",
            "Part cost with this config", "Config entry cost. Click twice to buy.", "Spawn this part with this config"
        };

        /// <summary>
        /// Unit shown in the range filter popup; null = column can't be range-filtered.
        /// Percent columns are filtered in percent, matching what the cells show.
        /// </summary>
        private static readonly string[] ColUnits = {
            null, null, null, "kN", "kN", "%", "s", "s", "t",
            "", "", "%", "°", "", null, null,
            null, null, "s", "s", "% (max data)", "%", "%", null, null, "√", "√", null
        };

        private static bool IsNumeric(int col) => ColUnits[col] != null;

        private static readonly int[] DefaultHidden = { (int)Col.Type };

        private enum TriState { Any, Yes, No }
        private enum AvailFilter { Any, Researched, Unlocked }

        private static readonly string[] KindLabels = { "Liquid", "Solid", "Generics", "Nuclear", "Electric", "Planes", "RCS" };
        private static readonly string[] KindTips = {
            "Liquid-fuelled rocket engines",
            "Solid rocket motors (and hybrids)",
            "Generic thrusters that use tech levels",
            "Nuclear thermal engines",
            "Electric propulsion",
            "Air-breathing engines (jets, props)",
            "RCS thrusters"
        };
        private const int KindCount = 7;

        // -- Persistent settings (static so they survive VAB/SPH switches) --
        private static readonly string SettingsPath = System.IO.Path.Combine(
            KSPUtil.ApplicationRootPath, "GameData", "RealFuels", "PluginData", "EngineBrowserSettings.cfg");
        private static bool _settingsLoaded;
        private static Rect _windowRect = new Rect(60, 80, 400, 100);
        private static int _visibleRows = 30;
        private static float _fontScale = 1f;
        private static readonly bool[] _colVisible = new bool[ColCount];
        private static int _sortCol = (int)Col.Family;
        private static bool _sortAsc = true;
        private static readonly bool[] _kindEnabled = { true, true, true, true, true, true, true };
        private static AvailFilter _avail = AvailFilter.Any;
        private static TriState _storable = TriState.Any;
        private static TriState _groundLit = TriState.Any;
        // Per-column range filters set from the header right-click popup: col -> {min, max} text.
        private static readonly Dictionary<int, string[]> _ranges = new Dictionary<int, string[]>();
        private static readonly HashSet<string> _excludedProps = new HashSet<string>();
        private static readonly HashSet<string> _excludedTechs = new HashSet<string>();
        private static bool _closeOnPick = true;
        private static TankFamily _tankFamily = TankFamily.Isogrid; // tank type for the Tank SF column
        // Material picked per tank type (base TANK_DEFINITION name); "" = best researched.
        private static readonly string[] _tankMaterial = { "", "", "" };

        // -- Window state --
        private const int WindowId = 0x52464542; // "RFEB"
        private const int PropsWindowId = WindowId + 1;
        private const int TechsWindowId = WindowId + 2;
        private const int CfgWindowId = WindowId + 3;
        private const int TooltipWindowId = WindowId + 4;
        private const int RangeWindowId = WindowId + 5;
        private const int MaterialsWindowId = WindowId + 6;
        private const string LockId = "RFEngineBrowserLock";
        private const string FieldPrefix = "RFEB_";

        private ApplicationLauncherButton _button;
        private Texture2D _ownIcon;
        private bool _show;
        private bool _showProps, _showTechs, _showCfg;
        private Rect _propsRect, _techsRect, _cfgRect;
        private Rect _cfgBtnRect;
        private Vector2 _listAnchor;         // screen point the propellant/tech popup opens at
        private bool _showMaterials;
        private Rect _materialsRect;
        private Vector2 _matAnchor;          // below the material button, in screen coordinates
        private bool _matClosedByClick;      // the click that closed the popup must not reopen it
        private int _rangeCol = -1;          // column whose range popup is open
        private Rect _rangeRect;
        private bool _rangeFocusPending;
        private Vector2 _tableScroll, _propsScroll, _techsScroll, _cfgScroll;
        private string _search = "";
        private string _rowsInput;
        private bool _editorLocked;
        private string _tooltip = string.Empty;
        private string _collectedTooltip = string.Empty;
        private bool _settingsDirty;
        private float _settingsDirtyAt;

        // Inputs the window size was last computed from; see the resize check in OnGUI.
        private int _sizeRows = -1;
        private float _sizeScale = -1f, _sizeTableW = -1f;
        private int _sizeScreenW = -1, _sizeScreenH = -1;
        private bool _resizeRequested;
        private int _dataVersion = -1;

        // -- Data state --
        private readonly List<EngineBrowserEntry> _filtered = new List<EngineBrowserEntry>();
        private bool _filterDirty = true;
        private float _lastStateRefresh = float.MinValue;
        private const float StateRefreshInterval = 1f;
        private readonly float[] _colWidths = new float[ColCount];
        private bool _widthsDirty = true;
        private string[] _allProps, _allTechs;
        private readonly Dictionary<string, int> _propCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _techCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, double> _costCache = new Dictionary<string, double>();
        private readonly Dictionary<string, string> _techTitles = new Dictionary<string, string>();
        private readonly string[] _headerLabels = new string[ColCount];
        private readonly string[] _headerTips = new string[ColCount];
        // Active column filters in column order: (col, "Name text"), shown after the tank options.
        private readonly List<(int col, string text)> _activeFilters = new List<(int col, string text)>();
        private EngineBrowserEntry _confirmBuy;
        private float _confirmBuyUntil;
        private bool _picking;

        // -- Styles --
        private float _stylesScale = -1f;
        private GUIStyle _cell, _cellCenter, _name, _nameLocked, _header, _btn, _btnOn,
            _btnBuy, _btnOwned, _label, _title, _field, _fieldBad, _tooltipStyle, _toggle, _dim, _link, _popupPanel;
        private readonly GUIContent _content = new GUIContent();

        private float RowHeight => Mathf.Round(20f * _fontScale);

        #region Lifecycle

        private void Start()
        {
            EnsureSettings();
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            GameEvents.OnTechnologyResearched.Add(OnTechResearched);
            GameEvents.onGUIApplicationLauncherUnreadifying.Add(RemoveButton);
            if (ApplicationLauncher.Ready)
                AddButton();
        }

        private void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            GameEvents.OnTechnologyResearched.Remove(OnTechResearched);
            GameEvents.onGUIApplicationLauncherUnreadifying.Remove(RemoveButton);
            RemoveButton(GameScenes.EDITOR);
            EditorUnlock();
            if (_settingsDirty)
                SaveSettings();
            if (_ownIcon != null)
                Destroy(_ownIcon);
            if (_popupPanel?.normal.background != null)
                Destroy(_popupPanel.normal.background);
        }

        private void AddButton()
        {
            if (_button != null || ApplicationLauncher.Instance == null)
                return;
            _button = ApplicationLauncher.Instance.AddModApplication(
                () => _show = true, CloseWindow, null, null, null, null,
                ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH, GetIcon());
        }

        private void RemoveButton(GameScenes _)
        {
            if (_button != null && ApplicationLauncher.Instance != null)
                ApplicationLauncher.Instance.RemoveModApplication(_button);
            _button = null;
        }

        private Texture GetIcon()
        {
            Texture tex = GameDatabase.Instance.GetTexture("Squad/PartList/SimpleIcons/R&D_node_icon_generalrocketry", false);
            if (tex != null)
                return tex;
            _ownIcon = new Texture2D(38, 38, TextureFormat.RGBA32, false);
            var px = new Color[38 * 38];
            for (int i = 0; i < px.Length; i++)
            {
                int x = i % 38, y = i / 38;
                bool on = x >= 12 && x <= 26 && y >= 8 && y <= 30 && (x <= 15 || y <= 11 || y >= 27 || (y >= 18 && y <= 20 && x <= 23));
                px[i] = on ? Color.white : Color.clear;
            }
            _ownIcon.SetPixels(px);
            _ownIcon.Apply();
            return _ownIcon;
        }

        private void CloseWindow()
        {
            _show = false;
            _showProps = _showTechs = _showCfg = false;
            EditorUnlock();
            if (_settingsDirty)
                SaveSettings();
        }

        private void CloseFromWindow()
        {
            // Route through the launcher so its toggle state stays in sync.
            if (_button != null)
                _button.SetFalse(true);
            else
                CloseWindow();
        }

        #endregion

        #region OnGUI

        private void OnGUI()
        {
            if (!_show || EditorLogic.fetch == null)
            {
                EditorUnlock();
                return;
            }

            if (Styles.styleEditorPanel == null)
                Styles.InitStyles();
            EngineConfigTextures.Instance.EnsureInitialized();
            EngineConfigStyles.Initialize();
            EnsureStyles();

            if (Event.current.type == EventType.Layout)
            {
                // A rebuilt database (new save or GameDatabase reload) invalidates everything
                // derived from the old entries.
                _ = EngineBrowserDatabase.Entries;
                if (EngineBrowserDatabase.Version != _dataVersion)
                {
                    _dataVersion = EngineBrowserDatabase.Version;
                    _tankDirty = true;
                    _widthsDirty = true;
                    _filterDirty = true;
                    _lastStateRefresh = float.MinValue;
                    _confirmBuy = null;
                    _filtered.Clear();
                }
                if (_widthsDirty)
                    BuildCellsAndWidths();
                if (Time.realtimeSinceStartup - _lastStateRefresh > StateRefreshInterval)
                    RefreshState();
                if (_filterDirty)
                    ApplyFilter();

                // Resize only when something that affects the size changed. Resetting the rect
                // on every event makes GUILayout re-grow it each pass, which flickers.
                float tableW = TableWidth();
                if (_resizeRequested || _sizeRows != _visibleRows || _sizeScale != _fontScale || _sizeTableW != tableW
                    || _sizeScreenW != Screen.width || _sizeScreenH != Screen.height)
                {
                    _resizeRequested = false;
                    _sizeRows = _visibleRows;
                    _sizeScale = _fontScale;
                    _sizeTableW = tableW;
                    _sizeScreenW = Screen.width;
                    _sizeScreenH = Screen.height;
                    _windowRect.width = WindowWidth();
                    _windowRect.height = 50f; // GUILayout grows it to fit the content
                }

                // Tooltip collected during the previous Repaint; switching it here keeps the
                // tooltip window identical between this frame's Layout and Repaint passes.
                _tooltip = _collectedTooltip;
            }

            if (Event.current.type == EventType.Repaint)
                _collectedTooltip = string.Empty;

            // Clicking anywhere outside a header filter popup closes it. Checked before the
            // windows run and not Use()d, so a right-click on another header reopens one there.
            if (Event.current.type == EventType.MouseDown)
            {
                Vector2 m = Event.current.mousePosition;
                if (_rangeCol >= 0 && !_rangeRect.Contains(m)) _rangeCol = -1;
                if (_showProps && !_propsRect.Contains(m)) _showProps = false;
                if (_showTechs && !_techsRect.Contains(m)) _showTechs = false;
                _matClosedByClick = _showMaterials && !_materialsRect.Contains(m);
                if (_matClosedByClick) _showMaterials = false;
            }

            Rect prev = _windowRect;
            _windowRect = ClickThruBlocker.GUILayoutWindow(WindowId, _windowRect, DrawWindow, "", Styles.styleEditorPanel);
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Mathf.Max(0, Screen.width - 100));
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Mathf.Max(0, Screen.height - 60));
            if (prev.x != _windowRect.x || prev.y != _windowRect.y)
                MarkSettingsDirty();

            if (_showProps)
                _propsRect = DrawPopup(PropsWindowId, _listAnchor, DrawPropsWindow);
            if (_showTechs)
                _techsRect = DrawPopup(TechsWindowId, _listAnchor, DrawTechsWindow);
            if (_showCfg)
                _cfgRect = DrawPopup(CfgWindowId, _windowRect.position + new Vector2(_cfgBtnRect.x, _cfgBtnRect.yMax), DrawCfgWindow);
            if (_showMaterials)
                _materialsRect = DrawPopup(MaterialsWindowId, _matAnchor, DrawMaterialsWindow, MaterialsPopupHeight());

            if (_rangeCol >= 0)
            {
                EnsurePopupPanel();
                _rangeRect = ClickThruBlocker.GUILayoutWindow(RangeWindowId, _rangeRect, DrawRangeWindow, "", _popupPanel);
                GUI.BringWindowToFront(RangeWindowId);
                if (_rangeFocusPending)
                    GUI.FocusWindow(RangeWindowId);
            }

            DrawTooltip();

            UpdateEditorLock();

            if (_settingsDirty && Time.realtimeSinceStartup - _settingsDirtyAt > 1f)
                SaveSettings();
        }

        /// <summary>Where a list popup anchored at a screen point (its top-left) is drawn.</summary>
        private Rect PopupRect(Vector2 anchor, float height = 460f)
        {
            float w = Mathf.Round(280f * _fontScale);
            float h = Mathf.Min(Mathf.Round(height * _fontScale), Screen.height - 40f);
            return new Rect(Mathf.Clamp(anchor.x, 0, Screen.width - w), Mathf.Clamp(anchor.y + 2f, 0, Screen.height - h), w, h);
        }

        private Rect DrawPopup(int id, Vector2 anchor, GUI.WindowFunction fn, float height = 460f)
        {
            EnsurePopupPanel();
            Rect result = ClickThruBlocker.GUIWindow(id, PopupRect(anchor, height), fn, "", _popupPanel);
            // Keep the popup above the main window so clicks on it never reach the table.
            GUI.BringWindowToFront(id);
            return result;
        }

        private void EnsurePopupPanel()
        {
            if (_popupPanel == null || _popupPanel.normal.background == null)
            {
                _popupPanel = new GUIStyle(Styles.styleEditorPanel);
                _popupPanel.normal.background = Styles.CreateColorPixel(new Color32(32, 32, 32, 250));
            }
        }

        private void CollectTooltip()
        {
            if (Event.current.type == EventType.Repaint && !string.IsNullOrEmpty(GUI.tooltip))
                _collectedTooltip = GUI.tooltip;
        }

        private void DrawTooltip()
        {
            if (string.IsNullOrEmpty(_tooltip))
                return;
            Vector2 mouse = Input.mousePosition;
            mouse.y = Screen.height - mouse.y;

            _content.text = _tooltip;
            _content.tooltip = null;
            _tooltipStyle.wordWrap = false;
            float w = Mathf.Min(_tooltipStyle.CalcSize(_content).x + 4f, 460f * _fontScale);
            _tooltipStyle.wordWrap = true;
            float h = _tooltipStyle.CalcHeight(_content, w);
            float x = mouse.x + 18f, y = mouse.y + 14f;
            if (x + w > Screen.width) x = mouse.x - w - 8f;
            if (y + h > Screen.height) y = Screen.height - h - 4f;

            string text = _tooltip;
            GUIStyle style = _tooltipStyle;
            ClickThruBlocker.GUIWindow(TooltipWindowId, new Rect(x, y, w, h),
                _ => GUI.Box(new Rect(0, 0, w, h), text, style), GUIContent.none, GUIStyle.none);
            GUI.BringWindowToFront(TooltipWindowId);
        }

        #endregion

        #region Main window

        private float TableWidth()
        {
            float w = 0f;
            for (int i = 0; i < ColCount; i++)
                if (_colVisible[i])
                    w += _colWidths[i];
            return w;
        }

        private float WindowWidth()
        {
            float w = Mathf.Max(TableWidth() + 16f + 12f, 900f * _fontScale);
            // Screen limit last: Mathf.Clamp returns the minimum when min > max.
            return Mathf.Min(w, Screen.width - 20f);
        }

        private void DrawWindow(int id)
        {
            var entries = EngineBrowserDatabase.Entries;
            bool changed = false;

            // -- Title row --
            GUILayout.BeginHorizontal();
            GUILayout.Label("Engine Browser", _title);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{_filtered.Count} / {entries.Count} engines", _dim);
            GUILayout.Space(10);
            if (GUILayout.Button(new GUIContent("Cfg", "Columns and options"), _showCfg ? _btnOn : _btn, GUILayout.Width(40 * _fontScale)))
                _showCfg = !_showCfg;
            if (Event.current.type == EventType.Repaint)
                _cfgBtnRect = GUILayoutUtility.GetLastRect();
            GUILayout.Label("Rows:", _label);
            if (_rowsInput == null)
                _rowsInput = _visibleRows.ToString();
            GUI.SetNextControlName(FieldPrefix + "rows");
            string rows = GUILayout.TextField(_rowsInput, 3, _field, GUILayout.Width(36 * _fontScale));
            if (rows != _rowsInput)
            {
                _rowsInput = rows;
                if (int.TryParse(rows, out int r) && r >= 5 && r <= 200)
                {
                    _visibleRows = r;
                    MarkSettingsDirty();
                }
            }
            if (GUILayout.Button("✕", EngineConfigStyles.CloseButton, GUILayout.Width(26)))
            {
                CloseFromWindow();
                GUIUtility.ExitGUI();
            }
            GUILayout.EndHorizontal();

            // -- Filter row --
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", _label);
            GUI.SetNextControlName(FieldPrefix + "search");
            string s = GUILayout.TextField(_search, _field, GUILayout.Width(200 * _fontScale));
            if (s != _search) { _search = s; _filterDirty = true; }
            GUILayout.Space(12);

            GUILayout.Label("Type", _label);
            bool all = _kindEnabled.All(k => k);
            if (GUILayout.Button(new GUIContent("All", "Show every engine type"), all ? _btnOn : _btn))
            {
                for (int k = 0; k < KindCount; k++) _kindEnabled[k] = true;
                changed = true;
            }
            for (int k = 0; k < KindCount; k++)
            {
                _content.text = KindLabels[k];
                _content.tooltip = KindTips[k] + (all ? "\nClick to show only this type." : "\nClick to toggle.");
                if (GUILayout.Button(_content, _kindEnabled[k] && !all ? _btnOn : _btn))
                {
                    if (all)
                    {
                        for (int j = 0; j < KindCount; j++) _kindEnabled[j] = j == k;
                    }
                    else
                    {
                        _kindEnabled[k] = !_kindEnabled[k];
                        if (!_kindEnabled.Any(x => x))
                            for (int j = 0; j < KindCount; j++) _kindEnabled[j] = true;
                    }
                    changed = true;
                }
            }
            GUILayout.Space(12);

            string availLabel = _avail == AvailFilter.Any ? "Any Status" : (_avail == AvailFilter.Researched ? "Researched" : "Unlocked");
            if (GUILayout.Button(new GUIContent(availLabel,
                "Cycle: any / tech researched / researched and entry cost paid (or free)"), _avail == AvailFilter.Any ? _btn : _btnOn))
            {
                _avail = (AvailFilter)(((int)_avail + 1) % 3);
                changed = true;
            }
            if (GUILayout.Button(new GUIContent(TriLabel(_storable, "Storable", "Storable", "Cryogenic"),
                "Cycle: don't care / storable only (no boil-off) / cryogenic only"), _storable == TriState.Any ? _btn : _btnOn))
            {
                _storable = Cycle(_storable);
                changed = true;
            }
            if (GUILayout.Button(new GUIContent(TriLabel(_groundLit, "Gnd Lit", "Gnd Lit", "Air Lit"),
                "Cycle: don't care / ground-lit only (pad ignition, no in-flight ignitions) / not ground-lit"), _groundLit == TriState.Any ? _btn : _btnOn))
            {
                _groundLit = Cycle(_groundLit);
                changed = true;
            }

            // Reset, tank type and the filter summary share the row: it has room to spare.
            GUILayout.Space(12);
            if (GUILayout.Button(new GUIContent("Reset Filters", "Clear search, all filters and column filters"), _btn))
            {
                ResetFilters();
                changed = true;
            }
            GUILayout.Space(12);
            GUILayout.Label("Tank SF", _label);
            TankFamily family = EngineBrowserTanks.Effective(_tankFamily);
            for (int f = 0; f < EngineBrowserTanks.FamilyCount; f++)
            {
                bool has = EngineBrowserTanks.HasFamily((TankFamily)f);
                _content.text = EngineBrowserTanks.FamilyLabels[f];
                _content.tooltip = has ? $"Compute the Tank SF column for {EngineBrowserTanks.FamilyLabels[f]} tanks" : "No tank types of this kind installed";
                GUI.enabled = has;
                if (GUILayout.Button(_content, (int)family == f ? _btnOn : _btn) && (int)family != f)
                {
                    _tankFamily = (TankFamily)f;
                    _tankDirty = true; // recompute structural factors
                    _lastStateRefresh = float.MinValue;
                    _showMaterials = false;
                    _resizeRequested = true; // the material button label changes
                    MarkSettingsDirty();
                }
                GUI.enabled = true;
            }
            string matTitle = MaterialTitle(family, CurrentMaterial(family));
            _content.text = $"{matTitle} ▾";
            _content.tooltip = "Tank material for the Tank SF column. Best researched picks the lightest researched one; " +
                               "pressure-fed engines use the material's highly pressurized version.";
            if (GUILayout.Button(_content, _showMaterials ? _btnOn : _btn))
            {
                // A click on the button while the popup is open already closed it (outside-click
                // check); don't reopen it on the same click.
                if (_matClosedByClick)
                    _matClosedByClick = false;
                else
                {
                    CloseHeaderPopups();
                    _showMaterials = true;
                    _materialsRect = PopupRect(_matAnchor, MaterialsPopupHeight());
                }
            }
            if (Event.current.type == EventType.Repaint)
            {
                Rect r = GUILayoutUtility.GetLastRect();
                _matAnchor = _windowRect.position + new Vector2(r.x, r.yMax);
            }
            if (_activeFilters.Count > 0)
            {
                // Each entry opens its column's filter popup, so filters on hidden columns stay editable.
                GUILayout.Space(12);
                for (int i = 0; i < _activeFilters.Count; i++)
                {
                    var (col, text) = _activeFilters[i];
                    _content.text = text;
                    _content.tooltip = "Click to edit this filter";
                    if (GUILayout.Button(_content, _link))
                    {
                        Rect r = GUILayoutUtility.GetLastRect();
                        OpenColumnFilter(col, GUIUtility.GUIToScreenPoint(new Vector2(r.x, r.yMax)));
                    }
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // -- Hint row --
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#FFA726>Right click on a column header to set filters.</color>", _label);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (changed)
            {
                _filterDirty = true;
                _resizeRequested = true; // button labels change width, let the window shrink back
                MarkSettingsDirty();
            }

            GUILayout.Space(4);

            // -- Table --
            float rowH = RowHeight;
            float headerH = rowH + 4f;
            float bodyW = _windowRect.width - 12f;
            bool needHScroll = TableWidth() + 16f > bodyW;
            float bodyH = _visibleRows * rowH + (needHScroll ? 16f : 0f);
            Rect area = GUILayoutUtility.GetRect(bodyW, headerH + bodyH, GUILayout.ExpandWidth(true));
            DrawTable(area, headerH, rowH);

            GUILayout.Space(4);
            CollectTooltip();
            GUI.DragWindow(new Rect(0, 0, 10000, 24 * _fontScale));
        }

        private static string TriLabel(TriState t, string any, string yes, string no)
            => t == TriState.Any ? any : (t == TriState.Yes ? yes + " Only" : no + " Only");

        private static TriState Cycle(TriState t) => (TriState)(((int)t + 1) % 3);

        /// <summary>
        /// "100-450 kN", ">=100 kN" or "<=450 kN" for a column range, using only bounds that parse
        /// (the same ones ApplyFilter uses); null when no bound parses.
        /// </summary>
        private static string RangeText(int col)
        {
            if (!_ranges.TryGetValue(col, out string[] r))
                return null;
            string lo = r[0].Trim(), hi = r[1].Trim();
            bool hasLo = !float.IsNaN(ParseOrNaN(lo)), hasHi = !float.IsNaN(ParseOrNaN(hi));
            string unit = ColUnits[col].Length > 0 && ColUnits[col] != "√" ? " " + ColUnits[col].Split(' ')[0] : string.Empty;
            if (hasLo && hasHi) return $"{lo}-{hi}{unit}";
            if (hasLo) return $"≥{lo}{unit}";
            if (hasHi) return $"≤{hi}{unit}";
            return null;
        }

        private static bool IsFilterable(int col) => IsNumeric(col) || col == (int)Col.Propellants || col == (int)Col.Tech;

        /// <summary>"allowed/total" for a checklist filter, or null when nothing in this install is excluded.</summary>
        private static string ListFilterText(string[] all, HashSet<string> excluded)
        {
            // Saved exclusions can name things this save doesn't list; they are kept but not counted.
            int ex = 0;
            foreach (string s in all)
                if (excluded.Contains(s))
                    ex++;
            return ex > 0 ? $"{all.Length - ex}/{all.Length}" : null;
        }

        /// <summary>Short description of a column's active filter, or null if it has none.</summary>
        private string ColumnFilterText(int col)
        {
            if (col == (int)Col.Propellants)
                return ListFilterText(_allProps, _excludedProps);
            if (col == (int)Col.Tech)
                return ListFilterText(_allTechs, _excludedTechs);
            return RangeText(col);
        }

        /// <summary>
        /// Rebuilds the cached header labels/tooltips and the filter summary. Called whenever the
        /// filter or sort changes, so drawing the header allocates nothing per GUI event.
        /// </summary>
        private void RebuildHeaderTexts()
        {
            var summary = new List<(int col, string text)>();
            for (int c = 0; c < ColCount; c++)
            {
                bool filterable = IsFilterable(c);
                string filterText = filterable && _allProps != null ? ColumnFilterText(c) : null;
                string label = ColNames[c];
                if (c == _sortCol)
                    label += _sortAsc ? " ↑" : " ↓";
                _headerLabels[c] = filterText != null ? $"<color=#FFD54F>{label}</color>" : label;
                _headerTips[c] = c == (int)Col.Pick ? ColTips[c]
                    : ColTips[c] + "\nClick to sort."
                      + (!filterable ? string.Empty
                         : filterText != null ? $"\nFiltered: {filterText}. Right-click to change."
                         : "\nRight-click to filter.");
                if (filterText != null)
                    summary.Add((c, $"<color=#FFD54F>{ColNames[c]} {filterText}</color>"));
            }
            // Summary entries are drawn one by one so each can be clicked to edit its filter.
            _activeFilters.Clear();
            _activeFilters.AddRange(summary);
        }

        private void CloseHeaderPopups()
        {
            _rangeCol = -1;
            _showProps = _showTechs = _showMaterials = false;
        }

        // -- Tank SF material picker --

        /// <summary>The picked material for a family if it is still installed, else "" (best researched).</summary>
        private static string CurrentMaterial(TankFamily family)
        {
            string id = _tankMaterial[(int)family];
            if (string.IsNullOrEmpty(id))
                return string.Empty;
            foreach (var m in EngineBrowserTanks.Materials(family))
                if (m.Id == id)
                    return id;
            return string.Empty;
        }

        private static string MaterialTitle(TankFamily family, string id)
        {
            if (string.IsNullOrEmpty(id))
                return "Best researched";
            foreach (var m in EngineBrowserTanks.Materials(family))
                if (m.Id == id)
                    return m.Title;
            return id;
        }

        private static bool IsTechResearched(string tech)
        {
            if (string.IsNullOrEmpty(tech) || HighLogic.CurrentGame == null || HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX
                || ResearchAndDevelopment.Instance == null)
                return true;
            return ResearchAndDevelopment.GetTechnologyState(tech) == RDTech.State.Available;
        }

        private float MaterialsPopupHeight()
            => Mathf.Min(460f, 70f + 24f * (EngineBrowserTanks.Materials(EngineBrowserTanks.Effective(_tankFamily)).Count + 1));

        private void DrawMaterialsWindow(int id)
        {
            TankFamily family = EngineBrowserTanks.Effective(_tankFamily);
            PopupHeader($"{EngineBrowserTanks.FamilyLabels[(int)family]} Material", ref _showMaterials);
            string current = CurrentMaterial(family);

            _content.text = "Best researched";
            _content.tooltip = "Lightest researched material that can hold the engine's propellants";
            bool pick = GUILayout.Toggle(current.Length == 0, _content, _toggle) && current.Length != 0;
            string chosen = pick ? string.Empty : null;

            foreach (var m in EngineBrowserTanks.Materials(family))
            {
                bool researched = IsTechResearched(m.Tech);
                _content.text = researched ? m.Title : $"<color=#FFA040>{m.Title}</color>";
                _content.tooltip = $"{m.Id}{(researched ? string.Empty : $"\nNot researched yet ({TechTitle(m.Tech)})")}";
                if (GUILayout.Toggle(m.Id == current, _content, _toggle) && m.Id != current)
                    chosen = m.Id;
            }

            if (chosen != null)
            {
                _tankMaterial[(int)family] = chosen;
                _tankDirty = true;                  // recompute structural factors
                _lastStateRefresh = float.MinValue;
                _resizeRequested = true;            // the material button label changes
                _showMaterials = false;
                MarkSettingsDirty();
            }
            CollectTooltip();
        }

        /// <summary>Opens the filter popup for a header: range box or propellant/tech checklist.</summary>
        private void OpenColumnFilter(int col, Vector2 screenPos)
        {
            CloseHeaderPopups();
            if (col == (int)Col.Propellants || col == (int)Col.Tech)
            {
                _listAnchor = screenPos;
                // Set the rect now so the outside-click check is right before the first draw.
                if (col == (int)Col.Propellants) { _showProps = true; _propsRect = PopupRect(screenPos); }
                else { _showTechs = true; _techsRect = PopupRect(screenPos); }
            }
            else if (IsNumeric(col))
                OpenRange(col, screenPos);
        }

        private void OpenRange(int col, Vector2 screenPos)
        {
            _rangeCol = col;
            float w = Mathf.Round(270f * _fontScale);
            _rangeRect = new Rect(Mathf.Clamp(screenPos.x, 0, Screen.width - w), Mathf.Clamp(screenPos.y + 2f, 0, Screen.height - 100f), w, 10f);
            _rangeFocusPending = true;
        }

        /// <summary>
        /// Keeps only characters a number can contain. Also keeps the saved "Col:lo:hi;..." range
        /// format unambiguous, since ':' and ';' can never be typed.
        /// </summary>
        private static string NumericChars(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char ch in s)
                if (char.IsDigit(ch) || ch == '.' || ch == ',' || ch == '-' || ch == '+' || ch == 'e' || ch == 'E')
                    sb.Append(ch);
            return sb.Length == s.Length ? s : sb.ToString();
        }

        /// <summary>Red text for a bound that won't parse, so it's clear it isn't filtering.</summary>
        private GUIStyle BoundStyle(string s)
            => s.Trim().Length > 0 && float.IsNaN(ParseOrNaN(s.Trim())) ? _fieldBad : _field;

        private void DrawRangeWindow(int id)
        {
            int col = _rangeCol;
            if (col < 0)
                return;
            string[] r = _ranges.TryGetValue(col, out string[] cur) ? cur : new[] { string.Empty, string.Empty };

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Filter {ColNames[col]}", _title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", EngineConfigStyles.CloseButton, GUILayout.Width(26)))
            {
                _rangeCol = -1;
                GUIUtility.ExitGUI();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName(FieldPrefix + "rangeMin");
            string a = GUILayout.TextField(r[0], 12, BoundStyle(r[0]), GUILayout.Width(80 * _fontScale));
            GUILayout.Label("–", _label);
            GUI.SetNextControlName(FieldPrefix + "rangeMax");
            string b = GUILayout.TextField(r[1], 12, BoundStyle(r[1]), GUILayout.Width(80 * _fontScale));
            if (ColUnits[col].Length > 0)
                GUILayout.Label(ColUnits[col], _dim);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear", _btn))
                a = b = string.Empty;
            GUILayout.FlexibleSpace();
            GUILayout.Label("Blank = no limit. Enter to close.", _dim);
            GUILayout.EndHorizontal();

            a = NumericChars(a);
            b = NumericChars(b);
            if (a != r[0] || b != r[1])
            {
                if (a.Trim().Length == 0 && b.Trim().Length == 0)
                    _ranges.Remove(col);
                else
                    _ranges[col] = new[] { a, b };
                _filterDirty = true;
                _resizeRequested = true; // the active-range summary label changes width
                MarkSettingsDirty();
            }

            if (_rangeFocusPending && Event.current.type == EventType.Repaint)
            {
                GUI.FocusControl(FieldPrefix + "rangeMin");
                _rangeFocusPending = false;
            }
            Event ev = Event.current;
            if (ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter || ev.keyCode == KeyCode.Escape))
            {
                _rangeCol = -1;
                GUIUtility.keyboardControl = 0;
                ev.Use();
            }
            CollectTooltip();
        }

        private void ResetFilters()
        {
            _search = "";
            for (int k = 0; k < KindCount; k++) _kindEnabled[k] = true;
            _avail = AvailFilter.Any;
            _storable = _groundLit = TriState.Any;
            _ranges.Clear();
            _rangeCol = -1;
            _showProps = _showTechs = false;
            _excludedProps.Clear();
            _excludedTechs.Clear();
        }

        #endregion

        #region Table

        private void DrawTable(Rect area, float headerH, float rowH)
        {
            float contentW = TableWidth();
            Rect headerRect = new Rect(area.x, area.y, area.width - 16f, headerH);
            Rect bodyRect = new Rect(area.x, area.y + headerH, area.width, area.height - headerH);

            // Header scrolls horizontally with the body.
            GUI.BeginGroup(headerRect);
            float x = -_tableScroll.x;
            for (int c = 0; c < ColCount; c++)
            {
                if (!_colVisible[c])
                    continue;
                float w = _colWidths[c];
                Rect cell = new Rect(x, 0, w, headerH);
                bool filterable = IsFilterable(c);

                // Right-click opens the column's filter popup. IMGUI buttons react to any mouse
                // button, so non-left clicks are Use()d on every header: on columns without a
                // filter a right-click then does nothing instead of sorting.
                Event ev = Event.current;
                if (ev.type == EventType.MouseDown && ev.button != 0 && cell.Contains(ev.mousePosition))
                {
                    if (filterable && ev.button == 1)
                        OpenColumnFilter(c, GUIUtility.GUIToScreenPoint(new Vector2(cell.x, cell.yMax)));
                    ev.Use();
                }

                _content.text = _headerLabels[c] ?? ColNames[c];
                _content.tooltip = _headerTips[c] ?? ColTips[c];
                if (GUI.Button(cell, _content, _header) && c != (int)Col.Pick)
                {
                    if (_sortCol == c)
                        _sortAsc = !_sortAsc;
                    else
                    {
                        _sortCol = c;
                        _sortAsc = true;
                    }
                    SortFiltered();
                    MarkSettingsDirty();
                }
                x += w;
            }
            GUI.EndGroup();

            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(new Rect(area.x, area.y + headerH - 1f, area.width, 1f), EngineConfigTextures.Instance.ChartSeparator);

            // Only left clicks act on rows (IMGUI buttons would otherwise Pick or Buy on a right-click).
            if (Event.current.type == EventType.MouseDown && Event.current.button != 0 && bodyRect.Contains(Event.current.mousePosition))
                Event.current.Use();

            Rect view = new Rect(0, 0, contentW, Mathf.Max(_filtered.Count * rowH, 1f));
            _tableScroll = GUI.BeginScrollView(bodyRect, _tableScroll, view, false, true);

            int first = Mathf.Max(0, Mathf.FloorToInt(_tableScroll.y / rowH));
            int last = Mathf.Min(_filtered.Count, first + Mathf.CeilToInt(bodyRect.height / rowH) + 1);
            for (int i = first; i < last; i++)
                DrawRow(_filtered[i], i, new Rect(0, i * rowH, contentW, rowH));

            if (_filtered.Count == 0)
                GUI.Label(new Rect(8, 4, 400, rowH), "No engines match the current filters.", _dim);

            GUI.EndScrollView();
        }

        private void DrawRow(EngineBrowserEntry e, int index, Rect r)
        {
            var tex = EngineConfigTextures.Instance;
            bool hover = r.Contains(Event.current.mousePosition);
            if (Event.current.type == EventType.Repaint)
            {
                if (!e.Researched)
                    GUI.DrawTexture(r, tex.RowLocked);
                else if (index % 2 == 1)
                    GUI.DrawTexture(r, tex.ZebraStripe);
                if (hover)
                    GUI.DrawTexture(r, tex.RowHover);
            }

            // Row-wide hover tooltip; built lazily for the hovered row only.
            _content.text = string.Empty;
            _content.tooltip = hover ? (e.Tooltip ?? (e.Tooltip = BuildTooltip(e))) : string.Empty;
            GUI.Label(r, _content, GUIStyle.none);

            float x = r.x;
            for (int c = 0; c < ColCount; c++)
            {
                if (!_colVisible[c])
                    continue;
                float w = _colWidths[c];
                Rect cr = new Rect(x, r.y, w, r.height);
                switch ((Col)c)
                {
                    case Col.Entry:
                        DrawEntryCell(e, new Rect(cr.x + 2, cr.y + 1, cr.width - 4, cr.height - 2));
                        break;
                    case Col.Pick:
                        DrawPickCell(e, new Rect(cr.x + 2, cr.y + 1, cr.width - 4, cr.height - 2));
                        break;
                    case Col.Family:
                    case Col.Config:
                        GUI.Label(cr, e.Cells[c], e.Researched ? _name : _nameLocked);
                        break;
                    case Col.Gimbal:
                    case Col.Igns:
                    case Col.Ullage:
                    case Col.PFed:
                    case Col.Store:
                        GUI.Label(cr, e.Cells[c], _cellCenter);
                        break;
                    default:
                        GUI.Label(cr, e.Cells[c], _cell);
                        break;
                }
                x += w;
            }

            if (Event.current.type == EventType.Repaint)
            {
                x = r.x;
                for (int c = 0; c < ColCount - 1; c++)
                {
                    if (!_colVisible[c])
                        continue;
                    x += _colWidths[c];
                    GUI.DrawTexture(new Rect(x, r.y, 1, r.height), tex.ColumnSeparator);
                }
            }
        }

        private void DrawEntryCell(EngineBrowserEntry e, Rect r)
        {
            bool confirming = _confirmBuy == e && Time.realtimeSinceStartup < _confirmBuyUntil;
            bool owned = e.Unlocked || e.EntryCost <= 0;
            bool canBuy = !owned && e.Researched && EntryCostManager.Instance != null;

            _content.text = e.Unlocked ? "Unlocked" : (e.EntryCost <= 0 ? "Free" : (confirming ? "Buy?" : e.EntryCost.ToString("N0")));
            _content.tooltip = string.Empty;
            if (!owned)
            {
                if (EngineConfigRP1Integration.TryGetCreditAdjustedCost(e.EntryCost, out double credits, out double after))
                    _content.tooltip = $"Entry cost: {e.EntryCost:N0}\nCredits available: {credits:N0}\n<b>After credits: {after:N0}</b>";
                else
                    _content.tooltip = $"Entry cost: {e.EntryCost:N0}";
                _content.tooltip += canBuy ? "\nClick twice to buy." : "\nResearch the tech first.";
            }

            GUI.enabled = canBuy;
            if (GUI.Button(r, _content, owned ? _btnOwned : _btnBuy))
            {
                if (confirming)
                {
                    _confirmBuy = null;
                    Purchase(e);
                }
                else
                {
                    _confirmBuy = e;
                    _confirmBuyUntil = Time.realtimeSinceStartup + 3f;
                }
            }
            GUI.enabled = true;
        }

        private void DrawPickCell(EngineBrowserEntry e, Rect r)
        {
            bool partsScreen = EditorLogic.fetch.editorScreen == EditorScreen.Parts;
            _content.text = "Pick";
            _content.tooltip = !e.PartAvailable ? "Part is not available yet"
                : (!partsScreen ? "Switch to the parts screen to pick" : "Pick up this part with this config");
            GUI.enabled = e.PartAvailable && partsScreen && !_picking;
            if (GUI.Button(r, _content, _btn))
                StartCoroutine(PickRoutine(e));
            GUI.enabled = true;
        }

        private string BuildTooltip(EngineBrowserEntry e)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"<b>{e.Family}</b>  <color=#9E9E9E>({e.Part.name})</color>\n");
            sb.Append($"<color=#FFA726>Config:</color> {e.Config}   <color=#FFA726>Type:</color> {KindLabels[(int)e.Kind]}\n");
            if (e.Tech.Length > 0)
                sb.Append($"<color=#FFA726>Requires:</color> {e.TechTitle}{(e.TechTitle != e.Tech ? $" <color=#9E9E9E>({e.Tech})</color>" : "")}{(e.Researched ? "" : " <color=#FF8A65>(not researched)</color>")}\n");
            if (e.Propellants.Length > 0)
            {
                var props = e.Propellants.Select(p => EngineBrowserDatabase.IsStorable(p) ? p : $"{p} <color=#80D9FF>(cryo)</color>");
                sb.Append($"<color=#FFA726>Propellants:</color> {string.Join(", ", props)}\n");
            }
            if (!string.IsNullOrEmpty(e.StructNote))
            {
                string sf = float.IsNaN(e.StructFactor) ? string.Empty : $"{e.StructFactor * 100f:F1} %, ";
                sb.Append($"<color=#FFA726>Tank SF ({EngineBrowserTanks.FamilyLabels[(int)EngineBrowserTanks.Effective(_tankFamily)]}):</color> {sf}{e.StructNote}\n");
            }
            if (e.GroundLit)
                sb.Append("<color=#FFEB3B>Ground-lit only: cannot be ignited in flight</color>\n");
            if (e.Description.Length > 0)
                sb.Append('\n').Append(e.Description);
            return sb.ToString().TrimEnd();
        }

        #endregion

        #region Actions

        private void Purchase(EngineBrowserEntry e)
        {
            string tech = e.Node.GetValue("techRequired");
            // DrawSelectButton is the hook RP-1 patches to apply unlock credits; the purchase
            // must run inside it.
            e.Module.DrawSelectButton(e.Node, false, cfgName => EntryCostManager.Instance.PurchaseConfig(cfgName, tech));
            _lastStateRefresh = float.MinValue;
        }

        private IEnumerator PickRoutine(EngineBrowserEntry e)
        {
            _picking = true;
            try
            {
                EditorLogic editor = EditorLogic.fetch;
                if (editor == null)
                    yield break;

                // Drop whatever is on the cursor first, like clicking the part list background.
                if (EditorLogic.SelectedPart != null)
                {
                    editor.OnPartListBackgroundTap();
                    yield return null;
                    if (EditorLogic.SelectedPart != null)
                        yield break;
                }

                bool emptyShip = editor.ship == null || editor.ship.parts.Count == 0;
                editor.OnPartListIconTap(e.Part);
                Part part = emptyShip ? EditorLogic.RootPart : EditorLogic.SelectedPart;
                if (part == null || part.name != e.Part.name || e.ModuleIndex >= part.Modules.Count
                    || !(part.Modules[e.ModuleIndex] is ModuleEngineConfigsBase mec))
                {
                    Debug.LogWarning($"[RFEngineBrowser] Could not find spawned {e.Part.name} to configure");
                    yield break;
                }

                // Set the persistent fields before OnStart, then apply properly once started.
                mec.ApplyBrowserVariant(e.ConfigName, e.PatchName, false);
                if (_closeOnPick)
                    CloseFromWindow();
                yield return null;
                yield return null;
                if (mec != null && mec.part != null)
                    mec.ApplyBrowserVariant(e.ConfigName, e.PatchName, true);
            }
            finally
            {
                _picking = false;
            }
        }

        #endregion

        #region Filtering and sorting

        private void RefreshState()
        {
            _lastStateRefresh = Time.realtimeSinceStartup;
            bool sandbox = HighLogic.CurrentGame == null || HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX;
            bool changed = false;
            // Entries of one part are contiguous and variants share config names, so cache the
            // per-part and per-config lookups instead of repeating them for every row.
            AvailablePart lastPart = null;
            bool partAvailable = false;
            _costCache.Clear();
            // Structural factors only change with the tank type/material and with research, so they
            // are recomputed on those events rather than every refresh.
            bool updateTanks = _tankDirty;
            _tankDirty = false;
            TankFamily family = EngineBrowserTanks.Effective(_tankFamily);
            string material = CurrentMaterial(family);
            Func<string, bool> techAvailable = null;
            if (updateTanks)
            {
                _tankCache.Clear();
                _techStateCache.Clear();
                techAvailable = TechResearchedCached;
            }
            foreach (var e in EngineBrowserDatabase.Entries)
            {
                if (e.Part != lastPart)
                {
                    lastPart = e.Part;
                    partAvailable = sandbox || ResearchAndDevelopment.PartModelPurchased(e.Part) || ResearchAndDevelopment.IsExperimentalPart(e.Part);
                }
                if (!_costCache.TryGetValue(e.ConfigName ?? string.Empty, out double cost))
                {
                    cost = EntryCostManager.Instance != null ? EntryCostManager.Instance.ConfigEntryCost(e.ConfigName) : 0d;
                    _costCache[e.ConfigName ?? string.Empty] = cost;
                }
                bool researched = EngineConfigTechLevels.CanConfig(e.Node);
                bool unlocked = EngineConfigTechLevels.UnlockedConfig(e.Node, e.Part.partPrefab);
                if (researched != e.Researched || unlocked != e.Unlocked || cost != e.EntryCost || partAvailable != e.PartAvailable)
                {
                    e.Researched = researched;
                    e.Unlocked = unlocked;
                    e.EntryCost = cost;
                    e.PartAvailable = partAvailable;
                    e.Tooltip = null;
                    changed = true;
                }

                if (!updateTanks)
                    continue;

                // Structural factor depends on researched tank types and the selected family;
                // engines with the same propellant mix and pressure-fed flag share one result.
                if (!_tankCache.TryGetValue(e.TankKey, out var tank))
                {
                    tank = EngineBrowserTanks.Evaluate(e.TankProps, e.PressureFed, family, material, techAvailable);
                    _tankCache[e.TankKey] = tank;
                }
                string note = float.IsNaN(tank.SF) ? tank.Note
                    : $"{tank.TankTitle}, mix density {tank.MixDensity:F3} kg/L";
                if (!SameFloat(tank.SF, e.StructFactor) || note != e.StructNote)
                {
                    e.StructFactor = tank.SF;
                    e.StructNote = note;
                    if (e.Cells != null)
                        e.Cells[(int)Col.StructFactor] = float.IsNaN(tank.SF) ? "-" : $"{tank.SF * 100f:F1} %";
                    e.Tooltip = null;
                    changed = true;
                }
            }
            if (changed)
                _filterDirty = true;
        }

        private static bool SameFloat(float a, float b) => a == b || (float.IsNaN(a) && float.IsNaN(b));

        /// <summary>IsTechResearched with a per-recompute cache: many tank types share a tech.</summary>
        private bool TechResearchedCached(string tech)
        {
            if (string.IsNullOrEmpty(tech))
                return true;
            if (!_techStateCache.TryGetValue(tech, out bool ok))
                _techStateCache[tech] = ok = IsTechResearched(tech);
            return ok;
        }

        private bool _tankDirty = true;

        private void OnTechResearched(GameEvents.HostTargetAction<RDTech, RDTech.OperationResult> _)
        {
            _tankDirty = true;
            _lastStateRefresh = float.MinValue;
        }

        private readonly Dictionary<string, EngineBrowserTanks.Result> _tankCache = new Dictionary<string, EngineBrowserTanks.Result>();
        private readonly Dictionary<string, bool> _techStateCache = new Dictionary<string, bool>();

        private static float ParseOrNaN(string s)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
               || float.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out f) ? f : float.NaN;

        /// <summary>
        /// Value a range filter compares against, in the units the column shows (percent
        /// columns in percent). NaN = no value, which fails any active bound.
        /// </summary>
        private static float NumericValue(EngineBrowserEntry e, int col)
        {
            float Pos(float v) => v >= 0f ? v : float.NaN;
            float Pct(float v) => v >= 0f ? v * 100f : float.NaN;
            switch ((Col)col)
            {
                case Col.Thrust: return Pos(e.Thrust);
                case Col.ThrustSL: return Pos(e.ThrustSL);
                case Col.MinThrottle: return Pct(e.MinThrottle);
                case Col.IspVac: return e.IspVac > 0f ? e.IspVac : float.NaN;
                case Col.IspSL: return e.IspSL > 0f ? e.IspSL : float.NaN;
                case Col.Mass: return Pos(e.Mass);
                case Col.TwrVac: return Pos(e.TwrVac);
                case Col.TwrSL: return Pos(e.TwrSL);
                case Col.StructFactor: return float.IsNaN(e.StructFactor) ? float.NaN : e.StructFactor * 100f;
                case Col.Gimbal: return Mathf.Max(e.Gimbal, 0f); // no gimbal = 0 deg
                case Col.Igns:
                    if (e.Ignitions == EngineBrowserEntry.IgnNone) return float.NaN;
                    return e.Ignitions == EngineBrowserEntry.IgnUnlimited ? float.PositiveInfinity : e.Ignitions;
                case Col.Rated:
                    if (e.Rated >= 0f) return e.Rated;
                    return e.RatedContinuous >= 0f ? e.RatedContinuous : float.PositiveInfinity; // shown as "inf"
                case Col.Tested: return e.Tested > 0f ? e.Tested : float.NaN;
                case Col.IgnRel: return Pct(e.IgnEnd);
                case Col.DU0: return Pct(e.CycleStart);
                case Col.DUMax: return Pct(e.CycleEnd);
                case Col.Cost: return e.Cost;
                case Col.Entry: return e.Unlocked ? 0f : (float)e.EntryCost;
                default: return float.NaN;
            }
        }

        private static bool InRanges(EngineBrowserEntry e, List<(int col, float lo, float hi)> ranges)
        {
            foreach (var (col, lo, hi) in ranges)
            {
                float v = NumericValue(e, col);
                if (float.IsNaN(v)) return false;
                if (!float.IsNaN(lo) && v < lo) return false;
                if (!float.IsNaN(hi) && v > hi) return false;
            }
            return true;
        }

        private void ApplyFilter()
        {
            _filterDirty = false;
            string[] terms = _search.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var ranges = new List<(int col, float lo, float hi)>();
            foreach (var kv in _ranges)
            {
                float lo = ParseOrNaN(kv.Value[0]), hi = ParseOrNaN(kv.Value[1]);
                if (!float.IsNaN(lo) || !float.IsNaN(hi))
                    ranges.Add((kv.Key, lo, hi));
            }

            _filtered.Clear();
            foreach (var e in EngineBrowserDatabase.Entries)
            {
                if (!_kindEnabled[(int)e.Kind])
                    continue;
                if (_avail == AvailFilter.Researched && !e.Researched)
                    continue;
                if (_avail == AvailFilter.Unlocked && !(e.Researched && (e.Unlocked || e.EntryCost <= 0)))
                    continue;
                if (_storable != TriState.Any && e.Storable != (_storable == TriState.Yes))
                    continue;
                if (_groundLit != TriState.Any && e.GroundLit != (_groundLit == TriState.Yes))
                    continue;
                if (!InRanges(e, ranges))
                    continue;
                if (_excludedProps.Count > 0 && e.Propellants.Any(_excludedProps.Contains))
                    continue;
                if (_excludedTechs.Count > 0 && _excludedTechs.Contains(e.Tech))
                    continue;
                bool match = true;
                foreach (string t in terms)
                {
                    if (e.SearchText.IndexOf(t, StringComparison.Ordinal) < 0)
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    _filtered.Add(e);
            }
            SortFiltered();
        }

        private void SortFiltered()
        {
            int col = _sortCol;
            int dir = _sortAsc ? 1 : -1;
            bool numeric = IsNumeric(col);
            _filtered.Sort((a, b) =>
            {
                int r;
                if (numeric)
                {
                    // Same values the range filters use; rows without a value ("-") sort last
                    // in both directions.
                    float va = NumericValue(a, col), vb = NumericValue(b, col);
                    bool ma = float.IsNaN(va), mb = float.IsNaN(vb);
                    r = ma || mb ? (ma == mb ? 0 : (ma ? 1 : -1)) : va.CompareTo(vb) * dir;
                }
                else
                    r = Compare(a, b, col) * dir;
                if (r == 0) r = string.Compare(a.Family, b.Family, StringComparison.OrdinalIgnoreCase);
                if (r == 0) r = string.Compare(a.Config, b.Config, StringComparison.OrdinalIgnoreCase);
                return r;
            });
            RebuildHeaderTexts();
        }

        private static int SpecRank(string spec)
        {
            switch (spec.ToLowerInvariant())
            {
                case "operational": return 0;
                case "prototype": return 1;
                case "concept": return 2;
                case "speculative": return 3;
                case "scifi": return 4;
                default: return 5;
            }
        }

        /// <summary>Sort order of the non-numeric columns; numeric ones sort on NumericValue.</summary>
        private static int Compare(EngineBrowserEntry a, EngineBrowserEntry b, int col)
        {
            switch ((Col)col)
            {
                case Col.Family: return string.Compare(a.Family, b.Family, StringComparison.OrdinalIgnoreCase);
                case Col.Config: return string.Compare(a.Config, b.Config, StringComparison.OrdinalIgnoreCase);
                case Col.Type: return a.Kind.CompareTo(b.Kind);
                case Col.Ullage: return a.Ullage.CompareTo(b.Ullage);
                case Col.PFed: return a.PressureFed.CompareTo(b.PressureFed);
                case Col.Propellants: return string.Compare(a.PropellantText, b.PropellantText, StringComparison.OrdinalIgnoreCase);
                case Col.Store: return a.Storable.CompareTo(b.Storable);
                case Col.Tech: return string.Compare(a.TechTitle, b.TechTitle, StringComparison.OrdinalIgnoreCase);
                case Col.Spec: return SpecRank(a.Spec).CompareTo(SpecRank(b.Spec));
                default: return 0;
            }
        }

        #endregion

        #region Cells and widths

        private void BuildCellsAndWidths()
        {
            _widthsDirty = false;
            var entries = EngineBrowserDatabase.Entries;

            _propCounts.Clear();
            _techCounts.Clear();
            _techTitles.Clear();
            foreach (var e in entries)
            {
                if (e.Cells == null)
                    e.Cells = BuildCells(e);
                foreach (string p in e.Propellants)
                    _propCounts[p] = _propCounts.TryGetValue(p, out int n) ? n + 1 : 1;
                string tech = e.Tech;
                _techCounts[tech] = _techCounts.TryGetValue(tech, out int m) ? m + 1 : 1;
                _techTitles[tech] = e.TechTitle;
            }
            _allProps = _propCounts.Keys.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            _allTechs = _techCounts.Keys.OrderBy(TechTitle, StringComparer.OrdinalIgnoreCase).ToArray();

            for (int c = 0; c < ColCount; c++)
            {
                _content.text = ColNames[c] + " ↑";
                _colWidths[c] = _header.CalcSize(_content).x + 6f;
            }
            foreach (var e in entries)
            {
                for (int c = 0; c < ColCount; c++)
                {
                    string s = e.Cells[c];
                    if (string.IsNullOrEmpty(s))
                        continue;
                    _content.text = s;
                    GUIStyle st = (c == (int)Col.Family || c == (int)Col.Config) ? _name : _cell;
                    float w = st.CalcSize(_content).x + 6f;
                    if (w > _colWidths[c])
                        _colWidths[c] = w;
                }
            }

            float sc = _fontScale;
            _colWidths[(int)Col.Family] = Mathf.Min(_colWidths[(int)Col.Family], 300f * sc);
            _colWidths[(int)Col.Config] = Mathf.Min(_colWidths[(int)Col.Config], 220f * sc);
            _colWidths[(int)Col.Propellants] = Mathf.Min(_colWidths[(int)Col.Propellants], 300f * sc);
            _colWidths[(int)Col.Gimbal] = Mathf.Min(_colWidths[(int)Col.Gimbal], 120f * sc);
            _colWidths[(int)Col.Tech] = Mathf.Min(_colWidths[(int)Col.Tech], 170f * sc);
            _colWidths[(int)Col.Rated] = Mathf.Min(_colWidths[(int)Col.Rated], 140f * sc);
            // Tank SF cells change with research and the tank type, so size for the widest value.
            _content.text = "100.0 %";
            _colWidths[(int)Col.StructFactor] = Mathf.Max(_colWidths[(int)Col.StructFactor], _cell.CalcSize(_content).x + 6f);
            _content.text = "Unlocked";
            float entryW = Mathf.Max(_btnBuy.CalcSize(_content).x, 80f * sc) + 8f;
            _colWidths[(int)Col.Entry] = Mathf.Max(_colWidths[(int)Col.Entry], entryW);
            _content.text = "Pick";
            _colWidths[(int)Col.Pick] = _btn.CalcSize(_content).x + 16f;
        }

        private static string FormatThrust(float kN)
        {
            if (kN < 0f) return "-";
            if (kN < 1f) return $"{kN * 1000f:N2} N";
            if (kN < 100f) return $"{kN:N2} kN";
            return $"{kN:N0} kN";
        }

        private static string FormatSeconds(float s) => s >= 0f ? $"{s:0.#}s" : "-";

        private static string SpecColor(string spec)
        {
            switch (SpecRank(spec))
            {
                case 0: return "#66DD66";
                case 1: return "#FFD54F";
                case 2: return "#FFA040";
                case 3: return "#B39DDB";
                case 4: return "#F48FB1";
                default: return "#B0B0B0";
            }
        }

        private static string BuildIgns(EngineBrowserEntry e)
        {
            switch (e.Ignitions)
            {
                case EngineBrowserEntry.IgnNone: return "-";
                case EngineBrowserEntry.IgnUnlimited: return "∞";
                case EngineBrowserEntry.IgnGround: return "<color=#FFEB3B>Gnd</color>";
                default: return e.Ignitions.ToString();
            }
        }

        private static string BuildRated(EngineBrowserEntry e)
        {
            if (e.Rated < 0f && e.RatedContinuous < 0f)
                return "∞";
            if (e.Rated >= 0f && e.RatedContinuous >= 0f && !Mathf.Approximately(e.Rated, e.RatedContinuous))
                return $"{FormatSeconds(e.RatedContinuous)} / {FormatSeconds(e.Rated)}";
            return FormatSeconds(e.Rated >= 0f ? e.Rated : e.RatedContinuous);
        }

        private const string Cross = "<color=#9E9E9E>✗</color>";

        private static string[] BuildCells(EngineBrowserEntry e)
        {
            var c = new string[ColCount];
            c[(int)Col.Family] = e.Family;
            c[(int)Col.Config] = e.Config;
            c[(int)Col.Type] = KindLabels[(int)e.Kind];
            c[(int)Col.Thrust] = FormatThrust(e.Thrust);
            c[(int)Col.ThrustSL] = FormatThrust(e.ThrustSL);
            c[(int)Col.MinThrottle] = e.MinThrottle >= 0f ? $"{e.MinThrottle * 100f:0} %" : "-";
            c[(int)Col.IspVac] = e.IspVac > 0f ? $"{e.IspVac:N0} s" : "-";
            c[(int)Col.IspSL] = e.IspSL > 0f ? $"{e.IspSL:N0} s" : "-";
            c[(int)Col.Mass] = e.Mass >= 0f ? $"{e.Mass:N3} t" : "-";
            c[(int)Col.TwrVac] = e.TwrVac >= 0f ? $"{e.TwrVac:0.00}" : "-";
            c[(int)Col.TwrSL] = e.TwrSL >= 0f ? $"{e.TwrSL:0.00}" : "-";
            c[(int)Col.StructFactor] = float.IsNaN(e.StructFactor) ? "-" : $"{e.StructFactor * 100f:F1} %";
            c[(int)Col.Gimbal] = e.Gimbal > 0f ? e.GimbalText : Cross;
            c[(int)Col.Igns] = BuildIgns(e);
            c[(int)Col.Ullage] = e.Ullage ? "✓" : Cross;
            c[(int)Col.PFed] = e.PressureFed ? "<color=#4DE64D>✓</color>" : "-";
            c[(int)Col.Propellants] = e.PropellantText;
            c[(int)Col.Store] = e.Storable ? "<color=#4DE64D>✓</color>" : "-";
            c[(int)Col.Rated] = BuildRated(e);
            c[(int)Col.Tested] = e.Tested > 0f ? FormatSeconds(e.Tested) : "-";
            c[(int)Col.IgnRel] = e.IgnStart >= 0f && e.IgnEnd >= 0f ? $"{e.IgnStart * 100f:F1} / {e.IgnEnd * 100f:F1} %" : "-";
            c[(int)Col.DU0] = e.CycleStart >= 0f ? $"{e.CycleStart * 100f:F1} %" : "-";
            c[(int)Col.DUMax] = e.CycleEnd >= 0f ? $"{e.CycleEnd * 100f:F1} %" : "-";
            c[(int)Col.Tech] = e.Tech.Length > 0 ? e.TechTitle : "-";
            c[(int)Col.Spec] = e.Spec.Length > 0 ? $"<color={SpecColor(e.Spec)}>{e.Spec}</color>" : "-";
            c[(int)Col.Cost] = e.Cost.ToString("N0");
            c[(int)Col.Entry] = string.Empty;
            c[(int)Col.Pick] = string.Empty;
            return c;
        }

        #endregion

        #region Popups

        private void PopupHeader(string title, ref bool show)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, _title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", EngineConfigStyles.CloseButton, GUILayout.Width(26)))
                show = false;
            GUILayout.EndHorizontal();
        }

        private void DrawPropsWindow(int id)
        {
            PopupHeader("Allowed Propellants", ref _showProps);
            GUILayout.Label("Engines using an unchecked propellant are hidden.", _dim);
            if (SetButtons(_excludedProps, _allProps))
                _filterDirty = true;
            _propsScroll = GUILayout.BeginScrollView(_propsScroll, GUILayout.ExpandHeight(true));
            foreach (string p in _allProps)
            {
                string label = EngineBrowserDatabase.IsStorable(p) ? $"{p} ({_propCounts[p]})" : $"{p} ({_propCounts[p]}) <color=#80D9FF>cryo</color>";
                if (SetToggle(_excludedProps, p, label, null))
                    _filterDirty = true;
            }
            GUILayout.EndScrollView();
            CollectTooltip();
        }

        private void DrawTechsWindow(int id)
        {
            PopupHeader("Tech Required", ref _showTechs);
            GUILayout.Label("Engines requiring an unchecked tech are hidden.", _dim);
            if (SetButtons(_excludedTechs, _allTechs))
                _filterDirty = true;
            _techsScroll = GUILayout.BeginScrollView(_techsScroll, GUILayout.ExpandHeight(true));
            foreach (string t in _allTechs)
            {
                string title = t.Length > 0 ? TechTitle(t) : "(none)";
                if (SetToggle(_excludedTechs, t, $"{title} ({_techCounts[t]})", t.Length > 0 && title != t ? t : null))
                    _filterDirty = true;
            }
            GUILayout.EndScrollView();
            CollectTooltip();
        }

        /// <summary>Tech title as resolved for the entries (EngineBrowserEntry.TechTitle).</summary>
        private string TechTitle(string id) => _techTitles.TryGetValue(id, out string title) ? title : id;

        /// <summary>All / None / Invert buttons for an exclusion set.</summary>
        private bool SetButtons(HashSet<string> excluded, string[] all)
        {
            bool changed = false;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("All", _btn)) { excluded.Clear(); changed = true; }
            if (GUILayout.Button("None", _btn)) { excluded.UnionWith(all); changed = true; }
            if (GUILayout.Button("Invert", _btn))
            {
                var inverted = all.Where(a => !excluded.Contains(a)).ToList();
                excluded.Clear();
                excluded.UnionWith(inverted);
                changed = true;
            }
            GUILayout.EndHorizontal();
            if (changed)
            {
                _resizeRequested = true; // the column filter summary changes width
                MarkSettingsDirty();
            }
            return changed;
        }

        private bool SetToggle(HashSet<string> excluded, string key, string label, string tooltip)
        {
            bool on = !excluded.Contains(key);
            _content.text = label;
            _content.tooltip = tooltip;
            bool now = GUILayout.Toggle(on, _content, _toggle);
            if (now == on)
                return false;
            if (now) excluded.Remove(key); else excluded.Add(key);
            _resizeRequested = true;
            MarkSettingsDirty();
            return true;
        }

        private void DrawCfgWindow(int id)
        {
            PopupHeader("Browser Settings", ref _showCfg);

            bool close = GUILayout.Toggle(_closeOnPick, new GUIContent("Close window after Pick"), _toggle);
            if (close != _closeOnPick) { _closeOnPick = close; MarkSettingsDirty(); }

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Font scale {_fontScale:F1}", _label, GUILayout.Width(110 * _fontScale));
            float scale = Mathf.Round(GUILayout.HorizontalSlider(_fontScale, 0.7f, 1.5f) * 10f) / 10f;
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(scale, _fontScale))
            {
                _fontScale = scale;
                MarkSettingsDirty();
            }

            GUILayout.Space(4);
            GUILayout.Label("Columns", _title);
            _cfgScroll = GUILayout.BeginScrollView(_cfgScroll, GUILayout.ExpandHeight(true));
            for (int c = 0; c < ColCount; c++)
            {
                string label = ColNames[c].Length > 0 ? ColNames[c] : "Pick button";
                bool v = GUILayout.Toggle(_colVisible[c], new GUIContent(label, ColTips[c]), _toggle);
                if (v != _colVisible[c])
                {
                    _colVisible[c] = v;
                    MarkSettingsDirty();
                }
            }
            GUILayout.EndScrollView();
            CollectTooltip();
        }

        #endregion

        #region Styles

        private void EnsureStyles()
        {
            if (_stylesScale == _fontScale && _cell != null)
                return;
            bool rescale = _cell != null; // styles existed: a font change, so re-measure columns
            _stylesScale = _fontScale;
            int F(int size) => Mathf.Max(8, Mathf.RoundToInt(size * _fontScale));

            _cell = new GUIStyle(GUI.skin.label)
            {
                fontSize = F(12),
                richText = true,
                wordWrap = false,
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 2, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                normal = { textColor = new Color(0.82f, 0.82f, 0.82f) }
            };
            _cellCenter = new GUIStyle(_cell) { alignment = TextAnchor.MiddleCenter };
            _name = new GUIStyle(_cell) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.95f, 0.95f, 0.95f) } };
            _nameLocked = new GUIStyle(_name) { normal = { textColor = new Color(1f, 0.62f, 0.25f) } };
            _header = new GUIStyle(_cell)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.LowerLeft,
                normal = { textColor = new Color(0.95f, 0.95f, 0.95f) },
                hover = { textColor = new Color(1f, 0.85f, 0.4f), background = EngineConfigTextures.Instance.RowHover }
            };

            _btn = new GUIStyle(HighLogic.Skin.button)
            {
                fontSize = F(11),
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(6, 6, 2, 2),
                margin = new RectOffset(2, 2, 2, 2),
                wordWrap = false
            };
            // Toggled-on buttons use the skin's pressed look plus green text; off buttons keep the
            // skin's normal light text (dimmed grey text on the grey button was hard to read).
            _btnOn = new GUIStyle(_btn);
            Texture2D pressed = _btn.onNormal.background ?? _btn.active.background;
            Texture2D pressedHover = _btn.onHover.background ?? pressed;
            if (pressed != null)
            {
                _btnOn.normal.background = pressed;
                _btnOn.hover.background = pressedHover;
            }
            _btnOn.normal.textColor = _btnOn.hover.textColor = new Color(0.45f, 1f, 0.45f);
            _btnBuy = new GUIStyle(_btn) { margin = new RectOffset(0, 0, 0, 0), padding = new RectOffset(3, 3, 1, 1) };
            _btnBuy.normal.textColor = _btnBuy.hover.textColor = new Color(1f, 0.85f, 0.3f);
            _btnOwned = new GUIStyle(_btnBuy);
            _btnOwned.normal.textColor = _btnOwned.hover.textColor = new Color(0.5f, 0.85f, 0.5f);

            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = F(12),
                wordWrap = false,
                richText = true,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
            };
            _dim = new GUIStyle(_label) { normal = { textColor = new Color(0.65f, 0.65f, 0.65f) } };
            // Clickable text (filter summary entries): looks like a label, highlights on hover.
            _link = new GUIStyle(_label) { margin = new RectOffset(2, 6, 2, 2), padding = new RectOffset(2, 2, 1, 1) };
            _link.hover.background = EngineConfigTextures.Instance.RowHover;
            _title = new GUIStyle(_label) { fontSize = F(14), fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _field = new GUIStyle(HighLogic.Skin.textField) { fontSize = F(12) };
            _fieldBad = new GUIStyle(_field);
            _fieldBad.normal.textColor = _fieldBad.focused.textColor = _fieldBad.hover.textColor = new Color(1f, 0.45f, 0.45f);
            _toggle = new GUIStyle(HighLogic.Skin.toggle) { fontSize = F(12), richText = true, wordWrap = false };
            _tooltipStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = F(12),
                richText = true,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(8, 8, 6, 6),
                normal = { textColor = Color.white, background = EngineConfigTextures.Instance.ChartTooltipBg }
            };

            if (rescale)
                _widthsDirty = true;
        }

        #endregion

        #region Editor lock

        private void UpdateEditorLock()
        {
            Vector2 mouse = Input.mousePosition;
            mouse.y = Screen.height - mouse.y;
            bool over = _windowRect.Contains(mouse)
                || (_showProps && _propsRect.Contains(mouse))
                || (_showTechs && _techsRect.Contains(mouse))
                || (_showCfg && _cfgRect.Contains(mouse))
                || (_rangeCol >= 0 && _rangeRect.Contains(mouse))
                || (_showMaterials && _materialsRect.Contains(mouse));
            bool typing = GUI.GetNameOfFocusedControl().StartsWith(FieldPrefix);
            if (over || typing)
                EditorLock();
            else
                EditorUnlock();
        }

        private void EditorLock()
        {
            if (_editorLocked || EditorLogic.fetch == null)
                return;
            EditorLogic.fetch.Lock(false, false, false, LockId);
            _editorLocked = true;
            KSP.UI.Screens.Editor.PartListTooltipMasterController.Instance?.HideTooltip();
        }

        private void EditorUnlock()
        {
            if (!_editorLocked)
                return;
            EditorLogic.fetch?.Unlock(LockId);
            _editorLocked = false;
        }

        #endregion

        #region Settings

        private void MarkSettingsDirty()
        {
            _settingsDirty = true;
            _settingsDirtyAt = Time.realtimeSinceStartup;
        }

        private static void EnsureSettings()
        {
            if (_settingsLoaded)
                return;
            _settingsLoaded = true;

            for (int c = 0; c < ColCount; c++)
                _colVisible[c] = !DefaultHidden.Contains(c);

            if (!System.IO.File.Exists(SettingsPath))
                return;
            try
            {
                ConfigNode node = ConfigNode.Load(SettingsPath)?.GetNode("ENGINE_BROWSER_SETTINGS");
                if (node == null)
                    return;

                float f = 0f;
                if (node.TryGetValue("windowX", ref f)) _windowRect.x = f;
                if (node.TryGetValue("windowY", ref f)) _windowRect.y = f;
                if (node.TryGetValue("fontScale", ref f)) _fontScale = Mathf.Clamp(f, 0.7f, 1.5f);
                int i = 0;
                if (node.TryGetValue("rows", ref i)) _visibleRows = Mathf.Clamp(i, 5, 200);
                string sortName = node.GetValue("sortCol");
                if (sortName != null)
                {
                    if (TryParseCol(sortName, out int sc))
                        _sortCol = sc;
                    else if (int.TryParse(sortName, out i) && i >= 0 && i < OldColOrder.Length && TryParseCol(OldColOrder[i], out sc))
                        _sortCol = sc; // pre-1.1 settings stored the column index
                }
                node.TryGetValue("sortAsc", ref _sortAsc);
                node.TryGetValue("closeOnPick", ref _closeOnPick);
                string tf = node.GetValue("tankFamily");
                if (tf != null && Enum.IsDefined(typeof(TankFamily), tf)) _tankFamily = (TankFamily)Enum.Parse(typeof(TankFamily), tf);
                if (node.GetValue("tankMaterials") is string tms)
                    foreach (string part in tms.Split(';'))
                    {
                        string[] kv = part.Split(':');
                        if (kv.Length == 2 && Enum.IsDefined(typeof(TankFamily), kv[0].Trim()))
                            _tankMaterial[(int)(TankFamily)Enum.Parse(typeof(TankFamily), kv[0].Trim())] = kv[1].Trim();
                    }
                string s = null;
                if (node.TryGetValue("avail", ref s) && Enum.IsDefined(typeof(AvailFilter), s)) _avail = (AvailFilter)Enum.Parse(typeof(AvailFilter), s);
                if (node.TryGetValue("storable", ref s) && Enum.IsDefined(typeof(TriState), s)) _storable = (TriState)Enum.Parse(typeof(TriState), s);
                if (node.TryGetValue("groundLit", ref s) && Enum.IsDefined(typeof(TriState), s)) _groundLit = (TriState)Enum.Parse(typeof(TriState), s);
                ParseRanges(node.GetValue("ranges"));
                // Older settings had fixed thrust / vacuum ISP range boxes.
                MigrateRange(node, "thrustMin", "thrustMax", (int)Col.Thrust);
                MigrateRange(node, "ispMin", "ispMax", (int)Col.IspVac);

                string hidden = node.GetValue("hiddenColumns");
                if (hidden != null)
                {
                    for (int c = 0; c < ColCount; c++)
                        _colVisible[c] = true;
                    foreach (string name in hidden.Split(','))
                        if (TryParseCol(name.Trim(), out int hc))
                            _colVisible[hc] = false;
                }
                else
                {
                    // Older settings: one bool per column in the old column order.
                    var old = new bool[OldColOrder.Length];
                    for (int c = 0; c < old.Length; c++)
                        old[c] = true;
                    if (node.GetValue("columns") is string cols)
                    {
                        ParseBools(cols, old);
                        for (int c = 0; c < old.Length; c++)
                            if (TryParseCol(OldColOrder[c], out int oc))
                                _colVisible[oc] = old[c];
                    }
                }
                ParseBools(node.GetValue("kinds"), _kindEnabled);
                if (!_kindEnabled.Any(k => k))
                    for (int k = 0; k < KindCount; k++) _kindEnabled[k] = true;
                ParseSet(node.GetValue("excludedProps"), _excludedProps);
                ParseSet(node.GetValue("excludedTechs"), _excludedTechs);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RFEngineBrowser] Could not load settings: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            _settingsDirty = false;
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath));
                var ic = CultureInfo.InvariantCulture;
                var root = new ConfigNode();
                ConfigNode node = root.AddNode("ENGINE_BROWSER_SETTINGS");
                node.AddValue("windowX", _windowRect.x.ToString(ic));
                node.AddValue("windowY", _windowRect.y.ToString(ic));
                node.AddValue("fontScale", _fontScale.ToString(ic));
                node.AddValue("rows", _visibleRows);
                node.AddValue("sortCol", ((Col)_sortCol).ToString());
                node.AddValue("sortAsc", _sortAsc);
                node.AddValue("closeOnPick", _closeOnPick);
                node.AddValue("tankFamily", _tankFamily.ToString());
                node.AddValue("tankMaterials", string.Join(";", Enumerable.Range(0, EngineBrowserTanks.FamilyCount).Select(i => $"{(TankFamily)i}:{_tankMaterial[i]}")));
                node.AddValue("avail", _avail.ToString());
                node.AddValue("storable", _storable.ToString());
                node.AddValue("groundLit", _groundLit.ToString());
                node.AddValue("hiddenColumns", string.Join(",", Enumerable.Range(0, ColCount).Where(c => !_colVisible[c]).Select(c => ((Col)c).ToString())));
                node.AddValue("ranges", string.Join(";", _ranges.Select(kv => $"{(Col)kv.Key}:{kv.Value[0].Trim()}:{kv.Value[1].Trim()}")));
                node.AddValue("kinds", string.Join(",", _kindEnabled));
                node.AddValue("excludedProps", string.Join(",", _excludedProps));
                node.AddValue("excludedTechs", string.Join(",", _excludedTechs));
                root.Save(SettingsPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RFEngineBrowser] Could not save settings: {ex.Message}");
            }
        }

        /// <summary>Column order of settings files written before columns were saved by name.</summary>
        private static readonly string[] OldColOrder = {
            "Family", "Config", "Type", "Thrust", "MinThrottle", "IspVac", "IspSL", "Mass", "Gimbal", "Igns", "Ullage", "PFed",
            "Propellants", "Store", "Rated", "Tested", "IgnRel", "DU0", "DUMax", "Tech", "Spec", "Cost", "Entry", "Pick"
        };

        private static bool TryParseCol(string name, out int col)
        {
            col = -1;
            if (string.IsNullOrEmpty(name) || !Enum.IsDefined(typeof(Col), name))
                return false;
            col = (int)(Col)Enum.Parse(typeof(Col), name);
            return true;
        }

        /// <summary>Parses "Thrust:100:;IspVac::450" into column ranges.</summary>
        private static void ParseRanges(string s)
        {
            _ranges.Clear();
            if (string.IsNullOrEmpty(s))
                return;
            foreach (string part in s.Split(';'))
            {
                string[] f = part.Split(':');
                if (f.Length == 3 && TryParseCol(f[0].Trim(), out int col) && IsNumeric(col) && (f[1].Trim().Length > 0 || f[2].Trim().Length > 0))
                    _ranges[col] = new[] { f[1].Trim(), f[2].Trim() };
            }
        }

        private static void MigrateRange(ConfigNode node, string minKey, string maxKey, int col)
        {
            string lo = node.GetValue(minKey)?.Trim() ?? string.Empty;
            string hi = node.GetValue(maxKey)?.Trim() ?? string.Empty;
            if ((lo.Length > 0 || hi.Length > 0) && !_ranges.ContainsKey(col))
                _ranges[col] = new[] { lo, hi };
        }

        private static void ParseBools(string s, bool[] target)
        {
            if (string.IsNullOrEmpty(s))
                return;
            string[] parts = s.Split(',');
            for (int i = 0; i < parts.Length && i < target.Length; i++)
                if (bool.TryParse(parts[i].Trim(), out bool b))
                    target[i] = b;
        }

        private static void ParseSet(string s, HashSet<string> target)
        {
            target.Clear();
            if (string.IsNullOrEmpty(s))
                return;
            foreach (string part in s.Split(','))
                if (part.Trim().Length > 0)
                    target.Add(part.Trim());
        }

        #endregion
    }
}
