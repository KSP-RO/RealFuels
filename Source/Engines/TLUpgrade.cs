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
                bool calc = true;
                if (node.HasValue("entryCost"))
                    if (double.TryParse(node.GetValue("entryCost"), out techLevelEntryCost))
                        calc = false;
                if (mec.part.partInfo != null)
                {
                    if (calc)
                    {
                        // calculate from what we know
                        double configEntryCost = 0d;
                        if (node.HasValue("cost"))
                            double.TryParse(node.GetValue("cost"), out configEntryCost);
                        techLevelEntryCost += configEntryCost * RFSettings.Instance.configEntryCostMultiplier;
                    }
                    techLevelEntryCost += mec.part.partInfo.entryCost;
                }
                techLevelSciEntryCost = RFSettings.Instance.techLevelScienceEntryCostFraction * techLevelEntryCost;
                techLevelEntryCost *= RFSettings.Instance.techLevelEntryCostFraction;

                Load(node); // override the values if we have the real values.
            }
        }
        public void Load(ConfigNode node)
        {
            int itmp;
            double dtmp;

            if (node.HasValue("name"))
                name = node.GetValue("name");

            if (node.HasValue("currentTL"))
                if (int.TryParse(node.GetValue("currentTL"), out itmp))
                    currentTL = itmp;

            if (node.HasValue("techLevelEntryCost"))
            {
                if (double.TryParse(node.GetValue("techLevelEntryCost"), out dtmp))
                    techLevelEntryCost = dtmp;
            }
            if (node.HasValue("techLevelSciEntryCost"))
            {
                if (double.TryParse(node.GetValue("techLevelSciEntryCost"), out dtmp))
                    techLevelSciEntryCost = dtmp;
            }
        }
        public void Save(ConfigNode node)
        {
            node.AddValue("name", name);
            node.AddValue("currentTL", currentTL);
            node.AddValue("techLevelEntryCost", techLevelEntryCost.ToString("G17"));
            node.AddValue("techLevelSciEntryCost", techLevelSciEntryCost.ToString("G17"));
        }
    }
}
