using System;
using System.Collections.Generic;
using UnityEngine;

namespace RealFuels
{
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class ModuleInfoUpdater : MonoBehaviour
    {
        private bool hasRun = false;
        
        private void Update()
        {
            if (hasRun)
            {
                GameObject.Destroy(this);
                return;
            }

            hasRun = true;
            
            Debug.Log("[RealFuelsFilters] Updating info boxes");
            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
            {
                // We need to keep the modules and the moduleInfos in sync
                // so we store the last info outside the loop
                int i = 0;

                // Loop has two termination conditions, in case there aren't enough infos for the modules.
                for (int m = 0; m < ap.partPrefab.Modules.Count && i < ap.moduleInfos.Count; ++m)
                {
                    if (ap.partPrefab.Modules[m] is ModuleEngineConfigsBase mec)
                    {
                        for(; i < ap.moduleInfos.Count; ++i)
                        {
                            var mInfo = ap.moduleInfos[i];
                            if (mInfo == null)
                                continue;
                                
                            if (mInfo.moduleName.Equals("Engine Configs"))
                            {
                                mInfo.info = mec.GetInfo();
                                ++i; // advance to next info box
                                break;
                            }
                        }
                    }
                }
            }
        }
    }

    public class ConfigFilters
    {
        public Dictionary<string, Func<ConfigNode, bool>> configDisplayFilters;

        public ConfigFilters()
        {
            this.configDisplayFilters = new Dictionary<string, Func<ConfigNode, bool>>();
        }


        public List<ConfigNode> FilterDisplayConfigs(List<ConfigNode> configs)
        {
            return FilterConfigs(configs, configDisplayFilters);
        }

        private List<ConfigNode> FilterConfigs(List<ConfigNode> configs, Dictionary<string, Func<ConfigNode, bool>> filters)
        {
            List<ConfigNode> filteredConfigs = new List<ConfigNode>(configs);
            foreach (var filter in filters)
            {
                int count = filteredConfigs.Count;
                while(count-- > 0)
                {
                    if (!filter.Value(filteredConfigs[count]))
                        filteredConfigs.RemoveAt(count);
                }
            }
            return filteredConfigs;
        }

        private static ConfigFilters _instance;
        public static ConfigFilters Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new ConfigFilters();
                return _instance;
            }
        }
    }
}
