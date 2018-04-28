using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Linq;
using UnityEngine;

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
        public double throttlingRate = 10d;
        public double throttlingClamp = 1.1d;

        public bool ferociousBoilOff = false;
        public bool globalConductionCompensation = false;
        public bool debugBoilOff = false;
        public double QvCoefficient = 3.65; // convective coefficient for Real Fuels MLI calculations
        public double analyticInsulationMultiplier = 0.001;

        #region Ullage
        public bool simulateUllage = true;
        public bool limitedIgnitions = true;
        public bool shutdownEngineWhenUnstable = true;
        public bool explodeEngineWhenTooUnstable = false;
        public double stabilityPower = 0.03d;

        public double naturalDiffusionRateX = 0.02d;
        public double naturalDiffusionRateY = 0.03d;
        public double naturalDiffusionAccThresh = 0.01d;

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
            var title = MFSVersionReport.GetAssemblyTitle (asm);
            version = title + " " + MFSVersionReport.GetAssemblyVersionString (asm);

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
            node.TryGetValue("engineMassMultiplier", ref engineMassMultiplier);
            node.TryGetValue("heatMultiplier", ref heatMultiplier);
            node.TryGetValue("useRealisticMass", ref useRealisticMass);
            node.TryGetValue("varyThrust", ref varyThrust);

            if (node.HasNode("RF_TECHLEVELS"))
                techLevels = node.GetNode("RF_TECHLEVELS");

            // Throttling
            instantThrottleProps = new List<string>();
            if (node.HasNode("instantThrottleProps"))
                foreach (ConfigNode.Value val in node.GetNode("instantThrottleProps").values)
                    instantThrottleProps.Add(val.value);

            node.TryGetValue("throttlingRate", ref throttlingRate);
            node.TryGetValue("throttlingClamp", ref throttlingClamp);

            // Upgrade costs
            node.TryGetValue("configEntryCostMultiplier", ref configEntryCostMultiplier);
            node.TryGetValue("techLevelEntryCostFraction", ref techLevelEntryCostFraction);
            node.TryGetValue("configScienceCostMultiplier", ref configScienceCostMultiplier);
            node.TryGetValue("techLevelScienceEntryCostFraction", ref techLevelScienceEntryCostFraction);
            node.TryGetValue("configCostToScienceMultiplier", ref configCostToScienceMultiplier);
            node.TryGetValue("usePartNameInConfigUnlock", ref usePartNameInConfigUnlock);
            node.TryGetValue("ferociousBoilOff", ref ferociousBoilOff);
            node.TryGetValue("globalConductionCompensation", ref globalConductionCompensation);
            node.TryGetValue("debugBoilOff", ref debugBoilOff);
            node.TryGetValue("QvCoefficient", ref QvCoefficient);
            node.TryGetValue("analyticInsulationMultiplier", ref analyticInsulationMultiplier);

            #region Ullage
            if (node.HasNode("Ullage"))
            {
                ConfigNode ullageNode = node.GetNode("Ullage");

                ullageNode.TryGetValue("simulateUllage", ref simulateUllage);
                ullageNode.TryGetValue("limitedIgnitions", ref limitedIgnitions);
                ullageNode.TryGetValue("shutdownEngineWhenUnstable", ref shutdownEngineWhenUnstable);
                ullageNode.TryGetValue("explodeEngineWhenTooUnstable", ref explodeEngineWhenTooUnstable);
                ullageNode.TryGetValue("stabilityPower", ref stabilityPower);

                ullageNode.TryGetValue("naturalDiffusionRateX", ref naturalDiffusionRateX);
                ullageNode.TryGetValue("naturalDiffusionRateY", ref naturalDiffusionRateY);
                ullageNode.TryGetValue("naturalDiffusionAccThresh", ref naturalDiffusionAccThresh);

                ullageNode.TryGetValue("translateAxialCoefficientX", ref translateAxialCoefficientX);
                ullageNode.TryGetValue("translateAxialCoefficientY", ref translateAxialCoefficientY);

                ullageNode.TryGetValue("translateSidewayCoefficientX", ref translateSidewayCoefficientX);
                ullageNode.TryGetValue("translateSidewayCoefficientY", ref translateSidewayCoefficientY);

                ullageNode.TryGetValue("rotateYawPitchCoefficientX", ref rotateYawPitchCoefficientX);
                ullageNode.TryGetValue("rotateYawPitchCoefficientY", ref rotateYawPitchCoefficientY);

                ullageNode.TryGetValue("rotateRollCoefficientX", ref rotateRollCoefficientX);
                ullageNode.TryGetValue("rotateRollCoefficientY", ref rotateRollCoefficientY);

                ullageNode.TryGetValue("ventingVelocity", ref ventingVelocity);
                ullageNode.TryGetValue("ventingAccThreshold", ref ventingAccThreshold);
            }
            #endregion
        }
    }

}
