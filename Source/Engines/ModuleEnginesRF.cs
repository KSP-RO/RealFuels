using System;
using System.Collections.Generic;
using UnityEngine;
using SolverEngines;
using System.Linq;

namespace RealFuels
{
    public class ModuleEnginesRF : ModuleEnginesSolver
    {
        public const string groupName = "ModuleEnginesRF";
        public const string groupDisplayName = "Engine";
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
        public float throttlePressureFedStartupMult = 5f;

        [KSPField]
        public float throttleDownMult = 100f;

        [KSPField]
        public float throttleClamp = 0.005f;

        [KSPField]
        public double ratedBurnTime = -1d;

        [KSPField]
        public double ratedContinuousBurnTime = -1d;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Symmetric Auto-cutoff", groupName = groupName, groupDisplayName = groupDisplayName),
            UI_Toggle(disabledText = "No", enabledText = "Yes", affectSymCounterparts = UI_Scene.Editor)]
        public bool autoCutoff = true;

        #region Thrust Curve
        [KSPField]
        public bool thrustCurveUseTime = false;
        [KSPField]
        public string curveResource = string.Empty;

        protected Propellant curveProp;

        [KSPField(guiName = "Ignited for ", guiUnits = "s", guiFormat = "F3", groupName = groupName)]
        public float curveTime = 0f;
        #endregion

        #region TweakScale
        protected double scale = 1d;
        protected double ScaleRecip => 1d / scale;
        #endregion

        protected bool instantThrottle = false;
        protected float MinThrottle => minFuelFlow / maxFuelFlow;
        protected SolverRF rfSolver = null;

        protected List<float> backupPropellantRatios = new List<float>();
        protected Propellant oxidizerPropellant;
        protected Propellant fuelPropellant;
        protected double mixtureRatio = 0d;
        protected int numRealPropellants = 0;

        #region Ullage/Ignition
        [KSPField]
        public Vector3 thrustAxis = Vector3.zero;

        [KSPField(isPersistant = true)]
        protected bool ignited = false;

        [KSPField(guiName = "Tags", guiActiveEditor = true, groupName = groupName, groupDisplayName = groupDisplayName)]
        public string tags;

        [KSPField(guiName = "Propellant", groupName = groupName, groupDisplayName = groupDisplayName)]
        public string propellantStatus = "Stable";

        [KSPField(guiName = "Mass", guiActiveEditor = true, guiFormat = "F3", guiUnits = "t", groupName = groupName, groupDisplayName = groupDisplayName)]
        public float dispMass = 0;

        [KSPField(guiName = "Max Thrust", guiActiveEditor = true, groupName = groupName)]
        public string sThrust;

        [KSPField(guiName = "Isp", guiActiveEditor = true, groupName = groupName)]
        public string sISP;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Predicted Residuals", guiFormat = "P2", groupName = groupName, groupDisplayName = groupDisplayName)]
        public double predictedMaximumResidualsGUI = 0d;

        public double predictedMaximumResiduals = 0d;

        [KSPField(guiActive = true, guiName = "Mixture Ratio", guiFormat = "F3", groupName = groupName, groupDisplayName = groupDisplayName)]
        public double currentMixtureRatio = 0d;

        [KSPField(guiName = "Ignitions Remaining", isPersistant = true, groupName = groupName, groupDisplayName = groupDisplayName)]
        public int ignitions = -1;

        [KSPField(guiName = "Ignitions Remaining", groupName = groupName)]
        public string sIgnitions = string.Empty;

        [KSPField(guiName = "Min Throttle", guiActiveEditor = true, groupName = groupName, guiFormat = "P0")]
        protected float _minThrottle;

        [KSPField(guiName = "Effective Spool-Up Time", groupName = groupName, groupDisplayName = groupDisplayName, guiFormat = "F2", guiUnits = "s")]
        public float effectiveSpoolUpTime;

        [KSPField]
        public bool pressureFed = false;

        [KSPField]
        public double autoVariationScale = -1d;

        [KSPField]
        public double varyIsp = -1d;

        [KSPField]
        public double varyMixture = -1d;

        [KSPField]
        public double varyFlow = -1d;

        [KSPField]
        public double varyResiduals = -1d;

        [KSPField]
        public double residualsThresholdBase = -1d;

