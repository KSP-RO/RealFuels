using System;
using UnityEngine;

namespace RealFuels
{
    /// <summary>
    /// Mathematical calculations for chart rendering and TestFlight curve evaluation.
    /// All chart math extracted into static utility methods.
    /// </summary>
    public static class ChartMath
    {
        private const int CurvePoints = 100;

        #region Data Structures

        public struct SurvivalCurveData
        {
            public float[] SurvivalProbs;
            public float[] SurvivalProbsEnd;
            public float MinSurvivalProb;
        }

        #endregion

        #region TestFlight Curve Building

        /// <summary>
        /// Build the TestFlight cycle curve exactly as TestFlight_Generic_Engines.cfg does.
        /// </summary>
        public static FloatCurve BuildTestFlightCycleCurve(float ratedBurnTime, float testedBurnTime, float overburnPenalty, bool hasTestedBurnTime)
        {
            FloatCurve curve = new FloatCurve();

            curve.Add(0.00f, 10.00f);
            curve.Add(5.00f, 1.00f, -0.8f, 0f);

            float rbtCushioned = ratedBurnTime + 5f;
            curve.Add(rbtCushioned, 1f, 0f, 0f);

            if (hasTestedBurnTime)
            {
                float ratedToTestedInterval = testedBurnTime - rbtCushioned;
                float tbtTransitionSlope = 3.135f / ratedToTestedInterval * (overburnPenalty - 1.0f);
                curve.Add(testedBurnTime, overburnPenalty, tbtTransitionSlope, tbtTransitionSlope);

                float failTime = testedBurnTime * 2.5f;
                float tbtToFailInterval = failTime - testedBurnTime;
                float failInSlope = 1.989f / tbtToFailInterval * (100f - overburnPenalty);
                curve.Add(failTime, 100f, failInSlope, 0f);
            }
            else
            {
                float failTime = ratedBurnTime * 2.5f;
                float rbtToFailInterval = failTime - rbtCushioned;
                float failInSlope = 292.8f / rbtToFailInterval;
                curve.Add(failTime, 100f, failInSlope, 0f);
            }

            return curve;
        }

        /// <summary>
        /// Creates a TestFlight-style non-linear reliability curve that maps data units to reliability.
        /// </summary>
        public static FloatCurve CreateReliabilityCurve(float reliabilityStart, float reliabilityEnd)
        {
            FloatCurve curve = new FloatCurve();

            float failChanceStart = 1f - reliabilityStart;
            float failChanceEnd = 1f - reliabilityEnd;

            const float reliabilityMidV = 0.75f;
            const float reliabilityMidH = 0.4f;
            const float reliabilityMidTangentWeight = 0.5f;
            const float maxData = 10000f;

            curve.Add(0f, failChanceStart);

            float key1X = reliabilityMidH * 5000f + 1000f;
            float key1Y = failChanceStart + reliabilityMidV * (failChanceEnd - failChanceStart);

            float tangentPart1 = (failChanceEnd - failChanceStart) * 0.0001f * reliabilityMidTangentWeight;
            float linearTangent = (failChanceEnd - key1Y) / (maxData - key1X);
            float tangentPart2 = linearTangent * (1f - reliabilityMidTangentWeight);
            float key1Tangent = tangentPart1 + tangentPart2;

            curve.Add(key1X, key1Y, key1Tangent, key1Tangent);
            curve.Add(maxData, failChanceEnd, 0f, 0f);

            return curve;
        }

        /// <summary>
        /// Evaluates reliability at a given data value using TestFlight's non-linear curve.
        /// </summary>
        public static float EvaluateReliabilityAtData(float dataUnits, float reliabilityStart, float reliabilityEnd)
        {
            FloatCurve curve = CreateReliabilityCurve(reliabilityStart, reliabilityEnd);
            float failChance = curve.Evaluate(dataUnits);
            return 1f - failChance;
        }

        #endregion

        #region Survival Calculation

