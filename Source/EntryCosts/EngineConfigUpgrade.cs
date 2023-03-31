using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RealFuels
{
    public class EngineConfigUpgrade
    {
        #region Fields

        public string name;
        public string techRequired = string.Empty;
        
        #endregion

        #region Constructors

        public EngineConfigUpgrade(ConfigNode node, string Name = "")
        {
            Load(node);
            if(Name != string.Empty)
                name = Name;

            name = Utilities.GetPartName(Name);
        }

        #endregion

        #region Methods

        #region ConfigNode methods
        public void Load(ConfigNode node)
        {
            node.TryGetValue("name", ref name);

            double cost = 0d;
            node.TryGetValue("cost", ref cost);

            node.TryGetValue("techRequired", ref techRequired);
        }

        #endregion

        public double EntryCost()
        {
            return EntryCostDatabase.GetCost(name);
        }

        #endregion
    }
}
