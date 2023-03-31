﻿using System.Collections.Generic;
using System.Linq;

namespace RealFuels.Tanks
{
    public class TankDefinition : IConfigNode
    {
        [Persistent]
        public string name;

        [Persistent]
        public string title;

        [Persistent]
        public string description;

        [Persistent]
        public string basemass;

        [Persistent]
        public string baseCost;

        [Persistent]
        public bool highlyPressurized = false;

        [Persistent]
        public int numberOfMLILayers = 0;

        [Persistent]
        public int maxMLILayers = -1;

        [Persistent]
        public float minUtilization = 0;

        [Persistent]
        public float maxUtilization = 0;

        public Dictionary<string, FuelTank> tankList = new Dictionary<string, FuelTank>();

        public string Title => title ?? name;

        public TankDefinition() { }

        public TankDefinition(ConfigNode node)
        {
            Load(node);
        }

        public void Load(ConfigNode node)
        {
            if (! (node.name.Equals ("TANK_DEFINITION") && node.HasValue ("name")))
                return;

            ConfigNode.LoadObjectFromConfig(this, node);
            foreach (ConfigNode tankNode in node.GetNodes("TANK"))
            {
                string name = "";
                if (tankNode.TryGetValue("name", ref name) && !tankList.ContainsKey(name))
                    tankList.Add(name, new FuelTank(tankNode));
            }
            foreach (var t in tankList.Where(x => !x.Value.resourceAvailable).ToList())
                tankList.Remove(t.Key);
        }

        public void Save(ConfigNode node) => Save(node, true);

        public void Save(ConfigNode node, bool includeEmpty)
        {
            ConfigNode.CreateConfigFromObject(this, node);
            // Don't spam save files with empty tank nodes, only save the relevant stuff
            foreach (FuelTank tank in tankList.Values.Where(t => includeEmpty || t.amount > 0 || t.maxAmount > 0))
            {
                ConfigNode tankNode = new ConfigNode("TANK");
                tank.Save(tankNode);
                node.AddNode(tankNode);
            }
        }
    }
}