        /// <summary>
        /// Calculate survival curves for start and end reliability.
        /// </summary>
        public static SurvivalCurveData CalculateSurvivalCurves(float cycleReliabilityStart, float cycleReliabilityEnd,
            float ratedBurnTime, FloatCurve cycleCurve, float maxTime, int clusterSize)
        {
            float[] survivalProbsStart = new float[CurvePoints];
            float[] survivalProbsEnd = new float[CurvePoints];
            float minSurvivalProb = 1f;

            float baseRateStart = -Mathf.Log(cycleReliabilityStart) / ratedBurnTime;
            float baseRateEnd = -Mathf.Log(cycleReliabilityEnd) / ratedBurnTime;

            for (int i = 0; i < CurvePoints; i++)
            {
                float t = (i / (float)(CurvePoints - 1)) * maxTime;

                survivalProbsStart[i] = CalculateSurvivalProbAtTime(t, ratedBurnTime, cycleReliabilityStart, baseRateStart, cycleCurve);
                survivalProbsEnd[i] = CalculateSurvivalProbAtTime(t, ratedBurnTime, cycleReliabilityEnd, baseRateEnd, cycleCurve);

                minSurvivalProb = Mathf.Min(minSurvivalProb, survivalProbsStart[i]);
                minSurvivalProb = Mathf.Min(minSurvivalProb, survivalProbsEnd[i]);
            }

            // Apply cluster math
            if (clusterSize > 1)
            {
                for (int i = 0; i < CurvePoints; i++)
                {
                    survivalProbsStart[i] = Mathf.Pow(survivalProbsStart[i], clusterSize);
                    survivalProbsEnd[i] = Mathf.Pow(survivalProbsEnd[i], clusterSize);
                    minSurvivalProb = Mathf.Min(minSurvivalProb, survivalProbsStart[i]);
                    minSurvivalProb = Mathf.Min(minSurvivalProb, survivalProbsEnd[i]);
                }
            }

            float yAxisMin = RoundToNiceNumber(Mathf.Max(0f, minSurvivalProb - 0.02f), false);

            return new SurvivalCurveData
            {
                SurvivalProbs = survivalProbsStart,
                SurvivalProbsEnd = survivalProbsEnd,
                MinSurvivalProb = yAxisMin
            };
        }

        /// <summary>
        /// Calculate survival curve for a single reliability value.
        /// </summary>
        public static SurvivalCurveData CalculateSurvivalCurve(float cycleReliability, float ratedBurnTime,
            FloatCurve cycleCurve, float maxTime, int clusterSize)
        {
            float[] survivalProbs = new float[CurvePoints];
            float minSurvivalProb = 1f;

            float baseRate = -Mathf.Log(cycleReliability) / ratedBurnTime;

            for (int i = 0; i < CurvePoints; i++)
            {
                float t = (i / (float)(CurvePoints - 1)) * maxTime;
                survivalProbs[i] = CalculateSurvivalProbAtTime(t, ratedBurnTime, cycleReliability, baseRate, cycleCurve);
                minSurvivalProb = Mathf.Min(minSurvivalProb, survivalProbs[i]);
            }

            if (clusterSize > 1)
            {
                for (int i = 0; i < CurvePoints; i++)
                {
                    survivalProbs[i] = Mathf.Pow(survivalProbs[i], clusterSize);
                    minSurvivalProb = Mathf.Min(minSurvivalProb, survivalProbs[i]);
                }
            }

            return new SurvivalCurveData
            {
                SurvivalProbs = survivalProbs,
                SurvivalProbsEnd = new float[CurvePoints],
                MinSurvivalProb = minSurvivalProb
            };
        }

        /// <summary>
        /// Calculate survival probability at a specific time.
        /// </summary>
        public static float CalculateSurvivalProbAtTime(float time, float ratedBurnTime,
            float cycleReliability, float baseRate, FloatCurve cycleCurve)
        {
            if (time <= ratedBurnTime)
            {
                return Mathf.Pow(cycleReliability, time / ratedBurnTime);
            }
            else
            {
                float survivalToRated = cycleReliability;
                float integratedModifier = IntegrateCycleCurve(cycleCurve, ratedBurnTime, time, 20);
                float additionalFailRate = baseRate * integratedModifier;
                return Mathf.Clamp01(survivalToRated * Mathf.Exp(-additionalFailRate));
            }
        }