        [KSPField]
        public bool ullage = false;

        [KSPField(isPersistant = true)]
        public int partSeed = -1;

        [KSPField(isPersistant = true)]
        public double calculatedResiduals = -1d;

        public Ullage.UllageSet ullageSet;
        protected ConfigNode ullageNode;

        protected bool reignitable = true;
        protected bool ullageOK = true;
        protected bool throttledUp = false;
        protected bool ShowPropStatus => pressureFed || (ullage && RFSettings.Instance.simulateUllage);

        protected double localVaryFlow = 0d;
        protected double localVaryIsp = 0d;
        protected double localVaryMixture = 0d;
        protected double localResidualsThresholdBase = 0d;
        protected double localVaryResiduals = 0d;

        [SerializeField]
        public List<ModuleResource> ignitionResources;
        ScreenMessage igniteFailIgnitions;
        ScreenMessage igniteFailResources;
        ScreenMessage ullageFail;
        #endregion

        protected bool started = false; // Track start state, don't handle MEC notification before first start.

        protected static System.Random staticRandom = new System.Random();

        #endregion

        #region Overrides
        public override void CreateEngine()
        {
            rfSolver = new SolverRF();
            if (!useAtmCurve)
                atmCurve = null;
            if (!useVelCurve)
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
            double totalVariation = (1d + localVaryFlow) * (1d + localVaryIsp) - 1d;
            chamberNominalTemp *= (1d - totalVariation);

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
                localVaryFlow,
                localVaryIsp,
                localVaryMixture,
                engineType == EngineType.SolidBooster,
                partSeed);

            rfSolver.SetScale(scale);

