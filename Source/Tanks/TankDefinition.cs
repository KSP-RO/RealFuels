using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Linq;
using UnityEngine;

namespace RealFuels.Tanks
{
	public class TankDefinition : IConfigNode
	{
		[Persistent]
		public string name;

		[Persistent]
		public string basemass;

		[Persistent]
		public string baseCost;

        [Persistent]
        public string techRequired = "";

        [Persistent]
        public bool highlyPressurized = false;

        // TODO Replace with new MLI system and then deprecate
        [Persistent]
        public string outerInsulationFactor = "1.0";

        [Persistent]
        public int numberOfMLILayers = 0;

        [Persistent]
        public int maxMLILayers = -1;

        [Persistent]
        public float minUtilization = 0;

        [Persistent]
        public float maxUtilization = 0;

        public Tanks.FuelTankList tankList = new Tanks.FuelTankList ();


		public TankDefinition () { }

		public TankDefinition (ConfigNode node)
		{
			Load (node);
		}

		public void Load (ConfigNode node)
		{
			if (! (node.name.Equals ("TANK_DEFINITION") && node.HasValue ("name"))) {
				return;
			}

			ConfigNode.LoadObjectFromConfig (this, node);
			tankList.Load (node);

			for (int i = tankList.Count - 1; i >= 0; --i) {
				var tank = tankList[i];
				if (!tank.resourceAvailable) {
					//Debug.LogWarning ("[MFT] Unable to initialize tank definition for resource \"" + tank.name + "\" in tank definition \"" + name + "\" as this resource is not defined.");
					tankList.RemoveAt (i);
				}
			}
		}

		public void Save (ConfigNode node)
		{
			ConfigNode.CreateConfigFromObject (this, node);
			tankList.Save (node, true);
		}

        public bool canHave
        {
            get
            {
                if (techRequired.Equals("") || HighLogic.CurrentGame == null || HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX)
                    return true;
                return ResearchAndDevelopment.GetTechnologyState(techRequired) == RDTech.State.Available;
            }
        }
	}
}