        /// <summary>
        /// Numerically integrate the cycle curve from t1 to t2 using trapezoidal rule.
        /// </summary>
        public static float IntegrateCycleCurve(FloatCurve curve, float t1, float t2, int steps)
        {
            if (t2 <= t1) return 0f;

            float dt = (t2 - t1) / steps;
            float sum = 0f;

            for (int i = 0; i < steps; i++)
            {
                float tStart = t1 + i * dt;
                float tEnd = tStart + dt;
                float valueStart = curve.Evaluate(tStart);
                float valueEnd = curve.Evaluate(tEnd);
                sum += (valueStart + valueEnd) * 0.5f * dt;
            }

            return sum;
        }

        /// <summary>
        /// Find the time at which survival probability reaches a target percentage.
        /// Uses binary search to find the time that gives the desired survival probability.
        /// </summary>
        public static float FindTimeForSurvivalProb(float targetSurvivalProb, float ratedBurnTime,
            float cycleReliability, FloatCurve cycleCurve, float maxTime, int maxIterations = 50)
        {
            if (targetSurvivalProb >= 1f) return 0f;
            if (targetSurvivalProb <= 0f) return maxTime;

            float baseRate = -Mathf.Log(cycleReliability) / ratedBurnTime;
            
            // Binary search for the time that gives us the target survival probability
            float minTime = 0f;
            float maxSearchTime = maxTime;
            float tolerance = 0.01f; // 1% tolerance
            
            for (int i = 0; i < maxIterations; i++)
            {
                float midTime = (minTime + maxSearchTime) / 2f;
                float survivalProb = CalculateSurvivalProbAtTime(midTime, ratedBurnTime, cycleReliability, baseRate, cycleCurve);
                
                // If we're close enough, return
                if (Mathf.Abs(survivalProb - targetSurvivalProb) < tolerance * targetSurvivalProb)
                    return midTime;
                
                // Survival probability decreases with time, so if current is too high, we need more time
                if (survivalProb > targetSurvivalProb)
                    minTime = midTime;
                else
                    maxSearchTime = midTime;
            }
            
            return (minTime + maxSearchTime) / 2f;
        }

        #endregion

        #region Coordinate Conversion

        /// <summary>
        /// Convert time to x-position on the chart.
        /// </summary>
        public static float TimeToXPosition(float time, float maxTime, float plotX, float plotWidth, bool useLogScale)
        {
            if (useLogScale)
            {
                float logTime = Mathf.Log10(time + 1f);
                float logMax = Mathf.Log10(maxTime + 1f);
                return plotX + (logTime / logMax) * plotWidth;
            }
            else
            {
                return plotX + (time / maxTime) * plotWidth;
            }
        }

        /// <summary>
        /// Convert x-position back to time.
        /// </summary>
        public static float XPositionToTime(float xPos, float maxTime, float plotX, float plotWidth, bool useLogScale)
        {
            float normalizedX = (xPos - plotX) / plotWidth;
            normalizedX = Mathf.Clamp01(normalizedX);

            if (useLogScale)
            {
                float logMax = Mathf.Log10(maxTime + 1f);
                return Mathf.Pow(10f, normalizedX * logMax) - 1f;
            }
            else
            {
                return normalizedX * maxTime;
            }
        }

        /// <summary>
        /// Convert survival probability to y-position on the chart.
        /// </summary>
        public static float SurvivalProbToYPosition(float survivalProb, float yAxisMin, float plotY, float plotHeight, bool useLogScale)
        {
            if (useLogScale)
            {
                float logProb = Mathf.Log10(survivalProb + 0.0001f);
                float logMax = Mathf.Log10(1f + 0.0001f);
                float logMin = Mathf.Log10(yAxisMin + 0.0001f);
                float normalizedLog = (logProb - logMin) / (logMax - logMin);
                return plotY + plotHeight - (normalizedLog * plotHeight);
            }
            else
            {
                float normalizedSurvival = (survivalProb - yAxisMin) / (1f - yAxisMin);
                return plotY + plotHeight - (normalizedSurvival * plotHeight);
            }
        }

