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

        public static bool CompareLists<T>(List<T> a, List<T> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = a.Count - 1; i >= 0; --i)
                if (!b.Contains(a[i]))
                    return false;
            return true;
        }

        #region Finding resources
        public static List<PartResource> FindResources(Part part, Propellant p)
        {
            List<PartResource> list = new List<PartResource>();
            ResourceFlowMode flow = p.GetFlowMode();
            if (flow == ResourceFlowMode.STACK_PRIORITY_SEARCH || flow == ResourceFlowMode.STAGE_PRIORITY_FLOW_BALANCE || flow == ResourceFlowMode.STAGE_STACK_FLOW || flow == ResourceFlowMode.STAGE_STACK_FLOW_BALANCE)
            {
                HashSet<Part> visited = new HashSet<Part>();
                CrossfeedRecurseParts(visited, part, null);
                foreach (Part n in visited)
                {
                    n.Resources.GetAll(list, p.id);
                }
            }
            else
            {
                List<Part> parts;
                if (flow != ResourceFlowMode.NO_FLOW)
                {
                    if (part.vessel != null)
                        parts = part.vessel.parts;
                    else if (EditorLogic.fetch != null && EditorLogic.fetch.ship != null)
                        parts = EditorLogic.fetch.ship.parts;
                    else
                    {
                        parts = new List<Part>();
                        parts.Add(part);
                    }
                }
                else
                {
                    parts = new List<Part>();
                    parts.Add(part);
                }

                for (int i = parts.Count - 1; i >= 0; --i)
                {
                    Part foundPart = parts[i];
                    PartResource resource = foundPart.Resources.Get(p.id);
                    if (resource != null)
                        list.Add(resource);
                }
            }
            return list;
        }
        public static void CrossfeedRecurseParts(HashSet<Part> set, Part p, Vessel v)
        {
            if (set.Contains(p))
                return;

            set.Add(p);

            // check fuel lines
            Part otherPart;
            int fCount = p.fuelLookupTargets.Count;
            if (fCount > 0)
            {
                for (int i = fCount; i-- > 0;)
                {
                    otherPart = p.fuelLookupTargets[i];
                    if (otherPart != null && (otherPart.vessel == v || HighLogic.LoadedSceneIsEditor))
                    {
                        if ((otherPart == p.parent && p.isAttached)
                            || (otherPart != p.parent && otherPart.isAttached))
                        {
                            CrossfeedRecurseParts(set, otherPart, v);
                        }
                    }
                }
            }

            // if not, we only continue if the part has fuelCrossFeed enabled or if this same part is the one generating the request
            if (!p.fuelCrossFeed)
                return;

            // check surface attachments (i.e. our children)
            for (int i = p.children.Count; i-- > 0;)
            {
                otherPart = p.children[i];
                if (otherPart.srfAttachNode.attachedPart == p && otherPart.fuelCrossFeed)
                {
                    CrossfeedRecurseParts(set, otherPart, v);
                }
            }

            // check any neighbour nodes that are attached
            AttachNode node;
            for (int anI = p.attachNodes.Count; anI-- > 0;)
            {
                node = p.attachNodes[anI];

                if (!string.IsNullOrEmpty(p.NoCrossFeedNodeKey) && node.id.Contains(p.NoCrossFeedNodeKey))
                    continue;

                otherPart = node.attachedPart;

                if (!node.ResourceXFeed)
                    continue;

                if (otherPart == null)
                    continue;

                CrossfeedRecurseParts(set, otherPart, v);
            }

            // lastly check parent, if we haven't yet.
            if (p.parent != null)
            {
                AttachNode parentNode = p.FindAttachNodeByPart(p.parent);

                if (parentNode != null)
                {
                    if (string.IsNullOrEmpty(p.NoCrossFeedNodeKey) || !parentNode.id.Contains(p.NoCrossFeedNodeKey))
                    {
                        CrossfeedRecurseParts(set, p.parent, v);
                    }
                }
            }
        }
        #endregion
    }
}
