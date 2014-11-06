using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;

namespace RealFuels.Tanks
{
	public class TankDefinitionList : KeyedCollection<string, TankDefinition>, IConfigNode
	{
		protected override string GetKeyForItem(TankDefinition item)
		{
			return item.name;
		}

		public void Load(ConfigNode node)
		{
			foreach (ConfigNode tankNode in node.GetNodes("TANK_DEFINITION"))
				Add(new TankDefinition(tankNode));
		}

		public void Save(ConfigNode node)
		{
			foreach (TankDefinition tank in this)
			{
				ConfigNode tankNode = new ConfigNode("TANK");
				tank.Save(tankNode);
				node.AddNode(tankNode);
			}
		}
	}
}
