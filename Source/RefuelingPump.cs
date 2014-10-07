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
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Pump Enabled")]
        bool enablePump = false;

        [KSPField(isPersistant = true)]
        double pump_rate = 100.0; // 100L/sec per resource

        [KSPEvent(guiActive = false, guiActiveEditor = true, guiName = "Toggle Pump")]
        public void TogglePump()
        {
            enablePump = !enablePump;
        }

        public override string GetInfo ()
        {
            return "\nPump rate: " + pump_rate + "/s";
        }

		public override void OnStart (PartModule.StartState state)
		{
			if (HighLogic.LoadedSceneIsFlight) {
				if (vessel.situation == Vessel.Situations.LANDED
					&& vessel.landedAt.Contains ("KSC")) {
					Events["TogglePump"].guiActive = true;
				} else {
					enablePump = false;
				}
			}
		}

        public override void OnUpdate ()
        {
            if (!HighLogic.LoadedSceneIsEditor && timestamp > 0 && part.parent != null && part.vessel != null && enablePump)
            {
                FillAttachedTanks(precisionDeltaTime);
            }
            base.OnUpdate();            //Needs to be at the end to prevent weird things from happening during startup and to make handling persistance easy; this does mean that the effects are delayed by a frame, but since they're constant, that shouldn't matter here
        }


        private void FillAttachedTanks(double deltaTime)
        {
            // sanity check
            if(deltaTime <= 0)
                return;

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
                        PartResource r = tank.resource;
						if (r == null) {
							continue;
						}
						PartResourceDefinition d = PartResourceLibrary.Instance.GetDefinition(r.resourceName);
						if (d == null) {
							continue;
						}
                        if (tank.amount < tank.maxAmount && tank.fillable && r.flowMode != PartResource.FlowMode.None && d.resourceTransferMode == ResourceTransferMode.PUMP)
                        {
							double amount = deltaTime * pump_rate;
							var game = HighLogic.CurrentGame;

							if (d.unitCost > 0 && game.Mode == Game.Modes.CAREER && Funding.Instance != null) {
								double funds = Funding.Instance.Funds;
								double cost = amount * d.unitCost;
								if (cost > funds)
								{
									amount = funds / d.unitCost;
									cost = funds;
								}
								Funding.Instance.AddFunds (-cost, TransactionReasons.VesselRollout);
							}
                            tank.amount = tank.amount + amount;
                        }
                    }
                }
            }
        }
    }
}
