using System;
using UnityEngine;

namespace RealFuels
{
    /// <summary>
    /// Handles all chart rendering for engine configuration failure probability visualization.
    /// Displays survival probability curves based on TestFlight data.
    /// </summary>
    public class EngineConfigChart
    {
        private readonly ModuleEngineConfigsBase _module;
        private readonly EngineConfigTextures _textures;

        // Chart state
        private bool _useLogScaleX = false;
        private bool _useLogScaleY = false;
        private bool _useHeatmapMode = false; // Toggle between line chart and heatmap

        // Heatmap cache to avoid regenerating every frame
        private Texture2D _cachedHeatmap = null;
        private float _lastHeatmapMaxTime = 0f;
        private float _lastHeatmapMaxData = 0f;
        private int _lastHeatmapClusterSize = 0;
        private float _lastHeatmapCycleStart = 0f;
        private float _lastHeatmapCycleEnd = 0f;
        private bool _lastHeatmapModeIsPercentage = false;
        private bool _lastHeatmapIncludeIgnition = false;

        // Simulation state
        private bool _useSimulatedData = false;
        private float _simulatedDataValue = 0f;
        private int _clusterSize = 1;
        private string _clusterSizeInput = "1";
        private string _dataValueInput = "0";
        private string _sliderTimeInput = "100.0";
        private bool _includeIgnition = false;
        
        // Slider mode toggle
        private bool _sliderModeIsPercentage = false; // false = time mode, true = percentage mode
        private float _sliderPercentage = 95.0f;
        private string _sliderPercentageInput = "95.0";

        // Public properties for external access
        public bool UseLogScaleX { get => _useLogScaleX; set => _useLogScaleX = value; }
        public bool UseLogScaleY { get => _useLogScaleY; set => _useLogScaleY = value; }
        public bool UseHeatmapMode { get => _useHeatmapMode; set => _useHeatmapMode = value; }
        public bool UseSimulatedData { get => _useSimulatedData; set => _useSimulatedData = value; }
        public float SimulatedDataValue { get => _simulatedDataValue; set => _simulatedDataValue = value; }
        public int ClusterSize { get => _clusterSize; set => _clusterSize = value; }
        public string ClusterSizeInput { get => _clusterSizeInput; set => _clusterSizeInput = value; }
        public string DataValueInput { get => _dataValueInput; set => _dataValueInput = value; }
        public string SliderTimeInput { get => _sliderTimeInput; set => _sliderTimeInput = value; }
        public bool IncludeIgnition { get => _includeIgnition; set => _includeIgnition = value; }
        public bool SliderModeIsPercentage { get => _sliderModeIsPercentage; set => _sliderModeIsPercentage = value; }
        public float SliderPercentage { get => _sliderPercentage; set => _sliderPercentage = value; }
        public string SliderPercentageInput { get => _sliderPercentageInput; set => _sliderPercentageInput = value; }

        public EngineConfigChart(ModuleEngineConfigsBase module)
        {
            _module = module;
            _textures = EngineConfigTextures.Instance;
        }

