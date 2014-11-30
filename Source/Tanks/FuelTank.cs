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
	// A FuelTank is a single TANK {} entry from the part.cfg file.
	// it defines four properties:
	// name         The name of the resource that can be stored.
	// utilization  How much of the tank is devoted to that resource (vs.
	//              how much is wasted in cryogenics or pumps).
	//              This is in resource units per volume unit.
	// mass         How much the part's mass is increased per volume unit
	//              of tank installed for this resource type. Tons per
	//              volume unit.
	// loss_rate    How quickly this resource type bleeds out of the tank.

	public class FuelTank: IConfigNode
	{
		//------------------- fields
		[Persistent]
		public string name = "UnknownFuel";
		[Persistent]
		public string note = "";
		[Persistent]
		public float utilization = 1.0f;
		[Persistent]
		public float mass = 0.0f;
		[Persistent]
		public float cost = 0.0f;
		[Persistent]
		public double loss_rate = 0.0;
		[Persistent]
		public float temperature = 300.0f;
		[Persistent]
		public bool fillable = true;

		public bool locked = false;

		public bool propagate = true;

		public bool resourceAvailable;

		internal string amountExpression;
		internal string maxAmountExpression;

		[NonSerialized]
		private ModuleFuelTanks module;

		//------------------- virtual properties
		public Part part
		{
			get {
				if (module == null) {
					return null;
				}
				return module.part;
			}
		}

		public PartResource resource
		{
			get {
				if (part == null) {
					return null;
				}
				return part.Resources[name];
			}
		}

		public double amount
		{
			get {
				if (module == null) {
					throw new InvalidOperationException ("Amount is not defined until instantiated in a tank");
				}

				if (resource == null) {
					return 0.0;
				}
				return resource.amount;
			}
			set {
				if (module == null) {
					throw new InvalidOperationException ("Amount is not defined until instantiated in a tank");
				}

				PartResource partResource = resource;
				if (partResource == null) {
					return;
				}

				if (value > partResource.maxAmount) {
					value = partResource.maxAmount;
				}

				if (value == partResource.amount) {
					return;
				}

				amountExpression = null;
				partResource.amount = value;
				if (HighLogic.LoadedSceneIsEditor) {
					module.RaiseResourceInitialChanged (partResource, amount);
					if (propagate) {
						foreach (Part sym in part.symmetryCounterparts) {
							PartResource symResc = sym.Resources[name];
							symResc.amount = value;
							PartMessageService.Send<PartResourceInitialAmountChanged> (this, sym, symResc, amount);
						}
					}
				}
			}
		}

		void DeleteTank ()
		{
			PartResource partResource = resource;
			// Delete it
			//Debug.LogWarning ("[MFT] Deleting tank from API " + name);
			maxAmountExpression = null;

			part.Resources.list.Remove (partResource);
			PartModule.Destroy (partResource);
			module.RaiseResourceListChanged ();
			//print ("Removed.");

			// Update symmetry counterparts.
			if (HighLogic.LoadedSceneIsEditor && propagate) {
				foreach (Part sym in part.symmetryCounterparts) {
					PartResource symResc = sym.Resources[name];
					sym.Resources.list.Remove (symResc);
					PartModule.Destroy (symResc);
					PartMessageService.Send<PartResourceListChanged> (this, sym);
				}
			}
			//print ("Sym removed");
		}

		void UpdateTank (double value)
		{
			PartResource partResource = resource;
			if (value > partResource.maxAmount) {
				// If expanding, modify it to be less than overfull
				double maxQty = module.AvailableVolume * utilization + partResource.maxAmount;
				if (maxQty < value) {
					value = maxQty;
				}
			}

			// Do nothing if unchanged
			if (value == partResource.maxAmount) {
				return;
			}

			//Debug.LogWarning ("[MFT] Updating tank from API " + name + " amount: " + value);
			maxAmountExpression = null;

			// Keep the same fill fraction
			double newAmount = value * fillFraction;

			partResource.maxAmount = value;
			module.RaiseResourceMaxChanged (partResource, value);
			//print ("Set new maxAmount");

			if (newAmount != partResource.amount) {
				partResource.amount = newAmount;
				module.RaiseResourceInitialChanged (partResource, newAmount);
			}

			// Update symmetry counterparts.
			if (HighLogic.LoadedSceneIsEditor && propagate) {
				foreach (Part sym in part.symmetryCounterparts) {
					PartResource symResc = sym.Resources[name];
					symResc.maxAmount = value;
					PartMessageService.Send<PartResourceMaxAmountChanged> (this, sym, symResc, value);

					if (newAmount != symResc.amount) {
						symResc.amount = newAmount;
						PartMessageService.Send<PartResourceInitialAmountChanged> (this, sym, symResc, newAmount);
					}
				}
			}

			//print ("Symmetry set");
		}

		void AddTank (double value)
		{
			PartResource partResource = resource;
			//Debug.LogWarning ("[MFT] Adding tank from API " + name + " amount: " + value);
			maxAmountExpression = null;

			ConfigNode node = new ConfigNode ("RESOURCE");
			node.AddValue ("name", name);
			node.AddValue ("amount", value);
			node.AddValue ("maxAmount", value);
#if DEBUG
			print (node.ToString ());
#endif
			partResource = part.AddResource (node);
			partResource.enabled = true;

			module.RaiseResourceListChanged ();

			// Update symmetry counterparts.
			if (HighLogic.LoadedSceneIsEditor && propagate) {
				foreach (Part sym in part.symmetryCounterparts) {
					PartResource symResc = sym.AddResource (node);
					symResc.enabled = true;
					PartMessageService.Send<PartResourceListChanged> (this, sym);
				}
			}
		}

		public double maxAmount {
			get {
				if (module == null) {
					throw new InvalidOperationException ("Maxamount is not defined until instantiated in a tank");
				}

				if (resource == null) {
					return 0.0f;
				}
				return resource.maxAmount;
			}

			set {
				if (module == null) {
					throw new InvalidOperationException ("Maxamount is not defined until instantiated in a tank");
				}
				//print ("*RK* Setting maxAmount of tank " + name + " of part " + part.name + " to " + value);

				PartResource partResource = resource;
				if (partResource != null && value <= 0.0) {
					DeleteTank ();
				} else if (partResource != null) {
					UpdateTank (value);
				} else if (value > 0.0) {
					AddTank (value);
				}
				module.massDirty = true;
			}

		}

		public double fillFraction
		{
			get {
				return amount / maxAmount;
			}
			set {
				amount = value * maxAmount;
			}
		}


		//------------------- implicit type conversions
		public override string ToString ()
		{
			if (name == null) {
				return "NULL";
			}
			return name;
		}

		//------------------- IConfigNode implementation
		public void Load (ConfigNode node)
		{
			if (!(node.name.Equals ("TANK") && node.HasValue ("name"))) {
				return;
			}

			ConfigNode.LoadObjectFromConfig (this, node);
			if (node.HasValue ("efficiency") && !node.HasValue ("utilization")) {
				float.TryParse (node.GetValue ("efficiency"), out utilization);
			}

			amountExpression = node.GetValue ("amount") ?? amountExpression;
			maxAmountExpression = node.GetValue ("maxAmount") ?? maxAmountExpression;

			resourceAvailable = PartResourceLibrary.Instance.GetDefinition (name) != null;
		}

		internal void InitializeAmounts ()
		{
			if (module == null) {
				return;
			}

			double v;
			if (maxAmountExpression == null) {
				maxAmount = 0;
				amount = 0;
				return;
			}

			if (maxAmountExpression.Contains ("%") && double.TryParse (maxAmountExpression.Replace ("%", "").Trim (), out v)) {
				maxAmount = v * utilization * module.volume * 0.01; // NK
			} else if (double.TryParse (maxAmountExpression, out v)) {
				maxAmount = v;
			} else {
				Debug.LogError ("Unable to parse max amount expression: " + maxAmountExpression + " for tank " + name);
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

			if (amountExpression.Equals ("full")) {
				amount = maxAmount;
			} else if (amountExpression.Contains ("%") && double.TryParse (amountExpression.Replace ("%", "").Trim (), out v)) {
				amount = v * maxAmount * 0.01;
			} else if (double.TryParse (amountExpression, out v)) {
				amount = v;
			} else {
				amount = maxAmount;
				Debug.LogError ("Unable to parse amount expression: " + amountExpression + " for tank " + name);
			}
			amountExpression = null;
		}

		public void Save (ConfigNode node)
		{
			if (name == null) {
				return;
			}
			ConfigNode.CreateConfigFromObject (this, node);

			if (module == null) {
				node.AddValue ("amount", amountExpression);
				node.AddValue ("maxAmount", maxAmountExpression);
			}
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

			return clone;
		}
	}
}
