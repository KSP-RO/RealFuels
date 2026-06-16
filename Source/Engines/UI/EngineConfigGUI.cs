using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using ClickThroughFix;
using KSP.Localization;
using KSP.UI.Screens;
using RealFuels.TechLevels;

namespace RealFuels
{
    /// <summary>
    /// Handles all GUI rendering for ModuleEngineConfigs.
    /// Manages the configuration selector window, column visibility, tooltips, and user interaction.
    /// </summary>
    public class EngineConfigGUI
    {
        private readonly ModuleEngineConfigsBase _module;
        private readonly EngineConfigTechLevels _techLevels;
        private readonly EngineConfigTextures _textures;
        private EngineConfigChart _chart;

        // GUI state
        private static Vector3 mousePos = Vector3.zero;
        private static Rect guiWindowRect = new Rect(0, 0, 0, 0);
        private static uint lastPartId = 0;
        private static int lastConfigCount = 0;
        private static bool lastCompactView = false;
        private static bool lastHasChart = false;
        private static bool lastShowSimPanel   = true;
        private static bool lastShowChartPanel = false;
        private static float lastFontScale = 1.0f;
        private string myToolTip = string.Empty;
        private int counterTT;
        private bool editorLocked = false;

        private Vector2 configScrollPos = Vector2.zero;
        private GUIContent configGuiContent;
        private static bool compactView = true;    // Default to compact view
        private bool useLogScaleX = false;
        private bool useLogScaleY = false;

        // Three-panel visibility flags (replaces the old single showBottomSection toggle)
        private static bool showSimPanel   = true;   // Panel 2: reliability stats + sim controls
        private static bool showChartPanel = false;  // Panel 3: chart only (hidden by default)

        // Column visibility customization
        private bool showColumnMenu = false;
        private static Rect columnMenuRect = new Rect(100, 100, 220, 500);
        private static bool[] columnsVisibleFull = new bool[18];
        private static bool[] columnsVisibleCompact = new bool[18];
        private static bool columnVisibilityInitialized = false;

        // Settings persistence
        private static readonly string SettingsPath = System.IO.Path.Combine(
            KSPUtil.ApplicationRootPath, "GameData", "RealFuels", "PluginData", "EngineConfigGUISettings.cfg");
        private static bool _settingsLoaded = false;
        private static bool _windowPositionRestored = false;
        private static bool _positionDirty = false;
        private static int _positionDirtyFrame = 0;

        // Font scaling (item 1)
        private static float _fontScale = 1.0f;

        // Burn-time display mode: false = seconds only, true = m:ss (item 9)
        private static bool _showTimeAsMinSec = false;

        // Simulation controls
        private bool useSimulatedData = false;
        private float simulatedDataValue = 0f;
        private int clusterSize = 1;
        private string clusterSizeInput = "1";
        private string dataValueInput = "0";
        private float sliderTime = 100f;
        private string sliderTimeInput = "100.0";
        private static bool includeIgnition = false; // Static so it persists across all engines
        private static bool sliderModeIsPercentage = false; // Static so it persists across all engines
        private float sliderPercentage = 95.0f;
        private string sliderPercentageInput = "95.0";

        private const int ConfigRowHeight = 22;
        private const int ConfigMaxVisibleRows = 16;
        private float[] ConfigColumnWidths = new float[18];

        private int toolTipWidth => EditorLogic.fetch.editorScreen == EditorScreen.Parts ? 320 : 380;

        // Window IDs — XOR with a RealFuels-specific magic constant so our windows never collide
        // with windows from other mods (e.g. RP-1's "Show Part Info") that also derive their IDs
        // from part.persistentId ± small offsets.  A collision makes Unity store layout positions
        // from the wrong window and produces the "Getting control N's position in a group with only
        // M controls when doing repaint" spam.
        private const int RFWindowMagic = unchecked((int)0x52465547); // "RFUG" — RealFuels Unique GUID
        private int MainWindowId    => unchecked((int)_module.part.persistentId) ^ RFWindowMagic;
        private int ColumnMenuId    => (unchecked((int)_module.part.persistentId) ^ RFWindowMagic) + 1;
        private int TooltipWindowId => (unchecked((int)_module.part.persistentId) ^ RFWindowMagic) + 2;

        public EngineConfigGUI(ModuleEngineConfigsBase module)
        {
            _module = module;
            _techLevels = new EngineConfigTechLevels(module);
            _textures = EngineConfigTextures.Instance;
        }

        private EngineConfigChart Chart
        {
            get
            {
                if (_chart == null)
                {
                    _chart = new EngineConfigChart(_module);
                    _chart.UseLogScaleX = useLogScaleX;
                    _chart.UseLogScaleY = useLogScaleY;
                    _chart.UseSimulatedData = useSimulatedData;
                    _chart.SimulatedDataValue = simulatedDataValue;
                    _chart.ClusterSize = clusterSize;
                }
                return _chart;
            }
        }

        #region Main GUI Entry Point

        public void OnGUI()
        {
            if (!_module.compatible || !_module.isMaster || !HighLogic.LoadedSceneIsEditor || EditorLogic.fetch == null)
                return;

            bool inPartsEditor = EditorLogic.fetch.editorScreen == EditorScreen.Parts;
            if (!(_module.showRFGUI && inPartsEditor))
            {
                EditorUnlock();
                return;
            }

            if (inPartsEditor && _module.part.symmetryCounterparts.FirstOrDefault(p => p.persistentId < _module.part.persistentId) is Part)
                return;

            // Load saved settings (column visibility, window position, view flags) on the
            // first OnGUI call.  Must run before any static state is read below.
            EnsureSettings();

            if (guiWindowRect.width == 0 && !_windowPositionRestored)
            {
                int posAdd = inPartsEditor ? 256 : 0;
                int posMult = (_module.offsetGUIPos == -1) ? (_module.part.Modules.Contains("ModuleFuelTanks") ? 1 : 0) : _module.offsetGUIPos;
                guiWindowRect = new Rect(posAdd + 430 * posMult, 365, 100, 100);
            }

            uint currentPartId = _module.part.persistentId;
            int currentConfigCount = _module.FilteredDisplayConfigs(false).Count;
            bool currentHasChart = CanShowChart(_module.config);
            bool contentChanged = currentPartId != lastPartId
                               || currentConfigCount != lastConfigCount
                               || compactView != lastCompactView
                               || currentHasChart != lastHasChart
                               || showSimPanel    != lastShowSimPanel
                               || showChartPanel  != lastShowChartPanel
                               || !Mathf.Approximately(_fontScale, lastFontScale);

            if (contentChanged)
            {
                float savedX = guiWindowRect.x;
                float savedY = guiWindowRect.y;
                float savedWidth = guiWindowRect.width;
                guiWindowRect = new Rect(savedX, savedY, savedWidth, 100);

                lastPartId = currentPartId;
                lastConfigCount = currentConfigCount;
                lastCompactView = compactView;
                lastHasChart = currentHasChart;
                lastShowSimPanel   = showSimPanel;
                lastShowChartPanel = showChartPanel;
                lastFontScale = _fontScale;
            }

            mousePos = Input.mousePosition;
            mousePos.y = Screen.height - mousePos.y;
            if (guiWindowRect.Contains(mousePos))
                EditorLock();
            else
                EditorUnlock();

            myToolTip = myToolTip.Trim();

            Rect prevWindowRect = guiWindowRect;
            guiWindowRect = ClickThruBlocker.GUILayoutWindow(MainWindowId, guiWindowRect, EngineManagerGUI, "", Styles.styleEditorPanel);
            if (guiWindowRect.x != prevWindowRect.x || guiWindowRect.y != prevWindowRect.y)
            {
                _positionDirty = true;
                _positionDirtyFrame = Time.frameCount;
            }

            if (showColumnMenu)
            {
                // Keep the Settings window dimensions in sync with the current font scale so
                // the scaled text always has room to breathe.
                columnMenuRect.width  = Mathf.Max(200, Mathf.RoundToInt(250 * _fontScale));
                columnMenuRect.height = Mathf.Max(300, Mathf.RoundToInt(500 * _fontScale));

                Rect prevMenuRect = columnMenuRect;
                columnMenuRect = ClickThruBlocker.GUIWindow(ColumnMenuId, columnMenuRect, DrawColumnMenuWindow, "", Styles.styleEditorPanel);
                if (columnMenuRect.x != prevMenuRect.x || columnMenuRect.y != prevMenuRect.y)
                {
                    _positionDirty = true;
                    _positionDirtyFrame = Time.frameCount;
                }
            }

            // Debounced position save: write to disk ~0.5 s after the last drag movement.
            if (_positionDirty && Time.frameCount > _positionDirtyFrame + 30)
            {
                _positionDirty = false;
                SaveSettings();
            }

            // Draw tooltip AFTER all windows.
            // IMPORTANT: GUI.Box drawn outside any window renders BEHIND all GUI.Window calls in
            // Unity IMGUI.  Wrapping in GUI.Window guarantees it renders on top because Unity
            // composites windows in call order (last = topmost).
            if (!string.IsNullOrEmpty(myToolTip))
            {
                bool isButtonTooltip = myToolTip.StartsWith("[BTN]");
                string displayText = isButtonTooltip ? myToolTip.Substring(5) : myToolTip;

                // Scale tooltip font with the rest of the UI (base 13 px).
                var tooltipStyle = new GUIStyle(EngineConfigStyles.ChartTooltip)
                {
                    fontSize = Mathf.Max(9, Mathf.RoundToInt(13 * _fontScale)),
                    wordWrap = false,
                    normal = { background = _textures.ChartTooltipBg }
                };

                var content = new GUIContent(displayText);

                float actualTooltipWidth, tooltipHeight;
                if (isButtonTooltip)
                {
                    Vector2 sz = tooltipStyle.CalcSize(content);
                    actualTooltipWidth = Mathf.Min(sz.x + 20, 400);
                    tooltipStyle.wordWrap = actualTooltipWidth >= 400;
                    tooltipHeight = tooltipStyle.CalcHeight(content, actualTooltipWidth);
                }
                else
                {
                    tooltipStyle.wordWrap = true;
                    actualTooltipWidth = toolTipWidth;
                    tooltipHeight = tooltipStyle.CalcHeight(content, actualTooltipWidth);
                }

                float tooltipX, tooltipY;
                if (isButtonTooltip)
                {
                    tooltipX = mousePos.x + 20;
                    tooltipY = mousePos.y + 10;
                    if (tooltipX + actualTooltipWidth > Screen.width)
                        tooltipX = mousePos.x - actualTooltipWidth - 10;
                    if (tooltipY + tooltipHeight > Screen.height)
                        tooltipY = Screen.height - tooltipHeight - 10;
                }
                else
                {
                    int offset = inPartsEditor ? -330 : 440;
                    tooltipX = guiWindowRect.xMin + offset;
                    tooltipY = mousePos.y - 5;
                }

                // Capture locals so the lambda is stable across layout/repaint events.
                string   ttText  = displayText;
                GUIStyle ttStyle = tooltipStyle;
                float    ttW     = actualTooltipWidth;
                float    ttH     = tooltipHeight;
                // Use ClickThruBlocker so the tooltip's uGUI overlay panel is present.
                // Without it, EventSystem.current.IsPointerOverGameObject() returns false
                // over the tooltip, making EditorActionPartSelector.LateUpdate() think the
                // mouse is over open space and allowing click-through to deselect the part.
                ClickThruBlocker.GUIWindow(
                    TooltipWindowId,
                    new Rect(tooltipX, tooltipY, ttW, ttH),
                    _ => GUI.Box(new Rect(0, 0, ttW, ttH), ttText, ttStyle),
                    GUIContent.none, GUIStyle.none);
            }
        }