        /// <summary>
        /// Draws the failure probability chart and info panel side by side.
        /// </summary>
        public void Draw(ConfigNode configNode, float width, float height, ref float sliderTime)
        {
            _textures.EnsureInitialized();
            EngineConfigStyles.Initialize();

            // Values are copied to CONFIG level by ModuleManager patch
            if (!configNode.HasValue("cycleReliabilityStart")) return;
            if (!configNode.HasValue("cycleReliabilityEnd")) return;
            if (!float.TryParse(configNode.GetValue("cycleReliabilityStart"), out float cycleReliabilityStart)) return;
            if (!float.TryParse(configNode.GetValue("cycleReliabilityEnd"), out float cycleReliabilityEnd)) return;

            // Validate reliability is in valid range
            if (cycleReliabilityStart <= 0f || cycleReliabilityStart > 1f) return;
            if (cycleReliabilityEnd <= 0f || cycleReliabilityEnd > 1f) return;

            float ratedBurnTime = 0;
            if (!configNode.TryGetValue("ratedBurnTime", ref ratedBurnTime) || ratedBurnTime <= 0) return;

            float ratedContinuousBurnTime = ratedBurnTime;
            configNode.TryGetValue("ratedContinuousBurnTime", ref ratedContinuousBurnTime);

            // Skip chart if this is a cumulative-limited engine (continuous << total)
            if (ratedContinuousBurnTime < ratedBurnTime * 0.9f)
            {
                // Display error message for dual burn time configs
                GUIStyle redCenteredStyle = new GUIStyle(GUI.skin.label)
                {
                    normal = { textColor = Color.red },
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true
                };

                GUILayout.BeginVertical(GUILayout.Width(width), GUILayout.Height(height));
                GUILayout.FlexibleSpace();
                GUILayout.Label("Dual burn time configurations (continuous/cumulative)\nare not supported for reliability charts", redCenteredStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
                return;
            }

            // Read testedBurnTime to match TestFlight's exact behavior
            float testedBurnTime = 0f;
            bool hasTestedBurnTime = configNode.TryGetValue("testedBurnTime", ref testedBurnTime) && testedBurnTime > ratedBurnTime;

            // Split the area: chart on left (58%), info on right (42%)
            float chartWidth = width * 0.58f;
            float infoWidth = width * 0.42f;

            float overburnPenalty = 2.0f;
            configNode.TryGetValue("overburnPenalty", ref overburnPenalty);

            // Build the actual TestFlight cycle curve
            FloatCurve cycleCurve = ChartMath.BuildTestFlightCycleCurve(ratedBurnTime, testedBurnTime, overburnPenalty, hasTestedBurnTime);

            // Main container
            Rect containerRect = GUILayoutUtility.GetRect(width, height);

            // Chart area (left side)
            const float padding = 38f;
            float plotWidth = chartWidth - padding * 2;
            float plotHeight = height - padding * 2;

            float maxTime = hasTestedBurnTime ? testedBurnTime * 3.5f : ratedBurnTime * 3.5f;

            Rect chartRect = new Rect(containerRect.x, containerRect.y, chartWidth, height);
            Rect plotArea = new Rect(chartRect.x + padding, chartRect.y + padding, plotWidth, plotHeight);

            // Info panel area (right side)
            Rect infoRect = new Rect(containerRect.x + chartWidth, containerRect.y, infoWidth, height);

            // Get ignition reliability values
            float ignitionReliabilityStart = 1f;
            float ignitionReliabilityEnd = 1f;
            configNode.TryGetValue("ignitionReliabilityStart", ref ignitionReliabilityStart);
            configNode.TryGetValue("ignitionReliabilityEnd", ref ignitionReliabilityEnd);

            // Calculate survival curves
            var curveData = ChartMath.CalculateSurvivalCurves(
                cycleReliabilityStart, cycleReliabilityEnd,
                ratedBurnTime, cycleCurve, maxTime, _clusterSize);

            // Get current data
            float realCurrentData = TestFlightWrapper.GetCurrentFlightData(_module.part);
            float realMaxData = TestFlightWrapper.GetMaximumData(_module.part);
            float currentDataValue = _useSimulatedData ? _simulatedDataValue : realCurrentData;
            float maxDataValue = realMaxData > 0f ? realMaxData : 10000f;
            float dataPercentage = (maxDataValue > 0f) ? Mathf.Clamp01(currentDataValue / maxDataValue) : 0f;
            bool hasCurrentData = (_useSimulatedData && currentDataValue >= 0f) || (realCurrentData >= 0f && realMaxData > 0f);

            float cycleReliabilityCurrent = 0f;
            ChartMath.SurvivalCurveData currentCurveData = default;
            float ignitionReliabilityCurrent = 0f;

            if (hasCurrentData)
            {
                cycleReliabilityCurrent = ChartMath.EvaluateReliabilityAtData(currentDataValue, cycleReliabilityStart, cycleReliabilityEnd);
                ignitionReliabilityCurrent = ChartMath.EvaluateReliabilityAtData(currentDataValue, ignitionReliabilityStart, ignitionReliabilityEnd);
                currentCurveData = ChartMath.CalculateSurvivalCurve(
                    cycleReliabilityCurrent, ratedBurnTime, cycleCurve, maxTime, _clusterSize);
            }

            // If including ignition, apply ignition reliability to all curves (vertical scaling)
            if (_includeIgnition)
            {
                // Apply cluster math to ignition probabilities
                float clusteredIgnitionStart = _clusterSize > 1 ? Mathf.Pow(ignitionReliabilityStart, _clusterSize) : ignitionReliabilityStart;
                float clusteredIgnitionEnd = _clusterSize > 1 ? Mathf.Pow(ignitionReliabilityEnd, _clusterSize) : ignitionReliabilityEnd;
                float clusteredIgnitionCurrent = hasCurrentData && _clusterSize > 1 ? Mathf.Pow(ignitionReliabilityCurrent, _clusterSize) : ignitionReliabilityCurrent;

                // Apply clustered ignition to start curve
                for (int i = 0; i < curveData.SurvivalProbs.Length; i++)
                {
                    curveData.SurvivalProbs[i] *= clusteredIgnitionStart;
                }

                // Apply clustered ignition to end curve
                for (int i = 0; i < curveData.SurvivalProbsEnd.Length; i++)
                {
                    curveData.SurvivalProbsEnd[i] *= clusteredIgnitionEnd;
                }

                // Apply clustered ignition to current curve if available
                if (hasCurrentData)
                {
                    for (int i = 0; i < currentCurveData.SurvivalProbs.Length; i++)
                    {
                        currentCurveData.SurvivalProbs[i] *= clusteredIgnitionCurrent;
                    }
                }

                // Update min survival prob
                curveData.MinSurvivalProb = Mathf.Min(
                    curveData.SurvivalProbs[curveData.SurvivalProbs.Length - 1],
                    curveData.SurvivalProbsEnd[curveData.SurvivalProbsEnd.Length - 1]
                );
            }

            // Draw chart - either heatmap or line chart based on mode
            if (_useHeatmapMode)
            {
                DrawHeatmapChart(chartRect, plotArea, cycleReliabilityStart, cycleReliabilityEnd,
                    ratedBurnTime, cycleCurve, maxTime, maxDataValue, sliderTime, currentDataValue,
                    ignitionReliabilityStart, ignitionReliabilityEnd);
            }
            else
            {
                DrawChartBackground(chartRect);
                DrawGrid(plotArea, curveData.MinSurvivalProb, maxTime);
                DrawCurves(plotArea, curveData, currentCurveData, hasCurrentData, maxTime, curveData.MinSurvivalProb);
                
                // Draw slider line (vertical for time mode, horizontal for percentage mode)
                if (_sliderModeIsPercentage)
                {
                    DrawSliderPercentageLine(plotArea, _sliderPercentage / 100f, curveData.MinSurvivalProb);
                }
                else
                {
                    DrawSliderTimeLine(plotArea, sliderTime, maxTime);
                }
                
                DrawAxisLabels(chartRect, plotArea, maxTime, curveData.MinSurvivalProb);
                DrawLegend(plotArea, hasCurrentData);
            }

            // Draw info panel
            DrawInfoPanel(infoRect, configNode, ratedBurnTime, testedBurnTime, hasTestedBurnTime,
                cycleReliabilityStart, cycleReliabilityEnd, hasCurrentData, cycleReliabilityCurrent,
                ignitionReliabilityStart, ignitionReliabilityEnd, ignitionReliabilityCurrent,
                dataPercentage, currentDataValue, maxDataValue, realCurrentData, realMaxData,
                cycleCurve, ref sliderTime, maxTime);

            // Sync back slider time input for consistency
            _sliderTimeInput = $"{sliderTime:F1}";
        }

        #region Chart Background & Zones

        private void DrawChartBackground(Rect chartRect)
        {
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(chartRect, _textures.ChartBg);
            }

            // Chart title
            GUI.Label(new Rect(chartRect.x, chartRect.y + 4, chartRect.width, 24),
                "Survival Probability vs Burn Time", EngineConfigStyles.ChartTitle);
        }


