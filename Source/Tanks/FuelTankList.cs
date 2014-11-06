using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using System.Collections.ObjectModel;
using KSPAPIExtensions;
using KSPAPIExtensions.PartMessage;

// ReSharper disable InconsistentNaming, CompareOfFloatsByEqualityOperator

namespace RealFuels.Tanks
{
	public class FuelTankList : KeyedCollection<string, FuelTank>, IConfigNode
	{
		public FuelTankList()
		{
		}

		public FuelTankList(ConfigNode node)
		{
			Load(node);
		}

		protected override string GetKeyForItem(FuelTank item)
		{
			return item.name;
		}

		public void Load(ConfigNode node)
		{
			if (node == null)
				return;
			foreach (ConfigNode tankNode in node.GetNodes("TANK"))
			{
					Add(new FuelTank(tankNode));
			}
		}

		public void Save(ConfigNode node)
		{
			foreach (FuelTank tank in this)
			{
				ConfigNode tankNode = new ConfigNode("TANK");
				tank.Save(tankNode);
				node.AddNode(tankNode);
			}
		}
	}
}
