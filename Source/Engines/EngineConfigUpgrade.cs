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
            double dtmp;
            bool btmp;
            unlocked = false;

            double cost = 0d;
            if (node.HasValue("cost"))
                double.TryParse(node.GetValue("cost"), out cost);

            if (node.HasValue("name"))
                name = node.GetValue("name");

            if (node.HasValue("entryCost"))
            {
                if (double.TryParse(node.GetValue("entryCost"), out dtmp))
                    entryCost = dtmp;
            }
            else
            {
                entryCost = Math.Max(0d, cost * RFSettings.Instance.configEntryCostMultiplier);
            }
            if (node.HasValue("sciEntryCost"))
            {
                if (double.TryParse(node.GetValue("sciEntryCost"), out dtmp))
                    sciEntryCost = dtmp;
            }
            else
            {
                sciEntryCost = Math.Max(0d, cost * RFSettings.Instance.configScienceCostMultiplier);
            }

            if (node.HasValue("unlocked"))
                if (bool.TryParse(node.GetValue("unlocked"), out btmp))
                    unlocked = btmp;

            if (node.HasValue("techRequired"))
                techRequired = node.GetValue("techRequired");

            if (node.HasNode("entryCostMultipliers"))
                LoadMultipliers(node.GetNode("entryCostMultipliers"));

            if (node.HasNode("entryCostSubtractors"))
                LoadSubtractors(node.GetNode("entryCostSubtractors"));
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("name", name);

            node.AddValue("entryCost", entryCost.ToString("G17"));
            node.AddValue("sciEntryCost", sciEntryCost.ToString("G17"));

            node.AddValue("unlocked", unlocked);

            if (techRequired != "")
                node.AddValue("techRequired", techRequired);

            if (entryCostMultipliers.Keys.Count > 0)
            {
                ConfigNode n = new ConfigNode("entryCostMultipliers");
                foreach (KeyValuePair<string, double> kvp in entryCostMultipliers)
                    n.AddValue(kvp.Key, kvp.Value.ToString("G17"));
                node.AddNode(n);
            }
            if (entryCostSubtractors.Keys.Count > 0)
            {
                ConfigNode n = new ConfigNode("entryCostSubtractors");
                foreach (KeyValuePair<string, double> kvp in entryCostSubtractors)
                    n.AddValue(kvp.Key, kvp.Value.ToString("G17"));
                node.AddNode(n);
            }
        }
        #endregion

        protected double ModCost(double cost, double subtractMultipler = 1.0d)
        {
            foreach (KeyValuePair<string, double> kvp in entryCostMultipliers)
            {
                if (RFUpgradeManager.Instance.ConfigUnlocked(kvp.Key))
                    cost *= kvp.Value;
            }

            foreach (KeyValuePair<string, double> kvp in entryCostSubtractors)
            {
                if (RFUpgradeManager.Instance.ConfigUnlocked(kvp.Key))
                    cost -= kvp.Value * subtractMultipler;
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
