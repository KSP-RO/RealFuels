using System;
using System.Collections.Generic;
using UnityEngine;

namespace RealFuels
{
    public class ConfigFilters : MonoBehaviour
    {
        public List<Func<ConfigNode, bool>> configDisplayFilters;

        public List<ConfigNode> FilterDisplayConfigs(List<ConfigNode> configs)
        {
            return FilterConfigs(configs, configDisplayFilters);
        }

        public List<ConfigNode> FilterConfigs(List<ConfigNode> configs, List<Func<ConfigNode,bool>> filters)
        {
            List<ConfigNode> filteredConfigs = new List<ConfigNode>(configs);
            foreach (var filter in filters)
            {
                int count = filteredConfigs.Count;
                while(count-- > 0)
                {
                    if (!filter(filteredConfigs[count]))
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
                if (_instance != null && _instance)
                    return _instance;

                GameObject gameObject = new GameObject(typeof(ConfigFilters).FullName);
                _instance = gameObject.AddComponent<ConfigFilters>();
                UnityEngine.Object.DontDestroyOnLoad(_instance);
                UnityEngine.Object.DontDestroyOnLoad(gameObject);
                return _instance;
            }
        }
    }
}