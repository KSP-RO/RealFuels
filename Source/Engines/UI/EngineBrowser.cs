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
            Family, Config, Type, Thrust, MinThrottle, IspVac, IspSL, Mass, Gimbal, Igns, Ullage, PFed,
            Propellants, Store, Rated, Tested, IgnRel, DU0, DUMax, Tech, Spec, Cost, Entry, Pick
        }
        private const int ColCount = 24;

        private static readonly string[] ColNames = {
            "Engine Family", "Config", "Type", "Thrust", "Min%", "ISP (Vac)", "ISP (SL)", "Mass", "Gimbal", "Igns", "Ullg", "PFed",
            "Propellants", "Store", "Rated", "Tested", "Ign%", "0-DU", "Max-DU", "Tech", "Spec", "Cost", "Entry Cost", ""
        };

        private static readonly string[] ColTips = {
            "Part title", "Engine configuration", "Engine type", "Maximum thrust (vacuum)", "Minimum throttle",
            "Vacuum specific impulse", "Sea-level specific impulse", "Engine mass", "Gimbal range", "Ignitions (Gnd = ground-lit only)",
            "Requires ullage", "Pressure-fed", "Propellants", "Storable: no cryogenic (boil-off) propellants",
            "Rated burn time (continuous / cumulative)", "Tested burn time", "Ignition reliability (0 data / max data)",
            "Cycle reliability at 0 data", "Cycle reliability at max data", "Tech node required", "Specification level",
            "Part cost with this config", "Config entry cost. Click twice to buy.", "Spawn this part with this config"
        };

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

        // ── Persistent settings (static so they survive VAB/SPH switches) ──
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
        private static string _thrustMin = "", _thrustMax = "", _ispMin = "", _ispMax = "";
        private static readonly HashSet<string> _excludedProps = new HashSet<string>();
        private static readonly HashSet<string> _excludedTechs = new HashSet<string>();
        private static bool _closeOnPick = true;

        // ── Window state ──
        private const int WindowId = 0x52464542; // "RFEB"
        private const int PropsWindowId = WindowId + 1;
        private const int TechsWindowId = WindowId + 2;
        private const int CfgWindowId = WindowId + 3;
        private const int TooltipWindowId = WindowId + 4;
        private const string LockId = "RFEngineBrowserLock";
        private const string FieldPrefix = "RFEB_";

        private ApplicationLauncherButton _button;
        private Texture2D _ownIcon;
        private bool _show;
        private bool _showProps, _showTechs, _showCfg;
        private Rect _propsRect, _techsRect, _cfgRect;
        private Rect _propsBtnRect, _techsBtnRect, _cfgBtnRect;
        private Vector2 _tableScroll, _propsScroll, _techsScroll, _cfgScroll;
        private string _search = "";
        private string _rowsInput;
        private bool _editorLocked;
        private string _tooltip = string.Empty;
        private string _collectedTooltip = string.Empty;
        private bool _settingsDirty;
        private float _settingsDirtyAt;

        // ── Data state ──
        private readonly List<EngineBrowserEntry> _filtered = new List<EngineBrowserEntry>();
        private bool _filterDirty = true;
        private float _lastStateRefresh = float.MinValue;
        private const float StateRefreshInterval = 1f;
        private readonly float[] _colWidths = new float[ColCount];
        private bool _widthsDirty = true;
        private string[] _allProps, _allTechs;
        private readonly Dictionary<string, int> _propCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _techCounts = new Dictionary<string, int>();
        private EngineBrowserEntry _confirmBuy;
        private float _confirmBuyUntil;
        private bool _picking;

        // ── Styles ──
        private float _stylesScale = -1f;
        private GUIStyle _cell, _cellCenter, _name, _nameLocked, _header, _btn, _btnOn, _btnOff,
            _btnBuy, _btnOwned, _label, _title, _field, _tooltipStyle, _toggle, _dim;
        private readonly GUIContent _content = new GUIContent();

        private float RowHeight => Mathf.Round(20f * _fontScale);

        #region Lifecycle

        private void Start()
        {
            EnsureSettings();
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            GameEvents.onGUIApplicationLauncherUnreadifying.Add(RemoveButton);
            if (ApplicationLauncher.Ready)
                AddButton();
        }

        private void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            GameEvents.onGUIApplicationLauncherUnreadifying.Remove(RemoveButton);
            RemoveButton(GameScenes.EDITOR);
            EditorUnlock();
            if (_settingsDirty)
                SaveSettings();
            if (_ownIcon != null)
                Destroy(_ownIcon);
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
                if (_widthsDirty)
                    BuildCellsAndWidths();
                if (Time.realtimeSinceStartup - _lastStateRefresh > StateRefreshInterval)
                    RefreshState();
                if (_filterDirty)
                    ApplyFilter();
            }

            if (Event.current.type == EventType.Repaint)
                _collectedTooltip = string.Empty;

            _windowRect.width = WindowWidth();
            _windowRect.height = 50f; // GUILayout grows it to fit the content
            Rect prev = _windowRect;
            _windowRect = ClickThruBlocker.GUILayoutWindow(WindowId, _windowRect, DrawWindow, "", Styles.styleEditorPanel);
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Mathf.Max(0, Screen.width - 100));
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Mathf.Max(0, Screen.height - 60));
            if (prev.x != _windowRect.x || prev.y != _windowRect.y)
                MarkSettingsDirty();

            if (_showProps)
                _propsRect = DrawPopup(PropsWindowId, _propsRect, _propsBtnRect, DrawPropsWindow);
            if (_showTechs)
                _techsRect = DrawPopup(TechsWindowId, _techsRect, _techsBtnRect, DrawTechsWindow);
            if (_showCfg)
                _cfgRect = DrawPopup(CfgWindowId, _cfgRect, _cfgBtnRect, DrawCfgWindow);

            if (Event.current.type == EventType.Repaint)
                _tooltip = _collectedTooltip;
            DrawTooltip();

            UpdateEditorLock();

            if (_settingsDirty && Time.realtimeSinceStartup - _settingsDirtyAt > 1f)
                SaveSettings();
        }

        private Rect DrawPopup(int id, Rect rect, Rect anchor, GUI.WindowFunction fn)
        {
            float w = Mathf.Round(280f * _fontScale);
            float h = Mathf.Min(Mathf.Round(460f * _fontScale), Screen.height - 40f);
            float x = Mathf.Clamp(_windowRect.x + anchor.x, 0, Screen.width - w);
            float y = Mathf.Clamp(_windowRect.y + anchor.yMax + 2f, 0, Screen.height - h);
            return ClickThruBlocker.GUIWindow(id, new Rect(x, y, w, h), fn, "", Styles.styleEditorPanel);
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
            float w = TableWidth() + 16f + 12f;
            return Mathf.Clamp(w, 900f * _fontScale, Screen.width - 20f);
        }

        private void DrawWindow(int id)
        {
            var entries = EngineBrowserDatabase.Entries;
            bool changed = false;

            // ── Title row ──
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

            // ── Filter row ──
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", _label);
            GUI.SetNextControlName(FieldPrefix + "search");
            string s = GUILayout.TextField(_search, _field, GUILayout.Width(200 * _fontScale));
            if (s != _search) { _search = s; _filterDirty = true; }
            GUILayout.Space(12);

            GUILayout.Label("Type", _label);
            bool all = _kindEnabled.All(k => k);
            if (GUILayout.Button(new GUIContent("All", "Show every engine type"), all ? _btnOn : _btnOff))
            {
                for (int k = 0; k < KindCount; k++) _kindEnabled[k] = true;
                changed = true;
            }
            for (int k = 0; k < KindCount; k++)
            {
                _content.text = KindLabels[k];
                _content.tooltip = KindTips[k] + (all ? "\nClick to show only this type." : "\nClick to toggle.");
                if (GUILayout.Button(_content, _kindEnabled[k] && !all ? _btnOn : (all ? _btn : _btnOff)))
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
                "Cycle: any / tech researched / researched and entry cost paid (or free)"), _avail == AvailFilter.Any ? _btnOff : _btnOn))
            {
                _avail = (AvailFilter)(((int)_avail + 1) % 3);
                changed = true;
            }
            if (GUILayout.Button(new GUIContent(TriLabel(_storable, "Storable", "Storable", "Cryogenic"),
                "Cycle: don't care / storable only (no boil-off) / cryogenic only"), _storable == TriState.Any ? _btnOff : _btnOn))
            {
                _storable = Cycle(_storable);
                changed = true;
            }
            if (GUILayout.Button(new GUIContent(TriLabel(_groundLit, "Gnd Lit", "Gnd Lit", "Air Lit"),
                "Cycle: don't care / ground-lit only (pad ignition, no in-flight ignitions) / not ground-lit"), _groundLit == TriState.Any ? _btnOff : _btnOn))
            {
                _groundLit = Cycle(_groundLit);
                changed = true;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // ── Range / dropdown row ──
            GUILayout.BeginHorizontal();
            changed |= RangeFields("Thrust", "kN", ref _thrustMin, ref _thrustMax, "thrust");
            GUILayout.Space(12);
            changed |= RangeFields("Vac ISP", "s", ref _ispMin, ref _ispMax, "isp");
            GUILayout.Space(16);

            string propLabel = _excludedProps.Count > 0 ? $"Propellants ({_allProps.Length - _excludedProps.Count}/{_allProps.Length}) ▾" : "Propellants ▾";
            if (GUILayout.Button(new GUIContent(propLabel, "Choose which propellants engines may use"),
                _excludedProps.Count > 0 || _showProps ? _btnOn : _btn, GUILayout.Width(170 * _fontScale)))
            {
                _showProps = !_showProps;
                _showTechs = false;
            }
            if (Event.current.type == EventType.Repaint)
                _propsBtnRect = GUILayoutUtility.GetLastRect();

            string techLabel = _excludedTechs.Count > 0 ? $"Tech Required ({_allTechs.Length - _excludedTechs.Count}/{_allTechs.Length}) ▾" : "Tech Required ▾";
            if (GUILayout.Button(new GUIContent(techLabel, "Choose which tech nodes to include"),
                _excludedTechs.Count > 0 || _showTechs ? _btnOn : _btn, GUILayout.Width(190 * _fontScale)))
            {
                _showTechs = !_showTechs;
                _showProps = false;
            }
            if (Event.current.type == EventType.Repaint)
                _techsBtnRect = GUILayoutUtility.GetLastRect();

            GUILayout.Space(12);
            if (GUILayout.Button(new GUIContent("Reset Filters", "Clear search and all filters"), _btn))
            {
                ResetFilters();
                changed = true;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (changed)
            {
                _filterDirty = true;
                MarkSettingsDirty();
            }

            GUILayout.Space(4);

            // ── Table ──
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

        private bool RangeFields(string label, string unit, ref string min, ref string max, string name)
        {
            bool changed = false;
            GUILayout.Label(label, _label);
            GUI.SetNextControlName(FieldPrefix + name + "Min");
            string a = GUILayout.TextField(min, 10, _field, GUILayout.Width(64 * _fontScale));
            GUILayout.Label("–", _label);
            GUI.SetNextControlName(FieldPrefix + name + "Max");
            string b = GUILayout.TextField(max, 10, _field, GUILayout.Width(64 * _fontScale));
            GUILayout.Label(unit, _dim);
            if (a != min) { min = a; changed = true; }
            if (b != max) { max = b; changed = true; }
            return changed;
        }

        private void ResetFilters()
        {
            _search = "";
            for (int k = 0; k < KindCount; k++) _kindEnabled[k] = true;
            _avail = AvailFilter.Any;
            _storable = _groundLit = TriState.Any;
            _thrustMin = _thrustMax = _ispMin = _ispMax = "";
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
                string label = ColNames[c];
                if (c == _sortCol)
                    label += _sortAsc ? " ↑" : " ↓";
                _content.text = label;
                _content.tooltip = ColTips[c] + (c == (int)Col.Pick ? string.Empty : "\nClick to sort.");
                if (GUI.Button(new Rect(x, 0, w, headerH), _content, _header) && c != (int)Col.Pick)
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
                sb.Append($"<color=#FFA726>Requires:</color> {e.TechTitle}{(e.Researched ? "" : " <color=#FF8A65>(not researched)</color>")}\n");
            if (e.Propellants.Length > 0)
            {
                var props = e.Propellants.Select(p => EngineBrowserDatabase.IsStorable(p) ? p : $"{p} <color=#80D9FF>(cryo)</color>");
                sb.Append($"<color=#FFA726>Propellants:</color> {string.Join(", ", props)}\n");
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
            foreach (var e in EngineBrowserDatabase.Entries)
            {
                bool researched = EngineConfigTechLevels.CanConfig(e.Node);
                bool unlocked = EngineConfigTechLevels.UnlockedConfig(e.Node, e.Part.partPrefab);
                double cost = EntryCostManager.Instance != null ? EntryCostManager.Instance.ConfigEntryCost(e.ConfigName) : 0d;
                bool partAvailable = sandbox || ResearchAndDevelopment.PartModelPurchased(e.Part) || ResearchAndDevelopment.IsExperimentalPart(e.Part);
                if (researched != e.Researched || unlocked != e.Unlocked || cost != e.EntryCost || partAvailable != e.PartAvailable)
                {
                    e.Researched = researched;
                    e.Unlocked = unlocked;
                    e.EntryCost = cost;
                    e.PartAvailable = partAvailable;
                    e.Tooltip = null;
                    changed = true;
                }
            }
            if (changed)
                _filterDirty = true;
        }

        private static float ParseOrNaN(string s)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
               || float.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out f) ? f : float.NaN;

        private void ApplyFilter()
        {
            _filterDirty = false;
            string[] terms = _search.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            float tMin = ParseOrNaN(_thrustMin), tMax = ParseOrNaN(_thrustMax);
            float iMin = ParseOrNaN(_ispMin), iMax = ParseOrNaN(_ispMax);

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
                if (!float.IsNaN(tMin) && !(e.Thrust >= tMin)) continue;
                if (!float.IsNaN(tMax) && !(e.Thrust >= 0 && e.Thrust <= tMax)) continue;
                if (!float.IsNaN(iMin) && !(e.IspVac >= iMin)) continue;
                if (!float.IsNaN(iMax) && !(e.IspVac >= 0 && e.IspVac <= iMax)) continue;
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
            _filtered.Sort((a, b) =>
            {
                int r = Compare(a, b, col) * dir;
                if (r == 0) r = string.Compare(a.Family, b.Family, StringComparison.OrdinalIgnoreCase);
                if (r == 0) r = string.Compare(a.Config, b.Config, StringComparison.OrdinalIgnoreCase);
                return r;
            });
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

        private static int IgnKey(int ign) => ign == EngineBrowserEntry.IgnUnlimited ? int.MaxValue : ign;

        private static int Compare(EngineBrowserEntry a, EngineBrowserEntry b, int col)
        {
            switch ((Col)col)
            {
                case Col.Family: return string.Compare(a.Family, b.Family, StringComparison.OrdinalIgnoreCase);
                case Col.Config: return string.Compare(a.Config, b.Config, StringComparison.OrdinalIgnoreCase);
                case Col.Type: return a.Kind.CompareTo(b.Kind);
                case Col.Thrust: return a.Thrust.CompareTo(b.Thrust);
                case Col.MinThrottle: return a.MinThrottle.CompareTo(b.MinThrottle);
                case Col.IspVac: return a.IspVac.CompareTo(b.IspVac);
                case Col.IspSL: return a.IspSL.CompareTo(b.IspSL);
                case Col.Mass: return a.Mass.CompareTo(b.Mass);
                case Col.Gimbal: return a.Gimbal.CompareTo(b.Gimbal);
                case Col.Igns: return IgnKey(a.Ignitions).CompareTo(IgnKey(b.Ignitions));
                case Col.Ullage: return a.Ullage.CompareTo(b.Ullage);
                case Col.PFed: return a.PressureFed.CompareTo(b.PressureFed);
                case Col.Propellants: return string.Compare(a.PropellantText, b.PropellantText, StringComparison.OrdinalIgnoreCase);
                case Col.Store: return a.Storable.CompareTo(b.Storable);
                case Col.Rated: return a.Rated.CompareTo(b.Rated);
                case Col.Tested: return a.Tested.CompareTo(b.Tested);
                case Col.IgnRel: return a.IgnEnd.CompareTo(b.IgnEnd);
                case Col.DU0: return a.CycleStart.CompareTo(b.CycleStart);
                case Col.DUMax: return a.CycleEnd.CompareTo(b.CycleEnd);
                case Col.Tech: return string.Compare(a.Tech, b.Tech, StringComparison.OrdinalIgnoreCase);
                case Col.Spec: return SpecRank(a.Spec).CompareTo(SpecRank(b.Spec));
                case Col.Cost: return a.Cost.CompareTo(b.Cost);
                case Col.Entry: return (a.Unlocked ? -1d : a.EntryCost).CompareTo(b.Unlocked ? -1d : b.EntryCost);
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
            foreach (var e in entries)
            {
                if (e.Cells == null)
                    e.Cells = BuildCells(e);
                foreach (string p in e.Propellants)
                    _propCounts[p] = _propCounts.TryGetValue(p, out int n) ? n + 1 : 1;
                string tech = e.Tech;
                _techCounts[tech] = _techCounts.TryGetValue(tech, out int m) ? m + 1 : 1;
            }
            _allProps = _propCounts.Keys.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            _allTechs = _techCounts.Keys.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray();

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
            c[(int)Col.MinThrottle] = e.MinThrottle >= 0f ? $"{e.MinThrottle * 100f:0} %" : "-";
            c[(int)Col.IspVac] = e.IspVac > 0f ? $"{e.IspVac:N0} s" : "-";
            c[(int)Col.IspSL] = e.IspSL > 0f ? $"{e.IspSL:N0} s" : "-";
            c[(int)Col.Mass] = e.Mass >= 0f ? $"{e.Mass:N3} t" : "-";
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
            c[(int)Col.Tech] = e.Tech.Length > 0 ? e.Tech : "-";
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
                string name = t.Length > 0 ? t : "(none)";
                string title = t.Length > 0 && ModuleEngineConfigsBase.techNameToTitle.TryGetValue(t, out string tt) ? tt : null;
                if (SetToggle(_excludedTechs, t, $"{name} ({_techCounts[t]})", title))
                    _filterDirty = true;
            }
            GUILayout.EndScrollView();
            CollectTooltip();
        }

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
                MarkSettingsDirty();
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
                _stylesScale = -1f; // rebuilt next frame
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
            bool rescale = _stylesScale > 0f;
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
            _btnOn = new GUIStyle(_btn);
            _btnOn.normal.textColor = _btnOn.hover.textColor = new Color(0.45f, 1f, 0.45f);
            _btnOff = new GUIStyle(_btn);
            _btnOff.normal.textColor = new Color(0.65f, 0.65f, 0.65f);
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
            _title = new GUIStyle(_label) { fontSize = F(14), fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _field = new GUIStyle(HighLogic.Skin.textField) { fontSize = F(12) };
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
                || (_showCfg && _cfgRect.Contains(mouse));
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
                if (node.TryGetValue("sortCol", ref i) && i >= 0 && i < ColCount) _sortCol = i;
                node.TryGetValue("sortAsc", ref _sortAsc);
                node.TryGetValue("closeOnPick", ref _closeOnPick);
                string s = null;
                if (node.TryGetValue("avail", ref s) && Enum.IsDefined(typeof(AvailFilter), s)) _avail = (AvailFilter)Enum.Parse(typeof(AvailFilter), s);
                if (node.TryGetValue("storable", ref s) && Enum.IsDefined(typeof(TriState), s)) _storable = (TriState)Enum.Parse(typeof(TriState), s);
                if (node.TryGetValue("groundLit", ref s) && Enum.IsDefined(typeof(TriState), s)) _groundLit = (TriState)Enum.Parse(typeof(TriState), s);
                _thrustMin = node.GetValue("thrustMin") ?? "";
                _thrustMax = node.GetValue("thrustMax") ?? "";
                _ispMin = node.GetValue("ispMin") ?? "";
                _ispMax = node.GetValue("ispMax") ?? "";
                ParseBools(node.GetValue("columns"), _colVisible);
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
                node.AddValue("sortCol", _sortCol);
                node.AddValue("sortAsc", _sortAsc);
                node.AddValue("closeOnPick", _closeOnPick);
                node.AddValue("avail", _avail.ToString());
                node.AddValue("storable", _storable.ToString());
                node.AddValue("groundLit", _groundLit.ToString());
                node.AddValue("thrustMin", _thrustMin);
                node.AddValue("thrustMax", _thrustMax);
                node.AddValue("ispMin", _ispMin);
                node.AddValue("ispMax", _ispMax);
                node.AddValue("columns", string.Join(",", _colVisible));
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
