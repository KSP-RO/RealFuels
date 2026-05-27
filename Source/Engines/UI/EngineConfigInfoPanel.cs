using System;
using UnityEngine;

namespace RealFuels
{
    /// <summary>
    /// Handles the info panel display showing reliability stats and simulation controls.
    /// Layout: DU reliability stats (left column) beside simulation controls (right column).
    /// All pixel geometry scales with <see cref="EngineConfigStyles.FontScale"/>.
    /// </summary>
    public class EngineConfigInfoPanel
    {
        private readonly ModuleEngineConfigsBase _module;
        private readonly EngineConfigTextures _textures;

        public EngineConfigInfoPanel(ModuleEngineConfigsBase module)
        {
            _module = module;
            _textures = EngineConfigTextures.Instance;
        }

        public void Draw(Rect rect, float ratedBurnTime, float testedBurnTime, bool hasTestedBurnTime,
            float cycleReliabilityStart, float cycleReliabilityEnd, float ignitionReliabilityStart, float ignitionReliabilityEnd,
            bool hasCurrentData, float cycleReliabilityCurrent, float ignitionReliabilityCurrent, float dataPercentage,
            float currentDataValue, float maxDataValue, float realCurrentData, float realMaxData,
            ref bool useSimulatedData, ref float simulatedDataValue, ref int clusterSize,
            ref string clusterSizeInput, ref string dataValueInput, ref float sliderTime, ref string sliderTimeInput,
            ref bool includeIgnition, ref bool sliderModeIsPercentage, ref float sliderPercentage, ref string sliderPercentageInput,
            FloatCurve cycleCurve, float maxGraphTime)
        {
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(rect, _textures.InfoPanelBg);

            // ── Left / right column split ─────────────────────────────────────
            // Give the left column (DU stats) slightly more room when Current DU
            // is present because there are three sections instead of two.
            float leftFraction = hasCurrentData ? 0.60f : 0.55f;
            float leftW  = Mathf.Floor(rect.width * leftFraction);
            float rightW = rect.width - leftW - 1f;

            Rect leftRect  = new Rect(rect.x,              rect.y, leftW,  rect.height);
            Rect rightRect = new Rect(rect.x + leftW + 1f, rect.y, rightW, rect.height);

            // Vertical separator
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(new Rect(rect.x + leftW, rect.y + 8f, 1f, rect.height - 16f), _textures.ChartSeparator);

            // Left: Starting / Current / Max DU reliability stats
            DrawReliabilitySection(leftRect, rect.y + 4f,
                ratedBurnTime,
                cycleReliabilityStart, cycleReliabilityEnd, cycleReliabilityCurrent,
                ignitionReliabilityStart, ignitionReliabilityEnd, ignitionReliabilityCurrent,
                hasCurrentData, cycleCurve, clusterSize, sliderTime, includeIgnition,
                sliderModeIsPercentage, sliderPercentage, maxGraphTime);

            // Right: simulation controls (mode toggle, sliders, data & cluster inputs)
            DrawSimulationControls(rightRect.x, rightRect.width, rect.y + 4f,
                ratedBurnTime,
                cycleReliabilityStart, cycleReliabilityEnd, cycleReliabilityCurrent,
                ignitionReliabilityStart, ignitionReliabilityEnd, ignitionReliabilityCurrent,
                hasCurrentData, maxGraphTime, maxDataValue, realCurrentData, realMaxData,
                ref useSimulatedData, ref simulatedDataValue, ref clusterSize, ref clusterSizeInput, ref dataValueInput,
                ref sliderTime, ref sliderTimeInput, ref includeIgnition,
                ref sliderModeIsPercentage, ref sliderPercentage, ref sliderPercentageInput,
                cycleCurve);
        }

        #region Reliability Section