            engineSolver = rfSolver;
        }


        public override void CalculateEngineParams()
        {
            double variance = rfSolver.MixtureRatioVariance();
            bool vary = variance != 0d && oxidizerPropellant != null && numRealPropellants == 2;
            if (vary)
            {
                double newMR = mixtureRatio * (1d + variance);
                double newOxidizerMult = (newMR * mixtureDensity) / (1 + newMR);
                double newFuelMult = mixtureDensity - newOxidizerMult;
                oxidizerPropellant.ratio = (float)(newOxidizerMult / oxidizerPropellant.resourceDef.density);
                fuelPropellant.ratio = (float)(newFuelMult / fuelPropellant.resourceDef.density);
                currentMixtureRatio = newMR;
            }
            base.CalculateEngineParams();
            if (vary)
            {
                for (int i = 0; i < propellants.Count; ++i)
                {
                    propellants[i].ratio = backupPropellantRatios[i];
                }
            }
            if (ignited && predictedMaximumResiduals > 0d)
            {
                predictedMaximumResidualsGUI = predictedMaximumResiduals = localResidualsThresholdBase + localVaryResiduals;
                if (localVaryMixture > 0d)
                {
                    double massExtra = CalculateMaxExtraMassFromMRVariation();
                    predictedMaximumResiduals += massExtra * MinPropellantFraction();
                    predictedMaximumResidualsGUI += massExtra;
                }
            }
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

            // Determine thrustAxis when creating prefab
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                thrustAxis = Vector3.zero;
                for (int i = 0; i < thrustTransforms.Count; ++i)
                {
                    thrustAxis -= thrustTransforms[i].forward * thrustTransformMultipliers[i];
                }
                thrustAxis = thrustAxis.normalized;
            }

            // load ignition resources
            if (node.HasNode("IGNITOR_RESOURCE"))
                ignitionResources.Clear();
            foreach (ConfigNode n in node.GetNodes("IGNITOR_RESOURCE"))
            {
                ModuleResource res = new ModuleResource();
                res.Load(n);
                ignitionResources.Add(res);
            }

            node.TryGetNode("Ullage", ref ullageNode);

            localVaryFlow = varyFlow;
            localVaryIsp = varyIsp;
            localVaryMixture = varyMixture;
            localResidualsThresholdBase = residualsThresholdBase;
            localVaryResiduals = varyResiduals;

            numRealPropellants = propellants.Where(p => !p.ignoreForIsp && p.resourceDef.density != 0).Count();

            // Create reasonable values for variation
            // Solids
            if (engineType == EngineType.SolidBooster)
            {
                double propMultiplier = 1d;
                string propName = propellants.Count > 0 ? propellants[0].name : string.Empty;
                //Debug.Log("MERF: For part " + part.name + " is solid, found propellant " + propName);
                switch (propName)
                {
                    case "NGNC": propMultiplier = 8d; break;
                    case "PSPC": propMultiplier = 3.5d; break;
                    case "PUPE": propMultiplier = 1.4d; break;
                    case "PBAA": propMultiplier = 1.6d; break;
                    case "PBAN": propMultiplier = 1.2d; break;
                    case "CTPB": propMultiplier = 1.18d; break;
                    case "HTPB": propMultiplier = 1d; break;
                    default: propMultiplier = 1d; break;
                }
                if (autoVariationScale >= 0d)
                    propMultiplier = autoVariationScale;

                if (localVaryIsp < 0d)
                    localVaryIsp = 0.01d * propMultiplier;
                if (localVaryFlow < 0d)
                    localVaryFlow = 0.015d * propMultiplier;
                localVaryMixture = 0d;

                if (localResidualsThresholdBase < 0d)
                    localResidualsThresholdBase = 0.01d * propMultiplier;
                if (localVaryResiduals < 0d)
                    localVaryResiduals = 0.003d * propMultiplier;
            }
            // Liquids
            else
            {
                // Detect upper vs. lower? How?
                if (pressureFed)
                {
                    if (localVaryIsp < 0d)
                        localVaryIsp = 0.01d * (autoVariationScale >= 0d ? autoVariationScale : 1d);
                    if (localVaryFlow < 0d)
                        localVaryFlow = 0.05d * (autoVariationScale >= 0d ? autoVariationScale : 1d);
                    if (localVaryMixture < 0d)
                        localVaryMixture = 0.05d * (autoVariationScale >= 0d ? autoVariationScale : 1d);

                    if (localResidualsThresholdBase < 0d)
                        localResidualsThresholdBase = 0.008d * (autoVariationScale >= 0d ? autoVariationScale : 1d);
                    if (localVaryResiduals < 0d)
                        localVaryResiduals = 0d;
                }
                else
                {
                    if (localVaryIsp < 0d)
                        localVaryIsp = 0.003d * (autoVariationScale >= 0d ? autoVariationScale : 1d);
                    if (localVaryFlow < 0d)
                        localVaryFlow = 0.005d * (autoVariationScale >= 0d ? autoVariationScale : 1d);
                    if (localVaryMixture < 0d)
                        localVaryMixture = 0.005d * (autoVariationScale >= 0d ? autoVariationScale : 1d);

                    if (residualsThresholdBase < 0d)
                        localResidualsThresholdBase = 0.004d * (autoVariationScale >= 0d ? autoVariationScale : 1d);
                    if (localVaryResiduals < 0d)
                        localVaryResiduals = 0d;
                }

                if (ignitions == -1 || ignitions > 4)
                    localResidualsThresholdBase *= 2d;
                else if (ignitions == 0)
                    localResidualsThresholdBase *= 0.8d;
                else
                    localResidualsThresholdBase *= (1d + (ignitions - 1) * 0.25d);
            }
            // Double-check mixture variance makes sense
            if (numRealPropellants != 2)
                localVaryMixture = 0d;

            localVaryFlow *= RFSettings.Instance.varianceAndResiduals;
            localVaryIsp *= RFSettings.Instance.varianceAndResiduals;
            localVaryMixture *= RFSettings.Instance.varianceAndResiduals;
            localResidualsThresholdBase *= RFSettings.Instance.varianceAndResiduals;
            localVaryResiduals *= RFSettings.Instance.varianceAndResiduals;
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
            CalcEffectiveSpoolUpTime();

            if (!HighLogic.LoadedSceneIsFlight)
            {
                partSeed = -1;
                calculatedResiduals = localResidualsThresholdBase;
                ignited = false;
            }
            else
            {
                if (partSeed == -1)
                {
                    // using egg's formula here that ensures a gradual climb from 0, 0.5 as median, and a very thin tail past 1.0
                    // egg will comment further here
                    calculatedResiduals = localResidualsThresholdBase + localVaryResiduals * UtilMath.Clamp01(Math.Exp(0.421404d * Utilities.GetNormal(staticRandom, 0d) - Math.Log(2d)));
                    partSeed = staticRandom.Next();
                }
            }

            base.Start();
            if (!(engineSolver is SolverRF)) CreateEngine();

            // Setup for mixture variation
            backupPropellantRatios = propellants.Select(p => p.ratio).ToList();
            var props = propellants.Where(p => !p.ignoreForIsp && p.resourceDef.density > 0).ToArray();
            if (props.Length >= 2)
            {
                fuelPropellant = props[0];
                oxidizerPropellant = props[1];
                currentMixtureRatio = mixtureRatio = (oxidizerPropellant.ratio * oxidizerPropellant.resourceDef.density) / (fuelPropellant.ratio * fuelPropellant.resourceDef.density);
            }
            else
                fuelPropellant = oxidizerPropellant = null;

            predictedMaximumResidualsGUI = predictedMaximumResiduals = localResidualsThresholdBase + localVaryResiduals;
            if (localVaryMixture > 0d)
            {
                double massExtra = CalculateMaxExtraMassFromMRVariation();
                predictedMaximumResidualsGUI += massExtra;
                if (HighLogic.LoadedSceneIsFlight)
                    massExtra *= MinPropellantFraction();
                predictedMaximumResiduals += massExtra;
            }

            ullageSet = new Ullage.UllageSet(this);
            ullageSet.Load(ullageNode);

            Fields[nameof(ignitions)].guiActive = ignitions >= 0 && RFSettings.Instance.limitedIgnitions;
            Fields[nameof(tags)].guiActiveEditor = ShowPropStatus;
            Fields[nameof(propellantStatus)].guiActive = Fields[nameof(propellantStatus)].guiActiveEditor = ShowPropStatus;
            Fields[nameof(sIgnitions)].guiActiveEditor = RFSettings.Instance.limitedIgnitions;

            igniteFailIgnitions = new ScreenMessage($"<color=orange>[{part.partInfo.title}]: no ignitions remaining!</color>", 5f, ScreenMessageStyle.UPPER_CENTER);
            igniteFailResources = new ScreenMessage($"<color=orange>[{part.partInfo.title}]: insufficient resources to ignite!</color>", 5f, ScreenMessageStyle.UPPER_CENTER);
            ullageFail = new ScreenMessage($"<color=orange>[{part.partInfo.title}]: vapor in feedlines, shut down!</color>", 5f, ScreenMessageStyle.UPPER_CENTER);

            Fields[nameof(thrustPercentage)].guiActive = Fields[nameof(thrustPercentage)].guiActiveEditor = minFuelFlow != maxFuelFlow;
            Fields[nameof(thrustCurveDisplay)].guiActive = useThrustCurve;

            var group = Fields[nameof(propellantStatus)].group;
            Fields[nameof(engineTemp)].group = group;
            Fields[nameof(actualThrottle)].group = group;
            Fields[nameof(realIsp)].group = group;
            Fields[nameof(finalThrust)].group = group;
            Fields[nameof(propellantReqMet)].group = group;
            Fields[nameof(fuelFlowGui)].group = group;
            if (Fields[nameof(massFlowGui)] != null)
                Fields[nameof(massFlowGui)].group = group;
            Fields[nameof(currentMixtureRatio)].group = group;

            Fields[nameof(currentMixtureRatio)].guiActive = oxidizerPropellant != null && mixtureRatio > 0d;

            Fields[nameof(effectiveSpoolUpTime)].guiActive = Fields[nameof(effectiveSpoolUpTime)].guiActiveEditor = engineType != EngineType.SolidBooster;

            SetFields();
            started = true;
        }

        private void OnToggleDisplayMode(BaseField f, object obj) => SetFields();

        private void SetFields()
        {
            _minThrottle = MinThrottle;
            tags = pressureFed ? "<color=orange>Pressure-Fed</color>" : string.Empty;
            if (ullage)
            {
                tags += pressureFed ? ", " : string.Empty;
                tags += "<color=yellow>Ullage</color>";
            }
            sISP = $"{atmosphereCurve.Evaluate(1):N0} (ASL) - {atmosphereCurve.Evaluate(0):N0} (Vac)";
            GetThrustData(out double thrustVac, out double thrustASL);
            sThrust = $"{Utilities.FormatThrust(thrustASL)} (ASL) - {Utilities.FormatThrust(thrustVac)} (Vac)";
            if (ignitions > 0)
                sIgnitions = $"{ignitions:N0}";
            else if (ignitions == -1)
                sIgnitions = "Unlimited";
            else
                sIgnitions = "<color=yellow>Ground Support Clamps</color>";

            dispMass = part.mass;
        }

        public virtual void Update()
        {
            if (!(ullageSet is Ullage.UllageSet && ShowPropStatus)) return;
            if (HighLogic.LoadedSceneIsEditor && !(part.PartActionWindow is UIPartActionWindow)) return;

            if (HighLogic.LoadedSceneIsEditor && pressureFed)
                ullageSet.EditorPressurized();

            if (pressureFed && !ullageSet.PressureOK())
                propellantStatus = "<color=red>Needs high pressure tanks</color>";
            else if (HighLogic.LoadedSceneIsFlight && ullage && RFSettings.Instance.simulateUllage)
            {
                propellantStatus = ullageSet.GetUllageState(out Color ullageColor);
                part.stackIcon.SetIconColor(ullageColor);
            }
            else
                propellantStatus = pressureFed ? "Feed pressure OK" : "Nominal";
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
                responseRate = 1E6f;
        }

        protected virtual float CalcUpdatedThrottle(float currThrottle, float reqThrottle)
        {
            float igniteLevel = 0.01f * throttleIgniteLevelMult;
            // This yields F-1 like curves where F-1 responserate is about 1.
            float deltaThrottle = reqThrottle - currThrottle;
            int deltaThrottleSign = Math.Sign(deltaThrottle);
            if (deltaThrottle != 0)
            {
                float deltaThisTick = throttleResponseRate * TimeWarp.fixedDeltaTime;
                deltaThrottle = Math.Abs(deltaThrottle);
                // FIXME this doesn't actually matter much because we force-set to 0 if not ignited...
                if (deltaThrottleSign < 0 && currThrottle <= igniteLevel)
                    deltaThisTick *= throttleDownMult;

                if (currThrottle > igniteLevel)
                {
                    float invDelta = 1f - deltaThrottle;
                    deltaThisTick *= (1f - invDelta * invDelta) * 5f * throttleStartedMult;
                }
                else
                    deltaThisTick *= 0.0005f + 4.05f * currThrottle * throttleStartupMult * (pressureFed ? throttlePressureFedStartupMult : 1);

                if (deltaThrottle > deltaThisTick && deltaThrottle > throttleClamp)
                    currThrottle += deltaThisTick * deltaThrottleSign;
                else
                    currThrottle = reqThrottle;
            }
            return currThrottle;
        }

        public override void UpdateThrottle()
        {
            if (throttleLocked)
                requestedThrottle = thrustPercentage * 0.01f; // We are overriding Solver's determination, so we have to include thrust limiter.

            if (ignited)
            {
                // thrustPercentage is already multiplied in by SolverEngines, don't include it here.
                float requiredThrottle = Mathf.Lerp(MinThrottle, 1f, requestedThrottle);
                currentThrottle = instantThrottle ? requiredThrottle : CalcUpdatedThrottle(currentThrottle, requiredThrottle);
            }
            else
                currentThrottle = 0f;

            actualThrottle = (int)(currentThrottle * 100f);
        }

        // Compute the 'effective spool-up time.' That is, the total spool-up time from 0% to 100%,
        // minus the total amount of fractional thrust produced during spool-up converted to the
        // equivalent amount of time firing at full thrust.
        //
        // Sketch:
        // The actual spool-up curve:
        // |          .-----
        // |        /
        // |      /
        // |.---'
        // +----------------> time
        // |~~~~~~~~~~|       total spool-up time
        //
        // is treated instead as:
        // |       ---------
        // |       |
        // |       |
        // |_______|
        // +----------------> time
        // |~~~~~~~~~~|       total spool-up time with original curve
        // |~~~~~~~|          effective spool-up time
        //         |~~|       equivalent amount of thrust as produced by the original curve, but at 100%
        protected virtual void CalcEffectiveSpoolUpTime()
        {
            float currThrottle = 0f, integratedThrottle = 0f, deltaT = 0f;
            while (currThrottle < 1f)
            {
                currThrottle = CalcUpdatedThrottle(currThrottle, 1f);
                integratedThrottle += currThrottle * TimeWarp.fixedDeltaTime;
                deltaT += TimeWarp.fixedDeltaTime;
            }
            effectiveSpoolUpTime = deltaT - integratedThrottle;
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
            if (allowShutdown)
                ignited = false; // FIXME handle engine spinning down, non-instant shutoff.
        }

        public override void UpdateSolver(EngineThermodynamics ambientTherm, double altitude, Vector3d vel, double mach, bool sIgnited, bool oxygen, bool underwater)
        {
            UnityEngine.Profiling.Profiler.BeginSample("ModuleEnginesRF.UpdateSolver");
            throttledUp = false;

            // handle ignition
            if (HighLogic.LoadedSceneIsFlight)
            {
                float controlThrottle = independentThrottle ? independentThrottlePercentage * 0.01f : vessel.ctrlState.mainThrottle;
                if (controlThrottle > 0f || throttleLocked)
                    throttledUp = true;
                else
                    ignited = false; // FIXME handle engine spinning down, non-instant shutoff.
                IgnitionUpdate();

                // Ullage
                if (ullage && RFSettings.Instance.simulateUllage)
                {
                    if (EngineIgnited && ignited && throttledUp && rfSolver.GetRunning())
                    {
                        double state = ullageSet.GetUllageStability();
                        double testValue = Math.Pow(state, RFSettings.Instance.stabilityPower);
                        if (staticRandom.NextDouble() > testValue)
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
                    thrustCurveRatio = (float)((curveProp.totalResourceAvailable - curveProp.totalResourceCapacity * calculatedResiduals) / (curveProp.totalResourceCapacity * (1d - calculatedResiduals)));
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
            bool wasIgnited = ignited;
            base.UpdateSolver(ambientTherm, altitude, vel, mach, ignited, oxygen, CheckTransformsUnderwater());

            // Post-update: shutdown symmetry counterparts if we exhausted our own propellants
            if (autoCutoff && allowShutdown && wasIgnited != rfSolver.GetRunning() && lastPropellantFraction <= 0d)
            {
                int idx = part.Modules.IndexOf(this);
                foreach (Part p in part.symmetryCounterparts)
                {
                    if (p != part)
                    {
                        ModuleEnginesRF otherMERF = p.Modules[idx] as ModuleEnginesRF;
                        if (otherMERF != null && otherMERF.ignited)
                        {
                            otherMERF.Flameout("No propellants", false, otherMERF.ignited);
                            otherMERF.ignited = false;
                            otherMERF.reignitable = false;
                        }
                    }
                }
            }
            UnityEngine.Profiling.Profiler.EndSample();
        }
        #endregion

        #region Interface
        public void SetScale(double newScale)
        {
            scale = newScale;
            rfSolver?.SetScale(scale);
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

        protected void GetThrustData(out double thrustVac, out double thrustASL)
        {
            rfSolver.SetPropellantStatus(true, true);

            bool oldE = EngineIgnited;
            bool oldIg = ignited;
            float oldThrottle = currentThrottle;
            double oldLastPropellantFraction = lastPropellantFraction;

            currentThrottle = 1f;
            lastPropellantFraction = 1d;
            EngineIgnited = true;
            ignited = true;

            ambientTherm = EngineThermodynamics.StandardConditions(true);
            inletTherm = ambientTherm;

            rfSolver.UpdateThrustRatio(1d);
            rfSolver.SetPropellantStatus(true, true);

            UpdateSolver(EngineThermodynamics.StandardConditions(true), 0d, Vector3d.zero, 0d, true, true, false);
            thrustASL = engineSolver.GetThrust() * 0.001d;
            double spaceHeight = Planetarium.fetch?.Home?.atmosphereDepth + 1000d ?? 141000d;
            UpdateSolver(EngineThermodynamics.VacuumConditions(true), spaceHeight, Vector3d.zero, 0d, true, true, false);
            thrustVac = engineSolver.GetThrust() * 0.001d;

            EngineIgnited = oldE;
            ignited = oldIg;
            currentThrottle = oldThrottle;
            lastPropellantFraction = oldLastPropellantFraction;
        }

        protected string GetThrustInfo()
        {
            string output = string.Empty;
            if (!(engineSolver is SolverRF))
                CreateEngine();

            GetThrustData(out double thrustVac, out double thrustASL);
            rfSolver.SetPropellantStatus(true, true);

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
            output += (output != string.Empty ? ", " : string.Empty) + "Ignitions: " + ((!RFSettings.Instance.limitedIgnitions || ignitions < 0) ? "Unlimited" : (ignitions > 0 ? ignitions.ToString() : "Ground Support Clamps"));
            output += (output != string.Empty ? ", " : string.Empty) + (ullage ? "Subject" : "Not subject") + " to ullage";
            output += "\n";

            return output;
        }

        public override string GetInfo()
        {
            string output = $"{GetThrustInfo()}" +
                $"<b>Engine Isp: </b>{atmosphereCurve.Evaluate(1):0.###} (ASL) - {atmosphereCurve.Evaluate(0):0.###} (Vac.)\n";
            output += $"{GetUllageIgnition()}\n";
            if (ratedBurnTime > 0d)
            {
                output += "<b>Rated Burn Time: </b>";
                if (ratedContinuousBurnTime > 0d)
                    output += $"{ratedContinuousBurnTime.ToString("F0")}/{ratedBurnTime.ToString("F0")}s\n";
                else
                    output += $"{ratedBurnTime.ToString("F0")}s\n";
            }
            output += $"<b><color=#99ff00ff>Propellants:</color></b>\n";

            foreach (Propellant p in propellants)
            {
                string units = (p.name == "ElectricCharge") ? "kW" : "L";
                string rate = (p.name == "ElectricCharge") ? string.Empty : "/s";
                float unitsSec = getMaxFuelFlow(p);
                string sUse = $"{unitsSec:G4}{units}{rate}";
                if (PartResourceLibrary.Instance?.GetDefinition(p.name) is PartResourceDefinition def && def.density > 0)
                    sUse += $" ({unitsSec * def.density * 1000f:G4} kg{rate})";
                output += $"- <b>{KSPUtil.PrintModuleName(p.name)}</b>: {sUse} maximum.\n";
                output += $"{p.GetFlowModeDescription()}";
            }
            output += $"<b>Variance: </b>{localVaryIsp:P2} Isp, {localVaryFlow:P1} flow, {localVaryMixture:P2} MR (stddev).\n";
            output += $"<b>Residuals: min </b>{localResidualsThresholdBase:P1} of propellant.\n";

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
                                if (staticRandom.NextDouble() > minResource && !CheatOptions.InfinitePropellant && !externalIgnition)
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

        #region Residuals
        public override bool PropellantAvailable()
        {
            for (int i = 0; i < propellants.Count; i++)
            {
                Propellant p = propellants[i];
                if (p.ignoreForIsp)
                {
                    if (p.totalResourceAvailable <= 0d)
                        return false;
                }
                else
                {
                    if (p.totalResourceAvailable / p.totalResourceCapacity < calculatedResiduals)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        protected double MinPropellantFraction()
        {
            double minFrac = 1d;
            for (int i = 0; i < propellants.Count; i++)
            {
                Propellant p = propellants[i];
                double frac = p.totalResourceAvailable / p.totalResourceCapacity;
                if (frac < minFrac)
                {
                    minFrac = frac;
                }
            }
            return UtilMath.Clamp01((minFrac - calculatedResiduals) / (1d - calculatedResiduals));
        }

        protected double CalculateMaxExtraMassFromMRVariation()
        {
            // Assume worst-case variation in mixture (to 1 standard deviation)
            double mTotal = 1d + mixtureRatio;
            double highMR = mixtureRatio * (1d + localVaryMixture);
            double newMROx = (highMR * mTotal) / (1 + highMR);
            double fuelRemaining = 1d - mixtureRatio / newMROx;
            double massExtra = fuelRemaining / mTotal;
            double lowMR = mixtureRatio * (1d - localVaryMixture);
            newMROx = (lowMR * mTotal) / (1 + lowMR);
            double newMRFuel = mTotal - newMROx;
            double oxRemaining = 1d - 1d / newMRFuel;
            return Math.Max(massExtra, oxRemaining * mixtureRatio / mTotal);
        }
        #endregion
    }
}
