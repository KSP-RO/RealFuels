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

        public double configEntryCostMultiplier = 20d;
        public double configScienceCostMultiplier = 0d;

        public double techLevelEntryCostFraction = 0.1d;
        public double techLevelScienceEntryCostFraction = 0d;

        public double configCostToScienceMultiplier = 0.1d;

        public bool usePartNameInConfigUnlock = true;

        public ConfigNode techLevels = null;

        public List<string> instantThrottleProps;

        public Dictionary<string, List<ConfigNode>> engineConfigs = null;

        public float EngineMassMultiplier
        {
            get { return useRealisticMass ? 1f : engineMassMultiplier; }
        }

        private static RFSettings _instance;
        public static RFSettings Instance
        {
            get
            {
                // no longer destroy on scene reload
                // because we need to store configuration data because of stupid serialization issues
                if (_instance != null && _instance)
                    return _instance;

                //Debug.Log("*MHE* Loading settings");

                GameObject gameObject = new GameObject(typeof(RFSettings).FullName);
                _instance = gameObject.AddComponent<RFSettings>();
                UnityEngine.Object.DontDestroyOnLoad(_instance);
                UnityEngine.Object.DontDestroyOnLoad(gameObject);
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
            if(engineConfigs == null)
                engineConfigs = new Dictionary<string, List<ConfigNode>>();

            ConfigNode node = GameDatabase.Instance.GetConfigNodes("RFSETTINGS").Last();
            
            Debug.Log("*RF* Loading RFSETTINGS global settings");

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

            if (node.HasValue("configEntryCostMultiplier"))
                double.TryParse(node.GetValue("configEntryCostMultiplier"), out configEntryCostMultiplier);

            if (node.HasValue("techLevelEntryCostFraction"))
                double.TryParse(node.GetValue("techLevelEntryCostFraction"), out techLevelEntryCostFraction);

            if (node.HasValue("configScienceCostMultiplier"))
                double.TryParse(node.GetValue("configScienceCostMultiplier"), out configScienceCostMultiplier);

            if (node.HasValue("techLevelScienceEntryCostFraction"))
                double.TryParse(node.GetValue("techLevelScienceEntryCostFraction"), out techLevelScienceEntryCostFraction);

            if (node.HasValue("configCostToScienceMultiplier"))
                double.TryParse(node.GetValue("configCostToScienceMultiplier"), out configCostToScienceMultiplier);

            if (node.HasValue("usePartNameInConfigUnlock"))
                bool.TryParse(node.GetValue("usePartNameInConfigUnlock"), out usePartNameInConfigUnlock);
        }
    }

}
