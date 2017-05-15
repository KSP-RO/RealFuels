﻿using System;
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
        private bool supportsBoiloff = false;
        public double sunAndBodyFlux = 0;

        public double outerInsulationFactor = 1.0;

        // for EngineIgnitor integration: store a public dictionary of all pressurized propellants
        [NonSerialized]
        public Dictionary<string, bool> pressurizedFuels = new Dictionary<string, bool>();

        [KSPField(guiActiveEditor = true, guiName = "Highly Pressurized?")]
        public bool highlyPressurized = false;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "Wall Temp.", guiUnits = "")]
        public string debug0Display;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "Heat Penetration", guiUnits = "")]
        public string debug1Display;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "Boil-off Loss", guiUnits = "")]
        public string debug2Display;

        [KSPField(isPersistant = true)]
        public double partPrevTemperature = -1;

        private static double ConductionFactors => RFSettings.Instance.globalConductionCompensation ? Math.Max(1.0d, PhysicsGlobals.ConductionFactor) : 1d;

        public double BoiloffMassRate => boiloffMass;


        private FlightIntegrator _flightIntegrator;

        double lowestTankTemperature = 300d;

        partial void OnStartRF(StartState state)
        {
            base.OnStart(state);

            if (HighLogic.LoadedSceneIsFlight)
            {
                for (int i = 0; i < vessel.vesselModules.Count; i++)
                {
                    if (vessel.vesselModules[i] is FlightIntegrator)
                    {
                        _flightIntegrator = vessel.vesselModules[i] as FlightIntegrator;
                    }
                }
            }


            CalculateTankArea(out tankArea);
            // changed from skin-internal to part.heatConductivity which affects
            part.heatConductivity = Math.Min(part.heatConductivity, outerInsulationFactor);
            // affects how fast internal temperatures change during analytic mode
            part.analyticInternalInsulationFactor *= outerInsulationFactor;

            for (int i = tankList.Count - 1; i >= 0; --i)
            {
                FuelTank tank = tankList[i];
                if (tank.maxAmount > 0.0 && (tank.vsp > 0.0 || tank.loss_rate > 0.0))
                {
                    supportsBoiloff = true;
                    break;
                }
            }

            Fields[nameof(debug0Display)].guiActive = RFSettings.Instance.debugBoilOff && this.supportsBoiloff;
            Fields[nameof(debug1Display)].guiActive = RFSettings.Instance.debugBoilOff && this.supportsBoiloff;
            Fields[nameof(debug2Display)].guiActive = RFSettings.Instance.debugBoilOff && this.supportsBoiloff;
        }

        public void FixedUpdate()
        {
            //print ("[Real Fuels]" + Time.time.ToString ());
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (RFSettings.Instance.debugBoilOff)
                {
                    //debug1Display = part.heatConductivity.ToString ("F12");
                    //debug2Display = FormatFlux (part.skinToInternalFlux);
                    debug1Display = "";
                    debug2Display = "";
                    debug0Display = "";
                }
                if (tankArea == 0d)
                    CalculateTankArea(out tankArea);

                // 
                if(!_flightIntegrator.isAnalytical)
                    StartCoroutine(CalculateTankLossFunction((double)TimeWarp.fixedDeltaTime));
            }
        }

        private IEnumerator CalculateTankLossFunction(double deltaTime, bool analyticalMode = false)
        {
            // Need to ensure that all heat compensation (radiators, heat pumps, etc) run first.
            if (!analyticalMode)
                yield return new WaitForFixedUpdate();
            
            boiloffMass = 0d;

            previewInternalFluxAdjust = 0;

            for (int i = tankList.Count - 1; i >= 0; --i)
            {
                FuelTank tank = tankList[i];
                if (tank.amount > 0d && (tank.vsp > 0.0 || tank.loss_rate > 0d))
                    lowestTankTemperature = Math.Min(lowestTankTemperature, tank.temperature);
            }

            if (tankList.Count > 0 && lowestTankTemperature < 300d && MFSSettings.radiatorMinTempMult >= 0d)
                part.radiatorMax = (lowestTankTemperature * MFSSettings.radiatorMinTempMult) / part.maxTemp;

            if (vessel != null && vessel.situation == Vessel.Situations.PRELAUNCH)
            {
                part.temperature = lowestTankTemperature;
                part.skinTemperature = lowestTankTemperature;
            }
            else
            {
                partPrevTemperature = part.temperature;

                double deltaTimeRecip = 1d / deltaTime;
                //Debug.Log("internalFlux = " + part.thermalInternalFlux.ToString() + ", thermalInternalFluxPrevious =" + part.thermalInternalFluxPrevious.ToString() + ", analytic internal flux = " + previewInternalFluxAdjust.ToString());

                double cooling = analyticalMode ? Math.Max(0, part.thermalInternalFluxPrevious) : 0;

                for (int i = tankList.Count - 1; i >= 0; --i)
                {
                    FuelTank tank = tankList[i];

                    if (tank.amount > 0d)
                    {
                        if (tank.vsp > 0.0)
                        {
                            // Opposite of original boil off code. Finds massLost first.
                            double massLost = 0.0;
                            double deltaTemp;
                            double hotTemp = part.temperature - (cooling * part.thermalMassReciprocal);
                            double tankRatio = tank.maxAmount / volume;

                            if (RFSettings.Instance.ferociousBoilOff)
                                hotTemp = Math.Max(((hotTemp * part.thermalMass) - (tank.temperature * part.resourceThermalMass)) / (part.thermalMass - part.resourceThermalMass), part.temperature);

                            deltaTemp = hotTemp - tank.temperature;

                            if (RFSettings.Instance.debugBoilOff)
                            {
                                if (debug2Display != "")
                                    debug2Display += " / ";

                                if (debug1Display != "")
                                    debug1Display += " / ";
                            
                                if (debug0Display != "")
                                    debug0Display += " / ";
                            }

                            if (RFSettings.Instance.debugBoilOff)
                                debug0Display += hotTemp.ToString("F6");
                            
                            if (deltaTemp > 0)
                            {
                                double wettedArea = tank.totalArea * (tank.amount / tank.maxAmount);

                                double Q = deltaTemp /
                                    ((tank.wallThickness / (tank.wallConduction * wettedArea))
                                     + (tank.insulationThickness / (tank.insulationConduction * wettedArea))
                                     + (tank.resourceConductivity > 0 ? (0.01 / (tank.resourceConductivity * wettedArea)) : 0));

                                Q *= 0.001d; // convert to kilowatts

                                massLost = Q / tank.vsp;

                                if (RFSettings.Instance.debugBoilOff)
                                {
                                    // Only do debugging displays if debugging enabled in RFSettings

                                    debug1Display += Utilities.FormatFlux(Q);
                                    debug2Display += (massLost * 1000 * 3600).ToString("F4") + "kg/hr";
                                }
                                massLost *= deltaTime; // Frame scaling
                            }

                            double lossAmount = massLost / tank.density;

                            if (double.IsNaN(lossAmount))
                                print("[RF] " + tank.name + " lossAmount is NaN!");
                            else
                            {
                                double heatLost = 0d;
                                if (lossAmount > tank.amount)
                                {
                                    tank.amount = 0d;
                                }
                                else
                                {
                                    tank.amount -= lossAmount;

                                    heatLost = -massLost * tank.vsp;

                                    heatLost *= ConductionFactors;

                                    // See if there is boiloff byproduct and see if any other parts want to accept it.
                                    if (tank.boiloffProductResource != null)
                                    {
                                        double boiloffProductAmount = -(massLost / tank.boiloffProductResource.density);
                                        double retainedAmount = part.RequestResource(tank.boiloffProductResource.id, boiloffProductAmount, ResourceFlowMode.STAGE_PRIORITY_FLOW);
                                        massLost -= retainedAmount * tank.boiloffProductResource.density;
                                    }

                                    boiloffMass += massLost;

                                }
                                // subtract heat from boiloff
                                // subtracting heat in analytic mode is tricky: Analytic flux handling is 'cheaty' and tricky to predict. 
                                if (!analyticalMode)
                                    part.AddThermalFlux(heatLost * deltaTimeRecip * 2.0d); // double because there is a bug in FlightIntegrator that cuts internal flux in half
                                else
                                {
                                    analyticInternalTemp = analyticInternalTemp + (heatLost * part.thermalMassReciprocal);
                                    previewInternalFluxAdjust -= heatLost * deltaTimeRecip * 2d;
                                    if (deltaTime > 0)
                                        print(part.name + " deltaTime = " + deltaTime + ", heat lost = " + heatLost + ", thermalMassReciprocal = " + part.thermalMassReciprocal);
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
                Debug.LogWarning("[RF] Unable to parse outerInsulationFactor");
        }

        public void CalculateTankArea(out float totalTankArea)
        {
            totalTankArea = 0f;

            for (int i = 0; i< 6; ++i)
            {
                totalTankArea += part.DragCubes.WeightedArea[i];
            }
#if DEBUG
            Debug.Log("[RF] Part WeightedArea: " + part.name + " = " + totalTankArea.ToString("F2"));
            Debug.Log("[RF] Part Area: " + part.name + " = " + part.DragCubes.Area.ToString("F2"));
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

                        if (RFSettings.Instance.debugBoilOff)
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

        #region IAnalyticTemperatureModifier
        // Analytic Interface
        public void SetAnalyticTemperature(FlightIntegrator fi, double analyticTemp, double toBeInternal, double toBeSkin)
        {
            //if (analyticInternalTemp > lowestTankTemperature)
            if(this.supportsBoiloff)
                print(part.name + " Analytic Temp = " + analyticTemp.ToString() + ", Analytic Internal = " + toBeInternal.ToString() + ", Analytic Skin = " + toBeSkin.ToString());
            
            analyticSkinTemp = toBeSkin;
            analyticInternalTemp = toBeInternal;
            if (this.supportsBoiloff)
            {
                StartCoroutine(CalculateTankLossFunction(fi.timeSinceLastUpdate, true));
            }
        }

        public double GetSkinTemperature(out bool lerp)
        {
            lerp = true;
            return analyticSkinTemp;
        }

        public double GetInternalTemperature(out bool lerp)
        {
            // During analytic, pin our internal temperature. We'll figure out the difference and apply as much boiloff flux as needed for this to be valid.
            if (supportsBoiloff)
                lerp = true;
            else
                lerp = true;
            return analyticInternalTemp;
        }
        #endregion

        #region Analytic Preview Interface
        public void AnalyticInfo(FlightIntegrator fi, double sunAndBodyIn, double backgroundRadiation, double radArea, double absEmissRatio, double internalFlux, double convCoeff, double ambientTemp, double maxPartTemp)
        {
            if (_flightIntegrator != fi)
                fi = _flightIntegrator;

            sunAndBodyFlux = sunAndBodyIn;
            //previewInternalFluxAdjust = internalFlux;
            //float deltaTime = (float)(Planetarium.GetUniversalTime() - vessel.lastUT);
            //if (this.supportsBoiloff)
            //{
            //    StartCoroutine(CalculateTankLossFunction(TimeWarp.fixedDeltaTime, true));
            //}
        }

        public double InternalFluxAdjust()
        {
            return previewInternalFluxAdjust;
        }
        #endregion

        static void print(string msg)
        {
            MonoBehaviour.print("[RealFuels.ModuleFuelTankRF] " + msg);
        }
    }
}
