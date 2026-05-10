using System;
using UnityEngine;

namespace RealFuels
{
    /// <summary>
    /// Handles the info panel display showing reliability stats, data gains, and simulation controls.
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
            // Draw background
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(rect, _textures.InfoPanelBg);
            }

            float yPos = rect.y + 4;

            // Draw reliability section (burn survival - three sections with optional ignition text)
            yPos = DrawReliabilitySection(rect, yPos, ratedBurnTime,
                cycleReliabilityStart, cycleReliabilityEnd, cycleReliabilityCurrent,
                ignitionReliabilityStart, ignitionReliabilityEnd, ignitionReliabilityCurrent,
                hasCurrentData, cycleCurve, clusterSize, sliderTime, includeIgnition,
                sliderModeIsPercentage, sliderPercentage, maxGraphTime);

            // Separator
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(rect.x + 8, yPos, rect.width - 16, 1), _textures.ChartSeparator);
            }
            yPos += 10;

            // Side-by-side: Data Gains (left) and Controls (right)
            yPos = DrawSideBySideSection(rect, yPos, ratedBurnTime, cycleReliabilityStart, cycleReliabilityEnd, cycleReliabilityCurrent,
                ignitionReliabilityStart, ignitionReliabilityEnd, ignitionReliabilityCurrent,
                hasCurrentData, maxGraphTime, maxDataValue, realCurrentData, realMaxData,
                ref useSimulatedData, ref simulatedDataValue, ref clusterSize, ref clusterSizeInput, ref dataValueInput,
                ref sliderTime, ref sliderTimeInput, ref includeIgnition, ref sliderModeIsPercentage, ref sliderPercentage, ref sliderPercentageInput,
                cycleCurve);
        }

        #region Reliability Section

        private float DrawReliabilitySection(Rect rect, float yPos,
            float ratedBurnTime,
            float cycleReliabilityStart, float cycleReliabilityEnd, float cycleReliabilityCurrent,
            float ignitionReliabilityStart, float ignitionReliabilityEnd, float ignitionReliabilityCurrent,
            bool hasCurrentData, FloatCurve cycleCurve,
            int clusterSize, float sliderTime, bool includeIgnition,
            bool sliderModeIsPercentage, float sliderPercentage, float maxGraphTime)
        {
            // Color codes
            string orangeColor = "#FF8033";
            string blueColor = "#7DD9FF";
            string greenColor = "#4DE64D";

            // Calculate base rates
            float baseRateStart = -Mathf.Log(cycleReliabilityStart) / ratedBurnTime;
            float baseRateEnd = -Mathf.Log(cycleReliabilityEnd) / ratedBurnTime;
            float baseRateCurrent = hasCurrentData ? -Mathf.Log(cycleReliabilityCurrent) / ratedBurnTime : 0f;

            // Determine what to display based on mode:
            // - Time mode: user picks time → show percentage
            // - Percentage mode: user picks percentage → show time
            float displayValueStart, displayValueEnd, displayValueCurrent;
            float surviveStart, surviveEnd, surviveCurrent;
            bool displayIsTime; // true = display time, false = display percentage
            
            if (sliderModeIsPercentage)
            {
                // Percentage mode: user picks percentage → show TIME
                displayIsTime = true;
                float targetProb = sliderPercentage / 100f;
                
                // The survival probability is what the user selected (already accounts for clustering/ignition)
                surviveStart = targetProb;
                surviveEnd = targetProb;
                surviveCurrent = targetProb;
                
                // Now reverse the clustering and ignition to find per-engine cycle probability
                float targetCycleProbStart = targetProb;
                float targetCycleProbEnd = targetProb;
                float targetCycleProbCurrent = targetProb;
                
                if (clusterSize > 1)
                {
                    targetCycleProbStart = Mathf.Pow(targetProb, 1f / clusterSize);
                    targetCycleProbEnd = Mathf.Pow(targetProb, 1f / clusterSize);
                    targetCycleProbCurrent = Mathf.Pow(targetProb, 1f / clusterSize);
                }
                
                // Apply ignition if needed before finding time
                if (includeIgnition)
                {
                    targetCycleProbStart /= ignitionReliabilityStart;
                    targetCycleProbEnd /= ignitionReliabilityEnd;
                    if (hasCurrentData) targetCycleProbCurrent /= ignitionReliabilityCurrent;
                }
                
                displayValueStart = ChartMath.FindTimeForSurvivalProb(targetCycleProbStart, ratedBurnTime, cycleReliabilityStart, cycleCurve, maxGraphTime);
                displayValueEnd = ChartMath.FindTimeForSurvivalProb(targetCycleProbEnd, ratedBurnTime, cycleReliabilityEnd, cycleCurve, maxGraphTime);
                
                if (hasCurrentData)
                {
                    displayValueCurrent = ChartMath.FindTimeForSurvivalProb(targetCycleProbCurrent, ratedBurnTime, cycleReliabilityCurrent, cycleCurve, maxGraphTime);
                }
                else
                {
                    displayValueCurrent = 0f;
                    surviveCurrent = 0f;
                }
            }
            else
            {
                // Time mode: user picks time → show PERCENTAGE
                displayIsTime = false;
                
                surviveStart = ChartMath.CalculateSurvivalProbAtTime(sliderTime, ratedBurnTime, cycleReliabilityStart, baseRateStart, cycleCurve);
                surviveEnd = ChartMath.CalculateSurvivalProbAtTime(sliderTime, ratedBurnTime, cycleReliabilityEnd, baseRateEnd, cycleCurve);
                surviveCurrent = hasCurrentData ? ChartMath.CalculateSurvivalProbAtTime(sliderTime, ratedBurnTime, cycleReliabilityCurrent, baseRateCurrent, cycleCurve) : 0f;
                
                // If including ignition, multiply by ignition reliability
                if (includeIgnition)
                {
                    surviveStart *= ignitionReliabilityStart;
                    surviveEnd *= ignitionReliabilityEnd;
                    if (hasCurrentData) surviveCurrent *= ignitionReliabilityCurrent;
                }

                // Apply cluster math
                if (clusterSize > 1)
                {
                    surviveStart = Mathf.Pow(surviveStart, clusterSize);
                    surviveEnd = Mathf.Pow(surviveEnd, clusterSize);
                    if (hasCurrentData) surviveCurrent = Mathf.Pow(surviveCurrent, clusterSize);
                }
                
                // Display values are the survival percentages (after clustering/ignition)
                displayValueStart = surviveStart * 100f;
                displayValueEnd = surviveEnd * 100f;
                displayValueCurrent = surviveCurrent * 100f;
            }

            // Layout: three sections side-by-side
            float sectionHeight = 125f;  // Keep constant height to prevent window jumping
            float totalWidth = rect.width - 16f;
            float numSections = hasCurrentData ? 3f : 2f;
            float sectionWidth = totalWidth / numSections;
            float startX = rect.x + 8f;

            float currentX = startX;

            // Calculate ignition probabilities with cluster math
            float igniteStart = clusterSize > 1 ? Mathf.Pow(ignitionReliabilityStart, clusterSize) : ignitionReliabilityStart;
            float igniteEnd = clusterSize > 1 ? Mathf.Pow(ignitionReliabilityEnd, clusterSize) : ignitionReliabilityEnd;
            float igniteCurrent = hasCurrentData ? (clusterSize > 1 ? Mathf.Pow(ignitionReliabilityCurrent, clusterSize) : ignitionReliabilityCurrent) : 0f;

            // Draw Starting DU section
            DrawSurvivalSection(currentX, yPos, sectionWidth, sectionHeight, "Starting DU", orangeColor, surviveStart, displayValueStart, displayIsTime, sliderTime, clusterSize, igniteStart, includeIgnition);
            currentX += sectionWidth;

            // Draw Current DU section (if applicable)
            if (hasCurrentData)
            {
                DrawSurvivalSection(currentX, yPos, sectionWidth, sectionHeight, "Current DU", blueColor, surviveCurrent, displayValueCurrent, displayIsTime, sliderTime, clusterSize, igniteCurrent, includeIgnition);
                currentX += sectionWidth;
            }

            // Draw Max DU section
            DrawSurvivalSection(currentX, yPos, sectionWidth, sectionHeight, "Max DU", greenColor, surviveEnd, displayValueEnd, displayIsTime, sliderTime, clusterSize, igniteEnd, includeIgnition);

            yPos += sectionHeight + 12;

            return yPos;
        }

        private void DrawSurvivalSection(float x, float y, float width, float height, string title, string color, float survivalProb, float displayValue, bool displayIsTime, float time, int clusterSize, float ignitionProb, bool includeIgnition)
        {
            // Header with colored text (no background)
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter,
                richText = true,
                normal = { textColor = Color.white }
            };

            string headerText = $"<color={color}>{title}</color>";
            GUI.Label(new Rect(x, y, width, 24), headerText, headerStyle);

            // Main display value (time or percentage depending on mode)
            string displayText;
            if (displayIsTime)
            {
                // Percentage mode: show time
                displayText = $"<size=24><b>{ChartMath.FormatTime(displayValue)}</b></size>";
            }
            else
            {
                // Time mode: show percentage
                displayText = $"<size=24><b>{displayValue:F2}%</b></size>";
            }
            
            GUIStyle displayStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(x, y + 26, width, 28), displayText, displayStyle);

            // Secondary info: "1 in X" text
            float survivalPercent = survivalProb * 100f;
            float failureRate = 1f - survivalProb;
            float oneInX = failureRate > 0.0001f ? (1f / failureRate) : 9999f;
            string entityText = clusterSize > 1 ? $"cluster of {clusterSize}" : "burn";
            
            // Show complementary info based on mode
            string secondaryText;
            if (displayIsTime)
            {
                // Percentage mode: show "1 in X burns (Y%)" format
                secondaryText = $"1 in <color=#FF6666>{oneInX:F1}</color> {entityText}s fail (<color=#90EE90>{survivalPercent:F1}%</color>)";
            }
            else
            {
                // Time mode: show "at time X, 1 in Y burns fail"
                secondaryText = $"at <color=#90EE90>{ChartMath.FormatTime(time)}</color>\n1 in <color=#FF6666>{oneInX:F1}</color> {entityText}s fail";
            }
            
            GUIStyle failStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.UpperCenter,
                richText = true,
                wordWrap = true,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
            };
            GUI.Label(new Rect(x + 4, y + 54, width - 8, 50), secondaryText, failStyle);

            // Small ignition probability text (only when not including ignition)
            if (!includeIgnition)
            {
                float ignitionPercent = ignitionProb * 100f;
                string ignitionText = $"Ignition: {ignitionPercent:F2}%";
                GUIStyle ignitionStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    alignment = TextAnchor.UpperCenter,
                    normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
                };
                GUI.Label(new Rect(x + 4, y + 92, width - 8, 18), ignitionText, ignitionStyle);
            }
        }

        #endregion

        #region Side-by-Side Section

        private float DrawSideBySideSection(Rect rect, float yPos, float ratedBurnTime,
            float cycleReliabilityStart, float cycleReliabilityEnd, float cycleReliabilityCurrent,
            float ignitionReliabilityStart, float ignitionReliabilityEnd, float ignitionReliabilityCurrent,
            bool hasCurrentData, float maxGraphTime, float maxDataValue,
            float realCurrentData, float realMaxData,
            ref bool useSimulatedData, ref float simulatedDataValue, ref int clusterSize,
            ref string clusterSizeInput, ref string dataValueInput, ref float sliderTime, ref string sliderTimeInput,
            ref bool includeIgnition, ref bool sliderModeIsPercentage, ref float sliderPercentage, ref string sliderPercentageInput,
            FloatCurve cycleCurve)
        {
            float columnStartY = yPos;
            float leftColumnWidth = rect.width * 0.5f;
            float rightColumnWidth = rect.width * 0.5f;
            float leftColumnX = rect.x;
            float rightColumnX = rect.x + leftColumnWidth;

            // Draw left column: Data Gains
            float leftColumnEndY = DrawDataGainsSection(leftColumnX, leftColumnWidth, columnStartY, ratedBurnTime);

            // Draw right column: Simulation Controls
            float rightColumnEndY = DrawSimulationControls(rightColumnX, rightColumnWidth, columnStartY,
                ratedBurnTime, cycleReliabilityStart, cycleReliabilityEnd, cycleReliabilityCurrent,
                ignitionReliabilityStart, ignitionReliabilityEnd, ignitionReliabilityCurrent,
                hasCurrentData, maxGraphTime, maxDataValue, realCurrentData, realMaxData,
                ref useSimulatedData, ref simulatedDataValue, ref clusterSize, ref clusterSizeInput, ref dataValueInput,
                ref sliderTime, ref sliderTimeInput, ref includeIgnition, ref sliderModeIsPercentage, ref sliderPercentage, ref sliderPercentageInput,
                cycleCurve);

            // Draw vertical separator
            if (Event.current.type == EventType.Repaint)
            {
                float separatorX = rect.x + leftColumnWidth;
                float separatorHeight = Mathf.Max(leftColumnEndY, rightColumnEndY) - columnStartY;
                GUI.DrawTexture(new Rect(separatorX, columnStartY, 1, separatorHeight), _textures.ChartSeparator);
            }

            return Mathf.Max(leftColumnEndY, rightColumnEndY) + 8;
        }

        private float DrawDataGainsSection(float x, float width, float yPos, float ratedBurnTime)
        {
            string purpleColor = "#CCB3FF";

            float dataRate = TestFlightConstants.RunningDataNumerator / ratedBurnTime;

            // Section header
            GUIStyle sectionStyle = EngineConfigStyles.InfoSection;
            sectionStyle.normal.textColor = new Color(0.8f, 0.7f, 1.0f);
            GUI.Label(new Rect(x, yPos, width, 20), "How To Gain Data:", sectionStyle);
            yPos += 24;

            GUIStyle bulletStyle = EngineConfigStyles.Bullet;
            GUIStyle indentedBulletStyle = EngineConfigStyles.IndentedBullet;
            GUIStyle footerStyle = EngineConfigStyles.Footer;
            float bulletHeight = 18;

            // Failures section
            GUI.Label(new Rect(x, yPos, width, bulletHeight), $"An engine can fail in {TestFlightConstants.FailureTypeNames.Length} ways:", bulletStyle);
            yPos += bulletHeight;

            for (int i = 0; i < TestFlightConstants.FailureTypeNames.Length; i++)
            {
                string failText = $" ({TestFlightConstants.FailureTypePercent[i]:F0}%) {TestFlightConstants.FailureTypeNames[i]} <color={purpleColor}>+{TestFlightConstants.FailureTypeDU[i]}</color> du";
                GUI.Label(new Rect(x, yPos, width, bulletHeight), failText, indentedBulletStyle);
                yPos += bulletHeight;
            }

            yPos += 4;

            // Running gains
            string runningText = $"Running gains <color={purpleColor}>{dataRate:F1}</color> du/s";
            GUI.Label(new Rect(x, yPos, width, bulletHeight), runningText, bulletStyle);
            yPos += bulletHeight;

            // Ignition failure
            string ignitionText = $"Ignition Fail <color={purpleColor}>+{TestFlightConstants.IgnitionFailDU}</color> du";
            GUI.Label(new Rect(x, yPos, width, bulletHeight), ignitionText, bulletStyle);
            yPos += bulletHeight + 8;

            // Footer
            string footerText = $"(no more than {TestFlightConstants.MaxDUPerFlight} du per flight)";
            GUI.Label(new Rect(x, yPos, width, bulletHeight), footerText, footerStyle);
            yPos += bulletHeight;

            return yPos;
        }

        private float DrawSimulationControls(float x, float width, float yPos,
            float ratedBurnTime, float cycleReliabilityStart, float cycleReliabilityEnd, float cycleReliabilityCurrent,
            float ignitionReliabilityStart, float ignitionReliabilityEnd, float ignitionReliabilityCurrent,
            bool hasCurrentData, float maxGraphTime, float maxDataValue,
            float realCurrentData, float realMaxData,
            ref bool useSimulatedData, ref float simulatedDataValue, ref int clusterSize,
            ref string clusterSizeInput, ref string dataValueInput, ref float sliderTime, ref string sliderTimeInput,
            ref bool includeIgnition, ref bool sliderModeIsPercentage, ref float sliderPercentage, ref string sliderPercentageInput,
            FloatCurve cycleCurve)
        {
            bool hasRealData = realCurrentData >= 0f && realMaxData > 0f;

            GUIStyle sectionStyle = EngineConfigStyles.InfoSection;
            sectionStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(x, yPos, width, 20), "Simulate:", sectionStyle);
            yPos += 24;

            GUIStyle buttonStyle = EngineConfigStyles.CompactButton;
            var inputStyle = new GUIStyle(GUI.skin.textField) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUIStyle controlStyle = EngineConfigStyles.Control;

            // Common width for all controls
            float btnWidth = width - 16;

            // Toggle button for slider mode
            string toggleButtonText = sliderModeIsPercentage ? "Mode: % → Time" : "Mode: Time → %";
            if (GUI.Button(new Rect(x + 8, yPos, btnWidth, 20), toggleButtonText, buttonStyle))
            {
                sliderModeIsPercentage = !sliderModeIsPercentage;
            }
            yPos += 24;

            // Slider control (changes based on mode)
            if (sliderModeIsPercentage)
            {
                // Percentage mode: pick percentage, see time
                GUI.Label(new Rect(x + 8, yPos, btnWidth, 16), "Survival %", controlStyle);
                yPos += 16;

                sliderPercentage = GUI.HorizontalSlider(new Rect(x + 8, yPos, btnWidth - 50, 16),
                    sliderPercentage, 0.1f, 99.9f, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);

                sliderPercentageInput = $"{sliderPercentage:F1}";
                GUI.SetNextControlName("sliderPercentageInput");
                string newPercentInput = GUI.TextField(new Rect(x + btnWidth - 35, yPos - 2, 40, 20),
                    sliderPercentageInput, 6, inputStyle);

                if (newPercentInput != sliderPercentageInput)
                {
                    sliderPercentageInput = newPercentInput;
                    if (GUI.GetNameOfFocusedControl() == "sliderPercentageInput" && float.TryParse(sliderPercentageInput, out float inputPercent))
                    {
                        inputPercent = Mathf.Clamp(inputPercent, 0.1f, 99.9f);
                        sliderPercentage = inputPercent;
                    }
                }
                yPos += 24;
            }
            else
            {
                // Time mode: pick time, see percentage
                GUI.Label(new Rect(x + 8, yPos, btnWidth, 16), "Burn Time (s)", controlStyle);
                yPos += 16;

                float maxSliderTime = maxGraphTime;
                sliderTime = GUI.HorizontalSlider(new Rect(x + 8, yPos, btnWidth - 50, 16),
                    sliderTime, 0f, maxSliderTime, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);

                sliderTimeInput = $"{sliderTime:F1}";
                GUI.SetNextControlName("sliderTimeInput");
                string newTimeInput = GUI.TextField(new Rect(x + btnWidth - 35, yPos - 2, 40, 20),
                    sliderTimeInput, 8, inputStyle);

                if (newTimeInput != sliderTimeInput)
                {
                    sliderTimeInput = newTimeInput;
                    if (GUI.GetNameOfFocusedControl() == "sliderTimeInput" && float.TryParse(sliderTimeInput, out float inputTime))
                    {
                        inputTime = Mathf.Clamp(inputTime, 0f, maxSliderTime);
                        sliderTime = inputTime;
                    }
                }
                yPos += 24;
            }

            // Include Ignition checkbox
            GUIStyle checkboxStyle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 12,
                normal = { textColor = Color.white }
            };
            includeIgnition = GUI.Toggle(new Rect(x + 8, yPos, btnWidth, 20), includeIgnition, " Include Ignition", checkboxStyle);
            yPos += 24;

            // Reset button
            string resetButtonText = hasRealData ? $"Set to Current du ({realCurrentData:F0})" : "Set to Current du (0)";
            if (GUI.Button(new Rect(x + 8, yPos, btnWidth, 20), resetButtonText, buttonStyle))
            {
                if (hasRealData)
                {
                    simulatedDataValue = realCurrentData;
                    dataValueInput = $"{realCurrentData:F0}";
                    useSimulatedData = false;
                }
                else
                {
                    simulatedDataValue = 0f;
                    dataValueInput = "0";
                    useSimulatedData = true;
                }
                clusterSize = 1;
                clusterSizeInput = "1";
            }
            yPos += 24;

            // Data slider
            GUI.Label(new Rect(x + 8, yPos, btnWidth, 16), "Data (du)", controlStyle);
            yPos += 16;

            if (!useSimulatedData)
            {
                simulatedDataValue = hasRealData ? realCurrentData : 0f;
                dataValueInput = $"{simulatedDataValue:F0}";
            }

            simulatedDataValue = GUI.HorizontalSlider(new Rect(x + 8, yPos, btnWidth - 50, 16),
                simulatedDataValue, 0f, maxDataValue, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);

            if (hasRealData && Mathf.Abs(simulatedDataValue - realCurrentData) > 0.1f)
                useSimulatedData = true;
            else if (!hasRealData && simulatedDataValue > 0.1f)
                useSimulatedData = true;

            dataValueInput = $"{simulatedDataValue:F0}";
            GUI.SetNextControlName("dataValueInput");
            string newDataInput = GUI.TextField(new Rect(x + btnWidth - 35, yPos - 2, 40, 20),
                dataValueInput, 6, inputStyle);

            if (newDataInput != dataValueInput)
            {
                dataValueInput = newDataInput;
                if (GUI.GetNameOfFocusedControl() == "dataValueInput" && float.TryParse(dataValueInput, out float inputDataValue))
                {
                    inputDataValue = Mathf.Clamp(inputDataValue, 0f, maxDataValue);
                    simulatedDataValue = inputDataValue;
                    useSimulatedData = true;
                }
            }
            yPos += 24;

            // Cluster slider
            GUI.Label(new Rect(x + 8, yPos, btnWidth, 16), "Cluster", controlStyle);
            yPos += 16;

            clusterSize = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 8, yPos, btnWidth - 50, 16),
                clusterSize, 1f, 100f, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb));

            clusterSizeInput = clusterSize.ToString();
            GUI.SetNextControlName("clusterSizeInput");
            string newClusterInput = GUI.TextField(new Rect(x + btnWidth - 35, yPos - 2, 40, 20),
                clusterSizeInput, 3, inputStyle);

            if (newClusterInput != clusterSizeInput)
            {
                clusterSizeInput = newClusterInput;
                if (GUI.GetNameOfFocusedControl() == "clusterSizeInput" && int.TryParse(clusterSizeInput, out int inputCluster))
                {
                    inputCluster = Mathf.Clamp(inputCluster, 1, 100);
                    clusterSize = inputCluster;
                    clusterSizeInput = clusterSize.ToString();
                }
            }
            yPos += 24;

            return yPos;
        }

        #endregion
    }
}
