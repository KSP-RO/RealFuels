using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using System.Collections.ObjectModel;

// ReSharper disable InconsistentNaming, CompareOfFloatsByEqualityOperator

namespace RealFuels.Tanks
{
	public class FuelTankList : KeyedCollection<string, FuelTank>, IConfigNode
	{
		public FuelTankList ()
		{
		}

		public FuelTankList (ConfigNode node)
		{
			Load (node);
		}

		public bool TryGet(string resource, out FuelTank tank)
		{
			if (Contains(resource)) {
				tank = this[resource];
				return true;
			}
			tank = null;
			return false;
		}

		protected override string GetKeyForItem (FuelTank item)
		{
			return item.name;
		}

		public void Load (ConfigNode node)
		{
			if (node == null)
				return;
			foreach (ConfigNode tankNode in node.GetNodes ("TANK")) {
                if (tankNode.HasValue("name"))
                {
                    if (!Contains(tankNode.GetValue("name")))
                        Add(new FuelTank(tankNode));
                    else
                    {
                        Debug.LogWarning("[MFS] Ignored duplicate definition of TANK of type " + tankNode.GetValue("name"));
                    }
                }
                else
                    Debug.LogWarning("[MFS] TANK node invalid, lacks name");
			}
		}

		public void Save (ConfigNode node)
		{
			Save(node, true);
		}

		public void Save (ConfigNode node, bool includeEmpty)
		{
			foreach (FuelTank tank in this) {
				// Don't spam save files with empty tank nodes, only save the relevant stuff
				if (includeEmpty || tank.amount > 0 || tank.maxAmount > 0)
				{
					ConfigNode tankNode = new ConfigNode ("TANK");
					tank.Save (tankNode);
					node.AddNode (tankNode);
				}
			}
		}

        public void TechAmounts()
        {
            for (int i = 0; i < this.Count; i++)
            {
                if (!this[i].canHave)
                    this[i].maxAmount = 0;
            }
        }
	}
}
