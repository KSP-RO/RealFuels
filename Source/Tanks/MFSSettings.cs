using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;

namespace RealFuels
{
	[KSPAddon (KSPAddon.Startup.Instantly, false)]
    public class MFSSettings : MonoBehaviour
	{
        public static bool useRealisticMass = true;
        public static float tankMassMultiplier = 1;
        public static float baseCostPV = 0.0f;
        public static float partUtilizationDefault = 86;
        public static bool partUtilizationTweakable = false;
        public static string unitLabel = "u";

        public static bool basemassUseTotalVolume = false;

        public static HashSet<string> ignoreFuelsForFill;
        public static Tanks.TankDefinitionList tankDefinitions;

		public static Dictionary<string, HashSet<string>> managedResources;
        public static Dictionary<string, double> resourceVsps;

        private static Dictionary<string, ConfigNode[]> overrides;

        static string version;
        public static string GetVersion ()
        {
            if (version != null) {
                return version;
            }

            var asm = Assembly.GetCallingAssembly ();
            var title = SystemUtils.GetAssemblyTitle (asm);
            version = title + " " + SystemUtils.GetAssemblyVersionString (asm);

            return version;
        }

		void Awake ()
		{
			ignoreFuelsForFill = null;
			tankDefinitions = null;
			managedResources = null;

			Destroy (this);
		}

        public static void SaveOverrideList(Part p, ConfigNode[] nodes)
        {
            string id = GetPartIdentifier(p);
            if (overrides.ContainsKey(id))
            {
                Debug.Log("*MFT* ERROR: overrides already stored for " + id);
            }
            else
            {
                overrides[id] = nodes;
            }
        }

        public static ConfigNode[] GetOverrideList(Part p)
        {
            string id = GetPartIdentifier(p);
            if (overrides.ContainsKey(id))
            {
                return overrides[id];
            }
            Debug.Log("*MFT* WARNING: no entry in overrides for " + id);
            return new ConfigNode[0];
        }

        private static string GetPartIdentifier(Part part)
        {
            string partName = part.name;
            if (part.partInfo != null)
                partName = part.partInfo.name;
            partName = partName.Replace(".", "-");
            partName = partName.Replace("_", "-");
            return partName;
        }

        public static void Initialize ()
        {
			ignoreFuelsForFill = new HashSet<string> ();
			tankDefinitions = new Tanks.TankDefinitionList ();
			managedResources = new Dictionary<string,HashSet<string>> ();
            overrides = new Dictionary<string, ConfigNode[]>();

            // fill vsps
            resourceVsps = new Dictionary<string, double>();
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("RESOURCE_DEFINITION"))
            {
                if (n.HasValue("vsp"))
                {
                    double dtmp;
                    if (double.TryParse(n.GetValue("vsp"), out dtmp))
                        resourceVsps[n.GetValue("name")] = dtmp;
                }
            }

            ConfigNode node = GameDatabase.Instance.GetConfigNodes ("MFSSETTINGS").LastOrDefault ();
            Debug.Log ("[MFS] Loading global settings");

			if (node != null) {
				bool tb;
				float tf;

				if (bool.TryParse (node.GetValue ("useRealisticMass"), out tb)) {
					useRealisticMass = tb;
				}
				if (float.TryParse (node.GetValue ("tankMassMultiplier"), out tf)) {
					tankMassMultiplier = tf;
				}
				if (float.TryParse (node.GetValue ("baseCostPV"), out tf)) {
					baseCostPV = tf;
				}
				if (float.TryParse (node.GetValue ("partUtilizationDefault"), out tf)) {
					partUtilizationDefault = tf;
				}
				if (bool.TryParse (node.GetValue ("partUtilizationTweakable"), out tb)) {
					partUtilizationTweakable = tb;
				}
				if (node.HasValue ("unitLabel")) {
					unitLabel = node.GetValue ("unitLabel");
				}
                if (bool.TryParse(node.GetValue("basemassUseTotalVolume"), out tb)) {
                    basemassUseTotalVolume = tb;
                }

				ConfigNode ignoreNode = node.GetNode ("IgnoreFuelsForFill");
				if (ignoreNode != null) {
					foreach (ConfigNode.Value v in ignoreNode.values) {
						ignoreFuelsForFill.Add (v.name);
					}
				}
			}

            foreach (ConfigNode defNode in GameDatabase.Instance.GetConfigNodes ("TANK_DEFINITION")) {
                if (tankDefinitions.Contains (defNode.GetValue ("name"))) {
                    Debug.LogWarning ("[MFS] Ignored duplicate definition of tank type " + defNode.GetValue ("name"));
                } else {
                    tankDefinitions.Add (new Tanks.TankDefinition (defNode));
				}
            }
        }
    }
}
