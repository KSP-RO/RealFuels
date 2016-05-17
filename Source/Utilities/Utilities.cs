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
            if (flow == ResourceFlowMode.STACK_PRIORITY_SEARCH)
            {
                HashSet<Part> visited = new HashSet<Part>();
                FindResources_StackPri(part, visited, list, p.id);
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
                    List<PartResource> resources = foundPart.Resources.GetAll(p.id);
                    for (int j = resources.Count - 1; j >= 0; --j)
                        list.Add(resources[j]);
                }
            }
            return list;
        }
        public static void FindResources_StackPri(Part part, HashSet<Part> visited, List<PartResource> resources, int resourceID)
        {
            // will never be called if we visited this part
            visited.Add(part);

            // check local first
            FindResources_Stack(part, visited, resources, resourceID);

            // Check self, else check parent.
            // weird logic, but this is what KSP does...
            PartResource resource = part.Resources.Get(resourceID);
            if (resource != null)
            {
                if(!resources.Contains(resource))
                    resources.Add(resource);
            }
            else
            {
                if (part.fuelCrossFeed && part.parent != null)
                {
                    AttachNode node = part.findAttachNodeByPart(part.parent);
                    if (node != null)
                        if (part.NoCrossFeedNodeKey == "" || !node.id.Contains(part.NoCrossFeedNodeKey))
                            if(!visited.Contains(part.parent))
                                FindResources_StackPri(part.parent, visited, resources, resourceID);
                }
            }
        }
        public static void FindResources_Stack(Part part, HashSet<Part> visited, List<PartResource> sources, int resourceID)
        {
            // will never be called if visited contains part.

            // use target list if it has targets
            for (int i = part.fuelLookupTargets.Count - 1; i >= 0; --i)
            {
                Part target = part.fuelLookupTargets[i];
                if (!visited.Contains(target) && (target == part.parent ? part.isAttached : target.isAttached))
                    FindResources_StackPri(part.fuelLookupTargets[i], visited, sources, resourceID);
            }

            // if we don't crossfeed, stop here.
            if (!part.fuelCrossFeed)
                return;

            // check any attached parts where we have crossfeed with them.
            AttachNode node;
            for (int j = part.attachNodes.Count - 1; j >= 0; --j)
            {
                node = part.attachNodes[j];
                if (part.NoCrossFeedNodeKey != "" && node.id.Contains(part.NoCrossFeedNodeKey))
                    continue;
                if (!node.ResourceXFeed || node.attachedPart == null)
                    continue;
                if(visited.Contains(node.attachedPart))
                    continue;

                FindResources_StackPri(node.attachedPart, visited, sources, resourceID);
            }
        }
        #endregion
    }
}
