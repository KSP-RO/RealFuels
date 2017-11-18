using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RealFuels;
using TestFlight;
using TestFlightAPI;

namespace TestFlightRF
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class TestFlightRealFuels : MonoBehaviour
    {
        IEnumerator Setup()
        {
            if (!PartLoader.Instance.IsReady() || PartResourceLibrary.Instance == null)
            {
                yield return null;
            }
            Startup();
        }

        public void Startup()
        {
            var burnTimes = new Dictionary<string, float>();
            foreach (AvailablePart part in PartLoader.LoadedPartsList)
            {
                // cache up the burn times first
                burnTimes.Clear();
                var engineCycles = part.partPrefab.Modules.GetModules<TestFlightReliability_EngineCycle>();
                if (engineCycles.Count <= 0)
                    continue;

                foreach (var engineCycle in engineCycles)
                {
                    if (engineCycle.engineConfig != "")
                    {
                        burnTimes[engineCycle.engineConfig] = engineCycle.ratedBurnTime;
                    }
                }
                // now add that info to the RF configs
                var allConfigs = part.partPrefab.Modules.GetModules<ModuleEngineConfigs>();
                if (allConfigs.Count <= 0)
                    continue;

                foreach (ModuleEngineConfigs mec in allConfigs)
                {
                    List<ConfigNode> configs = mec.configs;
                    foreach (ConfigNode node in configs)
                    {
                        if (node.HasValue("name"))
                        {
                            string configName = node.GetValue("name");
                            if (burnTimes.ContainsKey(configName))
                            {
                                if (node.HasValue("description"))
                                {
                                    string description = node.GetValue("description");
                                    description += String.Format("\n" + "    " + "<b>Rated Burn Time</b>: {0}", TestFlightUtil.FormatTime(burnTimes[configName], TestFlightUtil.TIMEFORMAT.SHORT_IDENTIFIER, true));
                                    node.SetValue("description", description, true);
                                }
                                else
                                {
                                    node.AddValue("description", String.Format("\n<b>Rated Burn Time</b>: {0}", TestFlightUtil.FormatTime(burnTimes[configName], TestFlightUtil.TIMEFORMAT.SHORT_IDENTIFIER, true)));
                                }
                            }
                        }
                    }
                    mec.SetConfiguration();
                }
            }
        }

        public void Start()
        {
            StartCoroutine("Setup");
        }
    }
}

