using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;

namespace RealFuels
{
    // ReSharper disable once InconsistentNaming
    public class RFSettings : MonoBehaviour
    {
        
        private float engineMassMultiplier = 1;
        
        public float heatMultiplier = 1;
        
        public bool useRealisticMass = true;

        public ConfigNode techLevels = null;

        public List<string> instantThrottleProps;

        public float EngineMassMultiplier
        {
            get { return useRealisticMass ? 1f : engineMassMultiplier; }
        }

        private static RFSettings _instance;
        public static RFSettings Instance
        {
            get
            {
                // Will get destroyed on scene load, which is what we want 
                // because this means the DB will get reloaded.
                if (_instance != null && _instance)
                    return _instance;

                //Debug.Log("*MHE* Loading settings");

                GameObject gameObject = new GameObject(typeof(RFSettings).FullName);
                _instance = gameObject.AddComponent<RFSettings>();
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
            ConfigNode node = GameDatabase.Instance.GetConfigNodes("RFSETTINGS").Last();
            Debug.Log("*RF* Loading engine global settings");

            if (node == null)
                throw new UnityException("*RF* Could not find RF global settings!");

            // parse values
            if (node.HasValue("engineMassMultiplier"))
                float.TryParse(node.GetValue("engineMassMultiplier"), out engineMassMultiplier);

            if (node.HasValue("heatMultiplier"))
                float.TryParse(node.GetValue("heatMultiplier"), out heatMultiplier);

            if (node.HasValue("useRealisticMass"))
                bool.TryParse(node.GetValue("useRealisticMass"), out useRealisticMass);

            if (node.HasNode("RF_TECHLEVELS"))
                techLevels = node.GetNode("RF_TECHLEVELS");

            instantThrottleProps = new List<string>();
            if (node.HasNode("instantThrottleProps"))
                foreach (ConfigNode.Value val in node.GetNode("instantThrottleProps").values)
                    instantThrottleProps.Add(val.value);
        }
    }

}
