using System;
using System.Collections.Generic;
using UnityEngine;

namespace RealFuels
{
    public class RefuelingPump : PartModule, IAnalyticPreview
    {
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Fuel Pump")]
        [UI_Toggle(affectSymCounterparts = UI_Scene.Editor, disabledText = "Disabled", enabledText = "Enabled")]
        bool enablePump = true;

        [KSPField(isPersistant = true)]
        double pump_rate = 100.0; // 100L/sec per resource

        private FlightIntegrator flightIntegrator;
        private readonly List<Tanks.ModuleFuelTanks> tanks = new List<Tanks.ModuleFuelTanks>();
        private readonly List<Tanks.FuelTank> tankDefs = new List<Tanks.FuelTank>();
        private readonly List<PartResource> batteries = new List<PartResource>();

        public override string GetInfo () => $"Pump rate: {pump_rate:F1}/s";

        public override void OnStart(PartModule.StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                flightIntegrator = vessel.vesselModules.Find(x => x is FlightIntegrator) as FlightIntegrator;
                if (flightIntegrator == null)
                    Debug.LogError("[RefuelingPump] could not find flight integrator!");
                Fields[nameof(enablePump)].guiActive = vessel?.situation == Vessel.Situations.PRELAUNCH;
                enablePump &= vessel?.situation == Vessel.Situations.PRELAUNCH;
                FindTanks();
                GameEvents.onVesselWasModified.Add(VesselModified);
            }
        }

        public void OnDestroy()
        {
            if (HighLogic.LoadedSceneIsFlight)
                GameEvents.onVesselWasModified.Remove(VesselModified);
        }

        private void VesselModified(Vessel v)
        {
            if (vessel == v)
                FindTanks();
        }

        public void FixedUpdate ()
        {
            if (HighLogic.LoadedSceneIsFlight && part.parent != null && part.vessel != null && !flightIntegrator.isAnalytical && enablePump)
                FillAttachedTanks(TimeWarp.fixedDeltaTime);
        }

        #region IAnalyticPreview

        public void AnalyticInfo(FlightIntegrator fi, double sunAndBodyIn, double backgroundRadiation, double radArea, double absEmissRatio, double internalFlux, double convCoeff, double ambientTemp, double maxPartTemp)
        {
            if (enablePump && fi.timeSinceLastUpdate < double.MaxValue * 0.99)
                FillAttachedTanks(fi.timeSinceLastUpdate);
        }

        public double InternalFluxAdjust() => 0;

        #endregion

        private void FindTanks()
        {
            tanks.Clear();
            tankDefs.Clear();
            batteries.Clear();
            foreach (Part p in vessel.parts)
            {
                if (p.FindModuleImplementing<Tanks.ModuleFuelTanks>() is Tanks.ModuleFuelTanks m)
                {
                    tanks.Add(m);
                    foreach (var tank in m.tankList.Values)
                    {
                        if (tank.maxAmount > 0 &&
                            tank.resource is PartResource r &&
                            PartResourceLibrary.Instance.GetDefinition(r.resourceName) is PartResourceDefinition d &&
                            tank.fillable &&
                            r.flowMode != PartResource.FlowMode.None && r.flowState &&
                            d.resourceTransferMode == ResourceTransferMode.PUMP)
                        {
                            tankDefs.Add(tank);
                        }
                    }
                }
                foreach (var partResource in p.Resources)
                    if (partResource.info.name == "ElectricCharge")
                        batteries.Add(partResource);
            }
        }

        private void FillAttachedTanks(double deltaTime)
        {
            // sanity check
            if(deltaTime <= 0)
                return;

            foreach (var m in tanks)
                m.fueledByLaunchClamp = true;

            foreach (var tank in tankDefs)
            {
                if (tank.maxAmount > 0 &&
                    tank.resource is PartResource r &&
                    PartResourceLibrary.Instance.GetDefinition(r.resourceName) is PartResourceDefinition d &&
                    tank.amount < tank.maxAmount &&
                    tank.fillable &&
                    r.flowMode != PartResource.FlowMode.None && r.flowState &&
                    d.resourceTransferMode == ResourceTransferMode.PUMP)
                {
                    double amount = Math.Min(deltaTime * pump_rate * tank.utilization, tank.maxAmount - tank.amount);

                    if (d.unitCost > 0 && HighLogic.CurrentGame.Mode == Game.Modes.CAREER && Funding.Instance?.Funds is double funds && funds > 0)
                    {
                        double cost = amount * d.unitCost;
                        if (cost > funds)
                        {
                            amount = funds / d.unitCost;
                            cost = funds;
                        }
                        Funding.Instance.AddFunds(-cost, TransactionReasons.VesselRollout);
                    }
                    tank.part.TransferResource(r, amount, part);
                }
            }
            foreach (var partResource in batteries)
            {
                if (partResource.flowMode != PartResource.FlowMode.None && 
                    partResource.flowState &&
                    partResource.info.resourceTransferMode == ResourceTransferMode.PUMP &&
                    partResource.amount < partResource.maxAmount)
                {
                    double amount = Math.Min(deltaTime * pump_rate, partResource.maxAmount - partResource.amount);
                    partResource.part.TransferResource(partResource, amount, part);
                }
            }
        }
    }
}
