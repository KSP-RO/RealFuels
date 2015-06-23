using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RealFuels
{
    public class Utilities
    {
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
                Debug.Log("*PC* ERROR: FC is null!");
                return;
            }
            if (fc.Curve == null)
            {
                Debug.Log("*PC* ERROR: FC's curve is null!");
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

        public static bool CompareLists<T>(List<T> a, List<T> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = a.Count - 1; i >= 0; --i)
                if (!b.Contains(a[i]))
                    return false;
            return true;
        }
    }
}