        private void DrawReliabilitySection(Rect panelRect, float yPos,
            float ratedBurnTime,
            float cycleReliabilityStart, float cycleReliabilityEnd, float cycleReliabilityCurrent,
            float ignitionReliabilityStart, float ignitionReliabilityEnd, float ignitionReliabilityCurrent,
            bool hasCurrentData, FloatCurve cycleCurve,
            int clusterSize, float sliderTime, bool includeIgnition,
            bool sliderModeIsPercentage, float sliderPercentage, float maxGraphTime)
        {
            string orangeColor = "#FF8033";
            string blueColor   = "#7DD9FF";
            string greenColor  = "#4DE64D";

            float baseRateStart   = -Mathf.Log(cycleReliabilityStart) / ratedBurnTime;
            float baseRateEnd     = -Mathf.Log(cycleReliabilityEnd)   / ratedBurnTime;
            float baseRateCurrent = hasCurrentData ? -Mathf.Log(cycleReliabilityCurrent) / ratedBurnTime : 0f;

            float displayValueStart, displayValueEnd, displayValueCurrent;
            float surviveStart, surviveEnd, surviveCurrent;
            bool displayIsTime;

            if (sliderModeIsPercentage)
            {
                displayIsTime = true;
                float targetProb = sliderPercentage / 100f;
                surviveStart = targetProb; surviveEnd = targetProb; surviveCurrent = targetProb;

                float tStart   = targetProb;
                float tEnd     = targetProb;
                float tCurrent = targetProb;

                if (clusterSize > 1)
                {
                    float inv = 1f / clusterSize;
                    tStart   = Mathf.Pow(targetProb, inv);
                    tEnd     = Mathf.Pow(targetProb, inv);
                    tCurrent = Mathf.Pow(targetProb, inv);
                }
                if (includeIgnition)
                {
                    tStart   /= ignitionReliabilityStart;
                    tEnd     /= ignitionReliabilityEnd;
                    if (hasCurrentData) tCurrent /= ignitionReliabilityCurrent;
                }

                displayValueStart = ChartMath.FindTimeForSurvivalProb(tStart, ratedBurnTime, cycleReliabilityStart, cycleCurve, maxGraphTime);
                displayValueEnd   = ChartMath.FindTimeForSurvivalProb(tEnd,   ratedBurnTime, cycleReliabilityEnd,   cycleCurve, maxGraphTime);
                displayValueCurrent = hasCurrentData
                    ? ChartMath.FindTimeForSurvivalProb(tCurrent, ratedBurnTime, cycleReliabilityCurrent, cycleCurve, maxGraphTime)
                    : 0f;
                if (!hasCurrentData) surviveCurrent = 0f;
            }
            else
            {
                displayIsTime = false;

                surviveStart   = ChartMath.CalculateSurvivalProbAtTime(sliderTime, ratedBurnTime, cycleReliabilityStart,   baseRateStart,   cycleCurve);
                surviveEnd     = ChartMath.CalculateSurvivalProbAtTime(sliderTime, ratedBurnTime, cycleReliabilityEnd,     baseRateEnd,     cycleCurve);
                surviveCurrent = hasCurrentData
                    ? ChartMath.CalculateSurvivalProbAtTime(sliderTime, ratedBurnTime, cycleReliabilityCurrent, baseRateCurrent, cycleCurve)
                    : 0f;

                if (includeIgnition)
                {
                    surviveStart *= ignitionReliabilityStart;
                    surviveEnd   *= ignitionReliabilityEnd;
                    if (hasCurrentData) surviveCurrent *= ignitionReliabilityCurrent;
                }
                if (clusterSize > 1)
                {
                    surviveStart   = Mathf.Pow(surviveStart,   clusterSize);
                    surviveEnd     = Mathf.Pow(surviveEnd,     clusterSize);
                    if (hasCurrentData) surviveCurrent = Mathf.Pow(surviveCurrent, clusterSize);
                }

                displayValueStart   = surviveStart   * 100f;
                displayValueEnd     = surviveEnd     * 100f;
                displayValueCurrent = surviveCurrent * 100f;
            }

            float s            = EngineConfigStyles.FontScale;
            float sectionH     = Mathf.RoundToInt(145 * s);  // extra room for Ignition label row
            float totalWidth   = panelRect.width - 16f;
            float numSections  = hasCurrentData ? 3f : 2f;
            float sectionWidth = totalWidth / numSections;
            float currentX     = panelRect.x + 8f;

            float igniteStart   = clusterSize > 1 ? Mathf.Pow(ignitionReliabilityStart,   clusterSize) : ignitionReliabilityStart;
            float igniteEnd     = clusterSize > 1 ? Mathf.Pow(ignitionReliabilityEnd,     clusterSize) : ignitionReliabilityEnd;
            float igniteCurrent = hasCurrentData
                ? (clusterSize > 1 ? Mathf.Pow(ignitionReliabilityCurrent, clusterSize) : ignitionReliabilityCurrent)
                : 0f;

            DrawSurvivalSection(currentX, yPos, sectionWidth, sectionH, "Starting DU", orangeColor,
                surviveStart, displayValueStart, displayIsTime, sliderTime, clusterSize, igniteStart, includeIgnition);
            currentX += sectionWidth;

            if (hasCurrentData)
            {
                DrawSurvivalSection(currentX, yPos, sectionWidth, sectionH, "Current DU", blueColor,
                    surviveCurrent, displayValueCurrent, displayIsTime, sliderTime, clusterSize, igniteCurrent, includeIgnition);
                currentX += sectionWidth;
            }

            DrawSurvivalSection(currentX, yPos, sectionWidth, sectionH, "Max DU", greenColor,
                surviveEnd, displayValueEnd, displayIsTime, sliderTime, clusterSize, igniteEnd, includeIgnition);
        }

