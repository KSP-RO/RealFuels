//#define DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP;

namespace ModularFuelTanks
{
	public class RefuelingPump: PartModule
	{
		[KSPField(isPersistant = true)]
		double timestamp = 0.0;

		[KSPField(isPersistant = true)]
		double pump_rate = 100.0; // 625 liters/second seems reasonable.

		public override string GetInfo ()
		{
			return "\nPump rate: " + pump_rate + "/s";
		}

		public override void OnUpdate ()
		{
			if (HighLogic.LoadedSceneIsEditor) {

			} else if (timestamp > 0 && part.parent != null && part.parent.Modules.Contains ("ModuleFuelTanks")) {
				// We're connected to a fuel tank, so let's top off any depleting resources

				// first, get the time since the last OnUpdate()
				double delta_t = Planetarium.GetUniversalTime () - timestamp;

				// now, let's look at what we're connected to.
				ModuleFuelTanks m = (ModuleFuelTanks) part.parent.Modules["ModuleFuelTanks"];

				// look through all tanks inside this part
				foreach(ModuleFuelTanks.FuelTank tank in m.fuelList) {
					// if a tank isn't full, start filling it.
					if(tank.amount < tank.maxAmount) {
						double top_off = delta_t * pump_rate;
						if(tank.amount + top_off < tank.maxAmount)
							tank.amount += top_off;
						else
							tank.amount = tank.maxAmount;
					}

				}
				// save the time so we can tell how much time has passed on the next update, even in Warp
				timestamp = Planetarium.GetUniversalTime ();
			} else {
				// save the time so we can tell how much time has passed on the next update, even in Warp
				timestamp = Planetarium.GetUniversalTime ();
			}
		}
	}
}
