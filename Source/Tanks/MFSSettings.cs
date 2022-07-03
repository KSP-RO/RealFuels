using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;

namespace RealFuels
{
    public class MFSSettings
	{
        public static bool useRealisticMass = true;
        public static float tankMassMultiplier = 1;
        public static float baseCostPV = 0.0f;
        public static float partUtilizationDefault = 86;
        public static bool partUtilizationTweakable = false;
        public static string unitLabel = "u";
        public static bool basemassUseTotalVolume = false;
        public static double radiatorMinTempMult = 0.99d;

        // Move all possible tank upgrades into the preview list in OnStart
        // It requires an external mod to be responsible for calling the Validate() method.
        public static bool previewAllLockedTypes = false;

        public static readonly HashSet<string> ignoreFuelsForFill = new HashSet<string>();
        public static readonly Dictionary<string, Tanks.TankDefinition> tankDefinitions = new Dictionary<string, Tanks.TankDefinition>();

        public static readonly Dictionary<string, double> resourceVsps = new Dictionary<string, double>();
        public static readonly Dictionary<string, double> resourceConductivities = new Dictionary<string, double>();

        private static readonly Dictionary<string, ConfigNode[]> overrides = new Dictionary<string, ConfigNode[]>();
        private static bool Initialized = false;

        static string version;
        public static string GetVersion ()
        {
            if (version != null) {
                return version;
            }

            var asm = Assembly.GetCallingAssembly ();
            var title = MFSVersionReport.GetAssemblyTitle (asm);
            version = title + " " + MFSVersionReport.GetAssemblyVersionString (asm);

            return version;
        }

        public static void SaveOverrideList(Part p, ConfigNode[] nodes)
        {
            string id = GetPartIdentifier(p);
            if (overrides.ContainsKey(id))
                Debug.Log("*MFT* ERROR: overrides already stored for " + id);
            else
                overrides[id] = nodes;
        }

        public static ConfigNode[] GetOverrideList(Part p)
        {
            string id = GetPartIdentifier(p);
            if (overrides.TryGetValue(id, out var result))
                return result;

            Debug.Log("*MFT* WARNING: no entry in overrides for " + id);
            return new ConfigNode[0];
        }

        private static string GetPartIdentifier(Part part)
        {
            string partName = part.partInfo != null ? part.partInfo.name : part.name;
            partName = Utilities.SanitizeName(partName);
            return partName;
        }

        public static void TryInitialize()
        {
            if (!Initialized) Initialize();
        }

        public static void Initialize ()
        {
            // fill vsps & conductivities
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("RESOURCE_DEFINITION"))
            {
                string nm = n.GetValue("name");
                double dtmp = 0;
                if (n.TryGetValue("vsp", ref dtmp))
                    resourceVsps[nm] = dtmp;
                if (n.TryGetValue("conductivity", ref dtmp))
                    resourceConductivities[nm] = dtmp;
            }

            ConfigNode node = GameDatabase.Instance.GetConfigNodes("MFSSETTINGS").LastOrDefault();
            Debug.Log ("[MFS] Loading global settings");

			if (node != null) {
                node.TryGetValue("useRealisticMass", ref useRealisticMass);
                node.TryGetValue("tankMassMultiplier", ref tankMassMultiplier);
                node.TryGetValue("baseCostPV", ref baseCostPV);
                node.TryGetValue("partUtilizationDefault", ref partUtilizationDefault);
                node.TryGetValue("partUtilizationTweakable", ref partUtilizationTweakable);
                node.TryGetValue("unitLabel", ref unitLabel);
                node.TryGetValue("basemassUseTotalVolume", ref basemassUseTotalVolume);
                node.TryGetValue("radiatorMinTempMult", ref radiatorMinTempMult);
                node.TryGetValue("previewAllLockedTypes", ref previewAllLockedTypes);

                ConfigNode ignoreNode = node.GetNode("IgnoreFuelsForFill");
				if (ignoreNode != null) {
					foreach (ConfigNode.Value v in ignoreNode.values) {
                        ignoreFuelsForFill.Add(v.name);
					}
				}
			}

            foreach (ConfigNode defNode in GameDatabase.Instance.GetConfigNodes("TANK_DEFINITION")) {
                if (tankDefinitions.ContainsKey(defNode.GetValue("name"))) {
                    Debug.LogWarning ("[MFS] Ignored duplicate definition of tank type " + defNode.GetValue ("name"));
                } else {
                    var def = new Tanks.TankDefinition(defNode);
                    tankDefinitions.Add(def.name, def);
				}
            }
            Initialized = true;
        }
    }
}
