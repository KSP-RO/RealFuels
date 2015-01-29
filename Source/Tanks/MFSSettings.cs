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

        public static HashSet<string> ignoreFuelsForFill;
        public static Tanks.TankDefinitionList tankDefinitions;


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

			Destroy (this);
		}

        public static void Initialize ()
        {
			ignoreFuelsForFill = new HashSet<string> ();
			tankDefinitions = new Tanks.TankDefinitionList ();

            ConfigNode node = GameDatabase.Instance.GetConfigNodes ("MFSSETTINGS").Last ();
            Debug.Log ("[MFS] Loading global settings");

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

            ConfigNode ignoreNode = node.GetNode ("IgnoreFuelsForFill");
            if (ignoreNode != null) {
                foreach (ConfigNode.Value v in ignoreNode.values) {
                    ignoreFuelsForFill.Add (v.name);
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
