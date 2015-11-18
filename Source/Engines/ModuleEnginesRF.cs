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
        public double chamberNominalTemp = 0d;
        [KSPField]
        public double extHeatkW = 0d;

        [KSPField]
        public float flowMultMin = 0.01f;

        [KSPField]
        public bool usesAir = false;


        [KSPField]
        public double varyThrust = 1d;

        [KSPField]
        public float throttleResponseRate = -1f;

        [KSPField]
        public float throttleDownMult = 100f;

        [KSPField]
        public float throttleClamp = 0.005f;

        

        #region Thrust Curve
        [KSPField]
        public bool useThrustCurve = false;
        [KSPField]
        public bool thrustCurveUseTime = false;
        [KSPField]
        public FloatCurve thrustCurve;
        [KSPField]
        public string curveResource = "";

        protected int curveProp = -1;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "% Rated Thrust", guiUnits = "%", guiFormat = "F3")]
        public float thrustCurveDisplay = 100f;
        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "Fuel Ratio", guiUnits = "%", guiFormat = "F3")]
        public float thrustCurveRatio = 1f;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "Ignited for ", guiUnits = "s", guiFormat = "F3")]
        public float curveTime = 0f;
        #endregion

        #region TweakScale
        protected double scale = 1d;
        protected double scaleRecip = 1d;
        #endregion


        protected bool instantThrottle = false;
        protected float minThrottle = 0f;
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
            if(!useAtmCurve)
                atmCurve = null;
            if(!useVelCurve)
                velCurve = null;
            if (!useThrustCurve)
                thrustCurve = null;
            
            // FIXME quick temp hax
            if (useAtmCurve)
            {
                if (maxEngineTemp == 0d)
                    maxEngineTemp = 2000d;
                if (chamberNominalTemp == 0d)
                    chamberNominalTemp = 950d;
            }
            else
            {
                if (maxEngineTemp == 0d)
                    maxEngineTemp = 3600d;
                if (chamberNominalTemp == 0d)
                    chamberNominalTemp = 3400d;
                if (tempGaugeMin == 0.8d)
                    tempGaugeMin = 0.95d;
            }
            double thrustVariation = varyThrust * RFSettings.Instance.varyThrust;
            chamberNominalTemp *= (1d - thrustVariation);

            rfSolver.InitializeOverallEngineData(
                minFuelFlow,
                maxFuelFlow,
                atmosphereCurve,
                useAtmCurve ? atmCurve : null,
                useVelCurve ? velCurve : null,
                useAtmCurveIsp ? atmCurveIsp : null,
                useVelCurveIsp ? velCurveIsp : null,
                disableUnderwater,
                throttleResponseRate,
                chamberNominalTemp,
                machLimit,
                machHeatMult,
                flowMultMin,
                flowMultCap,
                flowMultCapSharpness,
                thrustVariation,
                (float)part.name.GetHashCode());
            
            rfSolver.SetScale(scale);
            
            engineSolver = rfSolver;
        }
        public override void OnAwake()
        {
            base.OnAwake();
            if (thrustCurve == null)
                thrustCurve = new FloatCurve();
            if (ignitionResources == null)
                ignitionResources = new List<ModuleResource>();
        }
        public override void OnLoad(ConfigNode node)
        {
            if (thrustCurve == null)
                thrustCurve = new FloatCurve();

            base.OnLoad(node);
            // Manually reload ignitions if not in editor
            if(!HighLogic.LoadedSceneIsEditor)
                node.TryGetValue("ignited", ref ignited);
            int pCount = propellants.Count;
            // thrust curve
            useThrustCurve = false;
            if (node.HasNode("thrustCurve") && node.HasValue("curveResource"))
            {
                if (node.GetValue("curveResource") != curveResource)
                {
                    Debug.Log("*RFE* ERROR: curveResource doesn't match node's!");
                    curveResource = node.GetValue("curveResource");
                }
                if (thrustCurve == null)
                {
                    Debug.Log("*RFE* ERROR: have curve node but thrustCurve is null!");
                    thrustCurve = new FloatCurve();
                    thrustCurve.Load(node.GetNode("thrustCurve"));
                }

                if (curveResource != "")
                {
                    for (int i = 0; i < pCount; ++i)
                    {
                        if (propellants[i].name.Equals(curveResource))
                        {
                            curveProp = i;
                            break;
                        }
                    }
                    if (curveProp != -1)
                    {
                        useThrustCurve = true;
                    }
                }
            }

            // Set from propellants
            bool instantThrottle = false;
            for (int i = 0; i < pCount; ++i)
            {
                if (RFSettings.Instance.instantThrottleProps.Contains(propellants[i].name))
                {
                    instantThrottle = true;
                }
                // any other stuff
            }

            // FIXME calculating throttle change rate
            if (!instantThrottle)
            {
                if (throttleResponseRate <= 0f)
                    throttleResponseRate = (float)(10d / Math.Log(Math.Max(1.1, Math.Sqrt(part.mass * maxThrust * maxThrust))));
            }
            else
                throttleResponseRate = 1000000f;

            minThrottle = minFuelFlow / maxFuelFlow;

            // set fields
            Fields["thrustCurveDisplay"].guiActive = useThrustCurve;
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
            // manually save ignited if not editor
            if (!HighLogic.LoadedSceneIsEditor)
                node.AddValue("ignited", ignited);

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
            
            Fields["thrustCurveDisplay"].guiActive = useThrustCurve && state != StartState.Editor;
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
                float requiredThrottle = Mathf.Lerp(minThrottle, 1f, requestedThrottle * thrustPercentage * 0.01f);

                if (instantThrottle)
                    currentThrottle = requiredThrottle;
                else
                {
                    const float IGNITELEVEL = 0.01f;
                    // This yields F-1 like curves where F-1 responserate is about 1.
                    float deltaT = TimeWarp.fixedDeltaTime;

                    float delta = requiredThrottle - currentThrottle;
                    if (delta != 0f)
                    {
                        float thisTick = throttleResponseRate * deltaT;
                        float sign = 1f;
                        if (delta < 0)
                        {
                            sign = -1f;
                            delta = -delta;

                            // FIXME this doesn't actually matter much because we force-set to 0 if not ignited...
                            if (currentThrottle <= IGNITELEVEL)
                                thisTick *= throttleDownMult;
                        }

                        if (currentThrottle > IGNITELEVEL)
                        {
                            float invDelta = 1f - delta;
                            thisTick *= (1f - invDelta * invDelta) * 2.4f;
                        }
                        else
                            thisTick *= 0.0005f + 12.5f * currentThrottle;

                        if (delta > thisTick && delta > throttleClamp)
                            currentThrottle += thisTick * sign;
                        else
                            currentThrottle = requiredThrottle;
                    }
                }
            }
            else
                currentThrottle = 0f;

            actualThrottle = (int)(currentThrottle * 100f);
        }
        
        // from SolverEngines but we don't play FX here.
        public override void Activate()
        {
            if (!allowRestart && engineShutdown)
            {
                return; // If the engines were shutdown previously and restarting is not allowed, prevent restart of engines
            }
            if (!shieldedCanActivate && part.ShieldedFromAirstream)
            {
                ScreenMessages.PostScreenMessage("<color=orange>[" + part.partInfo.title + "]: Cannot activate while stowed!</color>", 6f, ScreenMessageStyle.UPPER_LEFT);
                return;
            }

            EngineIgnited = true;

            if (allowShutdown)
                Events["Shutdown"].active = true;
            else
                Events["Shutdown"].active = false;

            Events["Activate"].active = false;
        }

        // set ignited in shutdown
        public override void Shutdown()
        {
            base.Shutdown();
            ignited = false;
        }

        public override void UpdateFlightCondition(EngineThermodynamics ambientTherm, double altitude, Vector3d vel, double mach, bool oxygen, bool underwater)
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
                if (ullage && RFSettings.Instance.simulateUllage)
                {
                    propellantStatus = ullageSet.GetUllageState();
                    if (EngineIgnited && ignited && throttledUp && rfSolver.GetRunning())
                    {
                        double state = ullageSet.GetUllageStability();
                        double testValue = Math.Pow(state, RFSettings.Instance.stabilityPower);
                        if (UnityEngine.Random.value > testValue)
                        {
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
                    propellantStatus = "Feed pressure too low"; // override ullage status indicator
                    Flameout("Lack of pressure", false, ignited);
                    ignited = false;
                    reignitable = false;
                }

                rfSolver.SetEngineStatus(pressureOK, (ullageOK || !RFSettings.Instance.simulateUllage), ignited);

                // do thrust curve
                if (ignited && useThrustCurve)
                {
                    thrustCurveRatio = (float)((propellants[curveProp].totalResourceAvailable / propellants[curveProp].totalResourceCapacity));
                    if (thrustCurveUseTime)
                    {
                        thrustCurveDisplay = thrustCurve.Evaluate(curveTime);
                        if (EngineIgnited)
                        {
                            curveTime += TimeWarp.fixedDeltaTime;
                        }
                    }
                    else
                    {
                        thrustCurveDisplay = thrustCurve.Evaluate(thrustCurveRatio);
                    }
                    rfSolver.UpdateThrustRatio(thrustCurveDisplay);
                    thrustCurveDisplay *= 100f;
                }
            }

            // Set part temp
            rfSolver.SetPartTemp(part.temperature);

            // do heat
            heatProduction = (float)(scaleRecip * extHeatkW / PhysicsGlobals.InternalHeatProductionFactor * part.thermalMassReciprocal);

            // run base method code
            base.UpdateFlightCondition(ambientTherm, altitude, vel, mach, oxygen, CheckTransformsUnderwater());
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
            if (!throttleLocked)
            {
                if (minThrottle > 0f && minThrottle < 1f)
                    output += ", " + (minThrottle*100f).ToString("N0") + "% min throttle";
                else if(minThrottle == 1f)
                    output += ", unthrottleable";
            }
            else
                output += ", throttle locked";

            return output;
        }
        protected string GetThrustInfo()
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

            UpdateFlightCondition(ambientTherm, 0d, Vector3d.zero, 0d, true, false);
            double thrustASL = (engineSolver.GetThrust() * 0.001d);

            if (atmChangeFlow) // If it's a jet
            {
                output += "<b>Static Thrust: </b>" + (thrustASL).ToString("0.0##") + " kN" + ThrottleString();
                if (useVelCurve) // if thrust changes with mach
                {
                    float vMin, vMax, tMin, tMax;
                    velCurve.FindMinMaxValue(out vMin, out vMax, out tMin, out tMax); // get the max mult, and thus report maximum thrust possible.
                    output += "\n<b>Max. Thrust: </b>" + (thrustASL* vMax).ToString("0.0##") + " kN Mach " + tMax.ToString("0.#");
                }
            }
            else
            {
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

                UpdateFlightCondition(ambientTherm, spaceHeight, Vector3d.zero, 0d, true, false);
                double thrustVac = (engineSolver.GetThrust() * 0.001d);

                if (thrustASL != thrustVac)
                {
                    output += (throttleLocked ? "<b>" : "<b>Max. ") + "Thrust (Vac.): </b>" + (thrustVac).ToString("0.0##") + " kN" + ThrottleString()
                        + "\n" + (throttleLocked ? "<b>" : "<b>Max. ") + "Thrust (ASL): </b>" + (thrustASL).ToString("0.0##") + " kN";
                }
                else
                {
                    output += (throttleLocked ? "<b>" : "<b>Max. ") + "Thrust: </b>" + (thrustVac).ToString("0.0##") + " kN" + ThrottleString();
                }
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
            return GetThrustInfo() + GetUllageIgnition();
        }
        public string GetUllageIgnition()
        {
            string output = "";
            if (pressureFed)
                output += "Pressure-fed";
            if (ignitions >= 0 && RFSettings.Instance.limitedIgnitions)
                output += (output != "" ? ", " : "") + "Ignitions: " + ignitions;
            if (!ullage)
                output += (output != "" ? ", " : "") + "Not subject to ullage";
            if (output != "")
                output += "\n";

            return output;
        }

        public override string GetInfo()
        {
            string output = GetThrustInfo();

            output += "<b>Engine Isp: </b>" + (atmosphereCurve.Evaluate(1f)).ToString("0.###") + " (ASL) - " + (atmosphereCurve.Evaluate(0f)).ToString("0.###") + " (Vac.)\n";

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
                    if (ignitions == 0 && RFSettings.Instance.limitedIgnitions)
                    {
                        EngineIgnited = false; // don't play shutdown FX, just fail.
                        ScreenMessages.PostScreenMessage(igniteFailIgnitions);
                        FlightLogger.eventLog.Add("[" + FormatTime(vessel.missionTime) + "] " + igniteFailIgnitions.message);
                        Flameout("Ignition failed");
                        return;
                    }
                    else
                    {
                        if (RFSettings.Instance.limitedIgnitions)
                        {
                            if (ignitions > 0)
                                ignitions--;

                            // try to ignite
                            int count = ignitionResources.Count - 1;
                            if (count >= 0)
                            {
                                double minResource = 1d;
                                for (int i = count; i >= 0; --i)
                                {
                                    double req = ignitionResources[i].amount;
                                    double amt = part.RequestResource(ignitionResources[i].id, req);
                                    if (amt < req && req > 0d)
                                    {
                                        amt += part.RequestResource(ignitionResources[i].id, (req - 0.99d * amt));
                                        if (amt < req)
                                        {
                                            minResource = Math.Min(minResource, (amt / req));
                                            print("*RF* part " + part.partInfo.title + " requested " + req + " " + ignitionResources[i].name + " but got " + amt + ". MinResource now " + minResource);
                                        }
                                    }
                                }
                                if (minResource < 1d)
                                {
                                    if (UnityEngine.Random.value > (float)minResource)
                                    {
                                        EngineIgnited = false; // don't play shutdown FX, just fail.
                                        ScreenMessages.PostScreenMessage(igniteFailResources);
                                        FlightLogger.eventLog.Add("[" + FormatTime(vessel.missionTime) + "] " + igniteFailResources.message);
                                        Flameout("Ignition failed"); // yes play FX
                                        return;
                                    }
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
                UnFlameout(false);
                ignited = false; // just in case
            }
        }
        #endregion
    }
}
