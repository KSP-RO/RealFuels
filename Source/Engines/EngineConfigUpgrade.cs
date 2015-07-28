using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RealFuels
{
    public class EngineConfigUpgrade : IConfigNode
    {
        #region Fields
        public string name;
        public string techRequired = "";
        public bool unlocked = false;
        public double entryCost = 0d;
        public double sciEntryCost = 0d;
        public double maxSubtraction = double.MaxValue;
        public Dictionary<string, double> entryCostMultipliers = new Dictionary<string, double>();
        public Dictionary<string, double> entryCostSubtractors = new Dictionary<string, double>();
        #endregion

        #region Constructors
        public EngineConfigUpgrade(ConfigNode node, string Name = "")
        {
            Load(node);
            if(Name != "")
                name = Name;
        }
        #endregion

        #region Methods
        public void LoadMultipliers(ConfigNode node)
        {
            if (node == null)
                return;

            double dtmp;
            foreach (ConfigNode.Value v in node.values)
            {
                if (double.TryParse(v.value, out dtmp))
                    entryCostMultipliers[v.name] = dtmp;
            }
        }
        public void LoadSubtractors(ConfigNode node)
        {
            if (node == null)
                return;

            double dtmp;
            foreach (ConfigNode.Value v in node.values)
            {
                if (double.TryParse(v.value, out dtmp))
                    entryCostSubtractors[v.name] = dtmp;
            }
        }

        #region ConfigNode methods
        public void Load(ConfigNode node)
        {
            unlocked = false;

            node.TryGetValue("name", ref name);

            double cost = 0d;
            node.TryGetValue("cost", ref cost);

            if (!node.TryGetValue("entryCost", ref entryCost))
                entryCost = Math.Max(0d, cost * RFSettings.Instance.configEntryCostMultiplier);

            if (!node.TryGetValue("sciEntryCost", ref sciEntryCost))
                sciEntryCost = Math.Max(0d, cost * RFSettings.Instance.configScienceCostMultiplier);

            node.TryGetValue("unlocked", ref unlocked);

            node.TryGetValue("techRequired", ref techRequired);

            if (node.HasNode("entryCostMultipliers"))
                LoadMultipliers(node.GetNode("entryCostMultipliers"));

            if (node.HasNode("entryCostSubtractors"))
                LoadSubtractors(node.GetNode("entryCostSubtractors"));

            node.TryGetValue("maxSubtraction", ref maxSubtraction);
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("name", name);

            node.AddValue("unlocked", unlocked);
        }
        #endregion

        protected double ModCost(double cost, double subtractMultipler = 1.0d)
        {
            double subtract = 0d;

            foreach (KeyValuePair<string, double> kvp in entryCostSubtractors)
            {
                if (RFUpgradeManager.Instance.ConfigUnlocked(kvp.Key))
                    subtract += kvp.Value * subtractMultipler;
            }
            cost -= Math.Min(subtract, maxSubtraction);

            foreach (KeyValuePair<string, double> kvp in entryCostMultipliers)
            {
                if (RFUpgradeManager.Instance.ConfigUnlocked(kvp.Key))
                    cost *= kvp.Value;
            }

            if (cost > 0d)
                return cost;

            return 0d;
        }
        public double EntryCost()
        {
            return ModCost(entryCost);
        }
        public double SciEntryCost()
        {
            return ModCost(sciEntryCost, RFSettings.Instance.configCostToScienceMultiplier);
        }
        #endregion
    }
}
