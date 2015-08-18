using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RealFuels
{
    public class TLUpgrade : IConfigNode
    {
        public string name;
        public int currentTL = 0;
        public double techLevelEntryCost = 0d;
        public double techLevelSciEntryCost = 0d;
        public TLUpgrade(string n, int cTL, double ec, double sec)
        {
            name = n;
            currentTL = cTL;
            techLevelEntryCost = ec;
            techLevelSciEntryCost = sec;
        }
        /// <summary>
        ///  ONLY use when loading from sfs!
        /// </summary>
        /// <param name="node"></param>
        public TLUpgrade(ConfigNode node)
        {
            Load(node);
        }
        public TLUpgrade(ConfigNode node, ModuleEngineConfigs mec)
        {
            techLevelEntryCost = 0d;
            techLevelSciEntryCost = 0d;

            if (node.HasValue("name"))
            {
                bool calc = true, sciCalc = true;
                string cfgName = node.GetValue("name");

                calc = !node.TryGetValue("entryCost", ref techLevelEntryCost);

                sciCalc = !node.TryGetValue("sciEntryCost", ref techLevelSciEntryCost);
                
                if (mec.part.partInfo != null)
                {
                    double configCost = 0d;
                    node.TryGetValue("cost", ref configCost);

                    if (calc)
                    {
                        // calculate from what we know
                        techLevelEntryCost += configCost * RFSettings.Instance.configEntryCostMultiplier;
                    }
                    if (sciCalc)
                    {
                        techLevelSciEntryCost += configCost * RFSettings.Instance.configScienceCostMultiplier;
                    }

                    techLevelEntryCost += mec.part.partInfo.entryCost;
                    techLevelSciEntryCost += mec.part.partInfo.entryCost * RFSettings.Instance.configScienceCostMultiplier;
                }
                techLevelEntryCost = Math.Max(0d, techLevelEntryCost * RFSettings.Instance.techLevelEntryCostFraction);
                techLevelSciEntryCost = Math.Max(0d, techLevelSciEntryCost * RFSettings.Instance.techLevelScienceEntryCostFraction);

                currentTL = mec.techLevel;

                Load(node); // override the values if we have the real values.

                name = Utilities.GetPartName(mec.part) + cfgName;
            }
        }
        public void Load(ConfigNode node)
        {
            node.TryGetValue("name", ref name);

            node.TryGetValue("currentTL", ref currentTL);

            node.TryGetValue("techLevelEntryCost", ref techLevelEntryCost);
            node.TryGetValue("techLevelSciEntryCost", ref techLevelSciEntryCost);
        }
        public void Save(ConfigNode node)
        {
            node.AddValue("name", name);
            node.AddValue("currentTL", currentTL);
        }
    }
}
