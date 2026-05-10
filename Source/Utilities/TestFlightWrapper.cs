using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RealFuels
{
    public static class TestFlightWrapper
    {
        private const BindingFlags tfBindingFlags = BindingFlags.Public | BindingFlags.Static;
        private const BindingFlags tfInstanceFlags = BindingFlags.Public | BindingFlags.Instance;

        private static Type tfInterface = null;
        private static Type tfCore = null;
        private static MethodInfo addInteropValue = null;
        private static MethodInfo getFlightData = null;
        private static MethodInfo getMaxData = null;

        static TestFlightWrapper()
        {
            if (AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.assembly.GetName().Name == "TestFlightCore")?.assembly is Assembly tfAssembly)
            {
                tfInterface = Type.GetType("TestFlightCore.TestFlightInterface, TestFlightCore", false);
                tfCore = Type.GetType("TestFlightCore.TestFlightCore, TestFlightCore", false);

                if (tfInterface != null)
                {
                    Type[] argumentTypes = new[] { typeof(Part), typeof(string), typeof(string), typeof(string) };
                    addInteropValue = tfInterface.GetMethod("AddInteropValue", tfBindingFlags, null, argumentTypes, null);
                }

                if (tfCore != null)
                {
                    getFlightData = tfCore.GetMethod("GetFlightData", tfInstanceFlags);
                    getMaxData = tfCore.GetMethod("GetMaximumData", tfInstanceFlags);
                }
            }
        }

        public static void AddInteropValue(Part part, string name, string value, string owner)
        {
            if (addInteropValue != null)
            {
                try
                {
                    object[] parameters = { part, name, value, owner };
                    addInteropValue.Invoke(null, parameters);
                }
                catch { }
            }
        }

        /// <summary>
        /// Gets the current flight data for the active TestFlight configuration on this part.
        /// Returns -1 if TestFlight is not available or no data found.
        /// </summary>
        public static float GetCurrentFlightData(Part part)
        {
            if (tfCore == null || getFlightData == null || part == null)
                return -1f;

            try
            {
                // Find TestFlightCore module on the part
                foreach (PartModule pm in part.Modules)
                {
                    if (pm.GetType() == tfCore || pm.GetType().IsSubclassOf(tfCore))
                    {
                        object result = getFlightData.Invoke(pm, null);
                        if (result is float flightData)
                            return flightData;
                    }
                }
            }
            catch { }

            return -1f;
        }

        /// <summary>
        /// Gets the maximum data value for the active TestFlight configuration on this part.
        /// Returns -1 if TestFlight is not available or no data found.
        /// </summary>
        public static float GetMaximumData(Part part)
        {
            if (tfCore == null || getMaxData == null || part == null)
                return -1f;

            try
            {
                // Find TestFlightCore module on the part
                foreach (PartModule pm in part.Modules)
                {
                    if (pm.GetType() == tfCore || pm.GetType().IsSubclassOf(tfCore))
                    {
                        object result = getMaxData.Invoke(pm, null);
                        if (result is float maxData)
                            return maxData;
                    }
                }
            }
            catch { }

            return -1f;
        }

        /// <summary>
        /// Gets the current data percentage (0 to 1) between 0 and max data.
        /// Returns -1 if TestFlight is not available or no valid data.
        /// </summary>
        public static float GetDataPercentage(Part part)
        {
            float currentData = GetCurrentFlightData(part);
            float maxData = GetMaximumData(part);

            if (currentData < 0f || maxData <= 0f)
                return -1f;

            return Mathf.Clamp01(currentData / maxData);
        }
    }
}