        #endregion

        #region Grid & Axes

        private void DrawGrid(Rect plotArea, float yAxisMin, float maxTime)
        {
            GUIStyle labelStyle = EngineConfigStyles.GridLabel;

            if (_useLogScaleY)
            {
                float[] logValues = { 0.0001f, 0.001f, 0.01f, 0.1f, 1f };
                foreach (float survivalProb in logValues)
                {
                    if (survivalProb < yAxisMin) continue;
                    float y = ChartMath.SurvivalProbToYPosition(survivalProb, yAxisMin, plotArea.y, plotArea.height, _useLogScaleY);
                    DrawGridLine(plotArea.x, y, plotArea.width);
                    DrawYAxisLabel(plotArea.x, y, survivalProb);
                }
            }
            else
            {
                for (int i = 0; i <= 10; i++)
                {
                    bool isMajor = (i % 2 == 0);
                    float survivalProb = yAxisMin + (i / 10f) * (1f - yAxisMin);
                    float y = ChartMath.SurvivalProbToYPosition(survivalProb, yAxisMin, plotArea.y, plotArea.height, _useLogScaleY);

                    DrawGridLine(plotArea.x, y, plotArea.width, isMajor);
                    if (isMajor) DrawYAxisLabel(plotArea.x, y, survivalProb);
                }
            }
        }

        private void DrawGridLine(float x, float y, float width, bool major = true)
        {
            if (Event.current.type != EventType.Repaint) return;
            Rect lineRect = new Rect(x, y, width, 1);
            GUI.DrawTexture(lineRect, major ? _textures.ChartGridMajor : _textures.ChartGridMinor);
        }

        private void DrawYAxisLabel(float x, float y, float survivalProb)
        {
            float labelValue = survivalProb * 100f;
            string label = labelValue < 1f ? $"{labelValue:F2}%" :
                          (labelValue < 10f ? $"{labelValue:F1}%" : $"{labelValue:F0}%");
            GUI.Label(new Rect(x - 35, y - 10, 30, 20), label, EngineConfigStyles.GridLabel);
        }

        private void DrawAxisLabels(Rect chartRect, Rect plotArea, float maxTime, float yAxisMin)
        {
            // X-axis labels
            GUIStyle timeStyle = EngineConfigStyles.TimeLabel;

            if (_useLogScaleX)
            {
                float[] logTimes = { 0.1f, 1f, 10f, 60f, 300f, 600f, 1800f, 3600f };
                foreach (float time in logTimes)
                {
                    if (time > maxTime) break;
                    float x = ChartMath.TimeToXPosition(time, maxTime, plotArea.x, plotArea.width, _useLogScaleX);
                    GUI.Label(new Rect(x - 25, plotArea.y + plotArea.height + 2, 50, 20),
                        ChartMath.FormatTime(time), timeStyle);
                }
            }
            else
            {
                for (int i = 0; i <= 4; i++)
                {
                    float time = (i / 4f) * maxTime;
                    float x = ChartMath.TimeToXPosition(time, maxTime, plotArea.x, plotArea.width, _useLogScaleX);
                    GUI.Label(new Rect(x - 25, plotArea.y + plotArea.height + 2, 50, 20),
                        ChartMath.FormatTime(time), timeStyle);
                }
            }
        }

        #endregion

        #region Curve Drawing

        private void DrawCurves(Rect plotArea, ChartMath.SurvivalCurveData startCurve,
            ChartMath.SurvivalCurveData currentCurve, bool hasCurrentData,
            float maxTime, float yAxisMin)
        {
            if (Event.current.type != EventType.Repaint) return;

            // Convert to screen positions
            Vector2[] pointsStart = ConvertToScreenPoints(startCurve.SurvivalProbs, plotArea, maxTime, yAxisMin);
            Vector2[] pointsEnd = ConvertToScreenPoints(startCurve.SurvivalProbsEnd, plotArea, maxTime, yAxisMin);
            Vector2[] pointsCurrent = hasCurrentData ? ConvertToScreenPoints(currentCurve.SurvivalProbs, plotArea, maxTime, yAxisMin) : null;

            // Draw curves
            DrawCurveLine(pointsStart, _textures.ChartOrangeLine, plotArea);
            DrawCurveLine(pointsEnd, _textures.ChartGreenLine, plotArea);
            if (hasCurrentData && pointsCurrent != null)
                DrawCurveLine(pointsCurrent, _textures.ChartBlueLine, plotArea);
        }

        private Vector2[] ConvertToScreenPoints(float[] survivalProbs, Rect plotArea, float maxTime, float yAxisMin)
        {
            int count = survivalProbs.Length;
            Vector2[] points = new Vector2[count];

            for (int i = 0; i < count; i++)
            {
                float t = (i / (float)(count - 1)) * maxTime;
                float x = ChartMath.TimeToXPosition(t, maxTime, plotArea.x, plotArea.width, _useLogScaleX);
                float y = ChartMath.SurvivalProbToYPosition(survivalProbs[i], yAxisMin, plotArea.y, plotArea.height, _useLogScaleY);

                if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y))
                {
                    x = plotArea.x;
                    y = plotArea.y + plotArea.height;
                }
                points[i] = new Vector2(x, y);
            }