        private void DrawSurvivalSection(float x, float y, float width, float height,
            string title, string color,
            float survivalProb, float displayValue, bool displayIsTime,
            float time, int clusterSize, float ignitionProb, bool includeIgnition)
        {
            float s = EngineConfigStyles.FontScale;

            int titleFontSize  = Mathf.Max(10, Mathf.RoundToInt(18 * s));
            int valueFontSize  = Mathf.Max(10, Mathf.RoundToInt(20 * s));
            int valueSizeTag   = Mathf.Max(10, Mathf.RoundToInt(24 * s));
            int detailFontSize = Mathf.Max(8,  Mathf.RoundToInt(13 * s));

            float titleH = Mathf.RoundToInt(24 * s);
            float valueY = Mathf.RoundToInt(26 * s);
            float valueH = Mathf.RoundToInt(28 * s);
            float secY   = Mathf.RoundToInt(54 * s);
            float secH   = Mathf.RoundToInt(48 * s);
            // Ignition row sits clearly below the secondary text block (secY+secH = 102*s).
            float ignY   = Mathf.RoundToInt(108 * s);
            float ignH   = Mathf.RoundToInt(24 * s);

            // Title
            var headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = titleFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter,
                richText  = true,
                normal    = { textColor = Color.white }
            };
            GUI.Label(new Rect(x, y, width, titleH), $"<color={color}>{title}</color>", headerStyle);

            // Main value
            string displayText = displayIsTime
                ? $"<size={valueSizeTag}><b>{ChartMath.FormatTime(displayValue)}</b></size>"
                : $"<size={valueSizeTag}><b>{displayValue:F2}%</b></size>";

            var displayStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = valueFontSize,
                alignment = TextAnchor.MiddleCenter,
                richText  = true,
                normal    = { textColor = Color.white }
            };
            GUI.Label(new Rect(x, y + valueY, width, valueH), displayText, displayStyle);

            // Secondary "1 in X" text
            float failureRate = 1f - survivalProb;
            float oneInX      = failureRate > 0.0001f ? (1f / failureRate) : 9999f;
            string entityText = clusterSize > 1 ? $"cluster of {clusterSize}" : "burn";

            string secondaryText = displayIsTime
                ? $"1 in <color=#FF6666>{oneInX:F1}</color> {entityText}s fail (<color=#90EE90>{survivalProb * 100f:F1}%</color>)"
                : $"at <color=#90EE90>{ChartMath.FormatTime(time)}</color>\n1 in <color=#FF6666>{oneInX:F1}</color> {entityText}s fail";

