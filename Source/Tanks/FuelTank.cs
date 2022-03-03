using System;
using UnityEngine;

// ReSharper disable InconsistentNaming, CompareOfFloatsByEqualityOperator

namespace RealFuels.Tanks
{
    // A FuelTank is a single TANK {} entry from the part.cfg file.
    // it defines four properties:
    // name         The name of the resource that can be stored.
    // utilization  How much of the tank is devoted to that resource (vs.
    //              how much is wasted in cryogenics or pumps).
    //              This is in resource units per volume unit.
    // mass         How much the part's mass is increased per volume unit
    //              of tank installed for this resource type. Tons per
    //              volume unit.
    // temperature  the part temperature at which this tank's contents start boiling
    // loss_rate    How quickly this resource type bleeds out of the tank. 
    //              (TODO: instead of this unrealistic static loss_rate, all 
    //              resources should have vsp (heat of vaporization) added and optionally conduction)
    //

    public class FuelTank: IConfigNode
	{
		//------------------- fields
		[Persistent]
		public string name = "UnknownFuel";
		[Persistent]
		public string note = "";
      
        public string boiloffProduct = "";

        [Persistent]
		public float utilization = 1.0f;
		[Persistent]
		public float mass = 0.0f;
		[Persistent]
		public float cost = 0.0f;
        // TODO Retaining for fallback purposes but should be deprecated
		[Persistent]
		public double loss_rate = 0.0;

        public double vsp;

        public double resourceConductivity = 10;

        // cache for tank.totalArea and tank.tankRatio for use by ModuleFuelTanksRF
        public double totalArea = -1;
        public double tankRatio = -1;

        [Persistent]
		public double wallThickness = 0.1;
        [Persistent]
		public double wallConduction = 205; // Aluminum conductive factor (@cryogenic temperatures)
        [Persistent]
		public double insulationThickness = 0.0;
        [Persistent]
		public double insulationConduction = 1.0;
		[Persistent]
		public bool isDewar;

		[Persistent]
		public float temperature = 300.0f;
		[Persistent]
		public bool fillable = true;
        [Persistent]
        public string techRequired = "";

		public bool locked = false;

		public bool propagate = true;

        public double density = 0d;

        public bool resourceAvailable;

		internal string amountExpression;
		internal string maxAmountExpression;

		[NonSerialized]
		private ModuleFuelTanks module;


        public PartResourceDefinition boiloffProductResource;

		//------------------- virtual properties
		public Part part => module != null ? module.part : null;

		public PartResource resource => part != null ? part.Resources[name] : null;

        public void RaiseResourceInitialChanged (Part part, PartResource resource, double amount)
		{
			var data = new BaseEventDetails (BaseEventDetails.Sender.USER);
			data.Set<PartResource> ("resource", resource);
			data.Set<double> ("amount", amount);
			part.SendEvent ("OnResourceInitialChanged", data, 0);
		}

		public void RaiseResourceMaxChanged (Part part, PartResource resource, double amount)
		{
			var data = new BaseEventDetails (BaseEventDetails.Sender.USER);
			data.Set<PartResource> ("resource", resource);
			data.Set<double> ("amount", amount);
			part.SendEvent ("OnResourceMaxChanged", data, 0);
		}

		public void RaiseResourceListChanged (Part part)
		{
			part.ResetSimulationResources ();
			part.SendEvent ("OnResourceListChanged", null, 0);
		}

		public double amount
		{
			get {
				if (module == null)
					throw new InvalidOperationException ("Amount is not defined until instantiated in a tank");
				return (resource != null) ? resource.amount : 0;
			}
			set {
				if (module == null)
					throw new InvalidOperationException ("Amount is not defined until instantiated in a tank");

				PartResource partResource = resource;
				// Point of unmanaged resource is we don't manage them.  So bail out here.
				if (partResource == null || module.unmanagedResources.ContainsKey(partResource.resourceName))
					return;

				if (value > partResource.maxAmount)
					value = partResource.maxAmount;

				if (value == partResource.amount)
					return;

                amountExpression = null;

				partResource.amount = value;
				if (HighLogic.LoadedSceneIsEditor) {
					module.RaiseResourceInitialChanged(partResource, value);
					if (propagate) {
						foreach (Part sym in part.symmetryCounterparts)
						{
							PartResource symResc = sym.Resources[name];
							symResc.amount = value;
							RaiseResourceInitialChanged(sym, symResc, value);
						}
					}
				}
			}
		}

