using System;
using System.Reflection;
using UnityEngine;

namespace RealFuels
{
    public static class KerbalismInterface
    {
        private static bool _initialized;
        private static Func<Vessel, object> _getVesselData;
        private static Func<object, double> _getVesselTemperature;
        private static Func<object, double> _getVesselSurfaceArea;

        public static bool TryGetThermalData(Vessel vessel, out double vesselTemp, out double surfaceArea)
        {
            vesselTemp = 0;
            surfaceArea = -1;

            if (!_initialized)
                Initialize();

            if (_getVesselData == null || _getVesselTemperature == null || _getVesselSurfaceArea == null)
                return false;

            try
            {
                object vd = _getVesselData(vessel);
                if (vd == null) return false;
                vesselTemp = _getVesselTemperature(vd);
                surfaceArea = _getVesselSurfaceArea(vd);
                return surfaceArea > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void Initialize()
        {
            _initialized = true;
            try
            {
                Assembly kbAsm = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(new AssemblyName(a.FullName).Name, "Kerbalism", StringComparison.OrdinalIgnoreCase))
                    {
                        kbAsm = a;
                        break;
                    }
                }
                if (kbAsm == null) return;

                Type dbType = kbAsm.GetType("KERBALISM.DB");
                MethodInfo vesselDataMethod = dbType?.GetMethod("KerbalismData", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vessel) }, null);
                Type vdType = kbAsm.GetType("KERBALISM.VesselData");
                PropertyInfo tempProp = vdType?.GetProperty("VesselTemperature");
                PropertyInfo areaProp = vdType?.GetProperty("VesselSurfaceArea");

                if (vesselDataMethod != null)
                    _getVesselData = ReflectionHelpers.BuildStaticMethodDelegate(vesselDataMethod);
                if (tempProp != null)
                    _getVesselTemperature = ReflectionHelpers.BuildPropertyGetter<double>(tempProp);
                if (areaProp != null)
                    _getVesselSurfaceArea = ReflectionHelpers.BuildPropertyGetter<double>(areaProp);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RF] KerbalismInterface reflection init failed: {ex}");
            }
        }
    }
}
