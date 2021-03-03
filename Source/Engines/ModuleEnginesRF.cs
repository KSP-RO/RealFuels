using System;
using System.Collections.Generic;
using UnityEngine;
using SolverEngines;
using System.Linq;

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
        public float throttlePressureFedStartupMult = 5f;

        [KSPField]
        public float throttleDownMult = 100f;

        [KSPField]
        public float throttleClamp = 0.005f;

        #region Thrust Curve
        [KSPField]
        public bool thrustCurveUseTime = false;
        [KSPField]
        public string curveResource = string.Empty;

        protected Propellant curveProp;

        [KSPField(guiName = "Ignited for ", guiUnits = "s", guiFormat = "F3")]
        public float curveTime = 0f;
        #endregion

        #region TweakScale
        protected double scale = 1d;
        protected double scaleRecip = 1d;
        #endregion

        protected bool instantThrottle = false;
        protected float MinThrottle => minFuelFlow / maxFuelFlow;
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

        [KSPField(isPersistant = true)]
        protected bool ignited = false;

        [KSPField(guiName = "Propellant")]
        public string propellantStatus = "Stable";

        public Ullage.UllageSet ullageSet;
        protected ConfigNode ullageNode;

        protected bool reignitable = true;
        protected bool ullageOK = true;
        protected bool throttledUp = false;
        protected bool showPropStatus = false;
        [SerializeField]
        public List<ModuleResource> ignitionResources;
        ScreenMessage igniteFailIgnitions;
        ScreenMessage igniteFailResources;
        ScreenMessage ullageFail;
        #endregion

        protected bool started = false; // Track start state, don't handle MEC notification before first start.

        #endregion

        #region Overrides
        public override void CreateEngine()
        {
            rfSolver = new SolverRF();
            if(!useAtmCurve)
                atmCurve = null;
            if(!useVelCurve)
                velCurve = null;

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
                part.name.GetHashCode());

            rfSolver.SetScale(scale);

            engineSolver = rfSolver;
        }

        public override void OnAwake()
        {
            base.OnAwake();
            ullageNode = new ConfigNode();
            if (thrustCurve == null)
                thrustCurve = new FloatCurve();
            if (ignitionResources == null)
                ignitionResources = new List<ModuleResource>();
        }
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            // load ignition resources
            if (node.HasNode("IGNITOR_RESOURCE"))
                ignitionResources.Clear();
            foreach (ConfigNode n in node.GetNodes("IGNITOR_RESOURCE"))
            {
                ModuleResource res = new ModuleResource();
                res.Load(n);
                ignitionResources.Add(res);
            }

            // Determine thrustAxis when creating prefab
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                thrustAxis = Vector3.zero;
                foreach (Transform t in part.FindModelTransforms(thrustVectorTransformName))
                {
                    thrustAxis -= t.forward;
                }
                thrustAxis = thrustAxis.normalized;
            }

            node.TryGetNode("Ullage", ref ullageNode);
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

        // Event fired by MEC after changing a part.
        public virtual void UpdateUsedBy() { }
        public virtual void OnEngineConfigurationChanged()
        {
            if (started) Start();       // Wait until we've started once to allow this path to restart
        }

        public override void Start()
        {
            if (curveResource != string.Empty)
                curveProp = propellants.FirstOrDefault(x => x.name.Equals(curveResource));
            useThrustCurve = curveProp is Propellant;

            CalcThrottleResponseRate(ref throttleResponseRate, ref instantThrottle);

            if (!HighLogic.LoadedSceneIsFlight) ignited = false;

            base.Start();
            if (!(engineSolver is SolverRF)) CreateEngine();

            ullageSet = new Ullage.UllageSet(this);
            ullageSet.Load(ullageNode);

            showPropStatus = pressureFed || (ullage && RFSettings.Instance.simulateUllage);

            Fields[nameof(ignitions)].guiActive = Fields[nameof(ignitions)].guiActiveEditor = ignitions >= 0 && RFSettings.Instance.limitedIgnitions;
            Fields[nameof(propellantStatus)].guiActive = Fields[nameof(propellantStatus)].guiActiveEditor = showPropStatus;
            Fields[nameof(pressureFed)].guiActive = true;

            igniteFailIgnitions = new ScreenMessage($"<color=orange>[{part.partInfo.title}]: no ignitions remaining!</color>", 5f, ScreenMessageStyle.UPPER_CENTER);
            igniteFailResources = new ScreenMessage($"<color=orange>[{part.partInfo.title}]: insufficient resources to ignite!</color>", 5f, ScreenMessageStyle.UPPER_CENTER);
            ullageFail = new ScreenMessage($"<color=orange>[{part.partInfo.title}]: vapor in feedlines, shut down!</color>", 5f, ScreenMessageStyle.UPPER_CENTER);

            Fields[nameof(thrustPercentage)].guiActive = Fields[nameof(thrustPercentage)].guiActiveEditor = minFuelFlow != maxFuelFlow;
            Fields[nameof(thrustCurveDisplay)].guiActive = useThrustCurve && HighLogic.LoadedSceneIsFlight;
            started = true;
        }

        Color ullageColor = XKCDColors.White;

        public virtual void Update()
        {
            if (!(ullageSet is Ullage.UllageSet && showPropStatus)) return;

            UnityEngine.Profiling.Profiler.BeginSample("ModuleEnginesRF.Update.EditorPressurized");
            if (HighLogic.LoadedSceneIsEditor && pressureFed)
                ullageSet.EditorPressurized();
            UnityEngine.Profiling.Profiler.EndSample();
            UnityEngine.Profiling.Profiler.BeginSample("ModuleEnginesRF.Update.PropellantStatus");

            propellantStatus = "Nominal";
            if (HighLogic.LoadedSceneIsEditor && !pressureFed)
                propellantStatus = "OK";
            else if (pressureFed && !ullageSet.PressureOK())
                propellantStatus = "Feed pressure too low";
            else if (pressureFed && ullageSet.PressureOK())
            {
                if (HighLogic.LoadedSceneIsEditor)
                    propellantStatus = "Feed pressure OK";
                else if (ullage && RFSettings.Instance.simulateUllage)
                {
                    propellantStatus = ullageSet.GetUllageState(out ullageColor);
                    part.stackIcon.SetIconColor(ullageColor);
                }
            }
            UnityEngine.Profiling.Profiler.EndSample();
        }

        public virtual void CalcThrottleResponseRate(ref float responseRate, ref bool instant)
        {
            instant = false;
            foreach (Propellant p in propellants)
                instant |= RFSettings.Instance.instantThrottleProps.Contains(p.name);

            // FIXME calculating throttle change rate
            if (!instant)
            {
                if (responseRate <= 0f)
                    responseRate = (float)(RFSettings.Instance.throttlingRate / Math.Log(Math.Max(RFSettings.Instance.throttlingClamp, Math.Sqrt(part.mass * maxThrust * maxThrust))));
            }
            else
                responseRate = 1000000f;
        }

        public override void UpdateThrottle()
        {
            if (throttleLocked)
                requestedThrottle = 1f;

            if (ignited)
            {
                float requiredThrottle = Mathf.Lerp(MinThrottle, 1f, requestedThrottle * thrustPercentage * 0.01f);

                if (instantThrottle)
                    currentThrottle = requiredThrottle;
                else
                {
                    float IGNITELEVEL = 0.01f * throttleIgniteLevelMult;
                    // This yields F-1 like curves where F-1 responserate is about 1.
                    float deltaT = TimeWarp.fixedDeltaTime;

                    float delta = requiredThrottle - currentThrottle;
                    int sign = Math.Sign(delta);
                    if (sign != 0)
                    {
                        float thisTick = throttleResponseRate * deltaT;
                        delta = Math.Abs(delta);
                        // FIXME this doesn't actually matter much because we force-set to 0 if not ignited...
                        if (sign < 0 && currentThrottle <= IGNITELEVEL)
                            thisTick *= throttleDownMult;

                        if (currentThrottle > IGNITELEVEL)
                        {
                            float invDelta = 1f - delta;
                            thisTick *= (1f - invDelta * invDelta) * 5f * throttleStartedMult;
                        }
                        else
                            thisTick *= 0.0005f + 4.05f * currentThrottle * throttleStartupMult * (pressureFed ? throttlePressureFedStartupMult : 1);

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
                ScreenMessages.PostScreenMessage($"<color=orange>[{part.partInfo.title}]: Cannot activate while stowed!</color>", 6f, ScreenMessageStyle.UPPER_LEFT);
                return;
            }

            EngineIgnited = true;
            base.Activate();
            Events["Shutdown"].active = allowShutdown;
            Events["Activate"].active = false;
        }

        // set ignited in shutdown
        public override void Shutdown()
        {
            base.Shutdown();
            ignited = false;
        }

        public override void UpdateSolver(EngineThermodynamics ambientTherm, double altitude, Vector3d vel, double mach, bool sIgnited, bool oxygen, bool underwater)
        {
            UnityEngine.Profiling.Profiler.BeginSample("ModuleEnginesRF.UpdateSolver");
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
                if (ullage && RFSettings.Instance.simulateUllage)
                {
                    if (EngineIgnited && ignited && throttledUp && rfSolver.GetRunning())
                    {
                        double state = ullageSet.GetUllageStability();
                        double testValue = Math.Pow(state, RFSettings.Instance.stabilityPower);
                        if (UnityEngine.Random.value > testValue)
                        {
                            ScreenMessages.PostScreenMessage(ullageFail);
                            FlightLogger.fetch.LogEvent($"[{FormatTime(vessel.missionTime)}] {ullageFail.message}");
                            reignitable = false;
                            ullageOK = false;
                            ignited = false;
                            Flameout("Vapor in feed line");
                        }
                    }
                }
                if (!ullageSet.PressureOK())
                {
                    Flameout("Lack of pressure", false, ignited);
                    ignited = false;
                    reignitable = false;
                }

                rfSolver.SetPropellantStatus(ullageSet.PressureOK(), ullageOK || !RFSettings.Instance.simulateUllage);

                // do thrust curve
                if (ignited && useThrustCurve)
                {
                    thrustCurveRatio = (float)(curveProp.totalResourceAvailable / curveProp.totalResourceCapacity);
                    thrustCurveDisplay = thrustCurve.Evaluate(thrustCurveUseTime ? curveTime : thrustCurveRatio);
                    if (thrustCurveUseTime && EngineIgnited)
                        curveTime += TimeWarp.fixedDeltaTime;
                    rfSolver.UpdateThrustRatio(thrustCurveDisplay);
                }
            }

            // Set part temp
            rfSolver.SetPartTemp(part.temperature);

            // do heat
            // heatProduction = (float)(scaleRecip * extHeatkW / PhysicsGlobals.InternalHeatProductionFactor * part.thermalMassReciprocal);
            heatProduction = 0;

            // run base method code
            base.UpdateSolver(ambientTherm, altitude, vel, mach, ignited, oxygen, CheckTransformsUnderwater());
            UnityEngine.Profiling.Profiler.EndSample();
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
            if (throttleLocked) { return ", throttle locked"; }
            if (MinThrottle == 1f) { return ", unthrottleable"; }
            if (MinThrottle < 0f || MinThrottle > 1f) { return string.Empty; }
            return $", {MinThrottle:P0} min throttle";
        }
        protected string GetThrustInfo()
        {
            string output = string.Empty;
            if (!(engineSolver is SolverRF))
                CreateEngine();
            rfSolver.SetPropellantStatus(true, true);

            ambientTherm = EngineThermodynamics.StandardConditions(true);
            inletTherm = ambientTherm;

            currentThrottle = 1f;
            lastPropellantFraction = 1d;
            bool oldE = EngineIgnited;
            EngineIgnited = true;
            bool oldIg = ignited;
            ignited = true;

            rfSolver.UpdateThrustRatio(1d);
            rfSolver.SetPropellantStatus(true, true);

            UpdateSolver(ambientTherm, 0d, Vector3d.zero, 0d, true, true, false);
            double thrustASL = engineSolver.GetThrust() * 0.001d;

            var weight = part.mass * (Planetarium.fetch?.Home?.GeeASL * 9.80665 ?? 9.80665);

            if (atmChangeFlow) // If it's a jet
            {
                if (throttleLocked || MinThrottle == 1f)
                {
                    output += $"<b>Static Thrust: </b>{Utilities.FormatThrust(thrustASL)} (TWR {thrustASL / weight:0.0##}), {(throttleLocked ? "throttle locked" : "unthrottleable")}\n";
                }
                else
                {
                    output += $"{MinThrottle:P0} min throttle\n";
                    output += $"<b>Max. Static Thrust: </b>{Utilities.FormatThrust(thrustASL)} (TWR {thrustASL / weight:0.0##})\n";
                    output += $"<b>Min. Static Thrust: </b>{Utilities.FormatThrust(thrustASL * MinThrottle)} (TWR {thrustASL * MinThrottle / weight:0.0##})\n";
                }

                if (useVelCurve) // if thrust changes with mach
                {
                    velCurve.FindMinMaxValue(out float vMin, out float vMax, out float tMin, out float tMax); // get the max mult, and thus report maximum thrust possible.
                    output += $"<b>Max. Thrust: </b>{Utilities.FormatThrust(thrustASL * vMax)} at Mach {tMax:0.#} (TWR {thrustASL * vMax / weight:0.0##})\n";
                }
            }
            else
            {
                // get stats again
                ambientTherm = EngineThermodynamics.VacuumConditions(true);
                double spaceHeight = Planetarium.fetch?.Home?.atmosphereDepth + 1000d ?? 141000d;
                UpdateSolver(ambientTherm, spaceHeight, Vector3d.zero, 0d, true, true, false);
                double thrustVac = (engineSolver.GetThrust() * 0.001d);

                if (throttleLocked || MinThrottle == 1f)
                {
                    var suffix = throttleLocked ? "throttle locked" : "unthrottleable";
                    if (thrustASL != thrustVac)
                    {
                        output += $"<b>Thrust (Vac): </b>{Utilities.FormatThrust(thrustVac)} (TWR {thrustVac / weight:0.0##}), {suffix}\n";
                        output += $"<b>Thrust (ASL): </b>{Utilities.FormatThrust(thrustASL)} (TWR {thrustASL / weight:0.0##}), {suffix}\n";
                    }
                    else
                    {
                        output += $"<b>Thrust: </b>{Utilities.FormatThrust(thrustVac)} (TWR {thrustVac / weight:0.0##}), {suffix}\n";
                    }
                }
                else
                {
                    output += $"{MinThrottle:P0} min throttle\n";
                    if (thrustASL != thrustVac)
                    {
                        output += $"<b>Max. Thrust (Vac): </b>{Utilities.FormatThrust(thrustVac)} (TWR {thrustVac / weight:0.0##})\n";
                        output += $"<b>Max. Thrust (ASL): </b>{Utilities.FormatThrust(thrustASL)} (TWR {thrustASL / weight:0.0##})\n";
                        output += $"<b>Min. Thrust (Vac): </b>{Utilities.FormatThrust(thrustVac * MinThrottle)} (TWR {thrustVac * MinThrottle / weight:0.0##})\n";
                        output += $"<b>Min. Thrust (ASL): </b>{Utilities.FormatThrust(thrustASL * MinThrottle)} (TWR {thrustASL * MinThrottle / weight:0.0##})\n";
                    }
                    else
                    {
                        output += $"<b>Max. Thrust: </b>{Utilities.FormatThrust(thrustVac)} (TWR {thrustVac / weight:0.0##})\n";
                        output += $"<b>Min. Thrust: </b>{Utilities.FormatThrust(thrustVac * MinThrottle)} (TWR {thrustVac * MinThrottle / weight:0.0##})\n";
                    }
                }
            }
            EngineIgnited = oldE;
            ignited = oldIg;
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
            string output = pressureFed ? "Pressure-fed" : string.Empty;
            output += (output != string.Empty ? ", " : string.Empty) + "Ignitions: " + ((!RFSettings.Instance.limitedIgnitions || ignitions < 0) ? "Unlimited" : (ignitions > 0 ? ignitions.ToString() : "Ground only"));
            output += (output != string.Empty ? ", " : string.Empty) + (ullage ? "Subject" : "Not subject") + " to ullage";
            output += "\n";

            return output;
        }

        public override string GetInfo()
        {
            string output = $"{GetThrustInfo()}" +
                $"<b>Engine Isp: </b>{atmosphereCurve.Evaluate(1):0.###} (ASL) - {atmosphereCurve.Evaluate(0):0.###} (Vac.)\n";
            output += $"{GetUllageIgnition()}\n";
            output += $"<b><color=#99ff00ff>Propellants:</color></b>\n";

            foreach (Propellant p in propellants)
            {
                string units = (p.name == "ElectricCharge") ? "kW" : "L";
                string rate = (p.name == "ElectricCharge") ? string.Empty : "/s";
                float unitsSec = getMaxFuelFlow(p);
                string sUse = $"{unitsSec:G4}{units}{rate}";
                if (PartResourceLibrary.Instance?.GetDefinition(p.name) is PartResourceDefinition def && def.density > 0)
                    sUse += $" ({unitsSec * def.density * 1000f:G4} kg{rate})";
                output += $"- <b>{KSPUtil.PrintModuleName(p.name)}</b>: {sUse} maximum.\n{p.GetFlowModeDescription()}";
            }
            output += $"<b>Flameout under: </b>{ignitionThreshold:P1} of requirement remaining.\n";

            if (!allowShutdown) output += "\n<b><color=orange>Engine cannot be shut down!</color></b>";
            if (!allowRestart) output += "\n<b><color=orange>If shutdown, engine cannot restart.</color></b>";

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
                    /* As long as you're on the pad, you can always ignite */
                    bool externalIgnition = vessel.FindPartModulesImplementing<LaunchClamp>().Count > 0;
                    reignitable = false;
                    if (ignitions == 0 && RFSettings.Instance.limitedIgnitions && !CheatOptions.InfinitePropellant && !externalIgnition)
                    {
                        EngineIgnited = false; // don't play shutdown FX, just fail.
                        ScreenMessages.PostScreenMessage(igniteFailIgnitions);
                        FlightLogger.fetch.LogEvent($"[{FormatTime(vessel.missionTime)}] {igniteFailIgnitions.message}");
                        Flameout("Ignition failed");
                        return;
                    }
                    else
                    {
                        if (RFSettings.Instance.limitedIgnitions)
                        {
                            if (ignitions > 0)
                                --ignitions;

                            // try to ignite
                            double minResource = 1d;
                            foreach (var resource in ignitionResources)
                            {
                                double req = resource.amount;
                                double amt = part.RequestResource(resource.id, req);
                                if (amt < req && req > 0d)
                                {
                                    amt += part.RequestResource(resource.id, (req - 0.99d * amt));
                                    if (amt < req)
                                    {
                                        minResource = Math.Min(minResource, (amt / req));
                                        print($"*RF* part {part.partInfo.title} requested {req} {resource.name} but got {amt}. MinResource now {minResource}");
                                    }
                                }
                            }
                            if (minResource < 1d)
                            {
                                if (UnityEngine.Random.value > (float)minResource && !CheatOptions.InfinitePropellant && !externalIgnition)
                                {
                                    EngineIgnited = false; // don't play shutdown FX, just fail.
                                    ScreenMessages.PostScreenMessage(igniteFailResources);
                                    FlightLogger.fetch.LogEvent($"[{FormatTime(vessel.missionTime)}] {igniteFailResources.message}");
                                    Flameout("Ignition failed"); // yes play FX
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
                UnFlameout(false);
                ignited = false; // just in case
            }
        }
        #endregion
    }
}
