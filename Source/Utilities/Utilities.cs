using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RealFuels
{
    public class Utilities
    {
        private static bool? _kerbalismFound = null;

        public static bool KerbalismFound
        {
            get
            {
                if (!_kerbalismFound.HasValue)
                {
                    _kerbalismFound = false;
                    
                    foreach (var a in AssemblyLoader.loadedAssemblies)
                    {
                        // Kerbalism comes with more than one assembly. There is Kerbalism for debug builds, KerbalismBootLoader,
                        // then there are Kerbalism18 or Kerbalism16_17 depending on the KSP version, and there might be other
                        // assemblies like KerbalismContracts etc.
                        // So look at the assembly name object instead of the assembly name (which is the file name and could be renamed).

                        AssemblyName nameObject = new AssemblyName(a.assembly.FullName);
                        string realName = nameObject.Name; // Will always return "Kerbalism" as defined in the AssemblyName property of the csproj

                        if (string.Equals(realName, "Kerbalism", StringComparison.OrdinalIgnoreCase))
                        {
                            _kerbalismFound = true;
                            break;
                        }
                    }
                }

                return _kerbalismFound.Value;
            }
        }

        private static bool? _b9psFound = null;
        public static bool B9PSFound
        {
            get
            {
                if (!_b9psFound.HasValue)
                {
                    var assembly = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.assembly.GetName().Name == "B9PartSwitch")?.assembly;
                    _b9psFound = (assembly is Assembly);
                }
                return _b9psFound.Value;
            }
        }

        private static bool? _RP1Found = null;
        public static bool RP1Found
        {
            get
            {
                if (!_RP1Found.HasValue)
                {
                    var assembly = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.assembly.GetName().Name == "RP0")?.assembly;
                    _RP1Found = (assembly is Assembly);
                }
                return _RP1Found.Value;
            }
        }

        public static FloatCurve Mod(FloatCurve fc, float sMult, float vMult)
        {
            FloatCurve newCurve = new FloatCurve();
            AnimationCurve ac = fc.Curve;
            int kCount = ac.keys.Length;
            for (int i = 0; i < kCount; ++i)
            {
                Keyframe key = ac.keys[i];
                float mult = Mathf.Lerp(vMult, sMult, key.time);

                newCurve.Add(key.time,
                            key.value * mult,
                            key.inTangent * mult,
                            key.outTangent * mult);
            }
            return newCurve;
        }
        public static void PrintCurve(FloatCurve fc)
        {
            if (fc == null)
            {
                Debug.LogError("*PC* ERROR: FC is null!");
                return;
            }
            if (fc.Curve == null)
            {
                Debug.LogError("*PC* ERROR: FC's curve is null!");
                return;
            }
            int len = fc.Curve.keys.Length;
            Debug.Log("Curve has " + len + " keys.");
            for (int i = 0; i < len; ++i)
                Debug.Log("key = " + fc.Curve.keys[i].time + " " + fc.Curve.keys[i].value + " " + fc.Curve.keys[i].inTangent + " " + fc.Curve.keys[i].outTangent);
        }

        public string GetNodeNames(List<ConfigNode> list)
        {
            string output = "";
            foreach (ConfigNode n in list)
                if (n.HasValue("name"))
                    output += " " + n.GetValue("name");
            return output;
        }

        public static string PrintNode(ConfigNode n, string prefix)
        {
            string retstr = prefix + n.name + "\n" + prefix + "{\n";
            string prefix2 = prefix + "\t";
            foreach (ConfigNode.Value v in n.values)
            {
                retstr += prefix2 + v.name + " = " + v.value + "\n";
            }
            foreach (ConfigNode node in n.nodes)
                retstr += PrintNode(node, prefix2);
            retstr += prefix + "}\n";
            return retstr;
        }
        public static string PrintConfigs(List<ConfigNode> list)
        {
            string printStr = "\n";
            foreach (ConfigNode node in list)
                printStr += PrintNode(node, "");
            return printStr;
        }

        public static string GetPartName(Part part)
        {
            if (part.partInfo != null)
                return GetPartName(part.partInfo);
            return GetPartName(part.name);
        }

        public static string GetPartName(AvailablePart ap)
        {
            return GetPartName(ap.name);
        }
        public static string GetPartName(string partName)
        {
            partName = partName.Replace(".", "-");
            return partName.Replace("_", "-");
        }

        public static string SanitizeName(string name)
        {
            return name.Replace(".", "-").Replace("_", "-");
        }

        public static bool CompareLists<T>(List<T> a, List<T> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = a.Count - 1; i >= 0; --i)
                if (!b.Contains(a[i]))
                    return false;
            return true;
        }

        public static string FormatFlux(double flux, bool scale = false)
        {
            string unit = "W";
            if (scale)
            {
                flux *= TimeWarp.fixedDeltaTime;
                unit = "J";
            }
            return KSPUtil.PrintSI(flux * 1e3, unit, 4);
        }

        public static string FormatThrust(double thrust)
        {
            if (thrust < 1e-6)
            {
                return $"{thrust * 1e9:0.#} μN";
            }
            if (thrust < 1e-3)
            {
                return $"{thrust * 1e6:0.#} mN";
            }
            else if (thrust < 1.0)
            {
                return $"{thrust * 1e3:0.#} N";
            }
            else
            {
                return $"{thrust:0.#} kN";
            }
        }

        /// <summary>
        /// Gets a Normal distribution value.
        /// </summary>
        /// <param name="rnd">The System.Random to use for random values</param>
        /// <param name="negativeValueLimit">Values below this will result in a reroll. 0 means all values are acceptable.</param>
        /// <param name="positiveValueLimit">Values above this will result in a reroll. 0 means all values are acceptable</param>
        /// <returns></returns>
        public static double GetNormal(System.Random rnd, double negativeValueLimit, double positiveValueLimit)
        {
            double u, v, S, retVal;
            do
            {
                do
                {
                    u = rnd.NextDouble() * 2d - 1d;
                    v = rnd.NextDouble() * 2d - 1d;
                    S = u * u + v * v;
                }
                while (S >= 1d);

                double fac = Math.Sqrt(-2.0 * Math.Log(S) / S);
                retVal = u * fac;
            }
            while (retVal < 0 ? negativeValueLimit < 0 && retVal < negativeValueLimit : positiveValueLimit > 0 && retVal > positiveValueLimit);
            return retVal;
        }

        #region Finding resources
        public static List<PartResource> FindResources(Part part, Propellant p)
        {
            List<PartResource> list = new List<PartResource>();
            ResourceFlowMode flow = p.GetFlowMode();
            if (flow == ResourceFlowMode.STACK_PRIORITY_SEARCH || flow == ResourceFlowMode.STAGE_PRIORITY_FLOW_BALANCE || flow == ResourceFlowMode.STAGE_STACK_FLOW || flow == ResourceFlowMode.STAGE_STACK_FLOW_BALANCE)
            {
                foreach (Part n in part.crossfeedPartSet.GetParts())
                {
                    n.Resources.GetAll(list, p.id);
                }
            }
            else
            {
                List<Part> parts = null;
                if (flow != ResourceFlowMode.NO_FLOW)
                {
                    if (part.vessel != null)
                        parts = part.vessel.parts;
                    else if (EditorLogic.fetch != null && EditorLogic.fetch.ship != null)
                        parts = EditorLogic.fetch.ship.parts;
                }

                if (parts == null)
                {
                    part.Resources.GetAll(list, p.id);
                }
                else
                {
                    for (int i = parts.Count - 1; i >= 0; --i)
                    {
                        Part foundPart = parts[i];
                        foundPart.Resources.GetAll(list, p.id);
                    }
                }
            }
            return list;
        }
        #endregion
    }
}
