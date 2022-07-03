using System.Collections.Generic;
using System.Linq;

namespace RealFuels.Tanks
{
    public class TankDefinition : IConfigNode
    {
        [Persistent]
        public string name;

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


        public TankDefinition() { }

        public TankDefinition(ConfigNode node)
        {
            Load(node);
        }

        public void Load(ConfigNode node)
        {
            if (! (node.name.Equals ("TANK_DEFINITION") && node.HasValue ("name"))) {
                return;
            }

            ConfigNode.LoadObjectFromConfig(this, node);
            foreach (ConfigNode tankNode in node.GetNodes("TANK"))
            {
                string name = "";
                if (node.TryGetValue("name", ref name) && !tankList.ContainsKey(name))
                    tankList.Add(name, new FuelTank(tankNode));
            }
            foreach (var t in tankList.Where(x => !x.Value.resourceAvailable).ToList())
                tankList.Remove(t.Key);
        }

        public void Save (ConfigNode node)
        {
            ConfigNode.CreateConfigFromObject(this, node);
            foreach (FuelTank tank in tankList.Values)
            {
                ConfigNode tankNode = new ConfigNode("TANK");
                tank.Save(tankNode);
                node.AddNode(tankNode);
            }
        }
    }
}
