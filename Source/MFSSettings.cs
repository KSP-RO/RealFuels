using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;

namespace RealFuels
{
    // ReSharper disable once InconsistentNaming
    public class MFSSettings : MonoBehaviour
    {
        [Persistent]
        public bool useRealisticMass;
        [Persistent]
        public float tankMassMultiplier = 1;

        [Persistent]
        public float engineMassMultiplier = 1;
        [Persistent]
        public float heatMultiplier = 1;

        [Persistent]
        public float baseCostPV = 0.0f;

        [Persistent]
        public float partUtilizationDefault = 86;
        [Persistent]
        public bool partUtilizationTweakable = false;


        public HashSet<string> ignoreFuelsForFill = new HashSet<string>();

        public Tanks.TankDefinitionList tankDefinitions = new Tanks.TankDefinitionList();

        // TODO: Move engine tech levels into here too.

        private static MFSSettings _instance;
        public static MFSSettings Instance
        {
            get
            {
                // Will get destroyed on scene load, which is what we want 
                // because this means the DB will get reloaded.
                if (_instance != null && _instance)
                    return _instance;

                //Debug.Log("*MFS* Loading settings");

                GameObject gameObject = new GameObject(typeof(MFSSettings).FullName);
                _instance = gameObject.AddComponent<MFSSettings>();
                return _instance;
            }
        }

        static string version;
        public static string GetVersion()
        {
            if (version != null) {
                return version;
            }

            var asm = Assembly.GetCallingAssembly ();
            var title = SystemUtils.GetAssemblyTitle (asm);
            version = title + " " + SystemUtils.GetAssemblyVersionString (asm);

            return version;
        }

        internal void Awake()
        {
            ConfigNode node = GameDatabase.Instance.GetConfigNodes("MFSSETTINGS").Last();
            Debug.Log("*MFS* Loading global settings");

            ConfigNode.LoadObjectFromConfig(this, node);

            ConfigNode ignoreNode = node.GetNode("IgnoreFuelsForFill");
            if (ignoreNode != null)
                foreach (ConfigNode.Value v in ignoreNode.values)
                    ignoreFuelsForFill.Add(v.name);
            

            foreach (ConfigNode defNode in GameDatabase.Instance.GetConfigNodes("TANK_DEFINITION"))
            {
                if(tankDefinitions.Contains(defNode.GetValue("name")))
                    Debug.LogWarning("[MFS] Ignored duplicate definition of tank type " + defNode.GetValue("name"));
                else
                    tankDefinitions.Add(new Tanks.TankDefinition(defNode));
            }
        }
    }

}