        #endregion

        #region Formatting

        /// <summary>
        /// Format time in seconds to human-readable string (xd xh xm xs).
        /// </summary>
        public static string FormatTime(float timeInSeconds)
        {
            if (timeInSeconds < 0.1f) return "0s";

            int totalSeconds = Mathf.RoundToInt(timeInSeconds);
            int days = totalSeconds / 86400;
            int hours = (totalSeconds % 86400) / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int seconds = totalSeconds % 60;

            string result = "";
            if (days > 0) result += $"{days}d ";
            if (hours > 0) result += $"{hours}h ";
            if (minutes > 0) result += $"{minutes}m ";
            if (seconds > 0 || string.IsNullOrEmpty(result)) result += $"{seconds}s";

            return result.TrimEnd();
        }

        /// <summary>
        /// Format MTBF (mean time between failures) in human-readable units.
        /// </summary>
        public static string FormatMTBF(float mtbfSeconds)
        {
            if (float.IsInfinity(mtbfSeconds) || float.IsNaN(mtbfSeconds))
                return "∞";

            if (mtbfSeconds < 60f) return $"{mtbfSeconds:F1}s";
            if (mtbfSeconds < 3600f) return $"{mtbfSeconds / 60f:F1}m";
            if (mtbfSeconds < 86400f) return $"{mtbfSeconds / 3600f:F1}h";
            if (mtbfSeconds < 31536000f) return $"{mtbfSeconds / 86400f:F1}d";
            return $"{mtbfSeconds / 31536000f:F1}y";
        }

        #endregion

        #region Drawing Helpers

        /// <summary>
        /// Draw a line between two points with rotation.
        /// </summary>
        public static void DrawLine(Vector2 start, Vector2 end, Texture2D texture, float width)
        {
            if (texture == null) return;

            Vector2 diff = end - start;
            float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;
            float length = diff.magnitude;

            Matrix4x4 matrixBackup = GUI.matrix;
            try
            {
                GUIUtility.RotateAroundPivot(angle, start);
                GUI.DrawTexture(new Rect(start.x, start.y - width / 2, length, width), texture);
            }
            finally
            {
                GUI.matrix = matrixBackup;
            }
        }

        /// <summary>
        /// Draw a filled circle.
        /// </summary>
        public static void DrawCircle(Rect rect, Texture2D texture)
        {
            if (texture == null || Event.current.type != EventType.Repaint) return;

            float centerX = rect.x + rect.width / 2f;
            float centerY = rect.y + rect.height / 2f;
            float radius = rect.width / 2f;

            for (float r = radius; r > 0; r -= 0.5f)
            {
                float size = r * 2f;
                GUI.DrawTexture(new Rect(centerX - r, centerY - r, size, size), texture);
            }
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Round a value to a "nice" number (1, 2, or 5 times a power of 10).
        /// </summary>
        public static float RoundToNiceNumber(float value, bool roundUp)
        {
            if (value <= 0f) return 0f;

            float exponent = Mathf.Floor(Mathf.Log10(value));
            float fraction = value / Mathf.Pow(10f, exponent);

            float niceFraction;
            if (roundUp)
            {
                if (fraction <= 1f) niceFraction = 1f;
                else if (fraction <= 2f) niceFraction = 2f;
                else if (fraction <= 5f) niceFraction = 5f;
                else niceFraction = 10f;
            }
            else
            {
                if (fraction < 1.5f) niceFraction = 1f;
                else if (fraction < 3.5f) niceFraction = 2f;
                else if (fraction < 7.5f) niceFraction = 5f;
                else niceFraction = 10f;
            }

            return niceFraction * Mathf.Pow(10f, exponent);
        }

        #endregion
    }
}
