using System;
using System.Collections.Generic;
using UnityEngine;

namespace RealFuels
{
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class ModuleShowInfoUpdater : MonoBehaviour
    {
        protected bool run = true;
        private void Update()
        {
            if (run)
            {
                Debug.Log("[RealFuelsFilters] Updated info boxes");
                foreach (AvailablePart ap in PartLoader.LoadedPartsList)
                {
                    // workaround for FindModulesImplementing nullrefs when called on the strange kerbalEVA_RD_Exp prefab
                    // due to the (private) cachedModuleLists being null on it
                    if (ap.partPrefab.Modules.Count == 0)
                        continue;
                    if (ap.partPrefab.FindModulesImplementing<ModuleEngineConfigs>() is List<ModuleEngineConfigs> mecs)
                    {
                        int i = 0;
                        foreach (AvailablePart.ModuleInfo x in ap.moduleInfos)
                        {
                            if (x.moduleName.Equals("Engine Configs"))
                            {
                                x.info = mecs[i++].GetInfo();
                            }
                        }
                    }
                }

                run = false;
                GameObject.Destroy(this);
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