        #endregion

        #region GUI Windows

        private void EngineManagerGUI(int WindowID)
        {
            // Must initialize before any EngineConfigStyles.* property is accessed.
            // EnsureTexturesAndStyles is also called inside DrawConfigTable, but styles
            // are used earlier in this method (DescriptionLabel, CloseButton, etc.) and
            // a null GUIStyle throws a NullReferenceException that collapses the window.
            EnsureTexturesAndStyles();

            GUILayout.BeginVertical(GUILayout.ExpandHeight(false));
            GUILayout.Space(4);

            // Item 6: Composite description — "Configure [part]: [description]"
            GUILayout.BeginHorizontal();
            string descText = $"<b>Configure {_module.part.partInfo.title}:</b>  <color=#B4B4B4>{_module.EditorDescription}</color>";
            GUILayout.Label(descText, EngineConfigStyles.DescriptionLabel);
            GUILayout.FlexibleSpace();

            GUIStyle hdrBtn = EngineConfigStyles.CompactButton;

            // All header button widths scale with font so they never overflow at high scales.
            // Base values are tuned for 1.0× — multiplying by _fontScale keeps them proportional.
            int w_view    = Mathf.RoundToInt(58  * _fontScale);
            int w_timefmt = Mathf.RoundToInt(48  * _fontScale);
            int w_sim     = Mathf.RoundToInt(68  * _fontScale);
            int w_chart   = Mathf.RoundToInt(78  * _fontScale);
            int w_heatmap = Mathf.RoundToInt(62  * _fontScale);
            int w_settings= Mathf.RoundToInt(65  * _fontScale);

            // View toggle — "Full" / "Compact"
            if (GUILayout.Button(compactView ? "Full" : "Compact", hdrBtn, GUILayout.Width(w_view)))
            {
                compactView = !compactView;
                SaveSettings();
            }

            // Burn-time format toggle
            if (GUILayout.Button(_showTimeAsMinSec ? "m:ss" : "Sec", hdrBtn, GUILayout.Width(w_timefmt)))
            {
                _showTimeAsMinSec = !_showTimeAsMinSec;
                SaveSettings();
            }

            // Three-panel toggles
            bool canChart = CanShowChart(_module.config);
            if (GUILayout.Button(showSimPanel ? "Hide Sim" : "Show Sim", hdrBtn, GUILayout.Width(w_sim)))
            {
                showSimPanel = !showSimPanel;
                SaveSettings();
            }
            if (canChart)
            {
                if (GUILayout.Button(showChartPanel ? "Hide Chart" : "Show Chart", hdrBtn, GUILayout.Width(w_chart)))
                {
                    showChartPanel = !showChartPanel;
                    SaveSettings();
                }
                if (showChartPanel)
                {
                    bool currentHeatmapMode = Chart.UseHeatmapMode;
                    if (GUILayout.Button(currentHeatmapMode ? "Line" : "Heatmap", hdrBtn, GUILayout.Width(w_heatmap)))
                        Chart.UseHeatmapMode = !currentHeatmapMode;
                }
            }

            if (GUILayout.Button("Settings", hdrBtn, GUILayout.Width(w_settings)))
                showColumnMenu = !showColumnMenu;

            // Close button — fixed size (single glyph, no text scale needed)
            if (GUILayout.Button("✕", EngineConfigStyles.CloseButton, GUILayout.Width(25)))
            {
                SaveSettings();
                _module.CloseWindow();
                return;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(7);
            DrawConfigSelectors(_module.FilteredDisplayConfigs(false));

            // ── Panel 2: Simulation panel (reliability stats + simulation controls) ──
            if (showSimPanel)
            {
                bool hasChart = CanShowChart(_module.config);
                if (hasChart)
                {
                    GUILayout.Space(6);

                    // Push current state into the chart object before drawing
                    Chart.UseLogScaleX          = useLogScaleX;
                    Chart.UseLogScaleY          = useLogScaleY;
                    Chart.UseSimulatedData      = useSimulatedData;
                    Chart.SimulatedDataValue    = simulatedDataValue;
                    Chart.ClusterSize           = clusterSize;
                    Chart.ClusterSizeInput      = clusterSizeInput;
                    Chart.DataValueInput        = dataValueInput;
                    Chart.SliderTimeInput       = sliderTimeInput;
                    Chart.IncludeIgnition       = includeIgnition;
                    Chart.SliderModeIsPercentage = sliderModeIsPercentage;
                    Chart.SliderPercentage      = sliderPercentage;
                    Chart.SliderPercentageInput = sliderPercentageInput;

                    // Inform the info panel about the active time format (item 9).
                    EngineConfigChart.ShowTimeAsMinSec = _showTimeAsMinSec;

                    // Panel height: start offset (4px) + DU content (132×s) + bottom margin (8px).
                    // DU section ends at ignY(108s) + ignH(24s) = 132s from its start y.
                    // Sim section with step=28 ends at 4×28s + btnH(20s) = 132s from its start y.
                    // Both sections fit in 144s with an 8px margin at the bottom.
                    int infoPanelH = Mathf.Max(140, Mathf.RoundToInt(144 * _fontScale));

                    // Info panel only — chart section hidden (full-width stats + sim controls)
                    Chart.Draw(_module.config, guiWindowRect.width - 10, infoPanelH, ref sliderTime,
                               showChartArea: false, showInfoArea: true);

                    // Pull updated state back from the chart object
                    useLogScaleX           = Chart.UseLogScaleX;
                    useLogScaleY           = Chart.UseLogScaleY;
                    useSimulatedData       = Chart.UseSimulatedData;
                    simulatedDataValue     = Chart.SimulatedDataValue;
                    clusterSize            = Chart.ClusterSize;
                    clusterSizeInput       = Chart.ClusterSizeInput;
                    dataValueInput         = Chart.DataValueInput;
                    sliderTimeInput        = Chart.SliderTimeInput;
                    includeIgnition        = Chart.IncludeIgnition;
                    sliderModeIsPercentage = Chart.SliderModeIsPercentage;
                    sliderPercentage       = Chart.SliderPercentage;
                    sliderPercentageInput  = Chart.SliderPercentageInput;

                    GUILayout.Space(6);
                    _techLevels.DrawTechLevelSelector();
                }
                else if (_module.config != null && _module.techLevel != -1)
                {
                    _techLevels.DrawTechLevelPanel(guiWindowRect.width - 10);
                }
                else if (_module.config != null)
                {
                    GUILayout.Space(10);
                    string noChartMsg = (_module.type != null && _module.type.Contains("ModuleRCS"))
                        ? "RCS thrusters do not use burn-cycle reliability data."
                        : "No reliability data available for this configuration.";
                    GUILayout.Label(noChartMsg, EngineConfigStyles.NoChartLabel);
                    GUILayout.Space(10);
                }
            }

            // ── Panel 3: Chart panel (line/heatmap chart, full width) ──────────
            if (showChartPanel && CanShowChart(_module.config))
            {
                GUILayout.Space(6);

                Chart.UseLogScaleX          = useLogScaleX;
                Chart.UseLogScaleY          = useLogScaleY;
                Chart.UseSimulatedData      = useSimulatedData;
                Chart.SimulatedDataValue    = simulatedDataValue;
                Chart.ClusterSize           = clusterSize;
                Chart.ClusterSizeInput      = clusterSizeInput;
                Chart.DataValueInput        = dataValueInput;
                Chart.SliderTimeInput       = sliderTimeInput;
                Chart.IncludeIgnition       = includeIgnition;
                Chart.SliderModeIsPercentage = sliderModeIsPercentage;
                Chart.SliderPercentage      = sliderPercentage;
                Chart.SliderPercentageInput = sliderPercentageInput;

                EngineConfigChart.ShowTimeAsMinSec = _showTimeAsMinSec;

                // Chart only — info panel hidden (full-width chart)
                Chart.Draw(_module.config, guiWindowRect.width - 10, 375, ref sliderTime,
                           showChartArea: true, showInfoArea: false);

                useLogScaleX           = Chart.UseLogScaleX;
                useLogScaleY           = Chart.UseLogScaleY;
                useSimulatedData       = Chart.UseSimulatedData;
                simulatedDataValue     = Chart.SimulatedDataValue;
                clusterSize            = Chart.ClusterSize;
                clusterSizeInput       = Chart.ClusterSizeInput;
                dataValueInput         = Chart.DataValueInput;
                sliderTimeInput        = Chart.SliderTimeInput;
                includeIgnition        = Chart.IncludeIgnition;
                sliderModeIsPercentage = Chart.SliderModeIsPercentage;
                sliderPercentage       = Chart.SliderPercentage;
                sliderPercentageInput  = Chart.SliderPercentageInput;

                GUILayout.Space(6);
            }

            GUILayout.Space(8);
            GUILayout.EndVertical();

            if (!myToolTip.Equals(string.Empty) && GUI.tooltip.Equals(string.Empty))
            {
                if (counterTT > 4)
                {
                    myToolTip = GUI.tooltip;
                    counterTT = 0;
                }
                else
                {
                    counterTT++;
                }
            }
            else
            {
                myToolTip = GUI.tooltip;
                counterTT = 0;
            }

            GUI.DragWindow();
        }

        private void DrawColumnMenuWindow(int windowID)
        {
            float s = _fontScale;
            int closeW  = Mathf.RoundToInt(25 * s);
            int closeH  = Mathf.RoundToInt(22 * s);
            int titleH  = Mathf.RoundToInt(22 * s);
            // Header strip is the taller of the title or the close button, plus 6 px breathing room.
            int headerH = Mathf.Max(closeH, titleH) + 6;

            // Title label — styled like the main window's InfoSection to match the look.
            var titleStyle = new GUIStyle(EngineConfigStyles.InfoSection)
            {
                alignment = TextAnchor.MiddleLeft,
                padding   = new RectOffset(8, 0, 0, 0)
            };
            GUI.Label(new Rect(0, 2, columnMenuRect.width - closeW - 12, titleH + 2), "Column Settings", titleStyle);

            // Close button: right-aligned with 4 px margin from the window edge.
            if (GUI.Button(new Rect(columnMenuRect.width - closeW - 4, 4, closeW, closeH), "✕", EngineConfigStyles.CloseButton))
            {
                showColumnMenu = false;
                return;
            }

            // Thin separator under the header
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(new Rect(0, headerH - 1, columnMenuRect.width, 1), _textures.ChartSeparator);

            DrawColumnMenu(new Rect(0, headerH, columnMenuRect.width, columnMenuRect.height - headerH));
            GUI.DragWindow(); // Allow dragging from anywhere in the window
        }

        #endregion

        #region Config Table Drawing

        protected void DrawConfigSelectors(IEnumerable<ConfigNode> availableConfigNodes)
        {
            // Allow derived module classes to add custom UI before the config table
            _module.DrawConfigSelectors(availableConfigNodes);

            // Then draw the standard config table
            DrawConfigTable(_module.BuildConfigRows());
        }

        protected void DrawConfigTable(IEnumerable<ModuleEngineConfigsBase.ConfigRowDefinition> rows)
        {
            EnsureTexturesAndStyles();

            var rowList = rows.ToList();
            CalculateColumnWidths(rowList);

            float totalWidth = 0f;
            for (int i = 0; i < ConfigColumnWidths.Length; i++)
            {
                if (IsColumnVisible(i))
                    totalWidth += ConfigColumnWidths[i];
            }

            float requiredWindowWidth = totalWidth + 10f;
            const float minWindowWidth = 900f;
            const float minWindowWidthCompact = 550f;
            // Enforce full minimum width only when the chart panel is visible (chart needs the room).
            // The sim panel alone is narrower — use the compact minimum for that case.
            guiWindowRect.width = showChartPanel
                ? Mathf.Max(requiredWindowWidth, minWindowWidth)
                : Mathf.Max(requiredWindowWidth, minWindowWidthCompact);

            // Reduced height now that headers are horizontal (no rotation needed).
            Rect headerRowRect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.label, GUILayout.Height(24));
            float headerStartX = headerRowRect.x;
            DrawHeaderRow(new Rect(headerStartX, headerRowRect.y, totalWidth, headerRowRect.height));

            int actualRows = rowList.Count;
            int visibleRows = Mathf.Min(actualRows, ConfigMaxVisibleRows);
            int scrollViewHeight = visibleRows * ConfigRowHeight;

            configScrollPos = GUILayout.BeginScrollView(configScrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, EngineConfigStyles.TableScrollView, GUILayout.Height(scrollViewHeight));

            int rowIndex = 0;
            foreach (var row in rowList)
            {
                Rect rowRect = GUILayoutUtility.GetRect(GUIContent.none, EngineConfigStyles.TableRowLayout, GUILayout.Height(ConfigRowHeight));
                float rowStartX = rowRect.x;
                Rect tableRowRect = new Rect(rowStartX, rowRect.y, totalWidth, rowRect.height);
                bool isHovered = tableRowRect.Contains(Event.current.mousePosition);

                bool isLocked = !EngineConfigTechLevels.CanConfig(row.Node);
                if (Event.current.type == EventType.Repaint)
                {
                    if (!row.IsSelected && !isLocked && !isHovered && rowIndex % 2 == 1)
                    {
                        GUI.DrawTexture(tableRowRect, _textures.ZebraStripe);
                    }

                    if (row.IsSelected)
                        GUI.DrawTexture(tableRowRect, _textures.RowCurrent);
                    else if (isLocked)
                        GUI.DrawTexture(tableRowRect, _textures.RowLocked);
                    else if (isHovered)
                        GUI.DrawTexture(tableRowRect, _textures.RowHover);
                }

                string tooltip = GetRowTooltip(row.Node);
                if (configGuiContent == null)
                    configGuiContent = new GUIContent();
                configGuiContent.text = string.Empty;
                configGuiContent.tooltip = tooltip;
                GUI.Label(tableRowRect, configGuiContent, GUIStyle.none);

                // Call DrawSelectButton as a hook point for external mod compatibility (RP-1)
                // Pass null callback - we don't want to invoke anything during rendering, only during button clicks
                _module.DrawSelectButton(row.Node, row.IsSelected, null);

                DrawConfigRow(tableRowRect, row, isHovered, isLocked);

                if (Event.current.type == EventType.Repaint)
                {
                    DrawColumnSeparators(tableRowRect);
                }

                rowIndex++;
            }

            GUILayout.EndScrollView();
        }

