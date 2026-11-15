using KSP.Localization;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RealFuels
{
    public class RefuelingPump : PartModule, IAnalyticPreview
    {
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#RF_FuelPump_FuelPump")] // Fuel Pump
        [UI_Toggle(affectSymCounterparts = UI_Scene.Editor, disabledText = "#RF_disabletext2", enabledText = "#RF_enabletext2")] // DisabledEnabled
        bool enablePump = true;

        [KSPField(isPersistant = true)]
        double pump_rate = 100.0; // 100L/sec per resource

        private FlightIntegrator flightIntegrator;
        private readonly List<Tanks.ModuleFuelTanks> tanks = new List<Tanks.ModuleFuelTanks>();
        private readonly List<Tanks.FuelTank> tankDefs = new List<Tanks.FuelTank>();
        private readonly List<PartResource> batteries = new List<PartResource>();

        public override string GetInfo () => $"{Localizer.GetStringByTag("#RF_FuelPump_PumpRate")}: {pump_rate:F1}/s"; // Pump rate

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
                    foreach (var tank in m.tanksDict.Values)
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

            // Costs are summed and charged once per call
            bool chargeFunds = HighLogic.CurrentGame.Mode == Game.Modes.CAREER && Funding.Instance != null;
            double availableFunds = chargeFunds ? Math.Max(Funding.Instance.Funds, 0) : 0;
            double totalCost = 0;

            foreach (var tank in tankDefs)
            {
                if (tank.resource is PartResource r &&
                    PartResourceLibrary.Instance.GetDefinition(r.resourceName) is PartResourceDefinition d &&
                    tank.amount < tank.maxAmount &&
                    r.flowMode != PartResource.FlowMode.None && r.flowState)
                {
                    double amount = Math.Min(deltaTime * pump_rate * tank.utilization, tank.maxAmount - tank.amount);

                    if (chargeFunds && d.unitCost > 0)
                    {
                        // Pump only as much as the remaining funds can pay for
                        if (availableFunds <= 0)
                            continue;

                        double cost = amount * d.unitCost;
                        if (cost > availableFunds)
                        {
                            amount = availableFunds / d.unitCost;
                            cost = availableFunds;
                        }
                        availableFunds -= cost;
                        totalCost += cost;
                    }
                    tank.part.TransferResource(r, amount, part);
                }
            }

            if (totalCost > 0)
                Funding.Instance.AddFunds(-totalCost, TransactionReasons.VesselRollout);

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
