using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using KSP.Localization;
using KSP.UI.Screens;
using RealFuels.TechLevels;
using ClickThroughFix;

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
        private static bool lastShowBottomSection = true;
        private string myToolTip = string.Empty;
        private int counterTT;
        private bool editorLocked = false;

        private Vector2 configScrollPos = Vector2.zero;
        private GUIContent configGuiContent;
        private static bool compactView = true; // Default to compact view
        private bool useLogScaleX = false;
        private bool useLogScaleY = false;
        private static bool showBottomSection = true;

        // Column visibility customization
        private bool showColumnMenu = false;
        private static Rect columnMenuRect = new Rect(100, 100, 220, 500);
        private static bool[] columnsVisibleFull = new bool[18];
        private static bool[] columnsVisibleCompact = new bool[18];
        private static bool columnVisibilityInitialized = false;

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
            if (!(_module.showRFGUI && inPartsEditor) && !(EditorLogic.fetch.editorScreen == EditorScreen.Actions && EditorActionGroups.Instance.GetSelectedParts().Contains(_module.part)))
            {
                EditorUnlock();
                return;
            }

            if (inPartsEditor && _module.part.symmetryCounterparts.FirstOrDefault(p => p.persistentId < _module.part.persistentId) is Part)
                return;

            if (guiWindowRect.width == 0)
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
                               || showBottomSection != lastShowBottomSection;

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
                lastShowBottomSection = showBottomSection;
            }

            mousePos = Input.mousePosition;
            mousePos.y = Screen.height - mousePos.y;
            if (guiWindowRect.Contains(mousePos))
                EditorLock();
            else
                EditorUnlock();

            myToolTip = myToolTip.Trim();

            guiWindowRect = ClickThruBlocker.GUILayoutWindow(unchecked((int)_module.part.persistentId), guiWindowRect, EngineManagerGUI, Localizer.Format("#RF_Engine_WindowTitle", _module.part.partInfo.title), Styles.styleEditorPanel);

            if (showColumnMenu)
            {
                columnMenuRect = ClickThruBlocker.GUIWindow(unchecked((int)_module.part.persistentId) + 1, columnMenuRect, DrawColumnMenuWindow, "Column Settings", Styles.styleEditorPanel);
            }

            // Draw tooltip AFTER all windows to ensure it appears on top
            if (!string.IsNullOrEmpty(myToolTip))
            {
                // Check if this is a button tooltip (marked with [BTN])
                bool isButtonTooltip = myToolTip.StartsWith("[BTN]");
                string displayText = isButtonTooltip ? myToolTip.Substring(5) : myToolTip;

                var tooltipStyle = new GUIStyle(EngineConfigStyles.ChartTooltip)
                {
                    fontSize = 13,
                    wordWrap = false,  // Disable word wrap for button tooltips to get natural width
                    normal = { background = _textures.ChartTooltipBg }
                };

                var content = new GUIContent(displayText);

                // Calculate dynamic width based on content
                float actualTooltipWidth;
                float tooltipHeight;

                if (isButtonTooltip)
                {
                    // For button tooltips: use natural width of content with some padding
                    Vector2 contentSize = tooltipStyle.CalcSize(content);
                    actualTooltipWidth = Mathf.Min(contentSize.x + 20, 400); // Max 400px width
                    tooltipStyle.wordWrap = actualTooltipWidth >= 400; // Enable wrap only if we hit max width
                    tooltipHeight = tooltipStyle.CalcHeight(content, actualTooltipWidth);
                }
                else
                {
                    // For row tooltips: use fixed width with word wrap
                    tooltipStyle.wordWrap = true;
                    actualTooltipWidth = toolTipWidth;
                    tooltipHeight = tooltipStyle.CalcHeight(content, actualTooltipWidth);
                }

                // Position button tooltips near cursor, row tooltips at fixed offset
                float tooltipX, tooltipY;
                if (isButtonTooltip)
                {
                    // Position near cursor: to the right and slightly down
                    tooltipX = mousePos.x + 20;
                    tooltipY = mousePos.y + 10;

                    // Keep tooltip on screen
                    if (tooltipX + actualTooltipWidth > Screen.width)
                        tooltipX = mousePos.x - actualTooltipWidth - 10; // Show to the left instead
                    if (tooltipY + tooltipHeight > Screen.height)
                        tooltipY = Screen.height - tooltipHeight - 10;
                }
                else
                {
                    // Original positioning for row tooltips
                    int offset = inPartsEditor ? -330 : 440;
                    tooltipX = guiWindowRect.xMin + offset;
                    tooltipY = mousePos.y - 5;
                }

                // Draw tooltip with maximum priority depth (most negative = on top)
                int oldDepth = GUI.depth;
                GUI.depth = -100000; // Use very negative depth to ensure it's on top of everything
                GUI.Box(new Rect(tooltipX, tooltipY, actualTooltipWidth, tooltipHeight), displayText, tooltipStyle);
                GUI.depth = oldDepth;
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
            GUILayout.Space(12); // Increased spacing to prevent overlap with window title

            GUILayout.BeginHorizontal();
            GUILayout.Label(_module.EditorDescription, EngineConfigStyles.DescriptionLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(compactView ? "Full View" : "Compact View", GUILayout.Width(100)))
            {
                compactView = !compactView;
            }
            if (GUILayout.Button(showBottomSection ? "Hide Chart" : "Show Chart", GUILayout.Width(85)))
            {
                showBottomSection = !showBottomSection;
            }
            // Heatmap toggle button (only show if chart is visible and has all required reliability data)
            if (showBottomSection && CanShowChart(_module.config))
            {
                bool currentHeatmapMode = Chart.UseHeatmapMode;
                if (GUILayout.Button(currentHeatmapMode ? "Line Chart" : "Heatmap", GUILayout.Width(85)))
                {
                    Chart.UseHeatmapMode = !currentHeatmapMode;
                }
            }
            if (GUILayout.Button("Settings", GUILayout.Width(70)))
            {
                showColumnMenu = !showColumnMenu;
            }
            // Close button
            if (GUILayout.Button("✕", EngineConfigStyles.CloseButton, GUILayout.Width(25)))
            {
                _module.CloseWindow();
                return;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(7);
            DrawConfigSelectors(_module.FilteredDisplayConfigs(false));

            if (showBottomSection)
            {
                if (CanShowChart(_module.config))
                {
                    GUILayout.Space(6);

                    Chart.UseLogScaleX = useLogScaleX;
                    Chart.UseLogScaleY = useLogScaleY;
                    Chart.UseSimulatedData = useSimulatedData;
                    Chart.SimulatedDataValue = simulatedDataValue;
                    Chart.ClusterSize = clusterSize;
                    Chart.ClusterSizeInput = clusterSizeInput;
                    Chart.DataValueInput = dataValueInput;
                    Chart.SliderTimeInput = sliderTimeInput;
                    Chart.IncludeIgnition = includeIgnition;
                    Chart.SliderModeIsPercentage = sliderModeIsPercentage;
                    Chart.SliderPercentage = sliderPercentage;
                    Chart.SliderPercentageInput = sliderPercentageInput;

                    Chart.Draw(_module.config, guiWindowRect.width - 10, 375, ref sliderTime);

                    useLogScaleX = Chart.UseLogScaleX;
                    useLogScaleY = Chart.UseLogScaleY;
                    useSimulatedData = Chart.UseSimulatedData;
                    simulatedDataValue = Chart.SimulatedDataValue;
                    clusterSize = Chart.ClusterSize;
                    clusterSizeInput = Chart.ClusterSizeInput;
                    dataValueInput = Chart.DataValueInput;
                    sliderTimeInput = Chart.SliderTimeInput;
                    includeIgnition = Chart.IncludeIgnition;
                    sliderModeIsPercentage = Chart.SliderModeIsPercentage;
                    sliderPercentage = Chart.SliderPercentage;
                    sliderPercentageInput = Chart.SliderPercentageInput;

                    GUILayout.Space(6);
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

                _techLevels.DrawTechLevelSelector();
            }

            GUILayout.Space(4);
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
            // Close button in top right
            if (GUI.Button(new Rect(columnMenuRect.width - 29, 4, 25, 20), "✕", EngineConfigStyles.CloseButton))
            {
                showColumnMenu = false;
                return;
            }

            DrawColumnMenu(new Rect(0, 20, columnMenuRect.width, columnMenuRect.height - 20));
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
            // Only enforce full minimum width when bottom section is visible (chart needs the width)
            // Use smaller minimum for compact view
            guiWindowRect.width = showBottomSection
                ? Mathf.Max(requiredWindowWidth, minWindowWidth)
                : Mathf.Max(requiredWindowWidth, minWindowWidthCompact);

            Rect headerRowRect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.label, GUILayout.Height(45));
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
            
            // Dynamic header and tooltip for survival column based on mode
            string survivalHeader;
            string survivalTooltip;

            if (sliderModeIsPercentage)
            {
                survivalHeader = Localizer.Format("#RF_Engine_ColTimeAtSurvival", $"{sliderPercentage:F1}");
                survivalTooltip = Localizer.Format("#RF_Engine_TipTimeAtSurvival", $"{sliderPercentage:F1}");
            }
            else
            {
                survivalHeader = Localizer.Format("#RF_Engine_ColSurvivalAtTime", ChartMath.FormatTime(sliderTime));
                survivalTooltip = Localizer.Format("#RF_Engine_TipSurvivalAtTime", ChartMath.FormatTime(sliderTime));
            }

            string[] headers = {
                Localizer.GetStringByTag("#RF_Engine_ColName"),
                Localizer.GetStringByTag("#RF_EngineRF_Thrust"),
                Localizer.GetStringByTag("#RF_Engine_ColMinThrottle"),
                Localizer.GetStringByTag("#RF_Engine_Isp"),
                Localizer.GetStringByTag("#RF_Engine_Enginemass"),
                Localizer.GetStringByTag("#RF_Engine_TLTInfo_Gimbal"),
                Localizer.GetStringByTag("#RF_EngineRF_Ignitions"),
                Localizer.GetStringByTag("#RF_Engine_ullage"),
                Localizer.GetStringByTag("#RF_Engine_pressureFed"),
                Localizer.GetStringByTag("#RF_Engine_ColRatedBurnTime"),
                Localizer.GetStringByTag("#RF_Engine_ColTestedBurnTime"),
                Localizer.GetStringByTag("#RF_Engine_ColIgnReliability"),
                Localizer.GetStringByTag("#RF_Engine_ColBurnNoData"),
                Localizer.GetStringByTag("#RF_Engine_ColBurnMaxData"),
                survivalHeader,
                Localizer.GetStringByTag("#RF_Engine_Requires"),
                Localizer.GetStringByTag("#RF_Engine_ColExtraCost"),
                ""
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

            for (int i = 0; i < headers.Length; i++)
            {
                if (IsColumnVisible(i))
                {
                    DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[i], headerRect.height), headers[i], tooltips[i]);
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
            configGuiContent.tooltip = tooltip;
            Matrix4x4 matrixBackup = GUI.matrix;
            float offsetX = rect.width / 2f;
            Vector2 pivot = new Vector2(rect.x + offsetX, rect.y + rect.height + 4f);
            GUIUtility.RotateAroundPivot(-45f, pivot);
            GUI.Label(new Rect(rect.x + offsetX, rect.y + rect.height - 22f, 140f, 24f), configGuiContent, headerStyle);
            GUI.matrix = matrixBackup;
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
                    GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[index], rowRect.height), text, index == 0 ? primaryStyle : secondaryStyle);
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
            drawCell(7, GetBoolSymbol(row.Node, "ullage"));
            drawCell(8, GetBoolSymbol(row.Node, "pressureFed"));
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
            GUIStyle smallButtonStyle = GUI.skin.button;

            string configName = node.GetValue("name");
            bool canUse = EngineConfigTechLevels.CanConfig(node);
            bool unlocked = EngineConfigTechLevels.UnlockedConfig(node, _module.part);
            double cost = EntryCostManager.Instance.ConfigEntryCost(configName);

            if (cost <= 0 && !unlocked && canUse)
                EntryCostManager.Instance.PurchaseConfig(configName, node.GetValue("techRequired"));

            // Calculate button widths dynamically based on their labels
            string switchLabel = isSelected ? "Active" : "Switch";
            float switchWidth = smallButtonStyle.CalcSize(new GUIContent(switchLabel)).x + 10f; // Add padding

            GUI.enabled = canUse && !unlocked && cost > 0;
            string purchaseLabel;
            string purchaseTooltip = string.Empty;

            if (cost > 0)
            {
                // Check if we can use credits to reduce the cost
                double displayCost = cost;
                if (!unlocked && EngineConfigRP1Integration.TryGetCreditAdjustedCost(cost, out double creditsAvailable, out double costAfterCredits))
                {
                    displayCost = costAfterCredits;
                    double creditsUsed = cost - costAfterCredits;
                    // Use special marker [BTN] so we can position this tooltip near cursor
                    purchaseTooltip = $"[BTN]Entry Cost: {cost:N0}√\n" +
                                    $"<color=#FFEB3B>Credits Available: {creditsAvailable:N0}</color>\n" +
                                    $"<color=#FFEB3B>Credits Used: {creditsUsed:N0}</color>\n" +
                                    $"<b>Final Cost: {costAfterCredits:N0}√</b>";
                }

                // Show final cost after credits on the button
                purchaseLabel = unlocked ? "Owned" : $"Buy ({displayCost:N0}√)";
            }
            else
                purchaseLabel = unlocked ? "Owned" : "Free";

            float purchaseWidth = smallButtonStyle.CalcSize(new GUIContent(purchaseLabel)).x + 10f;

            // Position buttons: Switch on left, Purchase on right
            Rect switchRect = new Rect(rect.x, rect.y, switchWidth, rect.height);
            Rect purchaseRect = new Rect(rect.x + switchWidth + 4f, rect.y, purchaseWidth, rect.height);

            GUI.enabled = !isSelected;
            if (GUI.Button(switchRect, switchLabel, smallButtonStyle))
            {
                if (!unlocked && cost <= 0)
                {
                    // Auto-purchase free configs using DrawSelectButton callback
                    _module.DrawSelectButton(node, isSelected, (cfgName) =>
                    {
                        EntryCostManager.Instance.PurchaseConfig(cfgName, node.GetValue("techRequired"));
                    });
                }
                apply?.Invoke();
            }

            GUI.enabled = canUse && !unlocked && cost > 0;
            if (GUI.Button(purchaseRect, new GUIContent(purchaseLabel, purchaseTooltip), smallButtonStyle))
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

            string[] columnNames = {
                "Name", "Thrust", "Min%", "ISP", "Mass", "Gimbal",
                "Ignitions", "Ullage", "Press-Fed", "Rated (s)", "Tested (s)",
                "Ign Rel.", "Burn No Data", "Burn Max Data",
                "Survival", "Tech", "Cost", "Actions"
            };

            float yPos = menuRect.y + 5;
            float leftX = menuRect.x + 8;

            GUIStyle headerStyle = EngineConfigStyles.ColumnMenuHeader;
            GUIStyle labelStyle = EngineConfigStyles.ColumnMenuLabel;

            GUI.Label(new Rect(leftX + 80, yPos, 50, 16), "Full", headerStyle);
            GUI.Label(new Rect(leftX + 135, yPos, 60, 16), "Compact", headerStyle);
            yPos += 18;

            Rect scrollRect = new Rect(leftX, yPos, menuRect.width - 16, menuRect.height - 28);

            GUI.BeginGroup(scrollRect);
            float itemY = 0;

            for (int i = 0; i < columnNames.Length; i++)
            {
                GUI.Label(new Rect(0, itemY, 75, 18), columnNames[i], labelStyle);

                bool newFullVisible = GUI.Toggle(new Rect(85, itemY + 1, 18, 18), columnsVisibleFull[i], "");
                if (newFullVisible != columnsVisibleFull[i])
                {
                    columnsVisibleFull[i] = newFullVisible;
                }

                bool newCompactVisible = GUI.Toggle(new Rect(140, itemY + 1, 18, 18), columnsVisibleCompact[i], "");
                if (newCompactVisible != columnsVisibleCompact[i])
                {
                    columnsVisibleCompact[i] = newCompactVisible;
                }

                itemY += 20;
            }

            GUI.EndGroup();
        }

        private void InitializeColumnVisibility()
        {
            if (columnVisibilityInitialized)
                return;

            // Full view: all columns visible
            for (int i = 0; i < 18; i++)
                columnsVisibleFull[i] = true;

            // Compact view: Name, Thrust, ISP, Mass, Ignitions, Ullage, Press-Fed, Ign Rel., Survival, Tech, Cost, Actions
            for (int i = 0; i < 18; i++)
                columnsVisibleCompact[i] = false;

            int[] compactColumns = { 0, 1, 3, 4, 6, 7, 8, 11, 14, 15, 16, 17 };
            foreach (int col in compactColumns)
                columnsVisibleCompact[col] = true;

            columnVisibilityInitialized = true;
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

            for (int i = 0; i < ConfigColumnWidths.Length; i++)
            {
                ConfigColumnWidths[i] = 30f;
            }

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
                    GetBoolSymbol(row.Node, "ullage"),
                    GetBoolSymbol(row.Node, "pressureFed"),
                    GetRatedBurnTimeString(row.Node),
                    GetTestedBurnTimeString(row.Node),
                    GetIgnitionReliabilityString(row.Node),
                    GetCycleReliabilityStartString(row.Node),
                    GetCycleReliabilityEndString(row.Node),
                    GetSurvivalAtTimeString(row.Node),
                    GetTechString(row.Node),
                    GetCostDeltaString(row.Node),
                    ""
                };

                for (int i = 0; i < cellValues.Length; i++)
                {
                    if (!string.IsNullOrEmpty(cellValues[i]))
                    {
                        float width = cellStyle.CalcSize(new GUIContent(cellValues[i])).x + 10f;
                        if (width > ConfigColumnWidths[i])
                            ConfigColumnWidths[i] = width;
                    }
                }
            }

            // Calculate dynamic width for Actions column (index 17) based on button labels
            float maxActionWidth = 0f;
            var buttonStyle = GUI.skin.button;
            foreach (var row in rows)
            {
                string configName = row.Node.GetValue("name");
                bool unlocked = EngineConfigTechLevels.UnlockedConfig(row.Node, _module.part);
                double cost = EntryCostManager.Instance.ConfigEntryCost(configName);

                // Calculate Switch button width (must match DrawActionCell padding)
                string switchLabel = "Switch";
                float switchWidth = buttonStyle.CalcSize(new GUIContent(switchLabel)).x + 10f; // Add padding to match DrawActionCell

                // Calculate Purchase button width (can vary based on cost)
                string purchaseLabel;
                if (cost > 0)
                {
                    double displayCost = cost;
                    // Check if credits would reduce the cost
                    if (!unlocked && EngineConfigRP1Integration.TryGetCreditAdjustedCost(cost, out _, out double costAfterCredits))
                        displayCost = costAfterCredits;

                    purchaseLabel = unlocked ? "Owned" : $"Buy ({displayCost:N0}√)";
                }
                else
                {
                    purchaseLabel = unlocked ? "Owned" : "Free";
                }
                float purchaseWidth = buttonStyle.CalcSize(new GUIContent(purchaseLabel)).x + 10f; // Add padding to match DrawActionCell

                // Total width = both buttons + spacing between them (must match DrawActionCell)
                float totalWidth = switchWidth + purchaseWidth + 4f; // 4px spacing to match DrawActionCell
                if (totalWidth > maxActionWidth)
                    maxActionWidth = totalWidth;
            }

            ConfigColumnWidths[17] = Mathf.Max(maxActionWidth, 160f); // Minimum 160px
            ConfigColumnWidths[7] = Mathf.Max(ConfigColumnWidths[7], 30f);
            ConfigColumnWidths[8] = Mathf.Max(ConfigColumnWidths[8], 30f);
            ConfigColumnWidths[9] = Mathf.Max(ConfigColumnWidths[9], 50f);
            ConfigColumnWidths[10] = Mathf.Max(ConfigColumnWidths[10], 50f);

            // Fix survival column (index 14) to maximum width to prevent window resizing during slider use
            // Calculate width based on "100.0% / 100.0%" (the widest possible value)
            float maxSurvivalWidth = cellStyle.CalcSize(new GUIContent("100.0% / 100.0%")).x + 10f;
            ConfigColumnWidths[14] = maxSurvivalWidth;
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
            if (node.HasNode("atmosphereCurve"))
            {
                FloatCurve isp = new FloatCurve();
                isp.Load(node.GetNode("atmosphereCurve"));
                float ispVac = isp.Evaluate(isp.maxTime);
                float ispSL = isp.Evaluate(isp.minTime);
                return $"{ispVac:N0}-{ispSL:N0}";
            }

            if (node.HasValue("IspSL") && node.HasValue("IspV"))
            {
                float.TryParse(node.GetValue("IspSL"), out float ispSL);
                float.TryParse(node.GetValue("IspV"), out float ispV);
                if (_module.techLevel != -1)
                {
                    TechLevel cTL = new TechLevel();
                    if (cTL.Load(node, _module.techNodes, _module.engineType, _module.techLevel))
                    {
                        ispSL *= ModuleEngineConfigsBase.ispSLMult * cTL.AtmosphereCurve.Evaluate(1);
                        ispV *= ModuleEngineConfigsBase.ispVMult * cTL.AtmosphereCurve.Evaluate(0);
                    }
                }
                return $"{ispV:N0}-{ispSL:N0}";
            }

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

        internal string GetBoolSymbol(ConfigNode node, string key)
        {
            if (!node.HasValue(key))
                return "<color=#9E9E9E>✗</color>";
            bool isTrue = node.GetValue(key).ToLower() == "true";
            return isTrue ? "<color=#FFA726>✓</color>" : "<color=#9E9E9E>✗</color>";
        }

        internal string GetRatedBurnTimeString(ConfigNode node)
        {
            bool hasRatedBurnTime = node.HasValue("ratedBurnTime");
            bool hasRatedContinuousBurnTime = node.HasValue("ratedContinuousBurnTime");

            if (!hasRatedBurnTime && !hasRatedContinuousBurnTime)
                return "∞";

            if (hasRatedBurnTime && hasRatedContinuousBurnTime)
            {
                string continuous = node.GetValue("ratedContinuousBurnTime");
                string cumulative = node.GetValue("ratedBurnTime");
                return $"{continuous}/{cumulative}";
            }

            return hasRatedBurnTime ? node.GetValue("ratedBurnTime") : node.GetValue("ratedContinuousBurnTime");
        }

        internal string GetTestedBurnTimeString(ConfigNode node)
        {
            if (!node.HasValue("testedBurnTime"))
                return "-";

            float testedBurnTime = 0f;
            if (node.TryGetValue("testedBurnTime", ref testedBurnTime))
                return testedBurnTime.ToString("F0");

            return "-";
        }

        internal string GetIgnitionReliabilityString(ConfigNode node)
        {
            if (!node.HasValue("ignitionReliabilityStart") || !node.HasValue("ignitionReliabilityEnd"))
                return "-";

            if (!float.TryParse(node.GetValue("ignitionReliabilityStart"), out float valStart)) return "-";
            if (!float.TryParse(node.GetValue("ignitionReliabilityEnd"), out float valEnd)) return "-";

            return $"{valStart:P1} / {valEnd:P1}";
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

            // Faded colors for better readability in table
            string fadedOrange = "#FFB380";
            string fadedGreen = "#80E680";

            if (sliderModeIsPercentage)
            {
                // Percentage mode: show TIME to reach the selected percentage
                float targetProb = sliderPercentage / 100f;
                
                // Find time for start and end reliability (no clustering/ignition for table display)
                float timeStart = ChartMath.FindTimeForSurvivalProb(targetProb, ratedBurnTime, cycleReliabilityStart, cycleCurve, 10000f);
                float timeEnd = ChartMath.FindTimeForSurvivalProb(targetProb, ratedBurnTime, cycleReliabilityEnd, cycleCurve, 10000f);
                
                return $"<color={fadedOrange}>{ChartMath.FormatTime(timeStart)}</color> / <color={fadedGreen}>{ChartMath.FormatTime(timeEnd)}</color>";
            }
            else
            {
                // Time mode: show PERCENTAGE at the selected time
                float baseRateStart = -Mathf.Log(cycleReliabilityStart) / ratedBurnTime;
                float baseRateEnd = -Mathf.Log(cycleReliabilityEnd) / ratedBurnTime;

                float surviveStart = ChartMath.CalculateSurvivalProbAtTime(sliderTime, ratedBurnTime, cycleReliabilityStart, baseRateStart, cycleCurve);
                float surviveEnd = ChartMath.CalculateSurvivalProbAtTime(sliderTime, ratedBurnTime, cycleReliabilityEnd, baseRateEnd, cycleCurve);

                return $"<color={fadedOrange}>{surviveStart:P1}</color> / <color={fadedGreen}>{surviveEnd:P1}</color>";
            }
        }

        internal string GetTechString(ConfigNode node)
        {
            if (!node.HasValue("techRequired"))
                return "-";

            string tech = node.GetValue("techRequired");
            if (ModuleEngineConfigsBase.techNameToTitle.TryGetValue(tech, out string title))
                tech = title;

            return $"<size=11>{tech}</size>";
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
            return $"{sign}{curCost:N0}√";
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
    }
}
