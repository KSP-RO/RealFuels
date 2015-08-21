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
        #endregion

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = false, guiName = "Ignited for ", guiUnits = "s", guiFormat = "F3")]
        public new float curveTime = 0f;//TODO
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = false, guiName = "")]
        public string statusDEV = "";//TODO
        
        ScreenMessage igniteFailIgnitions;
        ScreenMessage igniteFailResources;
        ScreenMessage ullageFail;

        #region Overrides
        public override void CreateEngine()
        {
            double thrustVariation = varyThrust * RFSettings.Instance.varyThrust;
            maxEngineTemp = nominalTcns / 0.8;
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

                    float d = requiredThrottle - currentThrottle;
                    float thisTick = engineAccelerationSpeed * deltaT * d * 10;/*MAGIC*/ //TODO engineAccelerationSpeed
                    if (d < 0) thisTick = engineDecelerationSpeed * deltaT * d * 10;
                    if (Math.Abs((double)d) > Math.Abs(thisTick)) {
                        currentThrottle += thisTick;
                    } else
                        currentThrottle = requiredThrottle;
                }
            } else
                currentThrottle = 0f;

            actualThrottle = Mathf.RoundToInt(currentThrottle * 100f);
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
        #endregion


        #region Info
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
            ambientTherm = new EngineThermodynamics();
            ambientTherm.FromAmbientConditions(pressure, temperature, density);
            inletTherm = new EngineThermodynamics();
            inletTherm.CopyFrom(ambientTherm);

            currentThrottle = 1f;
            lastPropellantFraction = 1d;
            bool oldE = EngineIgnited;
            EngineIgnited = true;
            devSolver.UpdateThrustRatio(1d);
            UpdateFlightCondition(ambientTherm, 0d, Vector3d.zero, 0d, true);

            double thrust_atm = (devSolver.GetThrust() * 0.001d);
            double Isp_atm = devSolver.GetIsp();
            double Cstar_atm = devSolver.Cstar;
            double Ct_atm = devSolver.Ct;
            ambientTherm = new EngineThermodynamics();
            ambientTherm.FromAmbientConditions(0d, 4d, 0d);
            UpdateFlightCondition(ambientTherm, 0d, Vector3d.zero, 0d, true);
            double thrust_vac = (devSolver.GetThrust() * 0.001d);
            double Isp_vac = devSolver.GetIsp();
            double Cstar_vac = devSolver.Cstar;
            double Ct_vac = devSolver.Ct;

            output += "<b>Max. Thrust(ASL): </b>" + thrust_atm.ToString("N2") + " kN\n";
            output += "<b>Max. Thrust(Vac.): </b>" + thrust_vac.ToString("N2") + " kN";
            output += ThrottleString()+"\n";
            output += "<b><color=#0099ff>Ignitions Available: </color></b>" + ignitions + "\n";
            output += "<b><color=#0099ff>Max. Burn time: </color></b>" + maxBurnTime + " Sec.\n";
            output += "<b>Isp(ASL): </b>" + Isp_atm.ToString("N2") + " s\n";
            output += "<b>Isp(Vac.): </b>" + Isp_vac.ToString("N2") + " s\n";
            if (!primaryField) {
                output += "<b>C*(ASL):</b> " + Cstar_atm.ToString("N2") + "m/s\n";
                output += "<b>Ct(ASL):</b> " + Ct_atm.ToString("N2") + "\n";
                output += "<b>C*(Vac):</b> " + Cstar_vac.ToString("N2") + "m/s\n";
                output += "<b>Ct(Vac):</b> " + Ct_vac.ToString("N2") + "\n";
            }

            output += "\n";
            EngineIgnited = oldE;
            return output;
        }
        public override string GetModuleTitle()
        {
            return "Engine (EngineDevelopment)";
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
                string unitsUsed = unitsSec.ToString("N4") + units;
                if (PartResourceLibrary.Instance != null)
                {
                    PartResourceDefinition def = PartResourceLibrary.Instance.GetDefinition(p.name);
                    if (def != null && def.density > 0)
                        unitsUsed += " (" + (unitsSec * def.density * 1000f).ToString("N4") + " kg)";
                }
                unitsUsed += rate;
                output += "- <b>" + pName + "</b>: " + unitsUsed + " maximum.\n";
                output += p.GetFlowModeDescription();
            }
            output += "<b>Chamber Pressure:</b>" + nominalPcns + " kPa\n<b>Chamber Temperature:</b>" + nominalTcns + " K\n";
            output += "<b>Nozzle Exit Pressure:</b>" + nominalPe + " kPa\n<b>Nozzle Throat Area:</b>" + At + " m^2\n";
            output += "<b>Flameout under: </b>" + (ignitionThreshold * 100f).ToString("0.#") + "% of requirement remaining.\n";

            if (!allowShutdown) output += "\n" + "<b><color=orange>Engine cannot be shut down!</color></b>";
            if (!allowRestart) output += "\n" + "<b><color=orange>If shutdown, engine cannot restart.</color></b>";

            currentThrottle = 0f;

            return output;
        }
        #endregion

    }
}