            var failStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = detailFontSize,
                alignment = TextAnchor.UpperCenter,
                richText  = true,
                wordWrap  = true,
                normal    = { textColor = new Color(0.9f, 0.9f, 0.9f) }
            };
            GUI.Label(new Rect(x + 4, y + secY, width - 8, secH), secondaryText, failStyle);

            // Ignition probability (only when not folded into the main calc)
            if (!includeIgnition)
            {
                var ignStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize  = detailFontSize,
                    alignment = TextAnchor.UpperCenter,
                    normal    = { textColor = new Color(0.9f, 0.9f, 0.9f) }
                };
                GUI.Label(new Rect(x + 4, y + ignY, width - 8, ignH),
                    $"Ignition: {ignitionProb * 100f:F2}%", ignStyle);
            }
        }

        #endregion

        #region Simulation Controls

        private void DrawSimulationControls(float x, float width, float yPos,
            float ratedBurnTime, float cycleReliabilityStart, float cycleReliabilityEnd, float cycleReliabilityCurrent,
            float ignitionReliabilityStart, float ignitionReliabilityEnd, float ignitionReliabilityCurrent,
            bool hasCurrentData, float maxGraphTime, float maxDataValue,
            float realCurrentData, float realMaxData,
            ref bool useSimulatedData, ref float simulatedDataValue, ref int clusterSize,
            ref string clusterSizeInput, ref string dataValueInput, ref float sliderTime, ref string sliderTimeInput,
            ref bool includeIgnition, ref bool sliderModeIsPercentage, ref float sliderPercentage, ref string sliderPercentageInput,
            FloatCurve cycleCurve)
        {
            float s = EngineConfigStyles.FontScale;
            bool hasRealData = realCurrentData >= 0f && realMaxData > 0f;

            // Scaled pixel constants
            // step=28 gives rows enough breathing room that text-field borders don't touch.
            // This also makes the sim section height (4×step + btnH ≈ 132s) match the DU
            // section height (ignY + ignH = 108s + 24s = 132s) so the panel fits tightly.
            int btnH  = Mathf.Max(16, Mathf.RoundToInt(20 * s));
            int lbH   = Mathf.Max(12, Mathf.RoundToInt(16 * s));
            int step  = Mathf.Max(20, Mathf.RoundToInt(28 * s));

            // Two-column layout: 8px margin on each side, 8px gap between columns.
            float margin = 8f;
            float gap    = 8f;
            float innerW = width - margin * 2f;
            float colW   = (innerW - gap) / 2f;
            float col1X  = x + margin;
            float col2X  = x + margin + colW + gap;

            // Text-field width for the cluster-size input (# of Engines).
            float fieldW = Mathf.Max(40, Mathf.RoundToInt(50 * s));

            GUIStyle buttonStyle  = EngineConfigStyles.CompactButton;
            GUIStyle controlStyle = EngineConfigStyles.Control;

            // Input style: override the KSP textField background with a neutral mid-gray
            // so fields are clearly visible against the dark info-panel background.
            var inputStyle = new GUIStyle(HighLogic.Skin.textField)
            {
                fontSize  = Mathf.Max(8, Mathf.RoundToInt(12 * s)),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            inputStyle.normal.background  = _textures.InputFieldBg;
            inputStyle.focused.background = _textures.InputFieldBg;
            inputStyle.normal.textColor   = Color.white;
            inputStyle.focused.textColor  = new Color(0.9f, 0.95f, 1f);

            // ── Section header (full width) ─────────────────────────────────────────
            GUI.Label(new Rect(x, yPos, width, btnH), "Simulate:", EngineConfigStyles.InfoSection);
            yPos += step;

            // ── Row 1 — Col 1: Mode toggle │ Col 2: Burn Time input ────────────────
            string modeLabel = sliderModeIsPercentage ? "Mode: % → Time" : "Mode: Time → %";
            if (GUI.Button(new Rect(col1X, yPos, colW, btnH), modeLabel, buttonStyle))
                sliderModeIsPercentage = !sliderModeIsPercentage;

            // Col 2: Burn Time or Survival % label + field (mode-dependent)
            if (sliderModeIsPercentage)
            {
                string pctLabel  = "Survival %:";
                float  pctLabelW = controlStyle.CalcSize(new GUIContent(pctLabel)).x + 4f;
                float  pctFieldW = Mathf.Max(fieldW, colW - pctLabelW - 4f);
                GUI.Label(new Rect(col2X, yPos, pctLabelW, btnH), pctLabel, controlStyle);

                if (GUI.GetNameOfFocusedControl() != "sliderPercentageInput")
                    sliderPercentageInput = $"{sliderPercentage:F1}";
                GUI.SetNextControlName("sliderPercentageInput");
                string newPct = GUI.TextField(new Rect(col2X + pctLabelW + 4, yPos, pctFieldW, btnH), sliderPercentageInput, 6, inputStyle);
                if (newPct != sliderPercentageInput)
                {
                    sliderPercentageInput = newPct;
                    if (GUI.GetNameOfFocusedControl() == "sliderPercentageInput" &&
                        float.TryParse(sliderPercentageInput, out float ip))
                        sliderPercentage = Mathf.Clamp(ip, 0.1f, 99.9f);
                }
            }
            else
            {
                bool   minSecMode  = EngineConfigChart.ShowTimeAsMinSec;
                string timeLabel   = minSecMode ? "Burn Time (m:ss):" : "Burn Time (s):";
                float  timeLabelW  = controlStyle.CalcSize(new GUIContent(timeLabel)).x + 4f;
                float  timeFieldW  = Mathf.Max(fieldW, colW - timeLabelW - 4f);
                GUI.Label(new Rect(col2X, yPos, timeLabelW, btnH), timeLabel, controlStyle);

                if (GUI.GetNameOfFocusedControl() != "sliderTimeInput")
                    sliderTimeInput = FormatTimeForInput(sliderTime, minSecMode);
                GUI.SetNextControlName("sliderTimeInput");
                string newTime = GUI.TextField(new Rect(col2X + timeLabelW + 4, yPos, timeFieldW, btnH), sliderTimeInput, 8, inputStyle);
                if (newTime != sliderTimeInput)
                {
                    sliderTimeInput = newTime;
                    if (GUI.GetNameOfFocusedControl() == "sliderTimeInput" &&
                        TryParseBurnTimeInput(sliderTimeInput, out float it))
                        sliderTime = Mathf.Clamp(it, 0f, maxGraphTime);
                }
            }
            yPos += step;

            // ── Row 2 — Col 1: Set to Current du │ Col 2: Data (du) entry ──────────
            string resetText = hasRealData ? $"Set to Current ({realCurrentData:F0}du)" : "Set to Current du";
            if (GUI.Button(new Rect(col1X, yPos, colW, btnH), resetText, buttonStyle))
            {
                if (hasRealData) { simulatedDataValue = realCurrentData; dataValueInput = $"{realCurrentData:F0}"; useSimulatedData = false; }
                else             { simulatedDataValue = 0f;              dataValueInput = "0";                     useSimulatedData = true;  }
                clusterSize = 1; clusterSizeInput = "1";
            }

            // Col 2: Data (du) label + text field
            {
                string dataLabel  = "Data (du):";
                float  dataLabelW = controlStyle.CalcSize(new GUIContent(dataLabel)).x + 4f;
                float  dataFieldW = Mathf.Max(fieldW, colW - dataLabelW - 4f);
                GUI.Label(new Rect(col2X, yPos, dataLabelW, btnH), dataLabel, controlStyle);

                // Sync field text from simulatedDataValue unless the user is typing in the field.
                if (GUI.GetNameOfFocusedControl() != "dataValueInput")
                {
                    if (!useSimulatedData)
                        simulatedDataValue = hasRealData ? realCurrentData : 0f;
                    dataValueInput = $"{simulatedDataValue:F0}";
                }

                GUI.SetNextControlName("dataValueInput");
                string newData = GUI.TextField(new Rect(col2X + dataLabelW + 4, yPos, dataFieldW, btnH), dataValueInput, 6, inputStyle);
                if (newData != dataValueInput)
                {
                    dataValueInput = newData;
                    if (GUI.GetNameOfFocusedControl() == "dataValueInput" &&
                        float.TryParse(dataValueInput, out float idv))
                    {
                        simulatedDataValue = Mathf.Clamp(idv, 0f, maxDataValue);
                        useSimulatedData = true;
                    }
                }
            }
            yPos += step;

            // ── Data (du) slider — "Data (du):" label left, track fills remaining width ──
            // Ensure simulatedDataValue is correct before drawing the slider.
            if (!useSimulatedData && GUI.GetNameOfFocusedControl() != "dataValueInput")
                simulatedDataValue = hasRealData ? realCurrentData : 0f;

            {
                string sliderLabel  = "Data (du):";
                float  sliderLabelW = controlStyle.CalcSize(new GUIContent(sliderLabel)).x + 4f;
                float  sliderX      = x + margin + sliderLabelW + 4f;
                float  sliderTrackW = innerW - sliderLabelW - 4f;

                // Vertically center the label on the slider track height (lbH).
                GUI.Label(new Rect(x + margin, yPos, sliderLabelW, lbH), sliderLabel, controlStyle);

                // Custom track style: override background so it's visible against the dark panel.
                var sliderStyle = new GUIStyle(HighLogic.Skin.horizontalSlider);
                sliderStyle.normal.background = _textures.SliderTrackBg;

                // Custom thumb style: light blue-gray fill so it pops against both the track
                // and the panel background.  fixedHeight matches the label row; fixedWidth gives
                // the thumb a stable hit target.
                var thumbStyle = new GUIStyle(HighLogic.Skin.horizontalSliderThumb);
                thumbStyle.normal.background  = _textures.SliderThumbBg;
                thumbStyle.active.background  = _textures.SliderThumbBg;
                thumbStyle.hover.background   = _textures.SliderThumbBg;
                thumbStyle.fixedHeight = btnH;
                thumbStyle.fixedWidth  = Mathf.Max(10, Mathf.RoundToInt(14 * s));

                float newSliderVal = GUI.HorizontalSlider(
                    new Rect(sliderX, yPos, sliderTrackW, lbH),
                    simulatedDataValue, 0f, maxDataValue,
                    sliderStyle,
                    thumbStyle);

                if (!Mathf.Approximately(newSliderVal, simulatedDataValue))
                {
                    simulatedDataValue = newSliderVal;
                    useSimulatedData   = (hasRealData  && Mathf.Abs(simulatedDataValue - realCurrentData) > 0.1f)
                                      || (!hasRealData && simulatedDataValue > 0.1f);
                    if (GUI.GetNameOfFocusedControl() != "dataValueInput")
                        dataValueInput = $"{simulatedDataValue:F0}";
                }
            }
            yPos += step;

            // ── Bottom row — Include Ignition (KSP toggle) | # of Engines ──────────
            // GUI.Toggle lays out the checkbox image and label as a single combined block,
            // so alignment alone cannot independently center the text.  Instead: draw the
            // checkbox with no label (GUIContent.none), then render "Include Ignition" as a
            // plain label whose position we control precisely.
            //
            // Checkbox: square, centered vertically within btnH.
            float checkboxSize = Mathf.Max(14, Mathf.RoundToInt(18 * s));
            float checkboxY    = yPos + (btnH - checkboxSize) * 0.5f;
            var checkboxStyle  = new GUIStyle(HighLogic.Skin.toggle)
            {
                fixedWidth  = checkboxSize,
                fixedHeight = checkboxSize
            };
            includeIgnition = GUI.Toggle(
                new Rect(col1X, checkboxY, checkboxSize, checkboxSize),
                includeIgnition, GUIContent.none, checkboxStyle);

            // "Include Ignition" label: sits in the remaining colW space, vertically centered.
            var ignLabelStyle = new GUIStyle(HighLogic.Skin.label)
            {
                fontSize    = Mathf.Max(8, Mathf.RoundToInt(12 * s)),
                fontStyle   = FontStyle.Bold,
                alignment   = TextAnchor.MiddleLeft,
                fixedHeight = 0f,
                padding     = new RectOffset(0, 0, 0, 0),
                normal      = { textColor = Color.white }
            };
            float ignTextX = col1X + checkboxSize + 4f;
            float ignTextW = colW - checkboxSize - 4f;
            GUI.Label(new Rect(ignTextX, yPos, ignTextW, btnH), "Include Ignition", ignLabelStyle);

            // HighLogic.Skin.label base keeps the font consistent with the toggle.
            // fixedHeight = 0f + TextAnchor.MiddleLeft centers the text within btnH.
            var engLabelStyle = new GUIStyle(HighLogic.Skin.label)
            {
                fontSize    = Mathf.Max(8, Mathf.RoundToInt(12 * s)),
                fontStyle   = FontStyle.Bold,
                alignment   = TextAnchor.MiddleLeft,
                fixedHeight = 0f,
                padding     = new RectOffset(0, 0, 0, 0),
                normal      = { textColor = new Color(0.8f, 0.8f, 0.8f) }
            };
            {
                string engLabel  = "# of Engines:";
                float  engLabelW = engLabelStyle.CalcSize(new GUIContent(engLabel)).x + 4f;
                GUI.Label(new Rect(col2X, yPos, engLabelW, btnH), engLabel, engLabelStyle);

                clusterSizeInput = clusterSize.ToString();
                GUI.SetNextControlName("clusterSizeInput");
                string newCluster = GUI.TextField(new Rect(col2X + engLabelW + 4, yPos, fieldW, btnH), clusterSizeInput, 3, inputStyle);
                if (newCluster != clusterSizeInput)
                {
                    clusterSizeInput = newCluster;
                    if (GUI.GetNameOfFocusedControl() == "clusterSizeInput" &&
                        int.TryParse(clusterSizeInput, out int icv))
                    {
                        clusterSize = Mathf.Clamp(icv, 1, 100);
                        clusterSizeInput = clusterSize.ToString();
                    }
                }
            }
        }

        #endregion

        #region Time Formatting Helpers

        /// <summary>
        /// Format a burn-time value for the simulation-controls text field.
        /// In min:sec mode values ≥ 60 s render as "m:ss"; everything else as plain integer seconds.
        /// </summary>
        private static string FormatTimeForInput(float seconds, bool minSecMode)
        {
            if (!minSecMode || seconds < 60f) return $"{seconds:F0}";
            int mins = Mathf.FloorToInt(seconds / 60f);
            int secs = Mathf.RoundToInt(seconds % 60f);
            if (secs == 60) { mins++; secs = 0; }
            return $"{mins}:{secs:D2}";
        }

        /// <summary>
        /// Parse a burn-time string entered by the player.  Accepts:
        /// "90", "90.5", "90s", "1:30", "1:30.5", "1m30s", "1m30", "2m".
        /// Returns false if unparseable; <paramref name="seconds"/> is 0 in that case.
        /// </summary>
        private static bool TryParseBurnTimeInput(string input, out float seconds)
        {
            seconds = 0f;
            if (string.IsNullOrWhiteSpace(input)) return false;
            input = input.Trim().ToLower();
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            var nf = System.Globalization.NumberStyles.Float;

            // Plain seconds: "90" / "90.5" / "90s"
            string stripped = input.TrimEnd('s');
            if (float.TryParse(stripped, nf, ic, out float plain)) { seconds = plain; return true; }

            // Colon notation: "1:30" / "1:30.5"
            int colon = input.IndexOf(':');
            if (colon > 0 && colon < input.Length - 1)
            {
                if (float.TryParse(input.Substring(0, colon), nf, ic, out float cM) &&
                    float.TryParse(input.Substring(colon + 1).TrimEnd('s'), nf, ic, out float cS))
                { seconds = cM * 60f + cS; return true; }
            }

            // m/s notation: "1m30s" / "1m30" / "2m"
            int mIdx = input.IndexOf('m');
            if (mIdx > 0 && float.TryParse(input.Substring(0, mIdx), nf, ic, out float mM))
            {
                string secPart = input.Substring(mIdx + 1).TrimEnd('s');
                float mS = 0f;
                if (string.IsNullOrEmpty(secPart) || float.TryParse(secPart, nf, ic, out mS))
                { seconds = mM * 60f + mS; return true; }
            }

            return false;
        }

        #endregion
    }
}
