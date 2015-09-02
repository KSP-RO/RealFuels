using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;
using KSP;
using SolverEngines;

namespace RealFuels
{
    public class ModuleEnginesDEV : ModuleEnginesRF
    {
        #region Fields

        [KSPField]
        public float Mexh;
        [KSPField]
        public float fuelFraction;
        [KSPField]
        public float nominalTcns;
        [KSPField]
        public float nominalPcns;

        [KSPField]
        public float At;
        [KSPField]
        public float nominalPe;

        [KSPField]
        public float minMassFlow;
        [KSPField]
        public float maxMassFlow;
        [KSPField]
        public float maxBurnTime;

        [KSPField]
        public string nozzleType;
        [KSPField]
        public string chamberType;


        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = false, guiName = "Ignited for ", guiUnits = "s", guiFormat = "F3")]
        public new float curveTime = 0f;//TODO
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = false, guiName = "")]
        public string statusDEV = "";//TODO

        protected VInfoBox overpressureBox = null;
        ScreenMessage igniteFailIgnitions;
        ScreenMessage igniteFailResources;
        ScreenMessage ullageFail;
        #endregion
        #region Overrides
        public override void CreateEngine()
        {
            double thrustVariation = varyThrust * RFSettings.Instance.varyThrust;
            maxEngineTemp = nominalTcns / 0.75;
            SolverDEV devSolver = new SolverDEV();
            engineSolver = devSolver;
            devSolver.InitializeOverallEngineData(
                nominalTcns,
                nominalPcns,
                maxEngineTemp * 0.8,
                nominalPcns / 0.8,
                nominalPe,
                At,
                fuelFraction,
                8314 / Mexh,
                maxMassFlow,
                minFuelFlow,
                thrustVariation,
                nozzleType,
                chamberType
                );
            maxFuelFlow = maxMassFlow * 0.001f;
            minFuelFlow = minMassFlow * 0.001f;
            minThrottle = minFuelFlow / maxFuelFlow;
            
#if DEBUG
            Debug.Log($"Createngine:Tcns:{nominalTcns},Pcns:{nominalPcns},maxEngineTemp:{maxEngineTemp},nominalPe:{nominalPe},At:{At},Mexh:{Mexh}");
#endif
            devSolver.SetScale(scale);
        }
        public override void Start()
        {
            base.Start();
            if (ullageSet == null)
                ullageSet = new Ullage.UllageSet(this);

            Fields["ignitions"].guiActive = Fields["ignitions"].guiActiveEditor = (ignitions >= 0 && RFSettings.Instance.limitedIgnitions);
            Fields["propellantStatus"].guiActive = Fields["propellantStatus"].guiActiveEditor = (pressureFed || (ullage && RFSettings.Instance.simulateUllage));

            igniteFailIgnitions = new ScreenMessage("<color=orange>[" + part.partInfo.title + "]: no ignitions remaining!</color>", 5f, ScreenMessageStyle.UPPER_CENTER);
            igniteFailResources = new ScreenMessage("<color=orange>[" + part.partInfo.title + "]: insufficient resources to ignite!</color>", 5f, ScreenMessageStyle.UPPER_CENTER);
            ullageFail = new ScreenMessage("<color=orange>[" + part.partInfo.title + "]: vapor in feedlines, shut down!</color>", 5f, ScreenMessageStyle.UPPER_CENTER);
        }
        public override void OnStart(PartModule.StartState state)
        {
            base.OnStart(state);

            Fields["thrustCurveDisplay"].guiActive = false;
        }
        public override void UpdateThrottle()
        {
            if (throttleLocked)
                requestedThrottle = 1f;
            if (ignited) {
                if (instantThrottle)
                    currentThrottle = requestedThrottle * thrustPercentage * 0.01f;
                else {
                    float requiredThrottle = requestedThrottle * thrustPercentage * 0.01f;
                    float deltaT = TimeWarp.fixedDeltaTime;
                    const float IGNITELEVEL = 0.01f;
                    float delta = requiredThrottle - currentThrottle;
                    float thisTick = engineAccelerationSpeed * deltaT;/*MAGIC*/
                    if (delta < 0) thisTick = engineDecelerationSpeed * deltaT;
                    if (delta != 0f) {
                        float sign = 1f;
                        if (delta < 0) {
                            sign = -1f;
                            delta = -delta;

                            // FIXME this doesn't actually matter much because we force-set to 0 if not ignited...
                            if (currentThrottle <= IGNITELEVEL)
                                thisTick *= throttleDownMult;
                        }

                        if (currentThrottle > IGNITELEVEL) {
                            float invDelta = 1f - delta;
                            thisTick *= (1f - invDelta * invDelta) * 2.4f;
                        } else
                            thisTick *= 0.0005f + 12.5f * currentThrottle;

                        if (delta > thisTick && delta > throttleClamp)
                            currentThrottle += thisTick * sign;
                        else
                            currentThrottle = requiredThrottle;
                    }


                }
            } else
                currentThrottle = 0f;

            actualThrottle = currentThrottle * 100f;
        }


        public override void UpdateFlightCondition(EngineThermodynamics ambientTherm, double altitude, Vector3d vel, double mach, bool oxygen)
        {
            throttledUp = false;
            if (!(engineSolver is SolverDEV)) {
                base.UpdateFlightCondition(ambientTherm, altitude, vel, mach, oxygen);
                return;
            }
            SolverDEV devSolver = (engineSolver as SolverDEV);
            // handle ignition
            if (HighLogic.LoadedSceneIsFlight && vessel != null) {
                if (vessel.ctrlState.mainThrottle > 0f || throttleLocked)
                    throttledUp = true;
                else
                    ignited = false;
                IgnitionUpdate();

                // Ullage
                bool pressureOK = ullageSet.PressureOK();
                propellantStatus = "Nominal";
                if (ullage && RFSettings.Instance.simulateUllage) {
                    propellantStatus = ullageSet.GetUllageState();
                    if (EngineIgnited && ignited && throttledUp && devSolver.GetRunning()) {
                        curveTime += TimeWarp.fixedDeltaTime * devSolver.overPressureRatio * devSolver.overTempRatio;
                        if (curveTime > maxBurnTime) {
                            devSolver.SetDamageFrac(UnityEngine.Mathf.Pow(curveTime / maxBurnTime, 0.05f));/*MAGIC*/
                        }
                        double state = ullageSet.GetUllageStability();
                        double testValue = Math.Pow(state, RFSettings.Instance.stabilityPower);
                        if (((devSolver.failed & SolverDEV.isFailed.IGNITION) != SolverDEV.isFailed.NONE) 
                            && (UnityEngine.Random.value > devSolver.Stability) 
                            && (UnityEngine.Random.value > devSolver.Stability))/*MAGIC*/
                                testValue *= Mathf.Pow(devSolver.Stability, 2);
                        if (UnityEngine.Random.value > testValue) {
                            ScreenMessages.PostScreenMessage(ullageFail);
                            FlightLogger.eventLog.Add("[" + FormatTime(vessel.missionTime) + "] " + ullageFail.message);
                            reignitable = false;
                            ullageOK = false;
                            ignited = false;
                            Flameout("Vapor in feed line");
                        }
                    }
                }
                if (!pressureOK) {
                    propellantStatus = "Feed pressure too low"; // override ullage status indicator
                    vFlameout("Lack of pressure", false, ignited);
                    ignited = false;
                    reignitable = false;
                }

                devSolver.SetEngineStatus(pressureOK, (ullageOK || !RFSettings.Instance.simulateUllage), ignited);
            }

            // Set part temp
            devSolver.SetPartTemp(part.temperature);

            // do heat
            heatProduction = (float)(scaleRecip * devSolver.GetHeat() / PhysicsGlobals.InternalHeatProductionFactor * part.thermalMassReciprocal);

            // run base method code
            base.UpdateFlightCondition(ambientTherm, altitude, vel, mach, oxygen);
            
            Fields["statusDEV"].guiName = "";
            Fields["statusDEV"].guiActive = true;
            statusDEV = devSolver.statusString;
        }
        override protected void UpdateTemp()
        {
            if (tempRatio > 1d) {
                FlightLogger.eventLog.Add("[" + FormatTime(vessel.missionTime) + "] " + part.partInfo.title + " melted its internals from heat.");
                part.explode();
            } else
                UpdateOverheatBox(tempRatio, tempGaugeMin);
            double P = (engineSolver as SolverDEV).GetEnginePressure();
            if (P > nominalPcns / 0.64) {
                FlightLogger.eventLog.Add("[" + FormatTime(vessel.missionTime) + "] " + part.partInfo.title + " exploded due to overpressure.");
                part.explode();
            } else
                UpdateOverpressureBox(P * 0.8 / nominalPcns, tempGaugeMin);
        }
        protected void UpdateOverpressureBox(double val, double minVal)
        {
            if (val >= (minVal - 0.00001d)) {
                if (overpressureBox == null) {
                    overpressureBox = part.stackIcon.DisplayInfo();
                    overpressureBox.SetMsgBgColor(XKCDColors.DarkRed.A(0.6f));
                    overpressureBox.SetMsgTextColor(XKCDColors.OrangeYellow.A(0.6f));
                    overpressureBox.SetMessage("Eng. Pres.");
                    overpressureBox.SetProgressBarBgColor(XKCDColors.DarkRed.A(0.6f));
                    overpressureBox.SetProgressBarColor(XKCDColors.OrangeYellow.A(0.6f));
                }
                double scalar = 1d / (1d - minVal);
                double scaleFac = 1f - scalar;
                float gaugeMin = (float)(scalar * minVal + scaleFac);
                overpressureBox.SetValue(Mathf.Clamp01((float)(val * scalar + scaleFac)), gaugeMin, 1.0f);
            } else {
                if (overpressureBox != null) {
                    part.stackIcon.RemoveInfo(overpressureBox);
                    overpressureBox = null;
                }
            }
        }
        #endregion
        #region Info
        public override string GetModuleTitle() => "Engine (EngineDevelopment)";
        protected new string ThrottleString()
        {
            string output = "";
            double throttleP = 0d;
            if(minFuelFlow > 0d)
                throttleP = minFuelFlow / maxFuelFlow * 100d;
            if (minFuelFlow == maxFuelFlow)
                throttleP = 100d;
            if (!throttleLocked)
            {
                if (throttleP > 0d && throttleP < 100d)
                    output += ", " + throttleP.ToString("N0") + "% min throttle";
                else if(throttleP == 100d)
                    output += ", unthrottleable";
            }
            else
                output += ", throttle locked";

            return output;
        }
        protected new string GetThrustInfo() => GetStaticThrustInfo(false);
        public override string GetPrimaryField() => GetStaticThrustInfo(true);
        protected string GetStaticThrustInfo(bool primaryField)//TODO WIP
        {
            string output = "";
            if (engineSolver == null || !(engineSolver is SolverDEV))
                CreateEngine();
            SolverDEV devSolver = (engineSolver as SolverDEV);
            devSolver.SetEngineStatus(true, true, true);
            // get stats
            double pressure = 101.325d, temperature = 288.15d, density = 1.225d;
            if (Planetarium.fetch != null) {
                CelestialBody home = Planetarium.fetch.Home;
                if (home != null) {
                    pressure = home.GetPressure(0d);
                    temperature = home.GetTemperature(0d);
                    density = home.GetDensity(pressure, temperature);
                }
            }


            currentThrottle = 1f;
            lastPropellantFraction = 1d;
            bool oldE = EngineIgnited;
            EngineIgnited = true;
            devSolver.UpdateThrustRatio(1d);

            ambientTherm = new EngineThermodynamics();
            ambientTherm.FromAmbientConditions(pressure, temperature, density);
            inletTherm = new EngineThermodynamics();
            inletTherm.CopyFrom(ambientTherm);
            UpdateFlightCondition(ambientTherm, 0d, Vector3d.zero, 0d, true);
            double thrust_atm = (devSolver.GetThrust() * 0.001d);
            double Isp_atm = devSolver.GetIsp();
            double Cstar_atm = devSolver.Cstar;
            double Ct_atm = devSolver.Ct;

            ambientTherm = new EngineThermodynamics();
            ambientTherm.FromAmbientConditions(0d, 4d, 0d);
            inletTherm = new EngineThermodynamics();
            inletTherm.CopyFrom(ambientTherm);
            UpdateFlightCondition(ambientTherm, 0d, Vector3d.zero, 0d, true);
            double thrust_vac = (devSolver.GetThrust() * 0.001d);
            double Isp_vac = devSolver.GetIsp();
            double Cstar_vac = devSolver.Cstar;
            double Ct_vac = devSolver.Ct;
            double P_vac = devSolver.GetEnginePressure();
            double T_vac = devSolver.GetEngineTemp();

            ambientTherm = new EngineThermodynamics();
            ambientTherm.FromAmbientConditions(pressure * 2, temperature, density * 2);
            inletTherm = new EngineThermodynamics();
            inletTherm.CopyFrom(ambientTherm);
            UpdateFlightCondition(ambientTherm, 0d, Vector3d.zero, 0d, true);
            double thrust_2atm = (devSolver.GetThrust() * 0.001d);
            double Isp_2atm = devSolver.GetIsp();
            double Cstar_2atm = devSolver.Cstar;
            double Ct_2atm = devSolver.Ct;


            FloatCurve tC = new FloatCurve();
            tC.Add(0, (float)Isp_vac);
            tC.Add(1, (float)Isp_atm);
            tC.Add(2, (float)Isp_2atm);
            atmosphereCurve = tC;
            maxThrust = (float)Math.Max(thrust_vac, thrust_atm);

            output += "<b>Max. Thrust(<color=#00FF99>ASL</color>/<color=#99CCFF>Vac.</color>):</b> <color=#00FF99>" + thrust_atm.ToString("N2") + "</color><b>/</b><color=#99CCFF>" + thrust_vac.ToString("N1") + "</color>kN";
            output += ThrottleString()+"\n";
            output += "<b>Isp(<color=#00FF99>ASL</color>/<color=#99CCFF>Vac.</color>):</b> <color=#00FF99>" + Isp_atm.ToString("N2") + "</color><b>/</b><color=#99CCFF>" + Isp_vac.ToString("N2") + "</color>s\n";
            output += "<b><color=#0099ff>Ignitions Available: </color></b>" + ignitions + "\n";
            output += "<b><color=#0099ff>Max. Burn time: </color></b>" + maxBurnTime + " Sec.\n";
            if (!primaryField) {
                output += "<b>C*(<color=#00FF99>ASL</color>/<color=#99CCFF>Vac.</color>):</b> <color=#00FF99>" + Cstar_atm.ToString("N2") + "</color><b>/</b><color=#99CCFF>" + Cstar_vac.ToString("N2") + "</color>m/s\n";
                output += "<b>Ct(<color=#00FF99>ASL</color>/<color=#99CCFF>Vac.</color>):</b> <color=#00FF99>" + Ct_atm.ToString("N2") + "</color><b>/</b><color=#99CCFF>" + Ct_vac.ToString("N2") + "</color>\n";
                output += $"<b>Chamber Pressure:\n</b>{P_vac.ToString("N1")}<b>/</b>{nominalPcns.ToString("N1")}kPa\n<b>Chamber Temperature:\n</b>{T_vac.ToString("N1")}<b>/</b>{nominalTcns.ToString("N1")}K\n";
                output += $"<b>Nozzle Exit Pressure: </b>{nominalPe} kPa\n<b>Nozzle Throat Area:</b>{At} m^2\n";
            }

            output += "\n";
            EngineIgnited = oldE;
            return output;
        }
        public override string GetInfo()//TODO WIP
        {
            string output = GetStaticThrustInfo(false);

            //xoutput += "<b>Engine Isp: </b>" + (atmosphereCurve.Evaluate(1f)).ToString("0.###") + " (ASL) - " + (atmosphereCurve.Evaluate(0f)).ToString("0.###") + " (Vac.)\n";

            output += GetUllageIgnition();

            output += "\n<b><color=#99ff00ff>Propellants:</color></b>\n";
            Propellant p;
            string pName;
            for (int i = 0; i < propellants.Count; ++i)
            {
                p = propellants[i];
                pName = KSPUtil.PrintModuleName(p.name);
                string units = "L";
                string rate = " per second";
                if (p.name == "ElectricCharge")
                {
                    units = "kW";
                    rate = "";
                }
                float unitsSec = getMaxFuelFlow(p);
                string unitsUsed = unitsSec.ToString("N3") + units;
                if (PartResourceLibrary.Instance != null)
                {
                    PartResourceDefinition def = PartResourceLibrary.Instance.GetDefinition(p.name);
                    if (def != null && def.density > 0)
                        unitsUsed += " (" + (unitsSec * def.density * 1000f).ToString("N3") + " kg)";
                }
                unitsUsed += rate;
                output += "- <b>" + pName + "</b>: " + unitsUsed + " maximum.\n";
                output += p.GetFlowModeDescription();
            }
            output += "<b>Flameout under: </b>" + (ignitionThreshold * 100f).ToString("0.#") + "% of requirement remaining.\n";

            if (!allowShutdown) output += "\n" + "<b><color=orange>Engine cannot be shut down!</color></b>";
            if (!allowRestart) output += "\n" + "<b><color=orange>If shutdown, engine cannot restart.</color></b>";

            currentThrottle = 0f;

            return output;
        }
        #endregion

    }
}
