using System;
using System.Collections.Generic;
using UnityEngine;

// ReSharper disable InconsistentNaming, CompareOfFloatsByEqualityOperator

namespace RealFuels.Tanks
{
    internal class FuelInfo
	{
		public readonly string title;
		public readonly string Label;
		public readonly Dictionary<Propellant, double> propellantVolumeMults = new Dictionary<Propellant, double>();
		public readonly HashSet<string> partNames = new HashSet<string>();
		public readonly List<PartModule> sources = new List<PartModule>();
		public readonly double efficiency;
		public readonly double ratioFactor;

        // looks to see if we should ignore this fuel when creating an autofill for an engine
        private static bool IgnoreFuel(string name) => MFSSettings.ignoreFuelsForFill.Contains(name);

		private string BuildLabel()
		{
			var text = StringBuilderCache.Acquire();
			bool first = true;
			foreach (KeyValuePair<Propellant,double> kvp in propellantVolumeMults)
			{
				Propellant tfuel = kvp.Key;
				double mult = kvp.Value;
				if (PartResourceLibrary.Instance.GetDefinition(tfuel.name).resourceTransferMode != ResourceTransferMode.NONE && !IgnoreFuel(tfuel.name))
				{
					if (!first)
						text.Append(" / ");
					first = false;
					text.Append(Math.Round(100000 * tfuel.ratio * mult / efficiency, 0) * 0.001).Append("% ").Append(tfuel.name);
				}
			}
			return text.ToStringAndRelease();
		}

		public FuelInfo(List<Propellant> props, ModuleFuelTanks tank, PartModule source)
		{
			// tank math:
			// efficiency = sum[utilization * ratio]
			// then final volume per fuel = fuel_ratio / fuel_utilization / efficiency
			string _title = source.part.partInfo.title;
			if (source.part.Modules.GetModule("ModuleEngineConfigs") is PartModule pm && pm != null)
				_title = $"{pm.Fields["configuration"].GetValue<string>(pm)}: {_title}";
			title = _title;
			partNames.Add(title);
			sources.Add(source);
			ratioFactor = 0.0;
			efficiency = 0.0;

			foreach (Propellant tfuel in props)
			{
				PartResourceDefinition def = PartResourceLibrary.Instance.GetDefinition(tfuel.name);
				if (def == null) {
					Debug.LogError($"Unknown RESOURCE: {tfuel.name}");
					ratioFactor = 0.0;
					break;
				}
				if (def.resourceTransferMode != ResourceTransferMode.NONE)
				{
					if (tank.tankList.TryGet(tfuel.name, out FuelTank t))
					{
						double volumeMultiplier = 1d / t.utilization;
						efficiency += tfuel.ratio * volumeMultiplier;
						ratioFactor += tfuel.ratio;
						propellantVolumeMults.Add(tfuel, volumeMultiplier);
					} else if (!IgnoreFuel (tfuel.name)) {
						ratioFactor = 0.0;
						break;
					}
				}
			}
			Label = BuildLabel();
		}

		public void AddSource(PartModule source)
        {
			string _title = source.part.partInfo.title;
			if (source.part.Modules.GetModule("ModuleEngineConfigs") is PartModule pm && pm != null)
				_title = $"{pm.Fields["configuration"].GetValue<string>(pm)}: {_title}";
			sources.Add(source);
			partNames.Add(_title);
        }
	}
}