            return points;
        }

        private void DrawCurveLine(Vector2[] points, Texture2D texture, Rect plotArea)
        {
            float plotAreaRight = plotArea.x + plotArea.width;

            for (int i = 0; i < points.Length - 1; i++)
            {
                // Skip segments outside plot area
                if (points[i].x > plotAreaRight && points[i + 1].x > plotAreaRight) continue;
                if (points[i].x < plotArea.x && points[i + 1].x < plotArea.x) continue;

                ChartMath.DrawLine(points[i], points[i + 1], texture, 2.5f);
            }
        }

        #endregion

        #region Slider Lines

        private void DrawSliderTimeLine(Rect plotArea, float sliderTime, float maxTime)
        {
            if (Event.current.type != EventType.Repaint) return;

            float x = ChartMath.TimeToXPosition(sliderTime, maxTime, plotArea.x, plotArea.width, _useLogScaleX);

            // Clamp to plot area
            if (x < plotArea.x || x > plotArea.x + plotArea.width) return;

            // Draw white vertical line
            Color whiteTransparent = new Color(1f, 1f, 1f, 0.8f);
            Texture2D whiteLine = MakeTex(2, 2, whiteTransparent);
            GUI.DrawTexture(new Rect(x - 1f, plotArea.y, 2f, plotArea.height), whiteLine);
        }

        private void DrawSliderPercentageLine(Rect plotArea, float survivalProb, float yAxisMin)
        {
            if (Event.current.type != EventType.Repaint) return;

            float y = ChartMath.SurvivalProbToYPosition(survivalProb, yAxisMin, plotArea.y, plotArea.height, _useLogScaleY);

            // Clamp to plot area
            if (y < plotArea.y || y > plotArea.y + plotArea.height) return;

            // Draw white horizontal line
            Color whiteTransparent = new Color(1f, 1f, 1f, 0.8f);
            Texture2D whiteLine = MakeTex(2, 2, whiteTransparent);
            GUI.DrawTexture(new Rect(plotArea.x, y - 1f, plotArea.width, 2f), whiteLine);
        }

        private void DrawIntersectionDotsTimeMode(Rect plotArea, float sliderTime, ChartMath.SurvivalCurveData startCurve,
            ChartMath.SurvivalCurveData currentCurve, bool hasCurrentData, float maxTime, float yAxisMin)
        {
            if (Event.current.type != EventType.Repaint) return;

            float x = ChartMath.TimeToXPosition(sliderTime, maxTime, plotArea.x, plotArea.width, _useLogScaleX);
            if (x < plotArea.x || x > plotArea.x + plotArea.width) return;

            // Find the survival probabilities at the slider time by interpolating the curve data
            int curvePoints = startCurve.SurvivalProbs.Length;
            float normalizedTime = sliderTime / maxTime;
            int index = Mathf.Clamp(Mathf.RoundToInt(normalizedTime * (curvePoints - 1)), 0, curvePoints - 1);

            // Draw orange dot for start curve
            float yStart = ChartMath.SurvivalProbToYPosition(startCurve.SurvivalProbs[index], yAxisMin, plotArea.y, plotArea.height, _useLogScaleY);
            DrawDot(new Vector2(x, yStart), new Color(1f, 0.5f, 0.2f, 1f), 6f);

            // Draw green dot for end curve
            float yEnd = ChartMath.SurvivalProbToYPosition(startCurve.SurvivalProbsEnd[index], yAxisMin, plotArea.y, plotArea.height, _useLogScaleY);
            DrawDot(new Vector2(x, yEnd), new Color(0.3f, 0.9f, 0.3f, 1f), 6f);

            // Draw blue dot for current curve if available
            if (hasCurrentData)
            {
                float yCurrent = ChartMath.SurvivalProbToYPosition(currentCurve.SurvivalProbs[index], yAxisMin, plotArea.y, plotArea.height, _useLogScaleY);
                DrawDot(new Vector2(x, yCurrent), new Color(0.5f, 0.85f, 1.0f, 1f), 6f);
            }
        }

        private void DrawIntersectionDotsPercentageMode(Rect plotArea, float targetSurvivalProb,
            ChartMath.SurvivalCurveData startCurve, ChartMath.SurvivalCurveData currentCurve, bool hasCurrentData,
            float cycleReliabilityStart, float cycleReliabilityEnd, float cycleReliabilityCurrent,
            float ignitionReliabilityStart, float ignitionReliabilityEnd, float ignitionReliabilityCurrent,
            float ratedBurnTime, FloatCurve cycleCurve, float maxTime, float yAxisMin)
        {
            if (Event.current.type != EventType.Repaint) return;

            float y = ChartMath.SurvivalProbToYPosition(targetSurvivalProb, yAxisMin, plotArea.y, plotArea.height, _useLogScaleY);
            if (y < plotArea.y || y > plotArea.y + plotArea.height) return;

            // Calculate the times for each curve to reach the target survival probability
            // Need to account for ignition if it's included
            float targetCycleProbStart = _includeIgnition ? targetSurvivalProb / ignitionReliabilityStart : targetSurvivalProb;
            float targetCycleProbEnd = _includeIgnition ? targetSurvivalProb / ignitionReliabilityEnd : targetSurvivalProb;
            float targetCycleProbCurrent = _includeIgnition && hasCurrentData ? targetSurvivalProb / ignitionReliabilityCurrent : targetSurvivalProb;

            // Apply cluster math (need to reverse it)
            if (_clusterSize > 1)
            {
                targetCycleProbStart = Mathf.Pow(targetCycleProbStart, 1f / _clusterSize);
                targetCycleProbEnd = Mathf.Pow(targetCycleProbEnd, 1f / _clusterSize);
                if (hasCurrentData) targetCycleProbCurrent = Mathf.Pow(targetCycleProbCurrent, 1f / _clusterSize);
            }

            // Find times for each curve
            float timeStart = ChartMath.FindTimeForSurvivalProb(targetCycleProbStart, ratedBurnTime, cycleReliabilityStart, cycleCurve, maxTime);
            float timeEnd = ChartMath.FindTimeForSurvivalProb(targetCycleProbEnd, ratedBurnTime, cycleReliabilityEnd, cycleCurve, maxTime);

            // Draw orange dot for start curve
            float xStart = ChartMath.TimeToXPosition(timeStart, maxTime, plotArea.x, plotArea.width, _useLogScaleX);
            if (xStart >= plotArea.x && xStart <= plotArea.x + plotArea.width)
                DrawDot(new Vector2(xStart, y), new Color(1f, 0.5f, 0.2f, 1f), 6f);

            // Draw green dot for end curve
            float xEnd = ChartMath.TimeToXPosition(timeEnd, maxTime, plotArea.x, plotArea.width, _useLogScaleX);
            if (xEnd >= plotArea.x && xEnd <= plotArea.x + plotArea.width)
                DrawDot(new Vector2(xEnd, y), new Color(0.3f, 0.9f, 0.3f, 1f), 6f);

            // Draw blue dot for current curve if available
            if (hasCurrentData)
            {
                float timeCurrent = ChartMath.FindTimeForSurvivalProb(targetCycleProbCurrent, ratedBurnTime, cycleReliabilityCurrent, cycleCurve, maxTime);
                float xCurrent = ChartMath.TimeToXPosition(timeCurrent, maxTime, plotArea.x, plotArea.width, _useLogScaleX);
                if (xCurrent >= plotArea.x && xCurrent <= plotArea.x + plotArea.width)
                    DrawDot(new Vector2(xCurrent, y), new Color(0.5f, 0.85f, 1.0f, 1f), 6f);
            }
        }

        private void DrawDot(Vector2 position, Color color, float diameter)
        {
            if (Event.current.type != EventType.Repaint) return;
            
            float radius = diameter / 2f;
            
            // Draw filled circle using multiple overlapping squares for better visibility
            for (float r = radius; r > 0; r -= 0.3f)
            {
                float size = r * 2f;
                Color layerColor = new Color(color.r, color.g, color.b, color.a * (r / radius));
                Texture2D dotTexture = MakeTex(2, 2, layerColor);
                GUI.DrawTexture(new Rect(position.x - r, position.y - r, size, size), dotTexture);
                UnityEngine.Object.Destroy(dotTexture);
            }
            
            // Draw bright center
            Texture2D centerTexture = MakeTex(2, 2, color);
            GUI.DrawTexture(new Rect(position.x - 1, position.y - 1, 2, 2), centerTexture);
            UnityEngine.Object.Destroy(centerTexture);
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        #endregion

        #region Legend

        private void DrawLegend(Rect plotArea, bool hasCurrentData)
        {
            GUIStyle legendStyle = EngineConfigStyles.Legend;
            float legendWidth = 110f;
            float legendX = plotArea.x + plotArea.width - legendWidth;
            float legendY = plotArea.y + 5;

            // Orange line for 0 data
            GUI.DrawTexture(new Rect(legendX, legendY + 7, 15, 3), _textures.ChartOrangeLine);
            GUI.Label(new Rect(legendX + 18, legendY, 80, 18), "0 Data", legendStyle);

            if (hasCurrentData)
            {
                GUI.DrawTexture(new Rect(legendX, legendY + 25, 15, 3), _textures.ChartBlueLine);
                GUI.Label(new Rect(legendX + 18, legendY + 18, 100, 18), "Current Data", legendStyle);

                GUI.DrawTexture(new Rect(legendX, legendY + 43, 15, 3), _textures.ChartGreenLine);
                GUI.Label(new Rect(legendX + 18, legendY + 36, 80, 18), "Max Data", legendStyle);
            }
            else
            {
                GUI.DrawTexture(new Rect(legendX, legendY + 25, 15, 3), _textures.ChartGreenLine);
                GUI.Label(new Rect(legendX + 18, legendY + 18, 80, 18), "Max Data", legendStyle);
            }
        }

        #endregion

        #region Heatmap Visualization

        private void DrawHeatmapChart(Rect chartRect, Rect plotArea, float cycleReliabilityStart, float cycleReliabilityEnd,
            float ratedBurnTime, FloatCurve cycleCurve, float maxTime, float maxDataValue, float sliderTime, float currentDataValue,
            float ignitionReliabilityStart, float ignitionReliabilityEnd)
        {
            if (Event.current.type != EventType.Repaint) return;

            // Draw background
            GUI.DrawTexture(chartRect, _textures.ChartBg);

            // Dynamic title based on mode
            string chartTitle = _sliderModeIsPercentage ? "Time to Reach Survival %" : "Failure Probability Heatmap";
            GUI.Label(new Rect(chartRect.x, chartRect.y + 4, chartRect.width, 24),
                chartTitle, EngineConfigStyles.ChartTitle);

            // Check if we need to regenerate the heatmap
            bool needsRegeneration = _cachedHeatmap == null ||
                                    Mathf.Abs(_lastHeatmapMaxTime - maxTime) > 0.1f ||
                                    Mathf.Abs(_lastHeatmapMaxData - maxDataValue) > 0.1f ||
                                    _lastHeatmapClusterSize != _clusterSize ||
                                    Mathf.Abs(_lastHeatmapCycleStart - cycleReliabilityStart) > 0.001f ||
                                    Mathf.Abs(_lastHeatmapCycleEnd - cycleReliabilityEnd) > 0.001f ||
                                    _lastHeatmapModeIsPercentage != _sliderModeIsPercentage ||
                                    _lastHeatmapIncludeIgnition != _includeIgnition;

            if (needsRegeneration)
            {
                // Clean up old texture
                if (_cachedHeatmap != null)
                {
                    UnityEngine.Object.Destroy(_cachedHeatmap);
                }

                // Generate new heatmap texture
                int xSteps = 60;
                int ySteps = 40;
                _cachedHeatmap = new Texture2D(xSteps, ySteps, TextureFormat.RGB24, false);
                _cachedHeatmap.filterMode = FilterMode.Bilinear;

                if (_sliderModeIsPercentage)
                {
                    // Percentage mode: X = survival %, Y = data level, Color = time to reach that %
                    for (int dataIdx = 0; dataIdx < ySteps; dataIdx++)
                    {
                        for (int percentIdx = 0; percentIdx < xSteps; percentIdx++)
                        {
                            float dataLevel = (dataIdx / (float)(ySteps - 1)) * maxDataValue;
                            float survivalPercent = 0.1f + (percentIdx / (float)(xSteps - 1)) * 99.8f; // 0.1% to 99.9%
                            float targetProb = survivalPercent / 100f;
                            
                            // Calculate cycle reliability at this data level
                            float cycleReliability = ChartMath.EvaluateReliabilityAtData(dataLevel, cycleReliabilityStart, cycleReliabilityEnd);
                            float ignitionReliability = ChartMath.EvaluateReliabilityAtData(dataLevel, ignitionReliabilityStart, ignitionReliabilityEnd);
                            
                            // Reverse clustering and ignition to find per-engine probability
                            float targetCycleProb = targetProb;
                            if (_clusterSize > 1)
                            {
                                targetCycleProb = Mathf.Pow(targetProb, 1f / _clusterSize);
                            }
                            if (_includeIgnition)
                            {
                                targetCycleProb /= ignitionReliability;
                            }
                            
                            // Find time to reach this probability
                            float timeToReach = ChartMath.FindTimeForSurvivalProb(targetCycleProb, ratedBurnTime, cycleReliability, cycleCurve, maxTime);
                            
                            // Map time to color (green = short time, red = long time)
                            Color cellColor = GetTimeHeatmapColor(timeToReach, maxTime);
                            
                            _cachedHeatmap.SetPixel(percentIdx, dataIdx, cellColor);
                        }
                    }
                }
                else
                {
                    // Time mode: X = burn time, Y = data level, Color = failure probability
                    for (int dataIdx = 0; dataIdx < ySteps; dataIdx++)
                    {
                        for (int timeIdx = 0; timeIdx < xSteps; timeIdx++)
                        {
                            float dataLevel = (dataIdx / (float)(ySteps - 1)) * maxDataValue;
                            float burnTime = (timeIdx / (float)(xSteps - 1)) * maxTime;
                            
                            float cycleReliability = ChartMath.EvaluateReliabilityAtData(dataLevel, cycleReliabilityStart, cycleReliabilityEnd);
                            float ignitionReliability = ChartMath.EvaluateReliabilityAtData(dataLevel, ignitionReliabilityStart, ignitionReliabilityEnd);
                            
                            float baseRate = -Mathf.Log(cycleReliability) / ratedBurnTime;
                            float survivalProb = ChartMath.CalculateSurvivalProbAtTime(burnTime, ratedBurnTime, cycleReliability, baseRate, cycleCurve);
                            
                            if (_includeIgnition)
                            {
                                survivalProb *= ignitionReliability;
                            }
                            
                            if (_clusterSize > 1)
                            {
                                survivalProb = Mathf.Pow(survivalProb, _clusterSize);
                            }
                            
                            float failureProb = 1f - survivalProb;
                            Color cellColor = GetHeatmapColor(failureProb);
                            
                            _cachedHeatmap.SetPixel(timeIdx, dataIdx, cellColor);
                        }
                    }
                }
                
                _cachedHeatmap.Apply();

                // Update cache parameters
                _lastHeatmapMaxTime = maxTime;
                _lastHeatmapMaxData = maxDataValue;
                _lastHeatmapClusterSize = _clusterSize;
                _lastHeatmapCycleStart = cycleReliabilityStart;
                _lastHeatmapCycleEnd = cycleReliabilityEnd;
                _lastHeatmapModeIsPercentage = _sliderModeIsPercentage;
                _lastHeatmapIncludeIgnition = _includeIgnition;
            }

            // Draw the cached heatmap texture
            GUI.DrawTexture(plotArea, _cachedHeatmap);

            // Draw axes and labels
            DrawHeatmapAxes(chartRect, plotArea, maxTime, maxDataValue);
            DrawHeatmapLegend(plotArea, maxTime);
            
            // Draw marker dot for current simulation settings
            if (_sliderModeIsPercentage)
            {
                DrawHeatmapMarker(plotArea, _sliderPercentage, currentDataValue, 100f, maxDataValue);
            }
            else
            {
                DrawHeatmapMarker(plotArea, sliderTime, currentDataValue, maxTime, maxDataValue);
            }
        }

        private Color GetHeatmapColor(float failureProb)
        {
            // Color scheme: Green (safe) -> Yellow -> Orange -> Red (dangerous)
            // Adjusted for realistic rocket engine reliability
            // < 5% failure = green (acceptable/safe)
            // 5-10% failure = yellow (moderate risk)
            // 10-20% failure = orange (elevated risk)
            // 20-30% failure = red (high risk)
            // 30%+ failure = dark red (very dangerous)

            if (failureProb < 0.05f) // < 5% failure - green (safe)
            {
                float t = failureProb / 0.05f;
                return Color.Lerp(new Color(0.2f, 0.9f, 0.2f), new Color(0.5f, 0.95f, 0.3f), t);
            }
            else if (failureProb < 0.10f) // 5-10% failure - green to yellow
            {
                float t = (failureProb - 0.05f) / 0.05f;
                return Color.Lerp(new Color(0.5f, 0.95f, 0.3f), new Color(0.95f, 0.95f, 0.2f), t);
            }
            else if (failureProb < 0.20f) // 10-20% failure - yellow to orange
            {
                float t = (failureProb - 0.10f) / 0.10f;
                return Color.Lerp(new Color(0.95f, 0.95f, 0.2f), new Color(1.0f, 0.6f, 0.1f), t);
            }
            else if (failureProb < 0.30f) // 20-30% failure - orange to red
            {
                float t = (failureProb - 0.20f) / 0.10f;
                return Color.Lerp(new Color(1.0f, 0.6f, 0.1f), new Color(0.95f, 0.2f, 0.2f), t);
            }
            else // 30%+ failure - dark red (very dangerous)
            {
                float t = Mathf.Clamp01((failureProb - 0.30f) / 0.30f);
                return Color.Lerp(new Color(0.95f, 0.2f, 0.2f), new Color(0.6f, 0.1f, 0.1f), t);
            }
        }

        private Color GetTimeHeatmapColor(float time, float maxTime)
        {
            // Color scheme for time: Red (short time/hard to achieve) -> Orange -> Yellow -> Green (long time/easy to achieve)
            // INVERTED: More time = better (green), less time = harder to achieve (red)
            float normalizedTime = Mathf.Clamp01(time / maxTime);
            
            if (normalizedTime < 0.2f) // 0-20% of max time - dark red (very hard to achieve)
            {
                float t = normalizedTime / 0.2f;
                return Color.Lerp(new Color(0.6f, 0.1f, 0.1f), new Color(0.95f, 0.2f, 0.2f), t);
            }
            else if (normalizedTime < 0.4f) // 20-40% - red to orange
            {
                float t = (normalizedTime - 0.2f) / 0.2f;
                return Color.Lerp(new Color(0.95f, 0.2f, 0.2f), new Color(1.0f, 0.6f, 0.1f), t);
            }
            else if (normalizedTime < 0.6f) // 40-60% - orange to yellow
            {
                float t = (normalizedTime - 0.4f) / 0.2f;
                return Color.Lerp(new Color(1.0f, 0.6f, 0.1f), new Color(0.95f, 0.95f, 0.2f), t);
            }
            else if (normalizedTime < 0.8f) // 60-80% - yellow to green
            {
                float t = (normalizedTime - 0.6f) / 0.2f;
                return Color.Lerp(new Color(0.95f, 0.95f, 0.2f), new Color(0.5f, 0.95f, 0.3f), t);
            }
            else // 80-100% - bright green (very achievable/long time available)
            {
                float t = (normalizedTime - 0.8f) / 0.2f;
                return Color.Lerp(new Color(0.5f, 0.95f, 0.3f), new Color(0.2f, 0.9f, 0.2f), t);
            }
        }

        private void DrawHeatmapAxes(Rect chartRect, Rect plotArea, float maxTime, float maxDataValue)
        {
            GUIStyle labelStyle = EngineConfigStyles.GridLabel;
            GUIStyle timeStyle = EngineConfigStyles.TimeLabel;

            if (_sliderModeIsPercentage)
            {
                // Percentage mode: X = survival %, Y = data level
                // X-axis labels (Survival Percentage)
                for (int i = 0; i <= 4; i++)
                {
                    float percent = 0.1f + (i / 4f) * 99.8f;
                    float x = plotArea.x + (i / 4f) * plotArea.width;
                    GUI.Label(new Rect(x - 25, plotArea.y + plotArea.height + 2, 50, 20),
                        $"{percent:F0}%", timeStyle);
                }
            }
            else
            {
                // Time mode: X = burn time, Y = data level
                // X-axis labels (Burn Time)
                for (int i = 0; i <= 4; i++)
                {
                    float time = (i / 4f) * maxTime;
                    float x = plotArea.x + (i / 4f) * plotArea.width;
                    GUI.Label(new Rect(x - 25, plotArea.y + plotArea.height + 2, 50, 20),
                        ChartMath.FormatTime(time), timeStyle);
                }
            }

            // Y-axis labels (Data Level) - same for both modes
            for (int i = 0; i <= 4; i++)
            {
                float dataLevel = (i / 4f) * maxDataValue;
                float y = plotArea.y + plotArea.height - (i / 4f) * plotArea.height;
                GUI.Label(new Rect(plotArea.x - 35, y - 10, 30, 20),
                    $"{dataLevel:F0}", labelStyle);
            }

            // Axis titles
            GUIStyle axisTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
            };

            // X-axis title (changes based on mode)
            string xAxisTitle = _sliderModeIsPercentage ? "Survival Probability %" : "Burn Time";
            GUI.Label(new Rect(plotArea.x, plotArea.y + plotArea.height + 20, plotArea.width, 20),
                xAxisTitle, axisTitleStyle);

            // Y-axis title (rotated) - same for both modes
            Matrix4x4 matrixBackup = GUI.matrix;
            GUIUtility.RotateAroundPivot(-90f, new Vector2(plotArea.x - 20, plotArea.y + plotArea.height / 2));
            GUI.Label(new Rect(plotArea.x - 20 - plotArea.height / 2, plotArea.y + plotArea.height / 2 - 10, plotArea.height, 20),
                "Data Units (DU)", axisTitleStyle);
            GUI.matrix = matrixBackup;
        }

        private void DrawHeatmapLegend(Rect plotArea, float maxTime)
        {
            float legendWidth = 120f;
            float legendHeight = 100f;
            float legendX = plotArea.x + plotArea.width - legendWidth - 5;
            float legendY = plotArea.y + 5;

            // Draw legend background using GUI.color
            GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
            GUI.DrawTexture(new Rect(legendX, legendY, legendWidth, legendHeight), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUIStyle legendStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft
            };

            GUIStyle titleStyle = new GUIStyle(legendStyle)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 11
            };

            float yPos = legendY + 5;
            
            // Title and labels change based on mode
            string legendTitle;
            string[] labels;
            Color[] colors;
            
            if (_sliderModeIsPercentage)
            {
                // Percentage mode: showing time to reach percentage
                // Calculate actual time ranges based on maxTime
                float time80 = maxTime * 0.8f;
                float time60 = maxTime * 0.6f;
                float time40 = maxTime * 0.4f;
                float time20 = maxTime * 0.2f;
                
                legendTitle = "Burn Time";
                labels = new string[] {
                    $"> {ChartMath.FormatTime(time80)}",
                    $"{ChartMath.FormatTime(time60)}-{ChartMath.FormatTime(time80)}",
                    $"{ChartMath.FormatTime(time40)}-{ChartMath.FormatTime(time60)}",
                    $"{ChartMath.FormatTime(time20)}-{ChartMath.FormatTime(time40)}",
                    $"< {ChartMath.FormatTime(time20)}"
                };
                // Colors inverted: green first (long time), red last (short time)
                colors = new Color[] {
                    new Color(0.2f, 0.9f, 0.2f),    // Green - long time available
                    new Color(0.95f, 0.95f, 0.2f),  // Yellow
                    new Color(1.0f, 0.6f, 0.1f),    // Orange
                    new Color(0.95f, 0.2f, 0.2f),   // Red
                    new Color(0.6f, 0.1f, 0.1f)     // Dark red - short time
                };
            }
            else
            {
                // Time mode: showing failure probability
                // Green = low failure (good), Red = high failure (bad)
                legendTitle = "Failure Risk";
                labels = new string[] { "< 5% (Safe)", "5-10%", "10-20%", "20-30%", "> 30% (High)" };
                colors = new Color[] {
                    new Color(0.2f, 0.9f, 0.2f),    // Green - low failure (good)
                    new Color(0.95f, 0.95f, 0.2f),  // Yellow
                    new Color(1.0f, 0.6f, 0.1f),    // Orange
                    new Color(0.95f, 0.2f, 0.2f),   // Red
                    new Color(0.6f, 0.1f, 0.1f)     // Dark red - high failure (bad)
                };
            }
            
            GUI.Label(new Rect(legendX + 5, yPos, legendWidth - 10, 15), legendTitle, titleStyle);
            yPos += 18;

            for (int i = 0; i < labels.Length; i++)
            {
                // Draw color swatch using GUI.color (no texture creation)
                GUI.color = colors[i];
                GUI.DrawTexture(new Rect(legendX + 8, yPos, 12, 12), Texture2D.whiteTexture);
                GUI.color = Color.white;

                // Draw label
                GUI.Label(new Rect(legendX + 23, yPos - 2, legendWidth - 28, 15), labels[i], legendStyle);
                yPos += 15;
            }
        }

        private void DrawHeatmapMarker(Rect plotArea, float sliderTime, float currentDataValue, float maxTime, float maxDataValue)
        {
            // Calculate marker position
            float normalizedTime = Mathf.Clamp01(sliderTime / maxTime);
            float normalizedData = Mathf.Clamp01(currentDataValue / maxDataValue);
            
            float markerX = plotArea.x + normalizedTime * plotArea.width;
            float markerY = plotArea.y + plotArea.height - normalizedData * plotArea.height;
            
            // Draw crosshair marker using ChartMath.DrawLine (no texture creation)
            float crosshairSize = 12f;
            
            // Use existing line textures from _textures
            Color whiteColor = new Color(1f, 1f, 1f, 0.9f);
            
            // Horizontal line
            Vector2 hLineStart = new Vector2(markerX - crosshairSize, markerY);
            Vector2 hLineEnd = new Vector2(markerX + crosshairSize, markerY);
            ChartMath.DrawLine(hLineStart, hLineEnd, _textures.ChartGridMajor, 2f);
            
            // Vertical line
            Vector2 vLineStart = new Vector2(markerX, markerY - crosshairSize);
            Vector2 vLineEnd = new Vector2(markerX, markerY + crosshairSize);
            ChartMath.DrawLine(vLineStart, vLineEnd, _textures.ChartGridMajor, 2f);
            
            // Draw center dot with bright cyan color for high visibility
            Color dotColor = new Color(0f, 1f, 1f, 1f); // Cyan
            
            // Simplified dot drawing - just a few circles, no texture creation per layer
            for (float r = 4f; r > 0; r -= 1.5f)
            {
                float size = r * 2f;
                Color layerColor = new Color(dotColor.r, dotColor.g, dotColor.b, dotColor.a * (r / 4f));
                Rect dotRect = new Rect(markerX - r, markerY - r, size, size);
                
                // Use a simple colored rect instead of creating textures
                GUI.color = layerColor;
                GUI.DrawTexture(dotRect, Texture2D.whiteTexture);
            }
            GUI.color = Color.white; // Reset color
        }

        #endregion

        #region Info Panel Integration

        private void DrawInfoPanel(Rect rect, ConfigNode configNode, float ratedBurnTime, float testedBurnTime, bool hasTestedBurnTime,
            float cycleReliabilityStart, float cycleReliabilityEnd, bool hasCurrentData, float cycleReliabilityCurrent,
            float ignitionReliabilityStart, float ignitionReliabilityEnd, float ignitionReliabilityCurrent,
            float dataPercentage, float currentDataValue, float maxDataValue, float realCurrentData, float realMaxData,
            FloatCurve cycleCurve, ref float sliderTime, float maxTime)
        {

            var infoPanel = new EngineConfigInfoPanel(_module);
            infoPanel.Draw(rect, ratedBurnTime, testedBurnTime, hasTestedBurnTime,
                cycleReliabilityStart, cycleReliabilityEnd, ignitionReliabilityStart, ignitionReliabilityEnd,
                hasCurrentData, cycleReliabilityCurrent, ignitionReliabilityCurrent, dataPercentage,
                currentDataValue, maxDataValue, realCurrentData, realMaxData,
                ref _useSimulatedData, ref _simulatedDataValue, ref _clusterSize,
                ref _clusterSizeInput, ref _dataValueInput, ref sliderTime, ref _sliderTimeInput,
                ref _includeIgnition, ref _sliderModeIsPercentage, ref _sliderPercentage, ref _sliderPercentageInput,
                cycleCurve, maxTime);
        }

        #endregion
    }
}
