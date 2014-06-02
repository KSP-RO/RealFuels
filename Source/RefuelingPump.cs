//#define DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP;

namespace RealFuels
{
    public class RefuelingPump : ModularFuelPartModule
    {

        [KSPField(isPersistant = true)]
        double pump_rate = 100.0;

        public override string GetInfo ()
        {
            return "\nPump rate: " + pump_rate + "/s";
        }

        public override void OnUpdate ()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {

            }
            else if (timestamp > 0 && part.parent != null && part.vessel != null)
            {
                FillAttachedTanks(precisionDeltaTime);
            }
            base.OnUpdate();            //Needs to be at the end to prevent weird things from happening during startup and to make handling persistance easy; this does mean that the effects are delayed by a frame, but since they're constant, that shouldn't matter here
        }


        private void FillAttachedTanks(double deltaTime)
        {
            // now, let's look at what we're connected to.
            foreach (Part p in vessel.Parts) // look through all parts
            {
                if (p.Modules.Contains("ModuleFuelTanks"))
                {
                    ModuleFuelTanks m = (ModuleFuelTanks)p.Modules["ModuleFuelTanks"];
                    // look through all tanks inside this part
                    foreach (ModuleFuelTanks.FuelTank tank in m.tankList)
                    {
                        // if a tank isn't full, start filling it.
                        if (tank.amount < tank.maxAmount && tank.fillable)
                        {
                            tank.amount = tank.amount + deltaTime * pump_rate;
                        }
                    }
                }
            }
        }
    }
}
