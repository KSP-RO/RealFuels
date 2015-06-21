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

        public double varyThrust = 0d;

        public bool usePartNameInConfigUnlock = true;

        public ConfigNode techLevels = null;

        public List<string> instantThrottleProps;

        #region Ullage
        public bool simulateUllage = true;
        public bool shutdownEngineWhenUnstable = true;
        public bool explodeEngineWhenTooUnstable = false;

        public double naturalDiffusionRateX = 0.02d;
        public double naturalDiffusionRateY = 0.03d;

        public double translateAxialCoefficientX = 0.06d;
        public double translateAxialCoefficientY = 0.06d;

        public double translateSidewayCoefficientX = 0.04d;
        public double translateSidewayCoefficientY = 0.02d;

        public double rotateYawPitchCoefficientX = 0.003d;
        public double rotateYawPitchCoefficientY = 0.004d;

        public double rotateRollCoefficientX = 0.005d;
        public double rotateRollCoefficientY = 0.006d;

        public double ventingVelocity = 100.0d;
        public double ventingAccThreshold = 0.00000004d;
        #endregion

        // storage
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
            node.TryGetValue("engineMassMultiplier", out engineMassMultiplier);
            node.TryGetValue("heatMultiplier", out heatMultiplier);
            node.TryGetValue("useRealisticMass", out useRealisticMass);
            node.TryGetValue("varyThrust", out varyThrust);

            if (node.HasNode("RF_TECHLEVELS"))
                techLevels = node.GetNode("RF_TECHLEVELS");

            instantThrottleProps = new List<string>();
            if (node.HasNode("instantThrottleProps"))
                foreach (ConfigNode.Value val in node.GetNode("instantThrottleProps").values)
                    instantThrottleProps.Add(val.value);
            
            // Upgrade costs
            node.TryGetValue("configEntryCostMultiplier", out configEntryCostMultiplier);
            node.TryGetValue("techLevelEntryCostFraction", out techLevelEntryCostFraction);
            node.TryGetValue("configScienceCostMultiplier", out configScienceCostMultiplier);
            node.TryGetValue("techLevelScienceEntryCostFraction", out techLevelScienceEntryCostFraction);
            node.TryGetValue("configCostToScienceMultiplier", out configCostToScienceMultiplier);
            node.TryGetValue("usePartNameInConfigUnlock", out usePartNameInConfigUnlock);

            #region Ullage
            if (node.HasNode("Ullage"))
            {
                ConfigNode n2 = node.GetNode("Ullage");
                n2.TryGetValue("simulateUllage", ref simulateUllage);
                n2.TryGetValue("shutdownEngineWhenUnstable", ref shutdownEngineWhenUnstable);
                n2.TryGetValue("explodeEngineWhenTooUnstable", ref explodeEngineWhenTooUnstable);

                n2.TryGetValue("naturalDiffusionRateX", ref naturalDiffusionRateX);
                n2.TryGetValue("naturalDiffusionRateY", ref naturalDiffusionRateY);

                n2.TryGetValue("translateAxialCoefficientX", ref translateAxialCoefficientX);
                n2.TryGetValue("translateAxialCoefficientY", ref translateAxialCoefficientY);

                n2.TryGetValue("translateSidewayCoefficientX", ref translateSidewayCoefficientX);
                n2.TryGetValue("translateSidewayCoefficientY", ref translateSidewayCoefficientY);

                n2.TryGetValue("rotateYawPitchCoefficientX", ref rotateYawPitchCoefficientX);
                n2.TryGetValue("rotateYawPitchCoefficientY", ref rotateYawPitchCoefficientY);

                n2.TryGetValue("rotateRollCoefficientX", ref rotateRollCoefficientX);
                n2.TryGetValue("rotateRollCoefficientY", ref rotateRollCoefficientY);

                n2.TryGetValue("ventingVelocity", ref ventingVelocity);
                n2.TryGetValue("ventingAccThreshold", ref ventingAccThreshold);
            }
            #endregion
        }
    }

}