        private void DrawHeaderRow(Rect headerRect)
        {
            float currentX = headerRect.x;

            // Abbreviated display labels shown directly in the header row.
            // Full descriptive text is supplied as the tooltip (shown via the
            // [BTN]-prefixed tooltip system so it appears near the cursor).
            string survivalShort = sliderModeIsPercentage
                ? $"T@{sliderPercentage:F0}%"
                : $"Surv@{FormatBurnTimeDisplay(sliderTime)}";

            string survivalTooltip = sliderModeIsPercentage
                ? Localizer.Format("#RF_Engine_TipTimeAtSurvival", $"{sliderPercentage:F1}")
                : Localizer.Format("#RF_Engine_TipSurvivalAtTime", ChartMath.FormatTime(sliderTime));

            // Short labels — full text is in the tooltip so nothing is lost.
            string[] shortLabels = {
                "Name", "Thrust", "Min%", "ISP", "Mass", "Gim",
                "Igns", "Ullg", "PFed",
                "Rated", "Tested", "Ignition %", "0-DU", "Max-DU",
                survivalShort,
                "Tech", "Cost", ""
            };

            string[] tooltips = {
                Localizer.GetStringByTag("#RF_Engine_TipName"),
                Localizer.GetStringByTag("#RF_Engine_TipThrust"),
                Localizer.GetStringByTag("#RF_Engine_TipMinThrottle"),
                Localizer.GetStringByTag("#RF_Engine_TipIsp"),
                Localizer.GetStringByTag("#RF_Engine_TipMass"),
                Localizer.GetStringByTag("#RF_Engine_TipGimbal"),
                Localizer.GetStringByTag("#RF_Engine_TipIgnitions"),
                Localizer.GetStringByTag("#RF_Engine_TipUllage"),
                Localizer.GetStringByTag("#RF_Engine_TipPressureFed"),
                Localizer.GetStringByTag("#RF_Engine_TipRatedBurnTime"),
                Localizer.GetStringByTag("#RF_Engine_TipTestedBurnTime"),
                Localizer.GetStringByTag("#RF_Engine_TipIgnReliability"),
                Localizer.GetStringByTag("#RF_Engine_TipBurnNoData"),
                Localizer.GetStringByTag("#RF_Engine_TipBurnMaxData"),
                survivalTooltip,
                Localizer.GetStringByTag("#RF_Engine_TipRequires"),
                Localizer.GetStringByTag("#RF_Engine_TipExtraCost"),
                Localizer.GetStringByTag("#RF_Engine_TipActions")
            };

            for (int i = 0; i < shortLabels.Length; i++)
            {
                if (IsColumnVisible(i))
                {
                    DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[i], headerRect.height), shortLabels[i], tooltips[i]);
                    currentX += ConfigColumnWidths[i];
                }
            }
        }

        private void DrawHeaderCell(Rect rect, string text, string tooltip)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            GUIStyle headerStyle = hover ? EngineConfigStyles.HeaderCellHover : EngineConfigStyles.HeaderCell;

            if (configGuiContent == null)
                configGuiContent = new GUIContent();
            configGuiContent.text = text;
            // Prefix with [BTN] so the tooltip appears near the cursor (not at the fixed row offset).
            configGuiContent.tooltip = string.IsNullOrEmpty(tooltip) ? string.Empty : $"[BTN]{tooltip}";

            // Simple horizontal label — no rotation matrix needed.
            GUI.Label(new Rect(rect.x + 2, rect.y, rect.width - 2, rect.height), configGuiContent, headerStyle);
        }

        private void DrawConfigRow(Rect rowRect, ModuleEngineConfigsBase.ConfigRowDefinition row, bool isHovered, bool isLocked)
        {
            GUIStyle primaryStyle;
            if (isLocked)
                primaryStyle = EngineConfigStyles.RowPrimaryLocked;
            else if (isHovered)
                primaryStyle = EngineConfigStyles.RowPrimaryHover;
            else
                primaryStyle = EngineConfigStyles.RowPrimary;

            GUIStyle secondaryStyle = EngineConfigStyles.RowSecondary;

            float currentX = rowRect.x;
            string nameText = row.DisplayName;
            if (row.Indent) nameText = "    ↳ " + nameText;

            Action<int, string> drawCell = (index, text) =>
            {
                if (IsColumnVisible(index))
                {
                    GUIStyle cellStyle = (index == 0)  ? primaryStyle
                                       : (index == 15) ? EngineConfigStyles.TechCell
                                       // Ignitions(6), Ullage(7), PFed(8): center-aligned boolean/symbol columns
                                       : (index == 6 || index == 7 || index == 8) ? EngineConfigStyles.RowSecondaryCenter
                                       : secondaryStyle;
                    GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[index], rowRect.height), text, cellStyle);
                    currentX += ConfigColumnWidths[index];
                }
            };

            drawCell(0, nameText);
            drawCell(1, GetThrustString(row.Node));
            drawCell(2, GetMinThrottleString(row.Node));
            drawCell(3, GetIspString(row.Node));
            drawCell(4, GetMassString(row.Node));
            drawCell(5, GetGimbalString(row.Node));
            drawCell(6, GetIgnitionsString(row.Node));
            drawCell(7, GetUllageSymbol(row.Node));
            drawCell(8, GetPressureFedSymbol(row.Node));
            drawCell(9, GetRatedBurnTimeString(row.Node));
            drawCell(10, GetTestedBurnTimeString(row.Node));
            drawCell(11, GetIgnitionReliabilityString(row.Node));
            drawCell(12, GetCycleReliabilityStartString(row.Node));
            drawCell(13, GetCycleReliabilityEndString(row.Node));
            drawCell(14, GetSurvivalAtTimeString(row.Node));
            drawCell(15, GetTechString(row.Node));
            drawCell(16, GetCostDeltaString(row.Node));

            if (IsColumnVisible(17))
            {
                DrawActionCell(new Rect(currentX, rowRect.y + 1, ConfigColumnWidths[17], rowRect.height - 2), row.Node, row.IsSelected, row.Apply);
            }
        }

        private void DrawActionCell(Rect rect, ConfigNode node, bool isSelected, Action apply)
        {
            string configName = node.GetValue("name");
            bool canUse = EngineConfigTechLevels.CanConfig(node);
            bool unlocked = EngineConfigTechLevels.UnlockedConfig(node, _module.part);
            double cost = EntryCostManager.Instance.ConfigEntryCost(configName);

            if (cost <= 0 && !unlocked && canUse)
                EntryCostManager.Instance.PurchaseConfig(configName, node.GetValue("techRequired"));

            // Switch button style: standard KSP action button.
            GUIStyle switchStyle = EngineConfigStyles.ActionButton;

            string switchLabel = isSelected ? "Active" : "Switch";
            // Fix the switch button width to the wider of "Switch"/"Active" so the action
            // column doesn't shift when the selected config changes (issue 4).
            float switchWidth = Mathf.Max(
                switchStyle.CalcSize(new GUIContent("Switch")).x,
                switchStyle.CalcSize(new GUIContent("Active")).x
            ) + 10f;

            GUI.enabled = canUse && !unlocked && cost > 0;
            string purchaseLabel;
            string purchaseTooltip = string.Empty;

            if (cost > 0)
            {
                double displayCost = cost;
                if (!unlocked && EngineConfigRP1Integration.TryGetCreditAdjustedCost(cost, out double creditsAvailable, out double costAfterCredits))
                {
                    displayCost = costAfterCredits;
                    double creditsUsed = cost - costAfterCredits;
                    purchaseTooltip = $"[BTN]Entry Cost: {cost:N0}√\n" +
                                    $"<color=#FFEB3B>Credits Available: {creditsAvailable:N0}</color>\n" +
                                    $"<color=#FFEB3B>Credits Used: {creditsUsed:N0}</color>\n" +
                                    $"<b>Final Cost: {costAfterCredits:N0}√</b>";
                }
                purchaseLabel = unlocked ? "Owned" : $"Buy ({displayCost:N0}√)";
            }
            else
                purchaseLabel = unlocked ? "Owned" : "Free";

            // Purchase button colour: golden when there's a cost, green when free/owned.
            GUIStyle purchaseStyle = (cost > 0 && !unlocked)
                ? EngineConfigStyles.ActionButtonPurchase
                : EngineConfigStyles.ActionButtonOwned;

            // Purchase button fills all remaining column space so every row has identical
            // widths regardless of label text ("Owned", "Free", "Buy (12345√)").
            // The column is pre-sized to the widest possible row in CalculateColumnWidths,
            // so clamping to at least the CalcSize minimum keeps the layout valid when the
            // column is unexpectedly narrower than the label.
            float purchaseWidth = Mathf.Max(
                rect.width - switchWidth - 4f,
                purchaseStyle.CalcSize(new GUIContent(purchaseLabel)).x + 10f
            );

            Rect switchRect   = new Rect(rect.x, rect.y, switchWidth, rect.height);
            Rect purchaseRect = new Rect(rect.x + switchWidth + 4f, rect.y, purchaseWidth, rect.height);

            GUI.enabled = !isSelected;
            if (GUI.Button(switchRect, switchLabel, switchStyle))
            {
                if (!unlocked && cost <= 0)
                {
                    _module.DrawSelectButton(node, isSelected, (cfgName) =>
                    {
                        EntryCostManager.Instance.PurchaseConfig(cfgName, node.GetValue("techRequired"));
                    });
                }
                apply?.Invoke();
            }

            GUI.enabled = canUse && !unlocked && cost > 0;
            if (GUI.Button(purchaseRect, new GUIContent(purchaseLabel, purchaseTooltip), purchaseStyle))
            {
                // Call DrawSelectButton with PurchaseConfig as the callback
                // This ensures PurchaseConfig runs INSIDE DrawSelectButton (before RP-1's postfix clears techNode)
                // RP-1's Harmony patches: Prefix sets techNode -> DrawSelectButton body (with callback) -> Postfix clears techNode
                _module.DrawSelectButton(node, isSelected, (cfgName) =>
                {
                    if (EntryCostManager.Instance.PurchaseConfig(cfgName, node.GetValue("techRequired")))
                        apply?.Invoke();
                });
            }

            GUI.enabled = true;
        }

        private void DrawColumnSeparators(Rect rowRect)
        {
            float currentX = rowRect.x;
            for (int i = 0; i < ConfigColumnWidths.Length - 1; i++)
            {
                if (IsColumnVisible(i))
                {
                    currentX += ConfigColumnWidths[i];
                    Rect separatorRect = new Rect(currentX, rowRect.y, 1, rowRect.height);
                    GUI.DrawTexture(separatorRect, _textures.ColumnSeparator);
                }
            }
        }

        #endregion

        #region Column Management

        private void DrawColumnMenu(Rect menuRect)
        {
            InitializeColumnVisibility();

            // All pixel geometry scales with the current font scale so the panel
            // never has clipped text or dead whitespace at non-default scales.
            float s        = _fontScale;
            int pad        = Mathf.RoundToInt(8  * s);
            int rowH       = Mathf.RoundToInt(18 * s);
            int itemStep   = Mathf.RoundToInt(22 * s);
            int labelW     = Mathf.RoundToInt(88 * s);
            int toggleX1   = Mathf.RoundToInt(100 * s);  // "Full" toggle column
            int toggleX2   = Mathf.RoundToInt(158 * s);  // "Compact" toggle column
            int toggleSz   = Mathf.RoundToInt(18  * s);

            float yPos       = menuRect.y + Mathf.RoundToInt(5 * s);
            float leftX      = menuRect.x + pad;
            float innerWidth = menuRect.width - pad * 2f;

            GUIStyle headerStyle = EngineConfigStyles.ColumnMenuHeader;
            GUIStyle labelStyle  = EngineConfigStyles.ColumnMenuLabel;

            // ── Item 1: Font scale ───────────────────────────────────────────
            GUI.Label(new Rect(leftX, yPos, labelW, rowH), "Font Scale", headerStyle);
            yPos += rowH + 2;

            float sliderW = innerWidth - 45f;
            float newScale = GUI.HorizontalSlider(
                new Rect(leftX, yPos, sliderW, rowH),
                _fontScale, 0.7f, 1.5f);
            // Snap to 0.1 increments for a clean feel
            newScale = Mathf.Round(newScale * 10f) / 10f;
            if (!Mathf.Approximately(newScale, _fontScale))
            {
                _fontScale = newScale;
                EngineConfigStyles.FontScale = _fontScale;
                EngineConfigStyles.Reset();   // styles will be rebuilt lazily on next EnsureTexturesAndStyles()
                SaveSettings();
            }
            // Value label sits to the right of the slider track.
            GUI.Label(new Rect(leftX + sliderW + 5f, yPos - 2, 40f, rowH + 4), $"{_fontScale:F1}×", labelStyle);
            yPos += rowH + 8;

            // Divider gap
            yPos += 2;

            // ── Column visibility ────────────────────────────────────────────
            string[] columnNames = {
                "Name", "Thrust", "Min%", "ISP", "Mass", "Gimbal",
                "Ignitions", "Ullage", "Pres-Fed", "Rated", "Tested",
                "Ign%", "0-DU", "Max-DU",
                "Survival", "Tech", "Cost", "Actions"
            };

            // Column header labels aligned over their respective toggle columns.
            GUI.Label(new Rect(leftX + toggleX1 - 5, yPos, 50, rowH), "Full",    headerStyle);
            GUI.Label(new Rect(leftX + toggleX2 - 5, yPos, 60, rowH), "Compact", headerStyle);
            yPos += rowH + 2;

            float listHeight = menuRect.height - (yPos - menuRect.y) - 5f;
            Rect scrollRect = new Rect(leftX, yPos, innerWidth, listHeight);

            GUI.BeginGroup(scrollRect);
            float itemY = 0;

            for (int i = 0; i < columnNames.Length; i++)
            {
                GUI.Label(new Rect(0, itemY, labelW, rowH + 2), columnNames[i], labelStyle);

                bool newFullVisible = GUI.Toggle(new Rect(toggleX1, itemY + 1, toggleSz, toggleSz), columnsVisibleFull[i], "");
                if (newFullVisible != columnsVisibleFull[i])
                {
                    columnsVisibleFull[i] = newFullVisible;
                    SaveSettings();
                }

                bool newCompactVisible = GUI.Toggle(new Rect(toggleX2, itemY + 1, toggleSz, toggleSz), columnsVisibleCompact[i], "");
                if (newCompactVisible != columnsVisibleCompact[i])
                {
                    columnsVisibleCompact[i] = newCompactVisible;
                    SaveSettings();
                }

                itemY += itemStep;
            }

            GUI.EndGroup();
        }

        private static void InitializeColumnVisibility()
        {
            // EnsureSettings() (called from OnGUI before any state is read) is the
            // authoritative initializer.  This fallback handles the rare case where
            // IsColumnVisible is called before the first OnGUI pass.
            if (columnVisibilityInitialized)
                return;

            EnsureSettings();
        }

        private bool IsColumnVisible(int columnIndex)
        {
            InitializeColumnVisibility();

            if (columnIndex < 0 || columnIndex >= 18)
                return false;

            return compactView ? columnsVisibleCompact[columnIndex] : columnsVisibleFull[columnIndex];
        }

        private void CalculateColumnWidths(List<ModuleEngineConfigsBase.ConfigRowDefinition> rows)
        {
            GUIStyle cellStyle = EngineConfigStyles.CellMeasure;
            GUIStyle techStyle = EngineConfigStyles.TechCell;

            for (int i = 0; i < ConfigColumnWidths.Length; i++)
                ConfigColumnWidths[i] = 30f;

            foreach (var row in rows)
            {
                string nameText = row.DisplayName;
                if (row.Indent) nameText = "    ↳ " + nameText;

                string[] cellValues = new string[]
                {
                    nameText,
                    GetThrustString(row.Node),
                    GetMinThrottleString(row.Node),
                    GetIspString(row.Node),
                    GetMassString(row.Node),
                    GetGimbalString(row.Node),
                    GetIgnitionsString(row.Node),
                    GetUllageSymbol(row.Node),
                    GetPressureFedSymbol(row.Node),
                    GetRatedBurnTimeString(row.Node),
                    GetTestedBurnTimeString(row.Node),
                    GetIgnitionReliabilityString(row.Node),
                    GetCycleReliabilityStartString(row.Node),
                    GetCycleReliabilityEndString(row.Node),
                    GetSurvivalAtTimeString(row.Node),
                    GetTechString(row.Node),   // measured with TechCell (SF(11))
                    GetCostDeltaString(row.Node),
                    ""
                };

                for (int i = 0; i < cellValues.Length; i++)
                {
                    if (!string.IsNullOrEmpty(cellValues[i]))
                    {
                        // Tech column (15) uses TechCell style (SF(11)) so measure with it.
                        GUIStyle measureStyle = (i == 15) ? techStyle : cellStyle;
                        float width = measureStyle.CalcSize(new GUIContent(cellValues[i])).x + 10f;
                        if (width > ConfigColumnWidths[i])
                            ConfigColumnWidths[i] = width;
                    }
                }
            }

            // ── Ensure every column is at least as wide as its header label ──────────
            // This prevents truncated headers when content happens to be narrow.
            string survivalHeader = sliderModeIsPercentage
                ? $"T@{sliderPercentage:F0}%"
                : $"Surv@{FormatBurnTimeDisplay(sliderTime)}";

            string[] headerLabels = {
                "Name", "Thrust", "Min%", "ISP", "Mass", "Gim",
                "Igns", "Ullg", "PFed",
                "Rated", "Tested", "Ignition %", "0-DU", "Max-DU",
                survivalHeader, "Tech", "Cost", ""
            };

            GUIStyle hStyle = EngineConfigStyles.HeaderCell;
            for (int i = 0; i < headerLabels.Length && i < ConfigColumnWidths.Length; i++)
            {
                if (!string.IsNullOrEmpty(headerLabels[i]))
                {
                    float hw = hStyle.CalcSize(new GUIContent(headerLabels[i])).x + 16f;
                    if (hw > ConfigColumnWidths[i])
                        ConfigColumnWidths[i] = hw;
                }
            }

            // Calculate dynamic width for Actions column (index 17) based on button labels.
            // Use the same styles as DrawActionCell so widths match exactly.
            float maxActionWidth = 0f;
            GUIStyle switchMeasureStyle   = EngineConfigStyles.ActionButton;
            GUIStyle purchaseMeasureStyle = EngineConfigStyles.ActionButtonPurchase;
            GUIStyle ownedMeasureStyle    = EngineConfigStyles.ActionButtonOwned;
            foreach (var row in rows)
            {
                string configName = row.Node.GetValue("name");
                bool unlocked = EngineConfigTechLevels.UnlockedConfig(row.Node, _module.part);
                double cost = EntryCostManager.Instance.ConfigEntryCost(configName);

                // Measure both states so the column width doesn't change when selection changes.
                float switchWidth = Mathf.Max(
                    switchMeasureStyle.CalcSize(new GUIContent("Switch")).x,
                    switchMeasureStyle.CalcSize(new GUIContent("Active")).x
                ) + 10f;

                string purchaseLabel;
                GUIStyle pStyle;
                if (cost > 0)
                {
                    double displayCost = cost;
                    if (!unlocked && EngineConfigRP1Integration.TryGetCreditAdjustedCost(cost, out _, out double costAfterCredits))
                        displayCost = costAfterCredits;
                    purchaseLabel = unlocked ? "Owned" : $"Buy ({displayCost:N0}√)";
                    pStyle = (cost > 0 && !unlocked) ? purchaseMeasureStyle : ownedMeasureStyle;
                }
                else
                {
                    purchaseLabel = unlocked ? "Owned" : "Free";
                    pStyle = ownedMeasureStyle;
                }
                float purchaseWidth = pStyle.CalcSize(new GUIContent(purchaseLabel)).x + 10f;

                float totalWidth = switchWidth + purchaseWidth + 4f;
                if (totalWidth > maxActionWidth)
                    maxActionWidth = totalWidth;
            }

            ConfigColumnWidths[17] = Mathf.Max(maxActionWidth, 160f);  // Minimum 160px
            ConfigColumnWidths[7]  = Mathf.Max(ConfigColumnWidths[7],  30f);
            ConfigColumnWidths[8]  = Mathf.Max(ConfigColumnWidths[8],  30f);
            ConfigColumnWidths[9]  = Mathf.Max(ConfigColumnWidths[9],  50f);
            ConfigColumnWidths[10] = Mathf.Max(ConfigColumnWidths[10], 50f);
            // Item 5: cap the Requires/Tech column so long tech names don't blow out the table.
            // The full name is always visible in the row hover tooltip.
            ConfigColumnWidths[15] = Mathf.Min(ConfigColumnWidths[15], 110f);

            // Fix survival column (index 14) to a stable maximum width so the window doesn't
            // resize every time the slider moves.  Reference strings cover both display modes:
            //   • Time mode   — "99.9 / 99.9 / 99.9 %"  (3 decimals; "100" drops the decimal)
            //   • % mode      — time strings like "10m 0s / 10m 0s / 10m 0s"
            float survivalPctW  = cellStyle.CalcSize(new GUIContent("99.9 / 99.9 / 99.9 %")).x + 10f;
            float survivalTimeW = cellStyle.CalcSize(new GUIContent("10m 0s / 10m 0s / 10m 0s")).x + 10f;
            ConfigColumnWidths[14] = Mathf.Max(survivalPctW, survivalTimeW);
        }

        #endregion

        #region Cell Formatters

        internal string GetThrustString(ConfigNode node)
        {
            if (!node.HasValue(_module.thrustRating))
                return "-";

            float thrust = _module.scale * _techLevels.ThrustTL(node.GetValue(_module.thrustRating), node);
            if (thrust >= 100f)
                return $"{thrust:N0} kN";
            if (thrust < 1e-3f)
                return $"{thrust * 1e6f:N0} mN";
            if (thrust < 0.01f)
                return $"{thrust * 1e3f:N0} N";
            return $"{thrust:N2} kN";
        }

        internal string GetMinThrottleString(ConfigNode node)
        {
            float value = -1f;
            if (node.HasValue("minThrust") && node.HasValue(_module.thrustRating))
            {
                float.TryParse(node.GetValue("minThrust"), out float minT);
                float.TryParse(node.GetValue(_module.thrustRating), out float maxT);
                if (maxT > 0)
                    value = minT / maxT;
            }
            else if (node.HasValue("throttle"))
            {
                float.TryParse(node.GetValue("throttle"), out value);
            }

            if (value < 0f)
                return "-";
            return value.ToString("P0");
        }

        internal string GetIspString(ConfigNode node)
        {
            // Delegate to the same helper used by the TL stat panel so the table column
            // and the bottom comparison always agree.
            if (_techLevels.TryGetIspAtTL(node, _module.techLevel, out float vacIsp, out float slIsp))
                return $"{vacIsp:N0}-{slIsp:N0}";

            return "-";
        }

        internal string GetMassString(ConfigNode node)
        {
            if (_module.origMass <= 0f)
                return "-";

            float cMass = _module.scale * _module.origMass * RFSettings.Instance.EngineMassMultiplier;
            if (node.HasValue("massMult") && float.TryParse(node.GetValue("massMult"), out float ftmp))
                cMass *= ftmp;

            return $"{cMass:N3}t";
        }

        internal string GetGimbalString(ConfigNode node)
        {
            if (!_module.part.HasModuleImplementing<ModuleGimbal>())
                return "<color=#9E9E9E>✗</color>";

            var gimbals = _module.ExtractGimbals(node);

            if (gimbals.Count == 0 && _module.techLevel != -1 && (!_module.gimbalTransform.Equals(string.Empty) || _module.useGimbalAnyway))
            {
                TechLevel cTL = new TechLevel();
                if (cTL.Load(node, _module.techNodes, _module.engineType, _module.techLevel))
                {
                    float gimbalRange = cTL.GimbalRange;
                    if (node.HasValue("gimbalMult"))
                        gimbalRange *= float.Parse(node.GetValue("gimbalMult"), CultureInfo.InvariantCulture);

                    if (gimbalRange >= 0)
                        return $"{gimbalRange * _module.gimbalMult:0.#}°";
                }
            }

            if (gimbals.Count == 0)
            {
                foreach (var gimbalMod in _module.part.Modules.OfType<ModuleGimbal>())
                {
                    if (gimbalMod != null)
                    {
                        var gimbal = new Gimbal(gimbalMod.gimbalRange, gimbalMod.gimbalRange, gimbalMod.gimbalRange, gimbalMod.gimbalRange, gimbalMod.gimbalRange);
                        gimbals[gimbalMod.gimbalTransformName] = gimbal;
                    }
                }
            }

            if (gimbals.Count == 0)
                return "<color=#9E9E9E>✗</color>";

            var first = gimbals.Values.First();
            bool allSame = gimbals.Values.All(g => g.gimbalRange == first.gimbalRange
                && g.gimbalRangeXP == first.gimbalRangeXP
                && g.gimbalRangeXN == first.gimbalRangeXN
                && g.gimbalRangeYP == first.gimbalRangeYP
                && g.gimbalRangeYN == first.gimbalRangeYN);

            if (allSame)
                return first.Info();

            var uniqueInfos = gimbals.Values.Select(g => g.Info()).Distinct().OrderBy(s => s);
            return string.Join(", ", uniqueInfos);
        }

        internal string GetIgnitionsString(ConfigNode node)
        {
            if (!node.HasValue("ignitions"))
                return "-";

            if (!int.TryParse(node.GetValue("ignitions"), out int ignitions))
                return "∞";

            int resolved = _techLevels.ConfigIgnitions(ignitions);
            if (resolved == -1)
                return "∞";
            if (resolved == 0 && _module.literalZeroIgnitions)
                return "<color=#FFEB3B>Gnd</color>";
            return resolved.ToString();
        }

        // Kept for backward-compatibility with any subclass that may call it directly.
        internal string GetBoolSymbol(ConfigNode node, string key)
        {
            if (!node.HasValue(key))
                return "<color=#9E9E9E>✗</color>";
            bool isTrue = node.GetValue(key).ToLower() == "true";
            return isTrue ? "<color=#FFA726>✓</color>" : "<color=#9E9E9E>✗</color>";
        }

        /// <summary>Item 8: Ullage — white ✓ when required, grey ✗ when not.</summary>
        internal string GetUllageSymbol(ConfigNode node)
        {
            if (!node.HasValue("ullage"))
                return "<color=#9E9E9E>✗</color>";
            bool isTrue = node.GetValue("ullage").ToLower() == "true";
            return isTrue ? "<color=#FFFFFF>✓</color>" : "<color=#9E9E9E>✗</color>";
        }

        /// <summary>Item 8: Pressure-fed — green ✓ when true, plain hyphen when false.</summary>
        internal string GetPressureFedSymbol(ConfigNode node)
        {
            if (!node.HasValue("pressureFed"))
                return "-";
            bool isTrue = node.GetValue("pressureFed").ToLower() == "true";
            return isTrue ? "<color=#4DE64D>✓</color>" : "-";
        }

        /// <summary>
        /// Item 9: Format a burn-time in seconds for table display.
        /// Respects the <see cref="_showTimeAsMinSec"/> toggle — when true values ≥ 60 s
        /// are shown as "m:ss", otherwise always shown as plain seconds.
        /// </summary>
        private static string FormatBurnTimeDisplay(float seconds)
        {
            if (!_showTimeAsMinSec || seconds < 60f)
                return $"{seconds:F0}s";
            int mins = Mathf.FloorToInt(seconds / 60f);
            int secs = Mathf.RoundToInt(seconds % 60f);
            if (secs == 60) { mins++; secs = 0; }
            return $"{mins}:{secs:D2}";
        }

        /// <summary>
        /// Format a survival percentage for display in the table.
        /// Omits the decimal when the value rounds to exactly 100 so the column
        /// doesn't waste space on "100.0" — "100" is unambiguous.
        /// Input is in the 0–100 range (already multiplied by 100).
        /// </summary>
        private static string FormatSurvival(float pct)
            => pct >= 99.9995f ? "100" : $"{pct:F1}";

        internal string GetRatedBurnTimeString(ConfigNode node)
        {
            bool hasRated     = node.HasValue("ratedBurnTime");
            bool hasContinuous = node.HasValue("ratedContinuousBurnTime");

            if (!hasRated && !hasContinuous)
                return "∞";

            if (hasRated && hasContinuous)
            {
                float cont = 0f, cum = 0f;
                node.TryGetValue("ratedContinuousBurnTime", ref cont);
                node.TryGetValue("ratedBurnTime",           ref cum);
                return $"{FormatBurnTimeDisplay(cont)}/{FormatBurnTimeDisplay(cum)}";
            }

            if (hasRated)
            {
                float v = 0f;
                node.TryGetValue("ratedBurnTime", ref v);
                return FormatBurnTimeDisplay(v);
            }
            else
            {
                float v = 0f;
                node.TryGetValue("ratedContinuousBurnTime", ref v);
                return FormatBurnTimeDisplay(v);
            }
        }

        internal string GetTestedBurnTimeString(ConfigNode node)
        {
            if (!node.HasValue("testedBurnTime"))
                return "-";

            float testedBurnTime = 0f;
            if (node.TryGetValue("testedBurnTime", ref testedBurnTime))
                return FormatBurnTimeDisplay(testedBurnTime);

            return "-";
        }

        internal string GetIgnitionReliabilityString(ConfigNode node)
        {
            if (!node.HasValue("ignitionReliabilityStart") || !node.HasValue("ignitionReliabilityEnd"))
                return "-";

            if (!float.TryParse(node.GetValue("ignitionReliabilityStart"), out float valStart)) return "-";
            if (!float.TryParse(node.GetValue("ignitionReliabilityEnd"), out float valEnd)) return "-";

            return $"{valStart * 100:F1} / {valEnd * 100:F1} %";
        }

        internal string GetCycleReliabilityStartString(ConfigNode node)
        {
            if (!node.HasValue("cycleReliabilityStart"))
                return "-";
            if (float.TryParse(node.GetValue("cycleReliabilityStart"), out float val))
                return $"{val:P1}";
            return "-";
        }

        internal string GetCycleReliabilityEndString(ConfigNode node)
        {
            if (!node.HasValue("cycleReliabilityEnd"))
                return "-";
            if (float.TryParse(node.GetValue("cycleReliabilityEnd"), out float val))
                return $"{val:P1}";
            return "-";
        }

        internal string GetSurvivalAtTimeString(ConfigNode node)
        {
            // Check if we have reliability data
            if (!node.HasValue("cycleReliabilityStart") || !node.HasValue("cycleReliabilityEnd") || !node.HasValue("ratedBurnTime"))
                return "-";

            if (!float.TryParse(node.GetValue("cycleReliabilityStart"), out float cycleReliabilityStart)) return "-";
            if (!float.TryParse(node.GetValue("cycleReliabilityEnd"), out float cycleReliabilityEnd)) return "-";
            if (!float.TryParse(node.GetValue("ratedBurnTime"), out float ratedBurnTime)) return "-";

            if (cycleReliabilityStart <= 0f || cycleReliabilityEnd <= 0f || ratedBurnTime <= 0f) return "-";

            // Build cycle curve
            float testedBurnTime = 0f;
            bool hasTestedBurnTime = node.TryGetValue("testedBurnTime", ref testedBurnTime) && testedBurnTime > ratedBurnTime;
            float overburnPenalty = 2.0f;
            node.TryGetValue("overburnPenalty", ref overburnPenalty);
            FloatCurve cycleCurve = ChartMath.BuildTestFlightCycleCurve(ratedBurnTime, testedBurnTime, overburnPenalty, hasTestedBurnTime);

            // Current reliability — mirrors the chart's data logic so the table and chart are
            // always consistent: simulated data when the user has overridden it, real TestFlight
            // data otherwise.  Reflection handles are cached by TestFlightWrapper so this is cheap.
            float realCurrentData = TestFlightWrapper.GetCurrentFlightData(_module.part);
            float realMaxData     = TestFlightWrapper.GetMaximumData(_module.part);
            float currentDataValue = useSimulatedData ? simulatedDataValue : realCurrentData;
            bool  hasCurrentData   = (useSimulatedData && currentDataValue >= 0f)
                                  || (realCurrentData >= 0f && realMaxData > 0f);

            float cycleReliabilityCurrent = hasCurrentData
                ? ChartMath.EvaluateReliabilityAtData(currentDataValue, cycleReliabilityStart, cycleReliabilityEnd)
                : 0f;
            // Guard against log(0) if reliability somehow hits zero.
            if (cycleReliabilityCurrent <= 0f) cycleReliabilityCurrent = cycleReliabilityStart;

            // Color palette — matches the chart's own curve colours.
            const string colStart   = "#FFB380"; // faded orange — Start (new engine)
            const string colCurrent = "#80D9FF"; // light blue   — Current (matches chart)
            const string colEnd     = "#80E680"; // faded green  — End (fully matured)

            if (sliderModeIsPercentage)
            {
                // Percentage mode: show TIME to reach the selected survival percentage.
                float targetProb  = sliderPercentage / 100f;
                float timeStart   = ChartMath.FindTimeForSurvivalProb(targetProb, ratedBurnTime, cycleReliabilityStart,   cycleCurve, 10000f);
                float timeEnd     = ChartMath.FindTimeForSurvivalProb(targetProb, ratedBurnTime, cycleReliabilityEnd,     cycleCurve, 10000f);

                if (hasCurrentData)
                {
                    float timeCurrent = ChartMath.FindTimeForSurvivalProb(targetProb, ratedBurnTime, cycleReliabilityCurrent, cycleCurve, 10000f);
                    return $"<color={colStart}>{ChartMath.FormatTime(timeStart)}</color> / " +
                           $"<color={colCurrent}>{ChartMath.FormatTime(timeCurrent)}</color> / " +
                           $"<color={colEnd}>{ChartMath.FormatTime(timeEnd)}</color>";
                }
                return $"<color={colStart}>{ChartMath.FormatTime(timeStart)}</color> / " +
                       $"<color={colEnd}>{ChartMath.FormatTime(timeEnd)}</color>";
            }
            else
            {
                // Time mode: show PERCENTAGE survival at the selected burn time.
                float baseRateStart = -Mathf.Log(cycleReliabilityStart) / ratedBurnTime;
                float baseRateEnd   = -Mathf.Log(cycleReliabilityEnd)   / ratedBurnTime;

                float surviveStart = ChartMath.CalculateSurvivalProbAtTime(sliderTime, ratedBurnTime, cycleReliabilityStart, baseRateStart, cycleCurve);
                float surviveEnd   = ChartMath.CalculateSurvivalProbAtTime(sliderTime, ratedBurnTime, cycleReliabilityEnd,   baseRateEnd,   cycleCurve);

                if (hasCurrentData)
                {
                    float baseRateCurrent = -Mathf.Log(cycleReliabilityCurrent) / ratedBurnTime;
                    float surviveCurrent  = ChartMath.CalculateSurvivalProbAtTime(sliderTime, ratedBurnTime, cycleReliabilityCurrent, baseRateCurrent, cycleCurve);
                    return $"<color={colStart}>{FormatSurvival(surviveStart * 100)}</color> / " +
                           $"<color={colCurrent}>{FormatSurvival(surviveCurrent * 100)}</color> / " +
                           $"<color={colEnd}>{FormatSurvival(surviveEnd * 100)} %</color>";
                }
                return $"<color={colStart}>{FormatSurvival(surviveStart * 100)}</color> / " +
                       $"<color={colEnd}>{FormatSurvival(surviveEnd * 100)} %</color>";
            }
        }

        internal string GetTechString(ConfigNode node)
        {
            if (!node.HasValue("techRequired"))
                return "-";

            string tech = node.GetValue("techRequired");
            if (ModuleEngineConfigsBase.techNameToTitle.TryGetValue(tech, out string title))
                tech = title;

            // No inline <size> tag — TechCell GUIStyle (SF(11)) handles the font size,
            // and its TextClipping.Clip prevents multi-line overflow.
            return tech;
        }

        internal string GetCostDeltaString(ConfigNode node)
        {
            if (!node.HasValue("cost"))
                return "-";

            float curCost = _module.scale * float.Parse(node.GetValue("cost"), CultureInfo.InvariantCulture);
            if (_module.techLevel != -1)
                curCost = _techLevels.CostTL(curCost, node) - _techLevels.CostTL(0f, node);

            if (Mathf.Approximately(curCost, 0f))
                return "-";

            string sign = curCost < 0 ? string.Empty : "+";
            return $"{sign}{curCost:N0}";
        }

        #endregion

        #region Tooltips

        private string GetRowTooltip(ConfigNode node)
        {
            List<string> tooltipParts = new List<string>();

            // Color palette
            string headerColor = "#FFA726";  // Orange
            string propNameColor = "#7DD9FF"; // Cyan/Blue
            string valueColor = "#E6D68A";    // Yellow/Gold
            string unitColor = "#B0B0B0";     // Light gray

            if (node.HasValue("description"))
                tooltipParts.Add(node.GetValue("description"));

            if (node.HasValue("techRequired"))
            {
                string techId = node.GetValue("techRequired");
                if (!ModuleEngineConfigsBase.techNameToTitle.TryGetValue(techId, out string techTitle))
                    techTitle = techId;
                tooltipParts.Add($"<b><color={headerColor}>Requires:</color></b> {techTitle}");
            }

            if (node.HasNode("PROPELLANT"))
            {
                float thrust = 0f;
                float isp = 0f;

                if (node.HasValue(_module.thrustRating) && float.TryParse(node.GetValue(_module.thrustRating), out float maxThrust))
                    thrust = _techLevels.ThrustTL(node.GetValue(_module.thrustRating), node) * _module.scale;

                if (node.HasNode("atmosphereCurve"))
                {
                    var atmCurve = new FloatCurve();
                    atmCurve.Load(node.GetNode("atmosphereCurve"));
                    isp = atmCurve.Evaluate(0f);
                }

                const float g0 = 9.80665f;
                float thrustN = thrust * 1000f;
                float totalMassFlow = (thrustN > 0f && isp > 0f) ? thrustN / (isp * g0) : 0f;

                var propNodes = node.GetNodes("PROPELLANT");

                // First pass: accumulate the density-weighted volume ratio sum.
                // PROPELLANT ratios are volume ratios (units/s), not mass ratios, so we
                // need Σ(ratio_i × density_i) to correctly derive per-propellant flows
                // from the total mass flow.
                float weightedDensitySum = 0f; // kg/unit, weighted by volume ratio
                foreach (var propNode in propNodes)
                {
                    string ratioStr = null;
                    if (propNode.TryGetValue("ratio", ref ratioStr) && float.TryParse(ratioStr, out float ratio))
                    {
                        var resource = PartResourceLibrary.Instance?.GetDefinition(propNode.GetValue("name"));
                        if (resource != null && resource.density > 0)
                            weightedDensitySum += ratio * (float)(resource.density * 1000f);
                    }
                }

                // Second pass: build per-propellant display lines.
                // volumeFlow_i = totalMassFlow × ratio_i / Σ(ratio_j × density_j)
                // massFlow_i   = volumeFlow_i × density_i
                var propellantLines = new List<string>();
                foreach (var propNode in propNodes)
                {
                    string name = propNode.GetValue("name");
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    string line = $"  • <color={propNameColor}>{name}</color>";

                    string ratioStr2 = null;
                    if (propNode.TryGetValue("ratio", ref ratioStr2) && float.TryParse(ratioStr2, out float ratio)
                        && totalMassFlow > 0f && weightedDensitySum > 0f)
                    {
                        var resource = PartResourceLibrary.Instance?.GetDefinition(name);
                        if (resource != null && resource.density > 0)
                        {
                            float density = (float)(resource.density * 1000f); // t/unit → kg/unit
                            float volumeFlow = totalMassFlow * ratio / weightedDensitySum; // units/s
                            float massFlow = volumeFlow * density; // kg/s

                            line += $": <color={valueColor}>{volumeFlow:F2}</color> <color={unitColor}>units/s</color>";
                            string massFlowStr = massFlow >= 1f
                                ? $"{massFlow:F2} kg/s"
                                : $"{massFlow * 1000f:F1} g/s";
                            line += $" (<color={unitColor}>{massFlowStr}</color>)";
                        }
                        // No density data → show propellant name only; don't display wrong numbers
                    }

                    propellantLines.Add(line);
                }

                if (propellantLines.Count > 0)
                    tooltipParts.Add($"<b><color={headerColor}>Propellant Consumption:</color></b>\n{string.Join("\n", propellantLines)}");
            }

            return tooltipParts.Count > 0 ? string.Join("\n\n", tooltipParts) : string.Empty;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Returns true only when the config node contains all values required for the
        /// reliability chart to render — cycleReliabilityStart/End (valid 0–1 floats) and
        /// a positive ratedBurnTime. Mirrors the guard checks inside EngineConfigChart.Draw()
        /// so that every chart-visibility decision in the GUI stays in sync with what the
        /// chart can actually render.
        /// </summary>
        private static bool CanShowChart(ConfigNode config)
        {
            if (config == null) return false;
            if (!config.HasValue("cycleReliabilityStart") || !config.HasValue("cycleReliabilityEnd")) return false;
            if (!float.TryParse(config.GetValue("cycleReliabilityStart"), out float crs) || crs <= 0f || crs > 1f) return false;
            if (!float.TryParse(config.GetValue("cycleReliabilityEnd"), out float cre) || cre <= 0f || cre > 1f) return false;
            float rbt = 0f;
            if (!config.TryGetValue("ratedBurnTime", ref rbt) || rbt <= 0f) return false;
            return true;
        }

        internal void MarkWindowDirty()
        {
            lastPartId = 0;
        }

        private void EditorLock()
        {
            if (!editorLocked)
            {
                EditorLogic.fetch.Lock(false, false, false, "RFGUILock");
                editorLocked = true;
                KSP.UI.Screens.Editor.PartListTooltipMasterController.Instance?.HideTooltip();
            }
        }

        private void EditorUnlock()
        {
            if (editorLocked)
            {
                EditorLogic.fetch.Unlock("RFGUILock");
                editorLocked = false;
            }
        }

        private void EnsureTexturesAndStyles()
        {
            _textures.EnsureInitialized();
            EngineConfigStyles.Initialize();
        }

        #endregion

        #region Settings Persistence

        /// <summary>
        /// Loads saved settings from PluginData on first call. Sets column-visibility
        /// defaults first so that any values missing from the file fall back gracefully.
        /// Must be called before any static GUI state (compactView, showSimPanel,
        /// showChartPanel, guiWindowRect, columnMenu*) is read.
        /// </summary>
        private static void EnsureSettings()
        {
            if (_settingsLoaded) return;
            _settingsLoaded = true;

            // ── Column visibility defaults ────────────────────────────────────
            for (int i = 0; i < 18; i++)
                columnsVisibleFull[i] = true;

            for (int i = 0; i < 18; i++)
                columnsVisibleCompact[i] = false;

            int[] compactColumns = { 0, 1, 3, 4, 6, 7, 8, 11, 14, 15, 16, 17 };
            foreach (int col in compactColumns)
                columnsVisibleCompact[col] = true;

            columnVisibilityInitialized = true;

            // ── Load overrides from disk ──────────────────────────────────────
            if (!System.IO.File.Exists(SettingsPath)) return;
            try
            {
                ConfigNode root = ConfigNode.Load(SettingsPath);
                ConfigNode node = root?.GetNode("ENGINECONFIGGUI_SETTINGS");
                if (node == null) return;

                var ic = System.Globalization.CultureInfo.InvariantCulture;
                string v, v2;

                v = node.GetValue("compactView");
                if (v != null && bool.TryParse(v, out bool bCompact))
                    compactView = bCompact;

                v = node.GetValue("showSimPanel");
                if (v != null && bool.TryParse(v, out bool bSim))
                    showSimPanel = bSim;

                v = node.GetValue("showChartPanel");
                if (v != null && bool.TryParse(v, out bool bChart))
                    showChartPanel = bChart;

                v = node.GetValue("showTimeAsMinSec");
                if (v != null && bool.TryParse(v, out bool bMinSec))
                    _showTimeAsMinSec = bMinSec;

                v = node.GetValue("fontScale");
                if (v != null && float.TryParse(v, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float fScale))
                {
                    _fontScale = Mathf.Clamp(fScale, 0.7f, 1.5f);
                    EngineConfigStyles.FontScale = _fontScale;
                    // Don't reset here — Initialize() hasn't run yet on startup.
                    // The styles are built lazily on the first EnsureTexturesAndStyles() call.
                }

                v  = node.GetValue("windowX");
                v2 = node.GetValue("windowY");
                if (v != null && v2 != null &&
                    float.TryParse(v,  System.Globalization.NumberStyles.Float, ic, out float wx) &&
                    float.TryParse(v2, System.Globalization.NumberStyles.Float, ic, out float wy))
                {
                    // Width stays 0 so DrawConfigTable still sizes it correctly on the
                    // first frame; the position guard in OnGUI checks _windowPositionRestored.
                    guiWindowRect = new Rect(wx, wy, 0, 0);
                    _windowPositionRestored = true;
                }

                v  = node.GetValue("columnMenuX");
                v2 = node.GetValue("columnMenuY");
                if (v != null && v2 != null &&
                    float.TryParse(v,  System.Globalization.NumberStyles.Float, ic, out float cmx) &&
                    float.TryParse(v2, System.Globalization.NumberStyles.Float, ic, out float cmy))
                {
                    columnMenuRect = new Rect(cmx, cmy, columnMenuRect.width, columnMenuRect.height);
                }

                TryParseBoolArray(node.GetValue("columnsVisibleFull"),    columnsVisibleFull);
                TryParseBoolArray(node.GetValue("columnsVisibleCompact"), columnsVisibleCompact);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RFEngineConfigGUI] Could not load settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes all persistent GUI state to PluginData. Safe to call from within OnGUI
        /// (file I/O happens on the calling thread; KSP's editor runs on the main thread
        /// so this is fine for the low-frequency writes we perform here).
        /// </summary>
        private static void SaveSettings()
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath));
                var ic = System.Globalization.CultureInfo.InvariantCulture;

                ConfigNode root = new ConfigNode();
                ConfigNode node = root.AddNode("ENGINECONFIGGUI_SETTINGS");

                node.AddValue("compactView",       compactView.ToString());
                node.AddValue("showSimPanel",      showSimPanel.ToString());
                node.AddValue("showChartPanel",    showChartPanel.ToString());
                node.AddValue("showTimeAsMinSec",  _showTimeAsMinSec.ToString());
                node.AddValue("fontScale",         _fontScale.ToString(ic));
                node.AddValue("windowX",     guiWindowRect.x.ToString(ic));
                node.AddValue("windowY",     guiWindowRect.y.ToString(ic));
                node.AddValue("columnMenuX", columnMenuRect.x.ToString(ic));
                node.AddValue("columnMenuY", columnMenuRect.y.ToString(ic));
                node.AddValue("columnsVisibleFull",    string.Join(",", columnsVisibleFull));
                node.AddValue("columnsVisibleCompact", string.Join(",", columnsVisibleCompact));

                root.Save(SettingsPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RFEngineConfigGUI] Could not save settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Parses a comma-separated "True,False,..." string into an existing bool array.
        /// Silently ignores malformed entries; missing entries leave the default in place.
        /// </summary>
        private static void TryParseBoolArray(string s, bool[] target)
        {
            if (string.IsNullOrEmpty(s)) return;
            string[] parts = s.Split(',');
            for (int i = 0; i < parts.Length && i < target.Length; i++)
                if (bool.TryParse(parts[i].Trim(), out bool val))
                    target[i] = val;
        }

        #endregion
    }
}
