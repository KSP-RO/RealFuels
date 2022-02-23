using System.Collections.Generic;

namespace RealFuels.Tanks
{
	public class TankDefinitionList : Dictionary<string, TankDefinition>, IConfigNode
	{
		public void Load(ConfigNode node)
		{
			foreach (ConfigNode tankNode in node.GetNodes("TANK_DEFINITION"))
			{
				TankDefinition def = new TankDefinition(tankNode);
				Add(def.name, def);
			}
		}

		public void Save(ConfigNode node)
		{
			foreach (TankDefinition tank in Values)
			{
				ConfigNode tankNode = new ConfigNode("TANK");
				tank.Save(tankNode);
				node.AddNode(tankNode);
			}
		}
	}
}
