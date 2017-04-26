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
        protected Dictionary<string, float> burnTimes = null;

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
            burnTimes = new Dictionary<string, float>();
            foreach (AvailablePart part in PartLoader.LoadedPartsList)
            {
                // cache up the burn times first
                List<ITestFlightReliability> engineCycles = new List<ITestFlightReliability>();
                burnTimes.Clear();
                foreach (PartModule pm in part.partPrefab.Modules)
                {
                    ITestFlightReliability reliabilityModule = pm as ITestFlightReliability;
                    if (reliabilityModule != null)
                        engineCycles.Add(reliabilityModule);
                }
                if (engineCycles.Count <= 0)
                    continue;

                foreach (ITestFlightReliability rm in engineCycles)
                {
                    TestFlightReliability_EngineCycle engineCycle = rm as TestFlightReliability_EngineCycle;
                    if (engineCycle != null)
                    {
                        if (engineCycle.engineConfig != "")
                        {
                            burnTimes[engineCycle.engineConfig] = engineCycle.ratedBurnTime;
                        }
                    }
                }
                // now add that info to the RF configs
                List<ModuleEngineConfigs> allConfigs = new List<ModuleEngineConfigs>();
                allConfigs.AddRange(part.partPrefab.Modules.GetModules<ModuleEngineConfigs>());
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

