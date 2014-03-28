//#define DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP;

namespace ModularFuelTanks
{
	public class RefuelingPump : ModularFuelPartModule
	{

		[KSPField(isPersistant = true)]
		double pump_rate = 100.0; // 625 liters/second seems reasonable.

		public override string GetInfo ()
		{
			return "\nPump rate: " + pump_rate + "/s";
		}

		public override void OnUpdate ()
		{
            if (HighLogic.LoadedSceneIsEditor)
            {

            }
            else if (timestamp > 0 && part.parent != null && part.parent.Modules.Contains("ModuleFuelTanks"))
            {
                // We're connected to a fuel tank, so let's top off any depleting resources
                FillAttachedTanks(precisionDeltaTime);
            }
            base.OnUpdate();            //Needs to be at the end to prevent weird things from happening during startup and to make handling persistance easy; this does mean that the effects are delayed by a frame, but since they're constant, that shouldn't matter here
        }


        private void FillAttachedTanks(double deltaTime)
        {
            // now, let's look at what we're connected to.
            ModuleFuelTanks m = (ModuleFuelTanks)part.parent.Modules["ModuleFuelTanks"];

            // look through all tanks inside this part
            foreach (ModuleFuelTanks.FuelTank tank in m.fuelList)
            {
                // if a tank isn't full, start filling it.
                if (tank.amount < tank.maxAmount)
                {
                    double top_off = deltaTime * pump_rate;
                    if (tank.amount + top_off < tank.maxAmount)
                        tank.amount += top_off;
                    else
                        tank.amount = tank.maxAmount;
                }
            }
        }
	}
}
