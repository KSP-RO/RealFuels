using System;
using System.Collections.Generic;
using UnityEngine;
using SolverEngines;
using System.Linq;
using KSP.Localization;
using ROUtils;
using System.Reflection;
using System.Reflection.Emit;
using RealFuels.Tanks;

namespace RealFuels
{
    public class ModuleEnginesRF : ModuleEnginesSolver
    {
        public const string groupName = "ModuleEnginesRF";
        public const string groupDisplayName = "#RF_Engine_ButtonName"; // Engine
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

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "#RF_EngineRF_SymmetricAutocutoff", groupName = groupName, groupDisplayName = groupDisplayName), // Symmetric Auto-cutoff
            UI_Toggle(disabledText = "#RF_disabletext", enabledText = "#RF_enabletext", affectSymCounterparts = UI_Scene.Editor)] // NoYes
        public bool autoCutoff = true;

        [KSPField(isPersistant = false)]
        public FloatCurve throttleCurve;

        #region Thrust Curve
        [KSPField]
        public bool thrustCurveUseTime = false;
        [KSPField]
        public string curveResource = string.Empty;

        protected Propellant curveProp;

        [KSPField(guiName = "#RF_EngineRF_IgnitedFor", guiUnits = "s", guiFormat = "F3", groupName = groupName)] // Ignited for 
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

        [KSPField(guiName = "#RF_EngineRF_Tags", guiActiveEditor = true, groupName = groupName, groupDisplayName = groupDisplayName)] // Tags
        public string tags;

        [KSPField(guiName = "#RF_EngineRF_Propellant", groupName = groupName, groupDisplayName = groupDisplayName)] // Propellant
        public string propellantStatus = Localizer.GetStringByTag("#RF_EngineRF_Stable"); // "Stable"

        [KSPField(guiName = "#RF_EngineRF_Mass", guiActiveEditor = true, guiFormat = "F3", guiUnits = "t", groupName = groupName, groupDisplayName = groupDisplayName)] // Mass
        public float dispMass = 0;

        [KSPField(guiName = "#RF_EngineRF_MaxThrust", guiActiveEditor = true, groupName = groupName)] // Max Thrust
        public string sThrust;

        [KSPField(guiName = "#RF_Engine_Isp", guiActiveEditor = true, groupName = groupName)] // Isp
        public string sISP;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "#RF_EngineRF_PredictedResiduals", guiFormat = "P2", groupName = groupName, groupDisplayName = groupDisplayName)] // Predicted Residuals
        public double predictedMaximumResidualsGUI = 0d;

        public double predictedMaximumResiduals = 0d;

        [KSPField(guiActive = true, guiName = "#RF_EngineRF_MixtureRatio", guiFormat = "F3", groupName = groupName, groupDisplayName = groupDisplayName)] // Mixture Ratio
        public double currentMixtureRatio = 0d;

        [KSPField(guiName = "#RF_EngineRF_IgnitionsRemaining", isPersistant = true, groupName = groupName, groupDisplayName = groupDisplayName)] // Ignitions Remaining
        public int ignitions = -1;

        [KSPField(guiName = "#RF_EngineRF_IgnitionsRemaining", groupName = groupName)] // Ignitions Remaining
        public string sIgnitions = string.Empty;

        [KSPField(guiName = "#RF_EngineRF_MinThrottle", guiActiveEditor = true, groupName = groupName, guiFormat = "P0")] // Min Throttle
        protected float _minThrottle;

        [KSPField(guiName = "#RF_EngineRF_EffectiveSpoolUpTime", groupName = groupName, groupDisplayName = groupDisplayName, guiFormat = "F2", guiUnits = "s")] // Effective Spool-Up Time
        public float effectiveSpoolUpTime;

        [KSPField]
        public bool pressureFed = false;

        [KSPField]
        public float requiredTankPressure = 1.2f;

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

        Func<PartSet, Dictionary<int, PartSet.ResourcePrioritySet>> PartSetPullListGetter;
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
                UpdateResiduals();
            }
        }

        private readonly Dictionary<int, PartSet.ResourcePrioritySet> savedResourceSets = new Dictionary<int, PartSet.ResourcePrioritySet>();
        private readonly Dictionary<int, PartSet.ResourcePrioritySet> propellantSetDict = new Dictionary<int, PartSet.ResourcePrioritySet>();
        // Swap in our revised resource list for each propellant, discovered during UpdatePropellantStatus
        public override double RequestPropellant(double mass)
        {
            savedResourceSets.Clear();
            var pullList = PartSetPullListGetter(part.crossfeedPartSet);
            foreach (var kvp in propellantSetDict)
            {
                savedResourceSets.Add(kvp.Key, part.crossfeedPartSet.GetResourceList(kvp.Key, true, false));
                pullList[kvp.Key] = kvp.Value;
            }
            var result = base.RequestPropellant(mass);
            foreach (var kvp in savedResourceSets)
                pullList[kvp.Key] = kvp.Value;
            return result;
        }

        protected override void UpdatePropellantStatus(bool doGauge = true)
        {
            if (propellants == null)
                return;
            foreach (var propellant in propellants)
            {
                CustomUpdateConnectedResources(propellant, part);
                if (propellant.drawStackGauge && doGauge)
                    UpdatePropellantGauge(propellant);
            }
        }

        //public void Propellant.UpdateConnectedResources(Part p) => p.GetConnectedResourceTotals(this.id, this.GetFlowMode(), out this.actualTotalAvailable, out this.totalResourceCapacity);
        // --> Part.GetConnectedResourceTotals(resourceID, flowMode, false, out amount, out maxAmount, pulling);
        // This is what stock currently does, there's no magic yet, although traversing a PartSet should be interesting
        // We can either replicate this ourselves (yuck!) OR we can let stock do the original work, and then refine
        // the PartSet
        private void CustomUpdateConnectedResources(Propellant propellant, Part p)
        {
            var resourceID = propellant.id;
            var flowMode = propellant.GetFlowMode();
            bool simulate = false;
            bool pulling = true;
            double amount = 0;
            double maxAmount = 0;
            PartSet partSet = null;
            switch (flowMode)
            {
                case ResourceFlowMode.ALL_VESSEL:
                case ResourceFlowMode.STAGE_PRIORITY_FLOW:
                case ResourceFlowMode.ALL_VESSEL_BALANCE:
                case ResourceFlowMode.STAGE_PRIORITY_FLOW_BALANCE:
                    if (p.ship != null & simulate && HighLogic.LoadedSceneIsEditor)
                        partSet = p.ship.resourcePartSet;
                    else if (p.vessel != null)
                        partSet = simulate ? p.vessel.simulationResourcePartSet : p.vessel.resourcePartSet;
                    break;
                case ResourceFlowMode.STACK_PRIORITY_SEARCH:
                case ResourceFlowMode.STAGE_STACK_FLOW:
                case ResourceFlowMode.STAGE_STACK_FLOW_BALANCE:
                    partSet = simulate ? p.simulationCrossfeedPartSet : p.crossfeedPartSet;
                    break;
                default:
                    var res = simulate ? p.SimulationResources : p.Resources;
                    res.GetFlowingTotals(resourceID, out amount, out maxAmount, pulling);
                    break;
            }
            if (partSet != null)
                CustomGetConnectedResourceTotals(partSet, propellantSetDict, resourceID, out amount, out maxAmount, pulling, simulate);
            propellant.actualTotalAvailable = amount;
            propellant.totalResourceCapacity = maxAmount;
        }

        //protected PartSet.ResourcePrioritySet GetOrCreateList(int id, bool pulling, bool simulate)
        //public PartSet.ResourcePrioritySet PartSet.GetResourceList(int id, bool pulling, bool simulate) => this.GetOrCreateList(id, pulling, simulate);
        // We should build our custom replacements for these lists here.  Then perhaps replace them before and 
        // after the call to RequestPropellant

        public virtual void CustomGetConnectedResourceTotals(
          PartSet partSet,
          Dictionary<int, PartSet.ResourcePrioritySet> propSetDict,
          int id,
          out double amount,
          out double maxAmount,
          bool pulling,
          bool simulate)
        {
            amount = 0;
            maxAmount = 0;
            //PartSet.ResourcePrioritySet list1 = partSet.GetOrCreateList(id, pulling, simulate);
            PartSet.ResourcePrioritySet stockPrioritySet = partSet.GetResourceList(id, pulling, simulate);
            if (stockPrioritySet == null)
                return;
            if (!propSetDict.TryGetValue(id, out PartSet.ResourcePrioritySet rfPrioritySet) ||
                rfPrioritySet.lists.Count != stockPrioritySet.lists.Count)
            {
                propSetDict[id] = rfPrioritySet = new PartSet.ResourcePrioritySet()
                {
                    lists = new List<List<PartResource>>(stockPrioritySet.lists.Count),
                    set = new HashSet<PartResource>(),
                };
                for (int i = 0; i < stockPrioritySet.lists.Count; i++)
                    rfPrioritySet.lists.Add(new List<PartResource>());
            }
            rfPrioritySet.set.Clear();
            int priority = stockPrioritySet.lists.Count;
            while (--priority >= 0)
            {
                List<PartResource> stockResourceList = stockPrioritySet.lists[priority];
                List<PartResource> merfResourceList = rfPrioritySet.lists[priority];
                merfResourceList.Clear();
                int samePriorityIndex = stockResourceList.Count;
                while (--samePriorityIndex >= 0)
                {
                    PartResource partResource = stockResourceList[samePriorityIndex];
                    if ((partResource.part.FindModuleImplementing<ModuleFuelTanks>() is ModuleFuelTanks mft
                        && mft.tankGroups.FirstOrDefault() is TankGroup group
                        && group.pressurant is FuelTank pressurant
                        && group.CurrentAvailablePressurantVolume > mft.tankGroups.Sum(g => g.CurrentRequiredPressurantVolume))
                        || RFSettings.Instance.SolidFuelsIDs.Contains(id))
                    {
                        amount += pulling ? partResource.amount : partResource.maxAmount - partResource.amount;
                        maxAmount += partResource.maxAmount;
                        rfPrioritySet.set.Add(partResource);
                        merfResourceList.Add(partResource);
                    }
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
            if (throttleCurve == null)
                throttleCurve = new FloatCurve();
            FieldInfo valueField = typeof(PartSet).GetField("pullList", BindingFlags.Instance | BindingFlags.NonPublic);
            PartSetPullListGetter = CreateGetter<PartSet, Dictionary<int, PartSet.ResourcePrioritySet>>(valueField);
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

                if (residualsThresholdBase < 0d)
                {
                    if (ignitions == -1 || ignitions > 4)
                        localResidualsThresholdBase *= 2d;
                    else if (ignitions == 0)
                        localResidualsThresholdBase *= 0.8d;
                    else
                        localResidualsThresholdBase *= (1d + (ignitions - 1) * 0.25d);
                }
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
                    // using egg's formula here that ensures a gradual climb from 0, 0.5 as median (e^(0-ln(2)) == 0.5), and a very thin tail past 1.0
                    // Due to the exponent, this will never go below 0 so we needn't chop off variance in that direction.
                    // Chop in the + direction is where the result of the exp is <=1.0
                    calculatedResiduals = localResidualsThresholdBase + localVaryResiduals * Math.Exp(0.421404d * Utilities.GetNormal(staticRandom, 0d, 1.644851d) - Math.Log(2d));
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

            UpdateResiduals();

            ullageSet = new Ullage.UllageSet(this);
            ullageSet.Load(ullageNode);

            Fields[nameof(ignitions)].guiActive = ignitions >= 0 && RFSettings.Instance.limitedIgnitions;
            Fields[nameof(tags)].guiActiveEditor = ShowPropStatus;
            Fields[nameof(propellantStatus)].guiActive = Fields[nameof(propellantStatus)].guiActiveEditor = ShowPropStatus;
            Fields[nameof(sIgnitions)].guiActiveEditor = RFSettings.Instance.limitedIgnitions;

            igniteFailIgnitions = new ScreenMessage($"<color=orange>[{part.partInfo.title}]: {Localizer.GetStringByTag("#RF_EngineRF_IgnitionsFailmsgs1")}</color>", 5f, ScreenMessageStyle.UPPER_CENTER); // no ignitions remaining!
            igniteFailResources = new ScreenMessage($"<color=orange>[{part.partInfo.title}]: {Localizer.GetStringByTag("#RF_EngineRF_IgnitionsFailmsgs2")}</color>", 5f, ScreenMessageStyle.UPPER_CENTER); // insufficient resources to ignite!
            ullageFail = new ScreenMessage($"<color=orange>[{part.partInfo.title}]: {Localizer.GetStringByTag("#RF_EngineRF_IgnitionsFailmsgs3")}</color>", 5f, ScreenMessageStyle.UPPER_CENTER); // vapor in feedlines, shut down!

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

        private void SetFields()
        {
            _minThrottle = MinThrottle;
            tags = pressureFed ? $"<color=orange>{Localizer.GetStringByTag("#RF_EngineRF_PressureFed")}</color>" : string.Empty; // Pressure-Fed
            if (ullage)
            {
                tags += pressureFed ? ", " : string.Empty;
                tags += $"<color=yellow>{Localizer.GetStringByTag("#RF_EngineRF_Ullage")}</color>"; // Ullage
            }
            sISP = $"{atmosphereCurve.Evaluate(1):N0} (ASL) - {atmosphereCurve.Evaluate(0):N0} (Vac)"; // 
            GetThrustData(out double thrustVac, out double thrustASL);
            sThrust = $"{Utilities.FormatThrust(thrustASL)} (ASL) - {Utilities.FormatThrust(thrustVac)} (Vac)"; // 
            if (ignitions > 0)
                sIgnitions = $"{ignitions:N0}";
            else if (ignitions == -1)
                sIgnitions = Localizer.GetStringByTag("#RF_EngineRF_IgnitionUnlimited"); // "Unlimited"
            else
                sIgnitions = $"<color=yellow>{Localizer.GetStringByTag("#RF_EngineRF_GroundSupportClamps")}</color>"; // Ground Support Clamps

            dispMass = part.mass;
        }

        public virtual void Update()
        {
            if (!(ullageSet is Ullage.UllageSet && ShowPropStatus)) return;
            if (HighLogic.LoadedSceneIsEditor && !(part.PartActionWindow is UIPartActionWindow)) return;

            if (HighLogic.LoadedSceneIsEditor && pressureFed)
                ullageSet.EditorPressurized();

            if (pressureFed && !ullageSet.PressureOK())
                propellantStatus = $"<color=red>{Localizer.GetStringByTag("#RF_EngineRF_Needshighpressuretanks")}</color>"; // Needs high pressure tanks
            else if (HighLogic.LoadedSceneIsFlight)
            {
                if (!ignited && RFSettings.Instance.limitedIgnitions && !CheatOptions.InfinitePropellant && (
                        !reignitable
                        || ignitions == 0 && vessel.FindPartModuleImplementing<LaunchClamp>() == null))
                {
                    part.stackIcon.SetIconColor(XKCDColors.LightMauve);
                }
                else if (ullage && RFSettings.Instance.simulateUllage)
                {
                    propellantStatus = ullageSet.GetUllageState(out Color ullageColor);
                    part.stackIcon.SetIconColor(ullageColor);
                }
            }
            else
                propellantStatus = pressureFed ? Localizer.GetStringByTag("#RF_EngineRF_FeedPressureOK") : Localizer.GetStringByTag("#RF_EngineRF_Nominal"); // "Feed pressure OK""Nominal"
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
                float requiredThrottle;
                if (throttleCurve.maxTime >= 0f)
                    requiredThrottle = Mathf.Max(throttleCurve.Evaluate(requestedThrottle), MinThrottle);
                else
                    requiredThrottle = Mathf.Lerp(MinThrottle, 1f, requestedThrottle);
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
                ScreenMessages.PostScreenMessage($"<color=orange>[{part.partInfo.title}]: {Localizer.GetStringByTag("#RF_EngineRF_ShieldFailmsg")}</color>", 6f, ScreenMessageStyle.UPPER_LEFT); // Cannot activate while stowed!
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
                            Flameout(Localizer.GetStringByTag("#RF_EngineRF_Vaporinfeedline")); // "Vapor in feed line"
                        }
                    }
                }
                if (!ullageSet.PressureOK())
                {
                    Flameout(Localizer.GetStringByTag("#RF_EngineRF_Lackofpressure"), false, ignited); // "Lack of pressure"
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
                            otherMERF.Flameout(Localizer.GetStringByTag("#RF_EngineRF_Nopropellants"), false, otherMERF.ignited); // "No propellants"
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
            if (throttleLocked) { return ", throttle locked"; } // 
            if (MinThrottle == 1f) { return ", unthrottleable"; } // 
            if (MinThrottle < 0f || MinThrottle > 1f) { return string.Empty; }
            return $", {MinThrottle:P0} min throttle"; // 
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
                    output += $"<b>{Localizer.GetStringByTag("#RF_EngineRF_StaticThrust")}: </b>{Utilities.FormatThrust(thrustASL)} (TWR {thrustASL / weight:0.0##}), {(throttleLocked ? Localizer.GetStringByTag("#RF_EngineRF_throttlelocked") : Localizer.GetStringByTag("#RF_EngineRF_unthrottleable"))}\n"; // Static Thrust / "throttle locked""unthrottleable"
                }
                else
                {
                    output += $"{MinThrottle:P0} {Localizer.GetStringByTag("#RF_EngineRF_MinThrottle")}\n"; // min throttle
                    output += $"<b>{Localizer.GetStringByTag("#RF_EngineRF_MaxStaticThrust")}: </b>{Utilities.FormatThrust(thrustASL)} (TWR {thrustASL / weight:0.0##})\n"; // Max. Static Thrust
                    output += $"<b>{Localizer.GetStringByTag("#RF_EngineRF_MinStaticThrust")}: </b>{Utilities.FormatThrust(thrustASL * MinThrottle)} (TWR {thrustASL * MinThrottle / weight:0.0##})\n"; // Min. Static Thrust
                }

                if (useVelCurve) // if thrust changes with mach
                {
                    velCurve.FindMinMaxValue(out float vMin, out float vMax, out float tMin, out float tMax); // get the max mult, and thus report maximum thrust possible.
                    output += $"<b>{Localizer.GetStringByTag("#RF_EngineRF_MaxThrust_velcurve")}: {Localizer.Format("#RF_EngineRF_MaxThrust_atMach", Utilities.FormatThrust(thrustASL * vMax), $"{tMax:0.#}")}</b> (TWR {thrustASL * vMax / weight:0.0##})\n"; // Max. Thrust | {Utilities.FormatThrust(thrustASL * vMax)} at Mach {tMax:0.#}
                }
            }
            else
            {
                if (throttleLocked || MinThrottle == 1f)
                {
                    var suffix = throttleLocked ? Localizer.GetStringByTag("#RF_EngineRF_throttlelocked") : Localizer.GetStringByTag("#RF_EngineRF_unthrottleable"); // "throttle locked""unthrottleable"
                    if (thrustASL != thrustVac)
                    {
                        output += $"<b>{Localizer.GetStringByTag("#RF_EngineRF_ThrustInVac")}: </b>{Utilities.FormatThrust(thrustVac)} (TWR {thrustVac / weight:0.0##}), {suffix}\n"; // Thrust (Vac)
                        output += $"<b>{Localizer.GetStringByTag("#RF_EngineRF_ThrustInASL")}: </b>{Utilities.FormatThrust(thrustASL)} (TWR {thrustASL / weight:0.0##}), {suffix}\n"; // Thrust (ASL)
                    }
                    else
                    {
                        output += $"<b>{Localizer.GetStringByTag("#RF_EngineRF_Thrust")}: </b>{Utilities.FormatThrust(thrustVac)} (TWR {thrustVac / weight:0.0##}), {suffix}\n"; // Thrust
                    }
                }
                else
                {
                    output += $"{MinThrottle:P0} {Localizer.GetStringByTag("#RF_EngineRF_MinThrottle")}\n"; // min throttle
                    if (thrustASL != thrustVac)
                    {
                        output += $"<b>{Localizer.GetStringByTag("#RF_EngineRF_MAXThrustInVac")}: </b>{Utilities.FormatThrust(thrustVac)} (TWR {thrustVac / weight:0.0##})\n"; //Max. Thrust (Vac) 
                        output += $"<b>{Localizer.GetStringByTag("#RF_EngineRF_MAXThrustInASL")}: </b>{Utilities.FormatThrust(thrustASL)} (TWR {thrustASL / weight:0.0##})\n"; //Max. Thrust (ASL) 
                        output += $"<b>{Localizer.GetStringByTag("#RF_EngineRF_MINThrustInVac")}: </b>{Utilities.FormatThrust(thrustVac * MinThrottle)} (TWR {thrustVac * MinThrottle / weight:0.0##})\n"; // Min. Thrust (Vac)
                        output += $"<b>{Localizer.GetStringByTag("#RF_EngineRF_MINThrustInASL")}: </b>{Utilities.FormatThrust(thrustASL * MinThrottle)} (TWR {thrustASL * MinThrottle / weight:0.0##})\n"; // Min. Thrust (ASL)
                    }
                    else
                    {
                        output += $"<b>{Localizer.GetStringByTag("#RF_EngineRF_MaxThrust_velcurve")}: </b>{Utilities.FormatThrust(thrustVac)} (TWR {thrustVac / weight:0.0##})\n"; // Max. Thrust
                        output += $"<b>{Localizer.GetStringByTag("#RF_EngineRF_MinThrust_velcurve")}: </b>{Utilities.FormatThrust(thrustVac * MinThrottle)} (TWR {thrustVac * MinThrottle / weight:0.0##})\n"; // Min. Thrust
                    }
                }
            }
            return output;
        }

        public override string GetModuleTitle()
        {
            return "Engine (RealFuels)";
        }
        public override string GetModuleDisplayName()
        {
            return Localizer.GetStringByTag("#RF_EngineRF_EngineRealFuels");
        }
        public override string GetPrimaryField()
        {
            return GetThrustInfo() + GetUllageIgnition();
        }
        public string GetUllageIgnition()
        {
            string output = pressureFed ? Localizer.GetStringByTag("#RF_EngineRF_PressureFed") : string.Empty; // "Pressure-fed"
            output += (output != string.Empty ? ", " : string.Empty) + $"{Localizer.GetStringByTag("#RF_EngineRF_Ignitions")}: " + ((!RFSettings.Instance.limitedIgnitions || ignitions < 0) ? Localizer.GetStringByTag("#RF_EngineRF_IgnitionUnlimited") : (ignitions > 0 ? ignitions.ToString() : Localizer.GetStringByTag("#RF_EngineRF_GroundSupportClamps"))); // Ignitions"Unlimited""Ground Support Clamps"
            output += (output != string.Empty ? ", " : string.Empty) + (ullage ? Localizer.GetStringByTag("#RF_EngineRF_ullage_Subject") : Localizer.GetStringByTag("#RF_EngineRF_ullage_NotSubject")); // "Subject""Not subject" + " to ullage"
            output += "\n";

            return output;
        }

        
        public override string GetInfo()
        {
            string output = $"{GetThrustInfo()}" +
                $"<b>{Localizer.GetStringByTag("#RF_EngineRF_GetInfo1")}: </b>{atmosphereCurve.Evaluate(1):0.###} (ASL) - {atmosphereCurve.Evaluate(0):0.###} (Vac.)\n"; // Engine Isp
            output += $"{GetUllageIgnition()}\n";
            if (ratedBurnTime > 0d)
            {
                output += $"<b>{Localizer.GetStringByTag("#RF_Engine_RatedBurnTime")}: </b>"; // Rated Burn Time
                if (ratedContinuousBurnTime > 0d)
                    output += $"{ratedContinuousBurnTime.ToString("F0")}/{ratedBurnTime.ToString("F0")}s\n";
                else
                    output += $"{ratedBurnTime.ToString("F0")}s\n";
            }
            output += $"<b><color=#99ff00ff>{Localizer.GetStringByTag("#RF_EngineRF_GetInfo2")}:</color></b>\n"; // Propellants
            double massFlow = 0d;

            foreach (Propellant p in propellants)
            {
                float unitsSec = getMaxFuelFlow(p);
                massFlow += unitsSec * p.resourceDef.density;
                output += ResourceUnits.PrintRate(unitsSec, p.id, true, null, p, true);
            }
            if (massFlow > 0d)
                output += Localizer.Format("#autoLOC_900654") + " " + Localizer.Format(ResourceUnits.PerSecLocString, ResourceUnits.PrintMass(massFlow)) + "\n";

            output += Localizer.Format("#RF_EngineRF_GetInfo3", $"{localVaryIsp:P2}", $"{localVaryFlow:P1}", $"{localVaryMixture:P2}"); // $"<b>Variance: </b>{localVaryIsp:P2} Isp, {localVaryFlow:P1} flow, {localVaryMixture:P2} MR (stddev).\n"
            output += Localizer.Format("#RF_EngineRF_GetInfo4", $"{localResidualsThresholdBase:P1}"); // $"<b>Residuals: min </b>{localResidualsThresholdBase:P1} of propellant.\n"

            if (!allowShutdown) output += $"\n<b><color=orange>{Localizer.GetStringByTag("#RF_EngineRF_GetInfo5")}</color></b>"; // Engine cannot be shut down!
            if (!allowRestart) output += $"\n<b><color=orange>{Localizer.GetStringByTag("#RF_EngineRF_GetInfo6")}</color></b>"; // If shutdown, engine cannot restart.

            currentThrottle = 0f;

            return output;
        }
        #endregion

        #region Helpers

        static Func<S, T> CreateGetter<S, T>(FieldInfo field)
        {
            string methodName = field.ReflectedType.FullName + ".get_" + field.Name;
            DynamicMethod setterMethod = new DynamicMethod(methodName, typeof(T), new Type[1] { typeof(S) }, true);
            ILGenerator gen = setterMethod.GetILGenerator();
            if (field.IsStatic)
            {
                gen.Emit(OpCodes.Ldsfld, field);
            }
            else
            {
                gen.Emit(OpCodes.Ldarg_0);
                gen.Emit(OpCodes.Ldfld, field);
            }
            gen.Emit(OpCodes.Ret);
            return (Func<S, T>)setterMethod.CreateDelegate(typeof(Func<S, T>));
        }

        protected void IgnitionUpdate()
        {
            if (EngineIgnited && throttledUp)
            {
                if (!ignited && reignitable)
                {
                    /* As long as you're on the pad, you can always ignite */
                    reignitable = false;
                    if (ignitions == 0 && RFSettings.Instance.limitedIgnitions && !CheatOptions.InfinitePropellant && vessel.FindPartModuleImplementing<LaunchClamp>() == null)
                    {
                        EngineIgnited = false; // don't play shutdown FX, just fail.
                        ScreenMessages.PostScreenMessage(igniteFailIgnitions);
                        FlightLogger.fetch.LogEvent($"[{FormatTime(vessel.missionTime)}] {igniteFailIgnitions.message}");
                        Flameout(Localizer.GetStringByTag("#RF_EngineRF_Ignitionfailed")); // "Ignition failed"
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
                                if (staticRandom.NextDouble() > minResource && !CheatOptions.InfinitePropellant && vessel.FindPartModuleImplementing<LaunchClamp>() == null)
                                {
                                    EngineIgnited = false; // don't play shutdown FX, just fail.
                                    ScreenMessages.PostScreenMessage(igniteFailResources);
                                    FlightLogger.fetch.LogEvent($"[{FormatTime(vessel.missionTime)}] {igniteFailResources.message}");
                                    Flameout(Localizer.GetStringByTag("#RF_EngineRF_Ignitionfailed")); // "Ignition failed"  // yes play FX
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
                if (PropellantAvailable())
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

        protected void UpdateResiduals()
        {
            predictedMaximumResidualsGUI = localResidualsThresholdBase + localVaryResiduals;
            double minPF = HighLogic.LoadedSceneIsFlight ? MinPropellantFraction() : 1d;
            predictedMaximumResiduals = UtilMath.Lerp(calculatedResiduals, predictedMaximumResidualsGUI, minPF);
            if (localVaryMixture > 0d)
            {
                double massExtra = CalculateMaxExtraMassFromMRVariation();
                predictedMaximumResidualsGUI += massExtra;
                predictedMaximumResiduals += massExtra * minPF;
            }
        }
        #endregion
    }
}
