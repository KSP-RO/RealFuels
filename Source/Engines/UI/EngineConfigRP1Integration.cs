using System;
using System.Reflection;
using UnityEngine;

namespace RealFuels
{
    /// <summary>
    /// Handles reflection-based integration with the RP-1 mod (assembly name "RP-0").
    /// Binds lazily to RP0.UnlockCreditHandler to query available unlock credits,
    /// allowing the engine config GUI to show credit-adjusted purchase costs.
    /// Isolated here so cross-mod bridge logic stays separate from GUI rendering.
    /// </summary>
    internal static class EngineConfigRP1Integration
    {
        private static bool _checked = false;
        private static Type _unlockCreditHandlerType = null;
        private static PropertyInfo _unlockCreditInstanceProperty = null;
        private static PropertyInfo _totalCreditProperty = null;
        private static double _cachedCredits = 0;
        private static int _creditCacheFrame = -1;

        /// <summary>
        /// Lazily binds to RP-1's UnlockCreditHandler via reflection.
        /// Safe to call every frame — exits immediately after the first attempt.
        /// </summary>
        private static void CheckIntegration()
        {
            if (_checked) return;
            _checked = true;

            try
            {
                // The RP-1 assembly is named "RP-0" (with hyphen), not "RP0".
                // AssemblyLoader.LoadedAssemblyList is not a generic IEnumerable, so use foreach.
                AssemblyLoader.LoadedAssembly rp1Assembly = null;
                foreach (var a in AssemblyLoader.loadedAssemblies)
                {
                    if (a.name == "RP-0") { rp1Assembly = a; break; }
                }

                if (rp1Assembly != null)
                {
                    _unlockCreditHandlerType = rp1Assembly.assembly.GetType("RP0.UnlockCreditHandler");
                    if (_unlockCreditHandlerType != null)
                    {
                        _unlockCreditInstanceProperty = _unlockCreditHandlerType.GetProperty("Instance",
                            BindingFlags.Public | BindingFlags.Static);
                        _totalCreditProperty = _unlockCreditHandlerType.GetProperty("TotalCredit",
                            BindingFlags.Public | BindingFlags.Instance);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RealFuels] Failed to initialize RP-1 integration: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns true when RP-1 credits are available and would reduce <paramref name="entryCost"/>.
        /// Credits are cached per-frame to avoid repeated reflection overhead during GUI rendering.
        /// </summary>
        internal static bool TryGetCreditAdjustedCost(double entryCost, out double creditsAvailable, out double costAfterCredits)
        {
            creditsAvailable = 0;
            costAfterCredits = entryCost;

            CheckIntegration();

            if (_unlockCreditHandlerType == null || _unlockCreditInstanceProperty == null || _totalCreditProperty == null)
                return false;

            try
            {
                // Cache credits per frame to avoid expensive reflection calls every button render
                int currentFrame = Time.frameCount;
                if (_creditCacheFrame != currentFrame)
                {
                    var instance = _unlockCreditInstanceProperty.GetValue(null);
                    if (instance != null)
                    {
                        _cachedCredits = (double)_totalCreditProperty.GetValue(instance);
                        _creditCacheFrame = currentFrame;
                    }
                    else
                    {
                        return false;
                    }
                }

                creditsAvailable = _cachedCredits;
                double creditsUsed = Math.Min(creditsAvailable, entryCost);
                costAfterCredits = entryCost - creditsUsed;
                return creditsUsed > 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RealFuels] Failed to query RP-1 credits: {ex.Message}");
            }

            return false;
        }
    }
}
