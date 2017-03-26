using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RealFuels.Tanks
{
    public partial class ModuleFuelTanks : IAnalyticTemperatureModifier, IAnalyticPreview
    {
        protected float tankArea;
        private double boiloffMass = 0d;
        private double analyticSkinTemp;
        private double analyticInternalTemp;
        private double previewInternalFluxAdjust;

        public double outerInsulationFactor = 1.0;

        // for EngineIgnitor integration: store a public dictionary of all pressurized propellants
        [NonSerialized]
        public Dictionary<string, bool> pressurizedFuels = new Dictionary<string, bool>();

        [KSPField(guiActiveEditor = true, guiName = "Highly Pressurized?")]
        public bool highlyPressurized = false;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "Heat Penetration", guiUnits = "")]
        public string debug1Display;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "Boil-off Loss", guiUnits = "")]
        public string debug2Display;

        [KSPField(isPersistant = true)]
        public double partPrevTemperature = -1;

        private static double ConductionFactors => RFSettings.globalConductionCompensation ? PhysicsGlobals.ConductionFactor : 1d;

        public double BoiloffMassRate => boiloffMass;

        partial void OnStartRF(StartState state)
        {
            base.OnStart(state);

            CalculateTankArea(out tankArea);
            part.skinInternalConductionMult = Math.Min(part.skinInternalConductionMult, outerInsulationFactor);

            part.heatConductivity = Math.Min(part.heatConductivity, outerInsulationFactor);
            part.analyticInternalInsulationFactor = outerInsulationFactor;

            if (RFSettings.debugBoilOff)
            {
                Fields[nameof(debug1Display)].guiActive = true;
                Fields[nameof(debug2Display)].guiActive = true;
            }
        }

        public void FixedUpdate()
        {
            //print ("[Real Fuels]" + Time.time.ToString ());
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (RFSettings.debugBoilOff)
                {
                    //debug1Display = part.skinInternalConductionMult.ToString ("F12");
                    //debug2Display = FormatFlux (part.skinToInternalFlux * (part.skinTemperature - part.temperature));
                    debug1Display = "";
                    debug2Display = "";
                }
                if (tankArea == 0d)
                    CalculateTankArea(out tankArea);
                StartCoroutine(CalculateTankLossFunction(TimeWarp.fixedDeltaTime));
            }
        }

        private IEnumerator CalculateTankLossFunction(float deltaTime, bool analyticalMode = false)
        {
            // Need to ensure that all heat compensation (radiators, heat pumps, etc) run first.
            yield return new WaitForFixedUpdate();
            boiloffMass = 0d;

            double minTemp = 300d;
            for (int i = tankList.Count - 1; i >= 0; --i)
            {
                FuelTank tank = tankList[i];
                if (tank.amount > 0d && (tank.vsp > 0.0 || tank.loss_rate > 0d))
                    minTemp = Math.Min(minTemp, tank.temperature);
            }

            if (tankList.Count > 0 && minTemp < 300d && MFSSettings.radiatorMinTempMult >= 0d)
                part.radiatorMax = (minTemp * MFSSettings.radiatorMinTempMult) / part.maxTemp;

            if (vessel != null && vessel.situation == Vessel.Situations.PRELAUNCH)
            {
                part.temperature = minTemp;
                part.skinTemperature = minTemp;
            }
            else
            {
                partPrevTemperature = part.temperature;

                double deltaTimeRecip = 1d / deltaTime;
                for (int i = tankList.Count - 1; i >= 0; --i)
                {
                    FuelTank tank = tankList[i];

                    if (tank.amount > 0d)
                    {
                        if (tank.vsp > 0.0)
                        {
                            // Opposite of original boil off code. Finds massLost first.
                            double massLost = 0.0;
                            double deltaTemp = part.temperature - tank.temperature;

                            if (RFSettings.debugBoilOff)
                            {
                                if (debug2Display != "")
                                    debug2Display += " / ";

                                if (debug1Display != "")
                                    debug1Display += " / ";
                            }

                            if (deltaTemp > 0)
                            {
                                //double tankRatio = tank.maxAmount / volume;

                                double wettedArea = tank.totalArea * (tank.amount / tank.maxAmount);

                                double q = deltaTemp / ((tank.wallThickness / (tank.wallConduction * wettedArea))
                                                        + (tank.insulationThickness / (tank.insulationConduction * wettedArea))
                                                        + (0.01 / (tank.resourceConductivity * wettedArea)));

                                if (RFSettings.ferociousBoilOff)
                                    q *= (part.thermalMass / (part.thermalMass - part.resourceThermalMass)) * tank.tankRatio;


                                //q /= ConductionFactors;

                                q *= 0.001d; // convert to kilowatts

                                massLost = q / tank.vsp;

                                if (RFSettings.debugBoilOff)
                                {
                                    // Only do debugging displays if compiled for debugging.

                                    debug1Display += Utilities.FormatFlux(q);
                                    debug2Display += (massLost * 1000 * 3600).ToString("F4") + "kg/hr";
                                    //debug2Display += area.ToString("F2");

                                    //debug1Display = tank.wallThickness + " / " + tank.wallConduction;
                                    //debug2Display = tank.insulationThickness + " / " + tank.insulationConduction;
                                }
                                massLost *= deltaTime; // Frame scaling
                            }

                            double lossAmount = massLost / tank.density;

                            if (double.IsNaN(lossAmount))
                                Debug.Log("[MFT] " + tank.name + " lossAmount is NaN!");
                            else
                            {
                                if (lossAmount > tank.amount)
                                {
                                    tank.amount = 0d;
                                }
                                else
                                {
                                    tank.amount -= lossAmount;

                                    double fluxLost = -massLost * tank.vsp;

                                    fluxLost *= ConductionFactors;

                                    // See if there is boiloff byproduct and see if any other parts want to accept it.
                                    if (tank.boiloffProductResource != null)
                                    {
                                        double boiloffProductAmount = -(massLost / tank.boiloffProductResource.density);
                                        double retainedAmount = part.RequestResource(tank.boiloffProductResource.id, boiloffProductAmount, ResourceFlowMode.STAGE_PRIORITY_FLOW);
                                        massLost -= retainedAmount * tank.boiloffProductResource.density;
                                    }

                                    boiloffMass += massLost;

                                    // subtract heat from boiloff
                                    // TODO Fix analytic mode behavior or remove this. (currently unused as analyticMode is always false)
                                    if (analyticalMode)
                                        previewInternalFluxAdjust += fluxLost;
                                    else
                                        part.AddThermalFlux(fluxLost * deltaTimeRecip);
                                }
                            }
                        }
                        else if (tank.loss_rate > 0 && tank.amount > 0)
                        {
                            double deltaTemp = part.temperature - tank.temperature;
                            if (deltaTemp > 0)
                            {
                                double lossAmount = tank.maxAmount * tank.loss_rate * deltaTemp * deltaTime;
                                if (lossAmount > tank.amount)
                                {
                                    lossAmount = -tank.amount;
                                    tank.amount = 0d;
                                }
                                else
                                {
                                    lossAmount = -lossAmount;
                                    tank.amount += lossAmount;
                                }
                                double massLost = tank.density * lossAmount;
                                boiloffMass += massLost;
                            }
                        }
                    }
                }
            }
        }

        partial void UpdateTankTypeRF(TankDefinition def)
        {
            // Get pressurization
            highlyPressurized = def.highlyPressurized;

            if (isDatabaseLoad)
                UpdateEngineIgnitor(def);
        }

        private void UpdateEngineIgnitor(TankDefinition def)
        {
            // collect pressurized propellants for EngineIgnitor
            // XXX Dirty hack until engine ignitor is fixed
            fuelList.Clear();               //XXX
            fuelList.AddRange(tankList);    //XXX

            pressurizedFuels.Clear();
            for (int i = 0; i < tankList.Count; i++)
            {
                FuelTank f = tankList[i];
                pressurizedFuels[f.name] = def.highlyPressurized || f.note.ToLower().Contains("pressurized");
            }
        }

        partial void ParseInsulationFactor(ConfigNode node)
        {
            if (!node.HasValue("outerInsulationFactor"))
                return;

            string insulationFactor = node.GetValue("outerInsulationFactor");
            ParseInsulationFactor(insulationFactor);
        }

        private void ParseInsulationFactor(string insulationFactor)
        {
            if (!double.TryParse(insulationFactor, out outerInsulationFactor))
                Debug.LogWarning("[MFT] Unable to parse outerInsulationFactor");
        }

        public void CalculateTankArea(out float totalTankArea)
        {
            totalTankArea = 0f;

            for (int i = 0; i< 6; ++i)
            {
                totalTankArea += part.DragCubes.WeightedArea[i];
            }
#if DEBUG
            Debug.Log("[MFT] Part WeightedArea: " + part.name + " = " + totalTankArea.ToString("F2"));
            Debug.Log("[MFT] Part Area: " + part.name + " = " + part.DragCubes.Area.ToString("F2"));
#endif
            // This allows a rough guess as to individual tank surface area based on ratio of tank volume to total volume but it breaks down at very small fractions
            // So use greater of spherical calculation and tank ratio of total area.
            if (totalTankArea > 0.0)
            {
                double tankMaxAmount;
                for (int i = tankList.Count - 1; i >= 0; --i)
                {
                    FuelTank tank = tankList[i];
                    if (tank.maxAmount > 0.0)
                    {
                        tankMaxAmount = tank.maxAmount;

                        if (tank.utilization > 1.0)
                            tankMaxAmount /= utilization;

                        tank.tankRatio = tankMaxAmount / volume;

                        tank.totalArea = Math.Max(Math.Pow(Math.PI, 1.0 / 3.0) * Math.Pow((tankMaxAmount / 1000.0) * 6, 2.0 / 3.0), tank.totalArea = totalTankArea* tank.tankRatio);

                        if (RFSettings.debugBoilOff)
                        {
                            Debug.Log("[RF] " + tank.name + ".tankRatio = " + tank.tankRatio.ToString());
                            Debug.Log("[RF] " + tank.name + ".maxAmount = " + tankMaxAmount.ToString());
                            Debug.Log("[RF] " + part.name + ".totalTankArea = " + totalTankArea.ToString());
                            Debug.Log("[RF] Tank surface area = " + tank.totalArea.ToString());
                        }
                    }
                }
            }
        }

        #region Analytic Interfaces

        // Analytic Interface
        public void SetAnalyticTemperature(FlightIntegrator fi, double analyticTemp, double toBeInternal, double toBeSkin)
        {
            analyticSkinTemp = toBeSkin;
            analyticInternalTemp = toBeInternal;
        }

        public double GetSkinTemperature(out bool lerp)
        {
            lerp = true;
            return analyticSkinTemp;
        }

        public double GetInternalTemperature(out bool lerp)
        {
            lerp = true;
            return analyticInternalTemp;
        }

        // Analytic Preview Interface
        public void AnalyticInfo(FlightIntegrator fi, double sunAndBodyIn, double backgroundRadiation, double radArea, double internalFlux, double convCoeff, double ambientTemp, double maxPartTemp, double x)
        {
            //analyticalInternalFlux = internalFlux;
            float deltaTime = (float)(Planetarium.GetUniversalTime() - vessel.lastUT);
            CalculateTankLossFunction(deltaTime, true);
        }

        public double InternalFluxAdjust()
        {
            return previewInternalFluxAdjust;
        }

        #endregion
    }
}
