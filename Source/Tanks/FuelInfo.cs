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
	internal class FuelInfo
	{
		public string names;
		public readonly List<Propellant> propellants;
		public readonly double efficiency;
		public readonly double ratioFactor;

		// looks to see if we should ignore this fuel when creating an autofill for an engine
		private static bool IgnoreFuel (string name)
		{
			return MFSSettings.ignoreFuelsForFill.Contains (name);
		}

		public string Label
		{
			get {
				string label = "";
				foreach (Propellant tfuel in propellants) {
					if (PartResourceLibrary.Instance.GetDefinition (tfuel.name).resourceTransferMode != ResourceTransferMode.NONE && !IgnoreFuel (tfuel.name)) {
						if (label.Length > 0) {
							label += " / ";
						}
						label += Math.Round (100000 * tfuel.ratio / ratioFactor, 0)*0.001 + "% " + tfuel.name;
					}
				}
				return label;
			}
		}

		public FuelInfo (List<Propellant> props, ModuleFuelTanks tank, string title)
		{
			// tank math:
			// efficiency = sum[utilization * ratio]
			// then final volume per fuel = fuel_ratio / fuel_utilization / efficiency

			ratioFactor = 0.0;
			efficiency = 0.0;
			propellants = props;

			foreach (Propellant tfuel in propellants) {
				if (PartResourceLibrary.Instance.GetDefinition (tfuel.name) == null) {
					Debug.LogError ("Unknown RESOURCE {" + tfuel.name + "}");
					ratioFactor = 0.0;
					break;
				}
				if (PartResourceLibrary.Instance.GetDefinition (tfuel.name).resourceTransferMode != ResourceTransferMode.NONE) {
					FuelTank t;
					if (tank.tankList.TryGet (tfuel.name, out t)) {
						efficiency += tfuel.ratio/t.utilization;
						ratioFactor += tfuel.ratio;
					} else if (!IgnoreFuel (tfuel.name)) {
						ratioFactor = 0.0;
						break;
					}
				}
			}
			names = "Used by: " + title;
		}
	}
}
