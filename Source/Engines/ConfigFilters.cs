using System;
using System.Collections.Generic;
using UnityEngine;

namespace RealFuels
{
    public class ConfigFilters
    {
        public class FilterList
        {
            private List<Func<ConfigNode, bool>> filterList;

            public FilterList()
            {
                this.filterList = new List<Func<ConfigNode, bool>>();
            }

            public void AddFilter(Func<ConfigNode, bool> filter)
            {
                if (this.filterList.Contains(filter))
                {
                    return;
                }
                this.filterList.Add(filter);
            }

            public void RemoveFilter(Func<ConfigNode, bool> filter)
            {
                if (!this.filterList.Contains(filter))
                {
                    return;
                }
                this.filterList.Remove(filter);
            }

            public List<Func<ConfigNode, bool>>.Enumerator GetEnumerator() => this.filterList.GetEnumerator();

        }
        public FilterList configDisplayFilters;

        public ConfigFilters()
        {
            this.configDisplayFilters = new FilterList();
        }


        public List<ConfigNode> FilterDisplayConfigs(List<ConfigNode> configs)
        {
            return FilterConfigs(configs, configDisplayFilters);
        }

        public List<ConfigNode> FilterConfigs(List<ConfigNode> configs, FilterList filters)
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
                if (_instance == null)
                    return new ConfigFilters();
                return _instance;
            }
        }
    }
}