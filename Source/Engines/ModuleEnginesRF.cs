using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;
using KSP;
using SolverEngines;

namespace RealFuels
{
    public class ModuleEnginesRF : ModuleEnginesSolver
    {
        #region Fields

        [KSPField]
        public float flowMultMin = 0.01f;

        [KSPField]
        public bool usesAir = false;


        [KSPField]
        public double varyThrust = 1d;

        [KSPField(isPersistant = false, guiActive = false)]
        public float Pe = -1;
        [KSPField(isPersistant = false, guiActive = false)]
        public float Mexh = -1;
        [KSPField(isPersistant = false, guiActive = false)]
        public float At = -1;
        [KSPField(isPersistant = false, guiActive = false)]
        public float Tcns = -1;
        [KSPField(isPersistant = false, guiActive = false)]
        public float Pcns = -1;
        [KSPField(isPersistant = false, guiActive = false)]
        public float maxMassFlow = -1;
        [KSPField(isPersistant = false, guiActive = false)]
        public float minMassFlow = -1;
        [KSPField(isPersistant = false, guiActive = false)]
        public float fuelFraction = -1;

        #region TweakScale
        protected double scale = 1d;
        protected double scaleRecip = 1d;
        #endregion


        protected bool instantThrottle = false;
        protected float throttleResponseRate;
        protected SolverRF rfSolver = null;

        #region Ullage/Ignition
        [KSPField]
        public Vector3 thrustAxis = Vector3.zero;

        [KSPField]
        public bool pressureFed = false;

        [KSPField]
        public bool ullage = false;

        [KSPField(guiName = "Ignitions Remaining", isPersistant = true)]
        public int ignitions = -1;

        [KSPField(guiName = "Propellant")]
        public string propellantStatus = "Stable";

        public Ullage.UllageSet ullageSet;
        protected bool ignited = false;
        protected bool reignitable = true;
        protected bool ullageOK = true;
        protected bool throttledUp = false;
        [SerializeField]
        public List<ModuleResource> ignitionResources;
        ScreenMessage igniteFailIgnitions;
        ScreenMessage igniteFailResources;
        ScreenMessage ullageFail;
        #endregion
        #endregion