        public bool canHave => techRequired.Equals("") || HighLogic.CurrentGame == null || HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX
								|| ResearchAndDevelopment.GetTechnologyState(techRequired) == RDTech.State.Available;

		void DeleteTank()
		{
			PartResource partResource = resource;
			maxAmountExpression = null;

			if (module.unmanagedResources.ContainsKey(partResource.resourceName))
				return;

            part.Resources.Remove(partResource);
            part.SimulationResources.Remove(partResource);
			module.RaiseResourceListChanged();

			// Update symmetry counterparts.
			if (HighLogic.LoadedSceneIsEditor && propagate)
            {
				foreach (Part sym in part.symmetryCounterparts)
                {
					PartResource symResc = sym.Resources[name];
					sym.Resources.Remove(symResc);
					sym.SimulationResources.Remove(symResc);
					RaiseResourceListChanged(sym);
                }
            }
		}

		void UpdateTank (double value)
		{
			PartResource partResource = resource;
			if (module.unmanagedResources.ContainsKey(partResource.resourceName))
				return;

            if (value > partResource.maxAmount)
            {
				// If expanding, modify it to be less than overfull
				double maxQty = (module.AvailableVolume * utilization) + partResource.maxAmount;
				value = Math.Min(maxQty, value);
			}

			// Do nothing if unchanged
			if (value == partResource.maxAmount)
				return;

			maxAmountExpression = null;
			partResource.maxAmount = value;
			module.RaiseResourceMaxChanged(partResource, value);

			// Keep the same fill fraction
			double newAmount = value * fillFraction;
			if (newAmount != partResource.amount)
            {
				partResource.amount = newAmount;
				module.RaiseResourceInitialChanged(partResource, newAmount);
			}

			// Update symmetry counterparts.
			if (HighLogic.LoadedSceneIsEditor && propagate)
            {
				foreach (Part sym in part.symmetryCounterparts)
                {
					PartResource symResc = sym.Resources[name];
					symResc.maxAmount = value;
					RaiseResourceMaxChanged(sym, symResc, value);

					if (newAmount != symResc.amount)
                    {
						symResc.amount = newAmount;
						RaiseResourceInitialChanged(sym, symResc, newAmount);
					}
				}
			}
		}

		void AddTank(double value)
		{
			if (module.unmanagedResources.ContainsKey(name))
				return;

			var resDef = PartResourceLibrary.Instance.GetDefinition(name);
			var res = part.Resources.Contains(name) ? part.Resources[name] : new PartResource(part);
			res.resourceName = name;
			res.SetInfo(resDef);
			res.amount = value;
			res.maxAmount = value;
			res._flowState = true;
			res.isTweakable = resDef.isTweakable;
			res.isVisible = resDef.isVisible;
			res.hideFlow = false;
			res._flowMode = PartResource.FlowMode.Both;
			part.Resources.dict.Add(resDef.id, res);
			//Debug.Log ($"[MFT] AddTank {res.resourceName} {res.amount} {res.maxAmount} {res.flowState} {res.isTweakable} {res.isVisible} {res.hideFlow} {res.flowMode}");

			module.RaiseResourceListChanged();

            // Update symmetry counterparts.
			if (HighLogic.LoadedSceneIsEditor && propagate)
            {
				foreach (Part sym in part.symmetryCounterparts)
                {
                    sym.Resources.dict.Add(resDef.id, new PartResource(res));
                    RaiseResourceListChanged(sym);
                }
            }
		}

