using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ReSharper disable InconsistentNaming, CompareOfFloatsByEqualityOperator

namespace RealFuels.Tanks
{
	internal class FuelInfo
	{
		public readonly string title;
		public readonly string Label;
		public readonly PartModule source;
		public readonly Dictionary<Propellant, double> propellantVolumeMults = new Dictionary<Propellant, double>();
		public readonly double efficiency;
		public readonly double ratioFactor;
		public readonly bool valid;

		// looks to see if we should ignore this fuel when creating an autofill for an engine
		private static bool IgnoreFuel(string name) => MFSSettings.ignoreFuelsForFill.Contains(name);

		private readonly List<string> labelString = new List<string>(10);
		private string BuildLabel()
		{
			labelString.Clear();
			foreach (KeyValuePair<Propellant,double> kvp in propellantVolumeMults)
			{
				Propellant tfuel = kvp.Key;
				if (PartResourceLibrary.Instance.GetDefinition(tfuel.name).resourceTransferMode != ResourceTransferMode.NONE && !IgnoreFuel(tfuel.name))
					labelString.Add($"{Math.Round(100 * tfuel.ratio * kvp.Value / efficiency, 3)}% {tfuel.name}");
			}
			return string.Join(" / ", labelString);
		}

		public FuelInfo(List<Propellant> props, ModuleFuelTanks tank, PartModule source)
		{
			// tank math:
			// efficiency = sum[ratio * volumePerUnit] == volume per [TotalRatio] unit draw of resources, not normalized
			// volume per unit of fuel = fuel_ratio * volumePerUnit / sum[fuel_ratio * volumePerUnit]

			this.source = source;
			string _title = source.part.partInfo.title;
			if (source.part.Modules.GetModule("ModuleEngineConfigs") is PartModule pm && pm != null)
				_title = $"{pm.Fields["configuration"].GetValue<string>(pm)}: {_title}";
			title = _title;
			ratioFactor = 0.0;
			efficiency = 0.0;

			// Error conditions: Resource not defined in library, or resource has no tank and is not in IgnoreFuel
			var missingRes = props.FirstOrDefault(p => PartResourceLibrary.Instance.GetDefinition(p.name) == null);
			var noTanks = props.Where(p => !tank.tankList.ContainsKey(p.name));
			bool noTanksAndNotIgnored = noTanks.Any(p => !IgnoreFuel(p.name));
			if (missingRes != null)
				Debug.LogError($"[MFT/RF] FuelInfo: Unknown RESOURCE: {missingRes.name}");

			valid = missingRes == null && !noTanksAndNotIgnored;
			if (!valid)
				return;

			foreach (Propellant tfuel in props)
			{
				if (tank.tankList.TryGetValue(tfuel.name, out FuelTank t))
				{
					double volumePerUnit = 1d / t.utilization;
					efficiency += tfuel.ratio * volumePerUnit;
					ratioFactor += tfuel.ratio;
					propellantVolumeMults.Add(tfuel, volumePerUnit);
				}
			}
			Label = BuildLabel();
		}
	}
}
