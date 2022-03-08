using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;

namespace RealFuels
{
    // ReSharper disable once InconsistentNaming
    public class RFSettings
    {
        [Persistent] public float engineMassMultiplier = 1;

        [Persistent] public float heatMultiplier = 1;

        [Persistent] public bool useRealisticMass = true;

        [Persistent] public double configEntryCostMultiplier = 20d;
        [Persistent] public double configScienceCostMultiplier = 0d;

        [Persistent] public double techLevelEntryCostFraction = 0.1d;
        [Persistent] public double techLevelScienceEntryCostFraction = 0d;

        [Persistent] public double configCostToScienceMultiplier = 0.1d;

        [Persistent] public double varianceAndResiduals = 0d;

        [Persistent] public bool usePartNameInConfigUnlock = true;

        public ConfigNode techLevels = null;

        public List<string> instantThrottleProps;
        [Persistent] public double throttlingRate = 10d;
        [Persistent] public double throttlingClamp = 1.1d;

        [Persistent] public bool ferociousBoilOff = false;
        [Persistent] public bool globalConductionCompensation = false;
        [Persistent] public bool debugBoilOff = false;
        [Persistent] public bool debugBoilOffPAW = true;
        [Persistent] public double QvCoefficient = 0.0028466; // convective coefficient for Real Fuels MLI calculations
        [Persistent] public double analyticInsulationMultiplier = 1;

        public List<string> Pressurants;

        #region Ullage
        [Persistent] public bool simulateUllage = true;
        [Persistent] public bool limitedIgnitions = true;
        [Persistent] public bool shutdownEngineWhenUnstable = true;
        [Persistent] public bool explodeEngineWhenTooUnstable = false;
        [Persistent] public double stabilityPower = 0.03d;

        [Persistent] public double naturalDiffusionRateX = 0.02d;
        [Persistent] public double naturalDiffusionRateY = 0.03d;
        [Persistent] public double naturalDiffusionAccThresh = 0.01d;

        [Persistent] public double translateAxialCoefficientX = 0.06d;
        [Persistent] public double translateAxialCoefficientY = 0.06d;

        [Persistent] public double translateSidewayCoefficientX = 0.04d;
        [Persistent] public double translateSidewayCoefficientY = 0.02d;

        [Persistent] public double rotateYawPitchCoefficientX = 0.003d;
        [Persistent] public double rotateYawPitchCoefficientY = 0.004d;

        [Persistent] public double rotateRollCoefficientX = 0.005d;
        [Persistent] public double rotateRollCoefficientY = 0.006d;

        [Persistent] public double ventingVelocity = 100.0d;
        [Persistent] public double ventingAccThreshold = 0.00000004d;
        #endregion

        // storage
        public Dictionary<string, List<ConfigNode>> engineConfigs = new Dictionary<string, List<ConfigNode>>(64);

        public float EngineMassMultiplier => useRealisticMass ? 1f : engineMassMultiplier;

        private static RFSettings _instance;
        public static RFSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new RFSettings();
                    _instance.Init();
                }
                return _instance;
            }
        }

        private static string version;
        public static string GetVersion()
        {
            if (version == null)
            {
                var asm = Assembly.GetCallingAssembly();
                version = $"{MFSVersionReport.GetAssemblyTitle(asm)} {MFSVersionReport.GetAssemblyVersionString(asm)}";
            }
            return version;
        }

        private void Init()
        {
            ConfigNode node = GameDatabase.Instance.GetConfigNodes("RFSETTINGS").Last();
            
            Debug.Log("*RF* Loading RFSETTINGS global settings");

            if (node == null)
                throw new UnityException("*RF* Could not find RF global settings!");

            // parse values
            ConfigNode.LoadObjectFromConfig(this, node);

            if (node.HasNode("RF_TECHLEVELS"))
                techLevels = node.GetNode("RF_TECHLEVELS");
            instantThrottleProps = node.HasNode("instantThrottleProps") ? node.GetNode("instantThrottleProps").GetValuesList("val") : new List<string>();
            Pressurants = node.HasNode("Pressurants") ? node.GetNode("Pressurants").GetValuesList("val") : new List<string>();

            if (node.HasNode("Ullage"))
                ConfigNode.LoadObjectFromConfig(this, node.GetNode("Ullage"));
        }
    }
}