		public double maxAmount {
			get {
				if (module == null) {
					throw new InvalidOperationException ("Maxamount is not defined until instantiated in a tank");
				}
				PartResource res = resource;
				return (res == null || module.unmanagedResources.ContainsKey(res.resourceName)) ? 0 : res.maxAmount;
			}

			set {
				if (module == null) {
					throw new InvalidOperationException ("Maxamount is not defined until instantiated in a tank");
				}
				//print ("*RK* Setting maxAmount of tank " + name + " of part " + part.name + " to " + value);

				PartResource partResource = resource;
				if (partResource != null && value <= 0.0)
					DeleteTank();
				else if (partResource != null)
					UpdateTank(value);
				else if (value > 0.0)
					AddTank(value);
                module.massDirty = true;
			}
		}

		public double fillFraction
		{
			get => amount / maxAmount;
			set => amount = value * maxAmount;
		}

		public override string ToString() => name ?? "NULL";

		//------------------- IConfigNode implementation
		public void Load (ConfigNode node)
		{
			if (!(node.name.Equals("TANK") && node.HasValue("name")))
				return;

			ConfigNode.LoadObjectFromConfig(this, node);
			if (node.HasValue("efficiency") && !node.HasValue("utilization"))
				node.TryGetValue("efficiency", ref utilization);

			node.TryGetValue("amount", ref amountExpression);
			node.TryGetValue("maxAmount", ref maxAmountExpression);

			resourceAvailable = PartResourceLibrary.Instance.GetDefinition(name) != null;
            MFSSettings.resourceVsps.TryGetValue(name, out vsp);
            MFSSettings.resourceConductivities.TryGetValue(name, out resourceConductivity);

			string boiloffRes = "";
			if (node.TryGetValue("boiloffProduct", ref boiloffRes))
				boiloffProductResource = PartResourceLibrary.Instance.GetDefinition(boiloffRes);

            GetDensity();
		}

		public void Save(ConfigNode node)
		{
			if (name == null) return;
			ConfigNode.CreateConfigFromObject (this, node);
			node.AddValue("amount", module == null ? amountExpression : amount.ToString("G17"));
			node.AddValue("maxAmount", module == null ? maxAmountExpression : maxAmount.ToString("G17"));
		}

		internal void InitializeAmounts()
		{
			if (module == null) return;

			if (maxAmountExpression == null)
			{
				maxAmount = 0;
				amount = 0;
				return;
			}

			if (maxAmountExpression.Contains("%") && double.TryParse(maxAmountExpression.Replace("%", "").Trim(), out double v))
				maxAmount = v * utilization * module.volume * 0.01; // NK
			else if (double.TryParse(maxAmountExpression, out v))
				maxAmount = v;
			else
			{
				Debug.LogError($"Unable to parse max amount expression: {maxAmountExpression} for tank {name}");
				maxAmount = 0;
				amount = 0;
				maxAmountExpression = null;
				return;
			}
			maxAmountExpression = null;

			if (amountExpression == null) {
				amount = maxAmount;
				return;
			}

			if (amountExpression.Equals("full", StringComparison.OrdinalIgnoreCase))
				amount = maxAmount;
			else if (amountExpression.Contains("%") && double.TryParse(amountExpression.Replace("%", "").Trim(), out v))
				amount = v * maxAmount * 0.01;
			else if (double.TryParse(amountExpression, out v))
				amount = v;
			else
			{
				amount = maxAmount;
				Debug.LogError($"Unable to parse amount expression: {amountExpression} for tank {name}");
			}
			amountExpression = null;
		}

		//------------------- Constructor
		public FuelTank (ConfigNode node)
		{
			Load (node);
		}

		internal FuelTank CreateCopy (ModuleFuelTanks toModule, ConfigNode overNode, bool initializeAmounts)
		{
			FuelTank clone = (FuelTank)MemberwiseClone ();
			clone.module = toModule;

			if (overNode != null) {
				clone.Load (overNode);
			}
			if (initializeAmounts) {
				clone.InitializeAmounts ();
			} else {
				clone.amountExpression = clone.maxAmountExpression = null;
			}
            clone.GetDensity();
			return clone;
		}

        internal void GetDensity()
        {
            PartResourceDefinition d = PartResourceLibrary.Instance.GetDefinition(name);
			density = (d != null) ? d.density : 0;
        }
	}
}