        #region Overrides
        public override void CreateEngine()
        {
            rfSolver = new SolverRF();
            double thrustVariation = varyThrust * RFSettings.Instance.varyThrust;
            rfSolver.InitializeOverallEngineData(
                Tcns,
                Pcns,
                maxEngineTemp*0.8d,
                Pcns/0.8d,
                Pe,
                At,
                fuelFraction,
                8314d / Mexh,
                maxMassFlow,
                minMassFlow,
                machLimit,
                machHeatMult,
                multFlow,
                thrustVariation
                );
            
            rfSolver.SetScale(scale);
            
            engineSolver = rfSolver;
        }
        public override void OnAwake()
        {
            base.OnAwake();
            if (ignitionResources == null)
                ignitionResources = new List<ModuleResource>();
        }
        public override void OnLoad(ConfigNode node)
        {

            base.OnLoad(node);
            int pCount = propellants.Count;

            // Set from propellants
            bool instantThrottle = true;
            for (int i = 0; i < pCount; ++i)
            {
                if (RFSettings.Instance.instantThrottleProps.Contains(propellants[i].name))
                {
                    instantThrottle = false;
                }
                // any other stuff
            }

            // FIXME calculating throttle change rate
            if (!instantThrottle)
                throttleResponseRate = (float)(10d / Math.Sqrt(Math.Sqrt(part.mass * maxThrust)));
            else
                throttleResponseRate = 1000000f;

            CreateEngine();

            if (ullageSet == null)
                ullageSet = new Ullage.UllageSet(this);

            // Get thrust axis (only on create prefabs)
            if (part.partInfo == null || part.partInfo.partPrefab == null)
            {
                thrustAxis = Vector3.zero;
                foreach(Transform t in part.FindModelTransforms(thrustVectorTransformName))
                {
                    thrustAxis -= t.forward;
                }
                thrustAxis = thrustAxis.normalized;
            }
            ullageSet.SetThrustAxis(thrustAxis);

            // ullage
            if (node.HasNode("Ullage"))
            {
                ullageSet.Load(node.GetNode("Ullage"));
            }

            ullageSet.SetUllageEnabled(ullage);

            // load ignition resources
            if (node.HasNode("IGNITOR_RESOURCE"))
                ignitionResources.Clear();
            foreach (ConfigNode n in node.GetNodes("IGNITOR_RESOURCE"))
            {
                ModuleResource res = new ModuleResource();
                res.Load(n);
                ignitionResources.Add(res);
            }
        }
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            if (ullageSet != null)
            {
                ConfigNode ullageNode = new ConfigNode("Ullage");
                ullageSet.Save(ullageNode);
                node.AddNode(ullageNode);
            }
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
        }
        public override void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                if (ullageSet != null && pressureFed)
                {
                    if (ullageSet.EditorPressurized()) // we need to recheck each frame. Expensive, but short of messages....
                        propellantStatus = "Feed pressure OK";
                    else
                        propellantStatus = "Feed pressure too low";
                }
                else
                    propellantStatus = "OK";
            }
            base.FixedUpdate();
        }
        public override void UpdateThrottle()
        {
            if (throttleLocked)
                requestedThrottle = 1f;
            if (ignited)
            {
                if (instantThrottle)
                    currentThrottle = requestedThrottle * thrustPercentage * 0.01f;
                else
                {
                    if ((0.01 > engineAccelerationSpeed || 0.01 > engineDecelerationSpeed) && throttleResponseRate > 0.01) {
                        engineAccelerationSpeed = engineDecelerationSpeed = throttleResponseRate;
                    }
                    float requiredThrottle = requestedThrottle * thrustPercentage * 0.01f;
                    float deltaT = TimeWarp.fixedDeltaTime;

                    float d = requiredThrottle - currentThrottle;
                    float thisTick = (d > 0 ? engineAccelerationSpeed : engineDecelerationSpeed) * d * deltaT;
                    if (Math.Abs((double)d) > (d > 0 ? engineAccelerationSpeed : engineDecelerationSpeed) * deltaT) {
                        currentThrottle += thisTick;
                    } else {
                        currentThrottle = requiredThrottle;
                    }
                }
            }
            else
                currentThrottle = 0f;

            currentThrottle = Mathf.Max(0.000f, currentThrottle);
            actualThrottle = Mathf.RoundToInt(currentThrottle * 100f);
        }
        
        // from SolverEngines but we don't play FX here.
        [KSPEvent(guiActive = true, guiName = "Activate Engine")]
        public override void Activate()
        {
            if (!allowRestart && engineShutdown)
            {
                return; // If the engines were shutdown previously and restarting is not allowed, prevent restart of engines
            }
            if (noShieldedStart && part.ShieldedFromAirstream)
            {
                ScreenMessages.PostScreenMessage("<color=orange>[" + part.partInfo.title + "]: Cannot activate while stowed!</color>", 6f, ScreenMessageStyle.UPPER_LEFT);
                return;
            }

            EngineIgnited = true;
            if (allowShutdown) Events["Shutdown"].active = true;
            else Events["Shutdown"].active = false;
            Events["Activate"].active = false;
        }

        // set ignited in shutdown
        [KSPEvent(guiActive = true, guiName = "Shutdown Engine")]
        public override void Shutdown()
        {
            base.Shutdown();
            ignited = false;
        }

        public override void UpdateFlightCondition(EngineThermodynamics ambientTherm, double altitude, Vector3d vel, double mach, bool oxygen)
        {
            throttledUp = false;

            // handle ignition
            if (HighLogic.LoadedSceneIsFlight)
            {
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
                    if (EngineIgnited && ignited && throttledUp && rfSolver.GetRunning()) {
                        double state = ullageSet.GetUllageStability();
                        double testValue = Math.Pow(state, RFSettings.Instance.stabilityPower);
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
                if (!pressureOK)
                {
                    ignited = false;
                    reignitable = false;
                    propellantStatus = "Feed pressure too low"; // override ullage status indicator
                    Flameout("Lack of pressure");
                }

                rfSolver.SetEngineStatus(pressureOK, (ullageOK || !RFSettings.Instance.simulateUllage), ignited);
            }

            // Set part temp
            rfSolver.SetPartTemp(part.temperature);

            // do heat
            heatProduction = (float)(scaleRecip * rfSolver.GetHeat() / PhysicsGlobals.InternalHeatProductionFactor * part.thermalMassReciprocal);

            // run base method code
            base.UpdateFlightCondition(ambientTherm, altitude, vel, mach, oxygen);
        }
        #endregion

        #region Interface
        public void SetScale(double newScale)
        {
            scale = newScale;
            scaleRecip = 1d / scale;
            if(rfSolver != null)
                rfSolver.SetScale(scale);
        }
        #endregion

        #region Info
        protected string ThrottleString()
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
        protected string GetThrustInfo(bool primaryField)
        {
            string output = "";
            if (engineSolver == null || !(engineSolver is SolverRF))
                CreateEngine();
            rfSolver.SetEngineStatus(true, true, true);
            // get stats
            double pressure = 101.325d, temperature = 288.15d, density = 1.225d;
            if (Planetarium.fetch != null)
            {
                CelestialBody home = Planetarium.fetch.Home;
                if (home != null)
                {
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
            rfSolver.UpdateThrustRatio(1d);

            UpdateFlightCondition(ambientTherm, 0d, Vector3d.zero, 0d, true);
            double thrustASL = (engineSolver.GetThrust() * 0.001d);
            double IspASL = rfSolver.Isp;
            double CtASL = rfSolver.Ct;
            double CstarASL = rfSolver.Cstar;
            // get stats again
            double spaceHeight = 131000d;
            pressure = 0d;
            density = 0d;
            if (Planetarium.fetch != null)
            {
                CelestialBody home = Planetarium.fetch.Home;
                if (home != null)
                {
                    temperature = home.GetTemperature(home.atmosphereDepth + 1d);
                    spaceHeight = home.atmosphereDepth + 1000d;
                }
            }
            else
                temperature = PhysicsGlobals.SpaceTemperature;
            ambientTherm.FromAmbientConditions(pressure, temperature, density);

            UpdateFlightCondition(ambientTherm, spaceHeight, Vector3d.zero, 0d, true);
            double thrustVac = (engineSolver.GetThrust() * 0.001d);
            double IspVac = rfSolver.Isp;
            double CtVac = rfSolver.Ct;
            double CstarVac = rfSolver.Cstar;
            if (thrustASL != thrustVac)
            {
                output += (throttleLocked ? "<b>" : "<b>Max. ") + "Thrust (Vac.): </b>" + (thrustVac).ToString("0.0##") + " kN"
                    + "\n" + (throttleLocked ? "<b>" : "<b>Max. ") + "Thrust (ASL): </b>" + (thrustASL).ToString("0.0##") + " kN\n" + ThrottleString();
            }
            else
            {
                output += (throttleLocked ? "<b>" : "<b>Max. ") + "Thrust: </b>" + (thrustVac).ToString("0.0##") + " kN" + ThrottleString();
            }
            output += "<b>Isp(ASL): </b>" + IspASL.ToString("N2") + " s\n";
            output += "<b>Isp(Vac.): </b>" + IspVac.ToString("N2") + " s\n";
            if (!primaryField) {
                if (CstarASL != CstarVac) {
                    output += "<b>C*(Vac):</b> " + CstarVac.ToString("N2") + " m/s\n";
                    output += "<b>C*(ASL):</b> " + CstarASL.ToString("N2") + " m/s\n";
                } else {
                    output += "<b>C*:</b> " + CstarVac.ToString("N2") + " m/s\n";
                }
                output += "<b>Ct(Vac):</b> " + CtVac.ToString("N2") + "\n";
                output += "<b>Ct(ASL):</b> " + CtASL.ToString("N2") + "\n";
                output += "<b>Max.fuel flow:</b> " + maxMassFlow.ToString("N2") + " kg/s\n";
                output += "<b>Min.fuel flow:</b> " + minMassFlow.ToString("N2") + " kg/s\n";
                output += "<b>C.Pressure:</b>" + rfSolver.GetEnginePressure().ToString("N2") + " kPa\n<b>C.Temperature:</b>" + rfSolver.GetEngineTemp().ToString("N2") + " K\n";
            }
            output += "\n";
            EngineIgnited = oldE;
            return output;
        }

        public override string GetModuleTitle()
        {
            return "Engine (RealFuels)";
        }
        public override string GetPrimaryField()
        {
            string output = GetThrustInfo(true);
            if (pressureFed)
                output += "Pressure-fed";
            if (ignitions >= 0 && RFSettings.Instance.limitedIgnitions)
                output += (pressureFed ? ", " : "") + "Ignitions: " + ignitions;
            if (pressureFed || (ignitions >= 0 && RFSettings.Instance.limitedIgnitions))
                output += "\n";

            return output;
        }

        public override string GetInfo()
        {
            string output = GetThrustInfo(false);

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
            output += "<b>Flameout under: </b>" + (ignitionThreshold * 100f).ToString("0.#") + "% of requirement remaining.\n";

            if (!allowShutdown) output += "\n" + "<b><color=orange>Engine cannot be shut down!</color></b>";
            if (!allowRestart) output += "\n" + "<b><color=orange>If shutdown, engine cannot restart.</color></b>";

            currentThrottle = 0f;

            return output;
        }
        #endregion

        #region Helpers
        protected void IgnitionUpdate()
        {
            if (EngineIgnited && throttledUp)
            {
                if (!ignited && reignitable)
                {
                    reignitable = false;
                    if (ignitions == 0)
                    {
                        EngineIgnited = false; // don't play shutdown FX, just fail.
                        ScreenMessages.PostScreenMessage(igniteFailIgnitions);
                        FlightLogger.eventLog.Add("[" + FormatTime(vessel.missionTime) + "] " + igniteFailIgnitions.message);
                        Flameout("Ignition failed");
                        return;
                    }
                    else
                    {
                        if (RFSettings.Instance.limitedIgnitions) {
                            if (ignitions > 0)
                                ignitions--;

                            // try to ignite
                            int count = ignitionResources.Count - 1;
                            if (count >= 0) {
                                double minResource = 1f;
                                for (int i = count; i >= 0; --i) {
                                    double req = ignitionResources[i].amount;
                                    double amt = (float)part.RequestResource(ignitionResources[i].id, req);
                                    if (amt < req)
                                        minResource = Math.Min(minResource, (amt / req));
                                }

                                if (UnityEngine.Random.value > (float)minResource) {
                                    EngineIgnited = false; // don't play shutdown FX, just fail.
                                    ScreenMessages.PostScreenMessage(igniteFailResources);
                                    FlightLogger.eventLog.Add("[" + FormatTime(vessel.missionTime) + "] " + igniteFailResources.message);
                                    Flameout("Ignition failed");
                                    return;
                                }
                            }
                        }
                        ignited = true;
                        PlayEngageFX();
                    }
                }
            }
            else
            {
                currentThrottle = 0f;
                reignitable = true; // reset
                ullageOK = true;
                UnFlameout();
                ignited = false; // just in case
            }
        }
        #endregion
    }
}
