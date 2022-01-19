using System;
using System.Collections.Generic;
using UnityEngine;

namespace RealFuels
{
    public class ConfigFilters
    {
        public class Filter
        {
            public string filterID;
            public Func<ConfigNode, bool> criteria;

            public Filter(string filterID, Func<ConfigNode, bool> criteria)
            {
                this.filterID = filterID;
                this.criteria = criteria;
            }
        }

        public class FilterList
        {
            private List<Filter> filterList;

            public FilterList()
            {
                this.filterList = new List<Filter>();
            }

            public void AddFilter(Filter filter)
            {
                if (this.filterList.Contains(filter))
                {
                    return;
                }
                this.filterList.Add(filter);
            }

            public void RemoveFilter(string id)
            {
                RemoveFilter(GetFilterFromID(id));
            }

            public void RemoveFilter(Filter filter)
            {
                if (!this.filterList.Contains(filter))
                {
                    return;
                }
                this.filterList.Remove(filter);
            }

            public Filter GetFilterFromID(string id)
            {
                foreach (var filter in this.filterList)
                {
                    if (filter.filterID == id)
                    {
                        return filter;
                    }
                }
                return null;
            }

            public List<Filter>.Enumerator GetEnumerator() => this.filterList.GetEnumerator();

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
                    if (!filter.criteria(filteredConfigs[count]))
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