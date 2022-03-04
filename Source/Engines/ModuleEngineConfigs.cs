using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;
using RealFuels.TechLevels;
using KSP.UI.Screens;

namespace RealFuels
{
    public struct Gimbal
    {
        public float gimbalRange;
        public float gimbalRangeXP;
        public float gimbalRangeXN;
        public float gimbalRangeYP;
        public float gimbalRangeYN;

        public Gimbal(float gimbalRange, float gimbalRangeXP, float gimbalRangeXN, float gimbalRangeYP, float gimbalRangeYN)
        {
            this.gimbalRange = gimbalRange;
            this.gimbalRangeXP = gimbalRangeXP;
            this.gimbalRangeXN = gimbalRangeXN;
            this.gimbalRangeYP = gimbalRangeYP;
            this.gimbalRangeYN = gimbalRangeYN;
        }

        public string Info()
        {
            if (new[] { gimbalRange, gimbalRangeXP, gimbalRangeXN, gimbalRangeYP, gimbalRangeYN }.Distinct().Count() == 1)
                return $"{gimbalRange:N1}d";
            if (new[] { gimbalRangeXP, gimbalRangeXN, gimbalRangeYP, gimbalRangeYN }.Distinct().Count() == 1)
                return $"{gimbalRangeXP:N1}d";
            var ret = string.Empty;
            if (gimbalRangeXP == gimbalRangeXN)
                ret += $"{gimbalRangeXP:N1}d pitch, ";
            else
                ret += $"+{gimbalRangeXP:N1}d/-{gimbalRangeXN:N1}d pitch, ";
            if (gimbalRangeYP == gimbalRangeYN)
                ret += $"{gimbalRangeYP:N1}d yaw";
            else
                ret += $"+{gimbalRangeYP:N1}d/-{gimbalRangeYN:N1}d yaw";
            return ret;
        }
    }

    public class ModuleEngineConfigs : ModuleEngineConfigsBase
    {
        public const string PatchNodeName = "SUBCONFIG";
        protected const string PatchNameKey = "__mpecPatchName";

        [KSPField(isPersistant = true)]
        public string activePatchName = "";

        [KSPField(isPersistant = true)]
        public bool dynamicPatchApplied = false;

        protected bool ConfigHasPatch(ConfigNode config) => GetPatchesOfConfig(config).Count > 0;

        protected List<ConfigNode> GetPatchesOfConfig(ConfigNode config)
        {
            ConfigNode[] list = config.GetNodes(PatchNodeName);
            List<ConfigNode> sortedList = ConfigFilters.Instance.FilterDisplayConfigs(list.ToList());
            return sortedList;
        }

        protected ConfigNode GetPatch(string configName, string patchName)
        {
            return GetPatchesOfConfig(GetConfigByName(configName))
                .FirstOrDefault(patch => patch.GetValue("name") == patchName);
        }

        protected bool ConfigIsPatched(ConfigNode config) => config.HasValue(PatchNameKey);

        // TODO: This is called a lot, performance concern?
        protected ConfigNode PatchConfig(ConfigNode parentConfig, ConfigNode patch, bool dynamic)
        {
            var patchedNode = parentConfig.CreateCopy();

            foreach (var key in patch.values.DistinctNames())
                patchedNode.RemoveValues(key);
            foreach (var nodeName in patch.nodes.DistinctNames())
                patchedNode.RemoveNodes(nodeName);

            patch.CopyTo(patchedNode);

            // Apply cost offset
            int costOffset = 0;
            patch.TryGetValue("costOffset", ref costOffset);
            int cost = 0;
            patchedNode.TryGetValue("cost", ref cost);
            cost += costOffset;
            patchedNode.SetValue("cost", cost, true);

            patchedNode.SetValue("name", parentConfig.GetValue("name"));
            if (!dynamic)
                patchedNode.AddValue(PatchNameKey, patch.GetValue("name"));
            return patchedNode;
        }

        public ConfigNode GetNonDynamicPatchedConfiguration() => GetSetConfigurationTarget(configuration);

        public void ApplyDynamicPatch(ConfigNode patch)
        {
            // Debug.Log($"**RFMPEC** dynamic patch applied to active config `{configurationDisplay}`");
            SetConfiguration(PatchConfig(GetNonDynamicPatchedConfiguration(), patch, true), false);
            dynamicPatchApplied = true;
        }

        protected override ConfigNode GetSetConfigurationTarget(string newConfiguration)
        {
            if (activePatchName == "")
                return base.GetSetConfigurationTarget(newConfiguration);
            return PatchConfig(GetConfigByName(newConfiguration), GetPatch(newConfiguration, activePatchName), false);
        }

        public override void SetConfiguration(string newConfiguration = null, bool resetTechLevels = false)
        {
            base.SetConfiguration(newConfiguration, resetTechLevels);
            if (dynamicPatchApplied)
            {
                dynamicPatchApplied = false;
                part.SendMessage("OnMPECDynamicPatchReset", SendMessageOptions.DontRequireReceiver);
            }
        }

        public override int UpdateSymmetryCounterparts()
        {
            DoForEachSymmetryCounterpart((engine) =>
                (engine as ModuleEngineConfigs).activePatchName = activePatchName);
            return base.UpdateSymmetryCounterparts();
        }

        public override string GetConfigInfo(ConfigNode config, bool addDescription = true, bool colorName = false)
        {
            var info = base.GetConfigInfo(config, addDescription, colorName);

            if (!ConfigHasPatch(config) || ConfigIsPatched(config))
                return info;

            if (addDescription) info += "\n";
            foreach (var patch in GetPatchesOfConfig(config))
                info += ConfigInfoString(PatchConfig(config, patch, false), false, colorName);
            return info;
        }

        public override string GetConfigDisplayName(ConfigNode node)
        {
            if (node.HasValue("displayName"))
                return node.GetValue("displayName");
            var name = node.GetValue("name");
            if (!node.HasValue(PatchNameKey))
                return name;
            return $"{name} [Subconfig {node.GetValue(PatchNameKey)}]";
        }

        protected override void DrawConfigSelectors(IEnumerable<ConfigNode> availableConfigNodes)
        {
            foreach (var node in availableConfigNodes)
            {
                DrawSelectButton(
                    node,
                    node.GetValue("name") == configuration && activePatchName == "",
                    (configName) =>
                    {
                        activePatchName = "";
                        GUIApplyConfig(configName);
                    });
                foreach (var patch in GetPatchesOfConfig(node))
                {
                    var patchedNode = PatchConfig(node, patch, false);
                    string patchName = patch.GetValue("name");
                    using (new GUILayout.HorizontalScope())
                    {
                        GUILayout.Space(30);
                        DrawSelectButton(
                            patchedNode,
                            node.GetValue("name") == configuration && patchName == activePatchName,
                            (configName) =>
                            {
                                activePatchName = patchName;
                                GUIApplyConfig(configName);
                            });
                    }
                }
            }
        }
    }

    public class ModuleEngineConfigsBase : PartModule, IPartCostModifier, IPartMassModifier
    {
        //protected const string groupName = "ModuleEngineConfigs";
        public const string groupName = ModuleEnginesRF.groupName;
        public const string groupDisplayName = "Engine Configs";
        #region Fields
        protected bool compatible = true;

        [KSPField(isPersistant = true)]
        public string configuration = string.Empty;

        // For display purposes only.
        [KSPField(guiName = "Configuration", isPersistant = true, guiActiveEditor = true,
            groupName = groupName, groupDisplayName = groupDisplayName)]
        public string configurationDisplay = string.Empty;

        // Tech Level stuff
        [KSPField(isPersistant = true)]
        public int techLevel = -1; // default: disable

        [KSPField]
        public int origTechLevel = -1; // default TL, starts disabled

        [KSPField]
        public float origMass = -1;
        protected float massDelta = 0;

        [KSPField]
        public string gimbalTransform = string.Empty;
        [KSPField]
        public float gimbalMult = 1f;
        [KSPField]
        public bool useGimbalAnyway = false;

        private Dictionary<string, Gimbal> defaultGimbals = null;

        [KSPField]
        public bool autoUnlock = true;

        [KSPField]
        public int maxTechLevel = -1;
        [KSPField]
        public int minTechLevel = -1;

        [KSPField]
        public string engineType = "L"; // default = lower stage

        [KSPField]
        public float throttle = 0.0f; // default min throttle level
        public float configThrottle;

        public string configDescription = string.Empty;

        public ConfigNode techNodes;

        [KSPField]
        public bool isMaster = true; //is this Module the "master" module on the part? (if false, don't do GUI)
        // For TestFlight integration, only ONE ModuleEngineConfigs (or child class) can be master module on a part.

        [KSPField]
        public string type = "ModuleEnginesRF";
        [KSPField]
        public bool useWeakType = true; // match any ModuleEngines*

        [KSPField]
        public string engineID = string.Empty;

        [KSPField]
        public int moduleIndex = -1;

        [KSPField]
        public int offsetGUIPos = -1;

        [KSPField(isPersistant = true)]
        public string thrustRating = "maxThrust";

        [KSPField(isPersistant = true)]
        public bool modded = false;

        [KSPField]
        public bool literalZeroIgnitions = false; /* Normally, ignitions = 0 means unlimited.  Setting this changes it to really mean zero */

        public List<ConfigNode> configs;
        internal List<ConfigNode> filteredDisplayConfigs;
        public ConfigNode config;

        public static Dictionary<string, string> techNameToTitle = new Dictionary<string, string>();

        // KIDS integration
        public static float ispSLMult = 1.0f;
        public static float ispVMult = 1.0f;

        [KSPField]
        public bool useConfigAsTitle = false;

        public float configMaxThrust = 1.0f;
        public float configMinThrust = 0.0f;
        public float configMassMult = 1.0f;
        public float configHeat = 0.0f;
        public float configCost = 0f;
        public float scale = 1f;
        #endregion

        #region TestFlight
        protected static bool tfChecked = false;
        protected static Type tfInterface = null;
        protected const BindingFlags tfBindingFlags = BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Static;

        public void UpdateTFInterops()
        {
            if (!tfChecked)
            {
                tfInterface = Type.GetType("TestFlightCore.TestFlightInterface, TestFlightCore", false);
                tfChecked = true;
            }
            try
            {
                tfInterface?.InvokeMember("AddInteropValue", tfBindingFlags, null, null, new object[] { part, isMaster ? "engineConfig" : "vernierConfig", configuration, "RealFuels" });
            }
            catch { }
        }
        #endregion

        #region B9PartSwitch
        protected static bool _b9psReflectionInitialized = false;
        protected static FieldInfo B9PS_moduleID;
        protected static MethodInfo B9PS_SwitchSubtype;
        protected static FieldInfo B9PS_switchInFlight;
        public Dictionary<string, PartModule> B9PSModules;

        private void InitializeB9PSReflection()
        {
            if (_b9psReflectionInitialized || !Utilities.B9PSFound) return;
            B9PS_moduleID = Type.GetType("B9PartSwitch.CustomPartModule, B9PartSwitch")?.GetField("moduleID");
            B9PS_SwitchSubtype = Type.GetType("B9PartSwitch.ModuleB9PartSwitch, B9PartSwitch")?.GetMethod("SwitchSubtype");
            B9PS_switchInFlight = Type.GetType("B9PartSwitch.ModuleB9PartSwitch, B9PartSwitch")?.GetField("switchInFlight");
            _b9psReflectionInitialized = true;
        }

        private void LoadB9PSModules()
        {
            IEnumerable<string> b9psModuleIDs = configs
                .Where(cfg => cfg.HasNode("LinkB9PSModule"))
                .SelectMany(cfg => cfg.GetNodes("LinkB9PSModule"))
                .Select(link => link?.GetValue("name"))
                .Where(moduleID => moduleID != null)
                .Distinct();

            B9PSModules = new Dictionary<string, PartModule>(b9psModuleIDs.Count());

            foreach (string moduleID in b9psModuleIDs)
            {
                var module = GetSpecifiedModules(part, string.Empty, -1, "ModuleB9PartSwitch", false)
                    .FirstOrDefault(m => (string)B9PS_moduleID?.GetValue(m) == moduleID);
                if (module == null)
                    Debug.LogError($"*RFMEC* B9PartSwitch module with ID {moduleID} was not found for {part}!");
                else
                    B9PSModules[moduleID] = module;
            }
        }

        /// <summary>
        /// Hide the GUI for all `ModuleB9PartSwitch`s managed by RF.
        /// This is somewhat of a hack-ish approach...
        /// </summary>
        private void HideB9PSVariantSelectors()
        {
            if (B9PSModules == null) return;
            foreach (var module in B9PSModules.Values)
            {
                module.Fields["currentSubtypeTitle"].guiActive = false;
                module.Fields["currentSubtypeTitle"].guiActiveEditor = false;
                module.Fields["currentSubtypeIndex"].guiActive = false;
                module.Fields["currentSubtypeIndex"].guiActiveEditor = false;
                module.Events["ShowSubtypesWindow"].guiActive = false;
                module.Events["ShowSubtypesWindow"].guiActiveEditor = false;
            }
        }

        private IEnumerator HideB9PSInFlightSelector_Coroutine(PartModule module)
        {
            yield return null;
            module.Events["ShowSubtypesWindow"].guiActive = false;
        }

        protected void ActivateB9PSVariantsOfConfig(ConfigNode node)
        {
            if (B9PSModules == null || B9PSModules.Count == 0) return;

            var subtypeSpecifications = new Dictionary<string, string>(B9PSModules.Count);
            if (node.GetNodes("LinkB9PSModule") is ConfigNode[] links)
            {
                foreach (ConfigNode link in links)
                {
                    string moduleID = null, subtype = null;
                    if (!link.TryGetValue("name", ref moduleID))
                        Debug.LogError($"*RFMEC* Config `{configurationDisplay}` of {part} has a LinkB9PSModule specification without a name key!");
                    if (!link.TryGetValue("subtype", ref subtype))
                        Debug.LogError($"*RFMEC* Config `{configurationDisplay}` of {part} has a LinkB9PSModule specification without a subtype key!");
                    if (moduleID != null && subtype != null)
                        subtypeSpecifications[moduleID] = subtype;
                }
            }

            foreach (var entry in B9PSModules)
            {
                string moduleID = entry.Key;
                PartModule module = entry.Value;

                if (HighLogic.LoadedSceneIsFlight
                    && B9PS_switchInFlight != null
                    && !(bool)B9PS_switchInFlight.GetValue(module)) continue;

                if (!subtypeSpecifications.TryGetValue(moduleID, out string subtypeName))
                {
                    Debug.LogError($"*RFMEC* Config {configurationDisplay} of {part} does not specify a subtype for linked B9PS module with ID {moduleID}; defaulting to `{configuration}`.");
                    subtypeName = configuration;
                }

                B9PS_SwitchSubtype?.Invoke(module, new object[] { subtypeName });
                if (HighLogic.LoadedSceneIsFlight) StartCoroutine(HideB9PSInFlightSelector_Coroutine(module));
            }
        }

        public void UpdateB9PSVariants() => ActivateB9PSVariantsOfConfig(config);
        #endregion

        #region Callbacks
        public float GetModuleCost(float stdCost, ModifierStagingSituation sit) => configCost;
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;

        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) => massDelta;
        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;

        [KSPEvent(guiActive = false, active = true)]
        void OnPartScaleChanged(BaseEventDetails data)
        {
            float factorAbsolute = data.Get<float>("factorAbsolute");
            float factorRelative = data.Get<float>("factorRelative");
            scale = factorAbsolute * factorAbsolute; // quadratic
            SetConfiguration();
            //Debug.Log($"[RFMEC] OnPartScaleChanged for {part}: factorRelative={factorRelative} | factorAbsolute={factorAbsolute}");
        }
        #endregion

        public static void BuildTechNodeMap()
        {
            if (techNameToTitle?.Count == 0)
            {
                string fullPath = KSPUtil.ApplicationRootPath + HighLogic.CurrentGame.Parameters.Career.TechTreeUrl;
                ConfigNode treeNode = new ConfigNode();
                if (ConfigNode.Load(fullPath) is ConfigNode fileNode && fileNode.TryGetNode("TechTree", ref treeNode))
                {
                    foreach (ConfigNode n in treeNode.GetNodes("RDNode"))
                    {
                        if (n.HasValue("id") && n.HasValue("title"))
                            techNameToTitle[n.GetValue("id")] = n.GetValue("title");
                    }
                }
            }
        }

        private void LoadDefaultGimbals()
        {
            defaultGimbals = new Dictionary<string, Gimbal>();
            foreach (var g in part.Modules.OfType<ModuleGimbal>())
                defaultGimbals[g.gimbalTransformName] = new Gimbal(g.gimbalRange, g.gimbalRangeXP, g.gimbalRangeXN, g.gimbalRangeYP, g.gimbalRangeYN);
        }

        public static void RelocateRCSPawItems(ModuleRCS module)
        {
            var field = module.Fields["thrusterPower"];
            field.guiActive = true;
            field.guiActiveEditor = true;
            field.guiName = "Thruster Power";
            field.guiUnits = "kN";
            field.group = new BasePAWGroup(groupName, groupDisplayName, false);
        }

        private List<ConfigNode> FilteredDisplayConfigs(bool update)
        {
            if (update || filteredDisplayConfigs == null)
            {
                filteredDisplayConfigs = ConfigFilters.Instance.FilterDisplayConfigs(configs);
            }
            return filteredDisplayConfigs;
        }

        #region PartModule Overrides
        public override void OnAwake()
        {
            techNodes = new ConfigNode();
            configs = new List<ConfigNode>();
            InitializeB9PSReflection();
        }

        public override void OnLoad(ConfigNode node)
        {
            if (!compatible)
                return;
            base.OnLoad(node);

            if (techLevel != -1)
            {
                if (maxTechLevel < 0)
                    maxTechLevel = TechLevel.MaxTL(node, engineType);
                if (minTechLevel < 0)
                    minTechLevel = Math.Min(origTechLevel, techLevel);
            }

            if (origMass > 0)
            {
                part.mass = origMass * RFSettings.Instance.EngineMassMultiplier;
                massDelta = (part?.partInfo?.partPrefab is Part p) ? part.mass - p.mass : 0;
            }

            if (node.GetNodes("CONFIG") is ConfigNode[] cNodes && cNodes.Length > 0)
            {
                configs.Clear();
                foreach (ConfigNode subNode in cNodes)
                {
                    //Debug.Log("*RFMEC* Load Engine Configs. Part " + part.name + " has config " + subNode.GetValue("name"));
                    ConfigNode newNode = new ConfigNode("CONFIG");
                    subNode.CopyTo(newNode);
                    configs.Add(newNode);
                }
            }

            foreach (ConfigNode n in node.GetNodes("TECHLEVEL"))
                techNodes.AddNode(n);

            ConfigSaveLoad();

            SetConfiguration();
        }

        public override void OnStart(StartState state)
        {
            if (!compatible)
                return;
            enabled = true;
            BuildTechNodeMap();

            Fields[nameof(showRFGUI)].guiActiveEditor = isMaster;
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onPartActionUIDismiss.Add(OnPartActionGuiDismiss);

            ConfigSaveLoad();

            LoadB9PSModules();

            LoadDefaultGimbals();

            SetConfiguration();

            Fields[nameof(showRFGUI)].guiName = GUIButtonName;

            // Why is this here, if KSP will call this normally?
            part.Modules.GetModule("ModuleEngineIgnitor")?.OnStart(state);
        }

        public override void OnStartFinished(StartState state)
        {
            HideB9PSVariantSelectors();
            if (pModule is ModuleRCS mrcs) RelocateRCSPawItems(mrcs);
        }
        #endregion

        #region Info Methods
        private string TLTInfo()
        {
            string retStr = string.Empty;
            if (engineID != string.Empty)
                retStr += $"(Bound to {engineID})\n";
            if (moduleIndex >= 0)
                retStr += $"(Bound to engine {moduleIndex} in part)\n";
            if (techLevel != -1)
            {
                TechLevel cTL = new TechLevel();
                if (!cTL.Load(config, techNodes, engineType, techLevel))
                    cTL = null;

                if (!string.IsNullOrEmpty(configDescription))
                    retStr += configDescription + "\n";

                retStr += $"Type: {engineType}. Tech Level: {techLevel} ({origTechLevel}-{maxTechLevel})";
                if (origMass > 0)
                    retStr += $", Mass: {part.mass:N3} (was {origMass * RFSettings.Instance.EngineMassMultiplier:N3})";
                if (configThrottle >= 0)
                    retStr += $", MinThr {configThrottle:P0}";

                float gimbalR = -1f;
                if (config.HasValue("gimbalRange"))
                    gimbalR = float.Parse(config.GetValue("gimbalRange"));
                else if (!gimbalTransform.Equals(string.Empty) || useGimbalAnyway)
                {
                    if (cTL != null)
                        gimbalR = cTL.GimbalRange;
                }
                if (gimbalR != -1f)
                    retStr += $", Gimbal {gimbalR:N1}";
            }
            return retStr;
        }

        virtual public string GetConfigDisplayName(ConfigNode node) => node.GetValue("name");

        public override string GetInfo()
        {
            if (!compatible)
                return string.Empty;
            var configsToDisplay = FilteredDisplayConfigs(true);
            if (configsToDisplay.Count < 2)
                return TLTInfo();

            string info = TLTInfo() + "\nAlternate configurations:\n";

            foreach (ConfigNode config in configsToDisplay)
                if (!config.GetValue("name").Equals(configuration))
                    info += GetConfigInfo(config, addDescription: false, colorName: true);

            return info;
        }

        protected string ConfigInfoString(ConfigNode config, bool addDescription, bool colorName)
        {
            TechLevel cTL = new TechLevel();
            if (!cTL.Load(config, techNodes, engineType, techLevel))
                cTL = null;
            var info = StringBuilderCache.Acquire();

            if (colorName)
                info.Append("<color=green>");
            info.Append(GetConfigDisplayName(config));
            if (colorName)
                info.Append("</color>");
            info.Append("\n");

            if (config.HasValue(thrustRating))
            {
                info.Append($"  {Utilities.FormatThrust(scale * ThrustTL(config.GetValue(thrustRating), config))}");
                // add throttling info if present
                if (config.HasValue("minThrust"))
                    info.Append($", min {float.Parse(config.GetValue("minThrust")) / float.Parse(config.GetValue(thrustRating)):P0}");
                else if (config.HasValue("throttle"))
                    info.Append($", min {float.Parse(config.GetValue("throttle")):P0}");
            }
            else
                info.Append("  Unknown Thrust");

            if (origMass > 0f)
            {
                float cMass = scale * origMass * RFSettings.Instance.EngineMassMultiplier;
                if (config.HasValue("massMult") && float.TryParse(config.GetValue("massMult"), out float ftmp))
                    cMass *= ftmp;

                info.Append($", {cMass:N3}t");
            }
            info.Append("\n");

            if (config.HasNode("atmosphereCurve"))
            {
                FloatCurve isp = new FloatCurve();
                isp.Load(config.GetNode("atmosphereCurve"));
                info.Append($"  Isp: {isp.Evaluate(isp.maxTime)} - {isp.Evaluate(isp.minTime)}s\n");
            }
            else if (config.HasValue("IspSL") && config.HasValue("IspV") && cTL != null)
            {
                float.TryParse(config.GetValue("IspSL"), out float ispSL);
                float.TryParse(config.GetValue("IspV"), out float ispV);
                ispSL *= ispSLMult * cTL.AtmosphereCurve.Evaluate(1);
                ispV *= ispVMult * cTL.AtmosphereCurve.Evaluate(0);
                info.Append($"  Isp: {ispSL:N0} - {ispV:N0}s\n");
            }

            if (config.HasValue("ratedBurnTime"))
            {
                if (config.HasValue("ratedContinuousBurnTime"))
                    info.Append($"  Rated burn time: {config.GetValue("ratedContinuousBurnTime")}/{config.GetValue("ratedBurnTime")}s\n");
                else
                    info.Append($"  Rated burn time: {config.GetValue("ratedBurnTime")}s\n");
            }

            if (part.HasModuleImplementing<ModuleGimbal>())
            {
                if (config.HasNode("GIMBAL"))
                {
                    foreach (KeyValuePair<string, Gimbal> kv in ExtractGimbals(config))
                    {
                        info.Append($"  Gimbal ({kv.Key}): {kv.Value.Info()}\n");
                    }
                }
                else if (config.HasValue("gimbalRange"))
                {
                    // The extracted gimbals contain `gimbalRange` et al. applied to either a specific
                    // transform or all the gimbal transforms on the part. Either way, the values
                    // are all the same, so just take the first one.
                    var gimbal = ExtractGimbals(config).Values.First();
                    info.Append($"  Gimbal {gimbal.Info()}\n");
                }
            }

            if (config.HasValue("ullage") || config.HasValue("ignitions") || config.HasValue("pressureFed"))
            {
                info.Append("  ");
                bool comma = false;
                if (config.HasValue("ullage"))
                {
                    info.Append(config.GetValue("ullage").ToLower() == "true" ? "ullage" : "no ullage");
                    comma = true;
                }
                if (config.HasValue("pressureFed") && config.GetValue("pressureFed").ToLower() == "true")
                {
                    if (comma)
                        info.Append(", ");
                    info.Append("pfed");
                    comma = true;
                }

                if (config.HasValue("ignitions"))
                {
                    if (int.TryParse(config.GetValue("ignitions"), out int ignitions))
                    {
                        if (comma)
                            info.Append(", ");
                        if (ignitions > 0)
                            info.Append($"{ignitions} ignition{(ignitions > 1 ? "s" : string.Empty)}");
                        else if (literalZeroIgnitions && ignitions == 0)
                            info.Append("ground ignition only");
                        else
                            info.Append("unl. ignitions");
                    }
                }
                info.Append("\n");
            }
            if (config.HasValue("cost") && float.TryParse(config.GetValue("cost"), out float cst))
                info.Append($"  ({scale * cst:N0} extra cost)\n"); // FIXME should get cost from TL, but this should be safe

            if (addDescription && config.HasValue("description"))
                info.Append($"\n  {config.GetValue("description")}\n");

            return info.ToStringAndRelease();
        }

        virtual public string GetConfigInfo(ConfigNode config, bool addDescription = true, bool colorName = false)
        {
            return ConfigInfoString(config, addDescription, colorName);
        }
        #endregion

        #region FX handling
        // Stop all effects registered with any config, but not with the current config
        private readonly HashSet<string> effectsToStop = new HashSet<string>();
        public void SetupFX()
        {
            List<string> effectsNames = new List<string>
            {
                "runningEffectName",
                "powerEffectName",
                "directThrottleEffectName",
                "disengageEffectName",
                "engageEffectName",
                "flameoutEffectName"
            };

            string val = string.Empty;
            IEnumerable<ConfigNode> others = configs.Where(x => !x.GetValue("name").Equals(configuration));
            ConfigNode ours = GetConfigByName(configuration);
            foreach (string fxName in effectsNames)
            {
                foreach (ConfigNode cfg in others)
                    if (cfg.TryGetValue(fxName, ref val))
                        effectsToStop.Add(val);
                if (ours is ConfigNode && ours.TryGetValue(fxName, ref val))
                    effectsToStop.Remove(val);
            }
        }
        public void StopFX()
        {
            foreach (var x in effectsToStop)
                part?.Effect(x, 0f);
        }
        #endregion

        #region Configuration
        public PartModule pModule = null;
        protected ConfigNode GetConfigByName(string name) => configs.Find(c => c.GetValue("name") == name);

        protected void SetConfiguration(ConfigNode newConfig, bool resetTechLevels)
        {
            string newConfiguration = newConfig.GetValue("name");

            if (configuration != newConfiguration)
            {
                if (resetTechLevels)
                    techLevel = origTechLevel;

                while (techLevel > 0 && !TechLevel.CanTL(newConfig, techNodes, engineType, techLevel))
                    --techLevel;
            }

            // for asmi
            if (useConfigAsTitle)
                part.partInfo.title = configuration;

            configuration = newConfiguration;
            configurationDisplay = GetConfigDisplayName(newConfig);
            config = new ConfigNode("MODULE");
            newConfig.CopyTo(config);
            config.name = "MODULE";

#if DEBUG
            Debug.Log($"replacing {type} with:\n{newConfig}");
#endif

            if ((pModule = GetSpecifiedModule(part, engineID, moduleIndex, type, useWeakType)) is null)
            {
                Debug.LogError($"*RFMEC* Could not find appropriate module of type {type}, with ID={engineID} and index {moduleIndex}");
                return;
            }

            Type mType = pModule.GetType();
            config.SetValue("name", mType.Name);

            ClearFloatCurves(mType, pModule, config, techLevel);
            ClearPropellantGauges(mType, pModule);

            if (type.Equals("ModuleRCS") || type.Equals("ModuleRCSFX"))
                ClearRCSPropellants(config);
            else
            { // is an ENGINE
                if (pModule is ModuleEngines mE && config.HasNode("PROPELLANT"))
                    mE.propellants.Clear();

                DoConfig(config);

                HandleEngineIgnitor(config);
                if (config.HasValue("ignitions"))
                {
                    if (HighLogic.LoadedSceneIsFlight && vessel?.situation != Vessel.Situations.PRELAUNCH)
                        config.RemoveValue("ignitions");
                    else if (int.TryParse(config.GetValue("ignitions"), out int ignitions))
                    {
                        ignitions = ConfigIgnitions(ignitions);
                        config.SetValue("ignitions", ignitions);
                    }
                }

                // Trigger re-computation of the response rate if one is not set explicitly.
                if (!config.HasValue("throttleResponseRate")) config.AddValue("throttleResponseRate", 0.0);

                if (pModule is ModuleEnginesRF)
                    (pModule as ModuleEnginesRF).SetScale(1d);
                pModule.Load(config);
            }
            // fix for editor NaN
            if (part.Resources.Contains("ElectricCharge") && part.Resources["ElectricCharge"].maxAmount < 0.1)
            { // hacking around a KSP bug here
                part.Resources["ElectricCharge"].amount = 0;
                part.Resources["ElectricCharge"].maxAmount = 0.1;
            }

            SetGimbalRange(config);

            if (!config.TryGetValue("cost", ref configCost))
                configCost = 0;
            if (!config.TryGetValue("description", ref configDescription))
                configDescription = string.Empty;

            UpdateOtherModules(config);

            // GUI disabled for now - UpdateTweakableMenu();

            // Prior comments suggest firing GameEvents.onEditorShipModified causes problems?
            part.SendMessage("OnEngineConfigurationChanged", SendMessageOptions.DontRequireReceiver);

            List<Part> parts;
            if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch.ship != null)
                parts = EditorLogic.fetch.ship.parts;
            else if (HighLogic.LoadedSceneIsFlight && vessel != null)
                parts = vessel.parts;
            else parts = new List<Part>();
            foreach (Part p in parts)
                p.SendMessage("UpdateUsedBy", SendMessageOptions.DontRequireReceiver);

            SetupFX();

            UpdateB9PSVariants();

            UpdateTFInterops(); // update TestFlight if it's installed

            StopFX();
        }

        /// Allows subclasses to determine the configuration to switch to based on additional info.
        /// Used by MPEC to inject the patch if necessary.
        virtual protected ConfigNode GetSetConfigurationTarget(string newConfiguration) => GetConfigByName(newConfiguration);

        virtual public void SetConfiguration(string newConfiguration = null, bool resetTechLevels = false)
        {
            if (newConfiguration == null)
                newConfiguration = configuration;

            ConfigSaveLoad();

            if (configs.Count == 0)
            {
                Debug.LogError($"*RFMEC* configuration set was empty for {part}!");
                StopFX();
                return;
            }

            ConfigNode newConfig = GetSetConfigurationTarget(newConfiguration);
            if (!(newConfig is ConfigNode))
            {
                newConfig = configs.First();
                string s = newConfig.GetValue("name");
                Debug.LogWarning($"*RFMEC* WARNING could not find configuration \"{newConfiguration}\" for part {part.name}: Fallback to \"{s}\".");
                newConfiguration = s;
            }

            SetConfiguration(newConfig, resetTechLevels);
        }

        virtual protected int ConfigIgnitions(int ignitions)
        {
            if (ignitions < 0)
            {
                ignitions = techLevel + ignitions;
                if (ignitions < 1)
                    ignitions = 1;
            }
            else if (ignitions == 0 && !literalZeroIgnitions)
                ignitions = -1;
            return ignitions;
        }

        #region SetConfiguration Tools
        private void ClearFloatCurves(Type mType, PartModule pm, ConfigNode cfg, int techLevel)
        {
            // clear all FloatCurves we need to clear (i.e. if our config has one, or techlevels are enabled)
            bool delAtmo = cfg.HasNode("atmosphereCurve") || techLevel >= 0;
            bool delDens = cfg.HasNode("atmCurve") || techLevel >= 0;
            bool delVel = cfg.HasNode("velCurve") || techLevel >= 0;
            foreach (FieldInfo field in mType.GetFields())
            {
                if (field.FieldType == typeof(FloatCurve) &&
                    ((field.Name.Equals("atmosphereCurve") && delAtmo)
                    || (field.Name.Equals("atmCurve") && delDens)
                    || (field.Name.Equals("velCurve") && delVel)))
                {
                    field.SetValue(pm, new FloatCurve());
                }
            }
        }

        private void ClearPropellantGauges(Type mType, PartModule pm)
        {
            foreach (FieldInfo field in mType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.FieldType == typeof(Dictionary<Propellant, ProtoStageIconInfo>) &&
                    field.GetValue(pm) is Dictionary<Propellant, ProtoStageIconInfo> boxes)
                {
                    foreach (ProtoStageIconInfo v in boxes.Values)
                    {
                        try
                        {
                            if (v is ProtoStageIconInfo)
                                pm.part.stackIcon.RemoveInfo(v);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError("*RFMEC* Trying to remove info box: " + e.Message);
                        }
                    }
                    boxes.Clear();
                }
            }
        }

        private void ClearRCSPropellants(ConfigNode cfg)
        {
            List<ModuleRCS> RCSModules = part.Modules.OfType<ModuleRCS>().ToList();
            if (RCSModules.Count > 0)
            {
                DoConfig(cfg);
                foreach (var rcsModule in RCSModules)
                {
                    if (cfg.HasNode("PROPELLANT"))
                        rcsModule.propellants.Clear();
                    rcsModule.Load(cfg);
                }
            }
        }

        private Dictionary<string, Gimbal> ExtractGimbals(ConfigNode cfg)
        {
            Gimbal ExtractGimbalKeys(ConfigNode c)
            {
                float.TryParse(c.GetValue("gimbalRange"), out float range);
                float xp = 0, xn = 0, yp = 0, yn = 0;
                if (!c.TryGetValue("gimbalRangeXP", ref xp))
                    xp = range;
                if (!c.TryGetValue("gimbalRangeXN", ref xn))
                    xn = range;
                if (!c.TryGetValue("gimbalRangeYP", ref yp))
                    yp = range;
                if (!c.TryGetValue("gimbalRangeYN", ref yn))
                    yn = range;
                return new Gimbal(range, xp, xn, yp, yn);
            }

            var gimbals = new Dictionary<string, Gimbal>();

            if (cfg.HasNode("GIMBAL"))
            {
                foreach (var node in cfg.GetNodes("GIMBAL"))
                {
                    if (!node.HasValue("gimbalTransform"))
                    {
                        Debug.LogError($"*RFMEC* Config {cfg.GetValue("name")} of part {part.name} has a `GIMBAL` node without a `gimbalTransform`!");
                        continue;
                    }
                    gimbals[node.GetValue("gimbalTransform")] = ExtractGimbalKeys(node);
                }
            }
            else if (cfg.HasValue("gimbalRange"))
            {
                var gimbal = ExtractGimbalKeys(cfg);
                if (this.gimbalTransform != string.Empty)
                    gimbals[this.gimbalTransform] = gimbal;
                else
                    foreach (var g in part.Modules.OfType<ModuleGimbal>())
                        gimbals[g.gimbalTransformName] = gimbal;
            }

            return gimbals;
        }

        private void SetGimbalRange(ConfigNode cfg)
        {
            if (!part.HasModuleImplementing<ModuleGimbal>()) return;
            // Do not override gimbals before default gimbals have been extracted.
            if (defaultGimbals == null) return;

            Dictionary<string, Gimbal> gimbalOverrides = ExtractGimbals(cfg);
            foreach (ModuleGimbal mg in part.Modules.OfType<ModuleGimbal>())
            {
                string transform = mg.gimbalTransformName;
                if (!gimbalOverrides.TryGetValue(transform, out Gimbal g))
                {
                    if (!defaultGimbals.ContainsKey(transform))
                    {
                        Debug.LogWarning($"*RFMEC* default gimbal settings were not found for gimbal transform `{transform}` for part {part.name}");
                        continue;
                    }
                    g = defaultGimbals[transform];
                }
                mg.gimbalRange = g.gimbalRange;
                mg.gimbalRangeXP = g.gimbalRangeXP;
                mg.gimbalRangeXN = g.gimbalRangeXN;
                mg.gimbalRangeYP = g.gimbalRangeYP;
                mg.gimbalRangeYN = g.gimbalRangeYN;
            }
        }

        private void HandleEngineIgnitor(ConfigNode cfg)
        {
            // Handle Engine Ignitor
            if (cfg.HasNode("ModuleEngineIgnitor"))
            {
                ConfigNode eiNode = cfg.GetNode("ModuleEngineIgnitor");
                if (part.Modules["ModuleEngineIgnitor"] is PartModule eiPM)
                {
                    if (eiNode.HasValue("ignitionsAvailable") &&
                        int.TryParse(eiNode.GetValue("ignitionsAvailable"), out int ignitions))
                    {
                        ignitions = ConfigIgnitions(ignitions);
                        eiNode.SetValue("ignitionsAvailable", ignitions);
                        eiNode.SetValue("ignitionsRemained", ignitions, true);
                    }
                    if (HighLogic.LoadedSceneIsEditor || (HighLogic.LoadedSceneIsFlight && vessel?.situation == Vessel.Situations.PRELAUNCH)) // fix for prelaunch
                    {
                        int remaining = (int)eiPM.GetType().GetField("ignitionsRemained").GetValue(eiPM);
                        eiNode.SetValue("ignitionsRemained", remaining, true);
                    }
                    ConfigNode tNode = new ConfigNode("MODULE");
                    eiNode.CopyTo(tNode);
                    tNode.SetValue("name", "ModuleEngineIgnitor", true);
                    eiPM.Load(tNode);
                }
                else // backwards compatible with EI nodes when using RF ullage etc.
                {
                    if (eiNode.HasValue("ignitionsAvailable") && !cfg.HasValue("ignitions"))
                        cfg.AddValue("ignitions", eiNode.GetValue("ignitionsAvailable"));
                    if (eiNode.HasValue("useUllageSimulation") && !cfg.HasValue("ullage"))
                        cfg.AddValue("ullage", eiNode.GetValue("useUllageSimulation"));
                    if (eiNode.HasValue("isPressureFed") && !cfg.HasValue("pressureFed"))
                        cfg.AddValue("pressureFed", eiNode.GetValue("isPressureFed"));
                    if (!cfg.HasNode("IGNITOR_RESOURCE"))
                        foreach (ConfigNode resNode in eiNode.GetNodes("IGNITOR_RESOURCE"))
                            cfg.AddNode(resNode);
                }
            }
        }

        #endregion
        virtual public void DoConfig(ConfigNode cfg)
        {
            configMaxThrust = configMinThrust = configHeat = -1f;
            float x = 1;
            if (cfg.TryGetValue(thrustRating, ref x))
                configMaxThrust = scale * x;
            if (cfg.TryGetValue("minThrust", ref x))
                configMinThrust = scale * x;
            if (cfg.TryGetValue("heatProduction", ref x))
                configHeat = (float)Math.Round(x * RFSettings.Instance.heatMultiplier, 0);

            configThrottle = throttle;
            if (cfg.HasValue("throttle"))
                float.TryParse(cfg.GetValue("throttle"), out configThrottle);
            else if (configMinThrust >= 0f && configMaxThrust >= 0f)
                configThrottle = configMinThrust / configMaxThrust;

            float TLMassMult = 1.0f;

            float gimbal = -1f;
            if (cfg.HasValue("gimbalRange"))
                gimbal = float.Parse(cfg.GetValue("gimbalRange"));

            float cost = 0f;
            if (cfg.HasValue("cost"))
                cost = scale * float.Parse(cfg.GetValue("cost"));

            if (techLevel != -1)
            {
                // load techlevels
                TechLevel cTL = new TechLevel();
                cTL.Load(cfg, techNodes, engineType, techLevel);
                TechLevel oTL = new TechLevel();
                oTL.Load(cfg, techNodes, engineType, origTechLevel);

                // set atmosphereCurve
                if (cfg.HasValue("IspSL") && cfg.HasValue("IspV"))
                {
                    cfg.RemoveNode("atmosphereCurve");

                    ConfigNode curve = new ConfigNode("atmosphereCurve");

                    // get the multipliers
                    float.TryParse(cfg.GetValue("IspSL"), out float ispSL);
                    float.TryParse(cfg.GetValue("IspV"), out float ispV);

                    // Mod the curve by the multipliers
                    FloatCurve newAtmoCurve = Utilities.Mod(cTL.AtmosphereCurve, ispSL, ispV);
                    newAtmoCurve.Save(curve);

                    cfg.AddNode(curve);
                }

                // set heatProduction
                if (configHeat > 0)
                    configHeat = MassTL(configHeat);

                // set thrust and throttle
                if (configMaxThrust >= 0)
                {
                    configMaxThrust = ThrustTL(configMaxThrust);
                    if (configMinThrust >= 0)
                        configMinThrust = ThrustTL(configMinThrust);
                    else if (thrustRating.Equals("thrusterPower"))
                        configMinThrust = configMaxThrust * 0.5f;
                    else
                    {
                        configMinThrust = configMaxThrust;
                        if (configThrottle > 1.0f)
                            configThrottle = techLevel >= configThrottle ? 1 : -1;
                        if (configThrottle >= 0.0f)
                        {
                            configThrottle = (float)(configThrottle * cTL.Throttle());
                            configMinThrust *= configThrottle;
                        }
                    }
                    configThrottle = configMinThrust / configMaxThrust;
                    if (origMass > 0)
                        TLMassMult = MassTL(1.0f);
                }
                // Don't want to change gimbals on TL-enabled engines willy-nilly
                // So we don't unless either a transform is specified, or we override.
                // We assume if it was specified in the CONFIG that we should use it anyway.
                if (gimbal < 0 && (!gimbalTransform.Equals(string.Empty) || useGimbalAnyway))
                    gimbal = cTL.GimbalRange;
                if (gimbal >= 0)
                {
                    // allow local override of gimbal mult
                    if (cfg.HasValue("gimbalMult"))
                        gimbal *= float.Parse(cfg.GetValue("gimbalMult"));
                }

                // Cost (multiplier will be 1.0 if unspecified)
                cost = scale * CostTL(cost, cfg);
            }
            else
            {
                if (cfg.HasValue(thrustRating) && configThrottle > 0f && !cfg.HasValue("minThrust"))
                {
                    configMinThrust = configThrottle * configMaxThrust;
                }
            }

            // Now update the cfg from what we did.
            // thrust updates
            if (configMaxThrust >= 0f)
                cfg.SetValue(thrustRating, configMaxThrust.ToString("0.0000"), true);
            if (configMinThrust >= 0f)
                cfg.SetValue("minThrust", configMinThrust.ToString("0.0000"), true); // will be ignored by RCS, so what.

            // heat update
            if (configHeat >= 0f)
                cfg.SetValue("heatProduction", configHeat.ToString("0"), true);

            // mass change
            if (origMass > 0)
            {
                configMassMult = scale;
                if (cfg.HasValue("massMult"))
                    if (float.TryParse(cfg.GetValue("massMult"), out float ftmp))
                        configMassMult *= ftmp;

                part.mass = origMass * configMassMult * RFSettings.Instance.EngineMassMultiplier * TLMassMult;
                massDelta = (part.partInfo?.partPrefab is Part p) ? part.mass - p.mass : 0;
            }

            // KIDS integration
            if (cfg.HasNode("atmosphereCurve"))
            {
                ConfigNode newCurveNode = new ConfigNode("atmosphereCurve");
                FloatCurve oldCurve = new FloatCurve();
                oldCurve.Load(cfg.GetNode("atmosphereCurve"));
                FloatCurve newCurve = Utilities.Mod(oldCurve, ispSLMult, ispVMult);
                newCurve.Save(newCurveNode);
                cfg.RemoveNode("atmosphereCurve");
                cfg.AddNode(newCurveNode);
            }
            // gimbal change
            if (gimbal >= 0 && !cfg.HasValue("gimbalRange")) // if TL set a gimbal
                cfg.AddValue("gimbalRange", $"{gimbal * gimbalMult:N4}");
            if (cost != 0f)
                cfg.SetValue("cost", $"{cost:N3}", true);
        }

        //called by StretchyTanks StretchySRB and ProcedrualParts
        virtual public void ChangeThrust(float newThrust)
        {
            foreach (ConfigNode c in configs)
            {
                c.SetValue("maxThrust", newThrust);
            }
            SetConfiguration(configuration);
        }

        // Used by ProceduralParts
        public void ChangeEngineType(string newEngineType)
        {
            engineType = newEngineType;
            SetConfiguration(configuration);
        }

        #region TechLevel and Required
        /// <summary>
        /// Is this config unlocked? Note: Is the same as CanConfig when not CAREER and no upgrade manager instance.
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        public static bool UnlockedConfig(ConfigNode config, Part p)
        {
            if (config == null)
                return false;
            if (!config.HasValue("name"))
                return false;
            if (EntryCostManager.Instance != null && HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode != Game.Modes.SANDBOX)
                return EntryCostManager.Instance.ConfigUnlocked((RFSettings.Instance.usePartNameInConfigUnlock ? Utilities.GetPartName(p) : string.Empty) + config.GetValue("name"));
            return true;
        }
        public static bool CanConfig(ConfigNode config)
        {
            if (config == null)
                return false;
            if (!config.HasValue("techRequired") || HighLogic.CurrentGame == null)
                return true;
            if (HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX || ResearchAndDevelopment.GetTechnologyState(config.GetValue("techRequired")) == RDTech.State.Available)
                return true;
            return false;
        }
        public static bool UnlockedTL(string tlName, int newTL)
        {
            if (EntryCostManager.Instance != null && HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode != Game.Modes.SANDBOX)
                return EntryCostManager.Instance.TLUnlocked(tlName) >= newTL;
            return true;
        }

        private double ThrustTL(ConfigNode cfg = null)
        {
            if (techLevel != -1 && !engineType.Contains("S"))
            {
                TechLevel oldTL = new TechLevel(), newTL = new TechLevel();
                if (oldTL.Load(cfg ?? config, techNodes, engineType, origTechLevel) &&
                    newTL.Load(cfg ?? config, techNodes, engineType, techLevel))
                    return newTL.Thrust(oldTL);
            }
            return 1;
        }

        private float ThrustTL(float thrust, ConfigNode cfg = null)
        {
            return (float)Math.Round(thrust * ThrustTL(cfg), 6);
        }

        private float ThrustTL(string thrust, ConfigNode cfg = null)
        {
            float.TryParse(thrust, out float tmp);
            return ThrustTL(tmp, cfg);
        }

        private double MassTL(ConfigNode cfg = null)
        {
            if (techLevel != -1)
            {
                TechLevel oldTL = new TechLevel(), newTL = new TechLevel();
                if (oldTL.Load(cfg ?? config, techNodes, engineType, origTechLevel) &&
                    newTL.Load(cfg ?? config, techNodes, engineType, techLevel))
                    return newTL.Mass(oldTL, engineType.Contains("S"));
            }
            return 1;
        }

        private float MassTL(float mass)
        {
            return (float)Math.Round(mass * MassTL(), 6);
        }
        private float CostTL(float cost, ConfigNode cfg = null)
        {
            TechLevel cTL = new TechLevel();
            TechLevel oTL = new TechLevel();
            if (cTL.Load(cfg, techNodes, engineType, techLevel) && oTL.Load(cfg, techNodes, engineType, origTechLevel) && part.partInfo != null)
            {
                // Bit of a dance: we have to figure out the total cost of the part, but doing so
                // also depends on us. So we zero out our contribution first
                // and then restore configCost.
                float oldCC = configCost;
                configCost = 0f;
                float totalCost = part.partInfo.cost + part.GetModuleCosts(part.partInfo.cost);
                configCost = oldCC;
                cost = (totalCost + cost) * (cTL.CostMult / oTL.CostMult) - totalCost;
            }

            return cost;
        }
        #endregion
        #endregion

        #region GUI
        public virtual string GUIButtonName => "Engine";
        public virtual string EditorDescription => "Select a configuration for this engine.";
        [KSPField(guiActiveEditor = true, guiName = "Engine", groupName = groupName),
         UI_Toggle(enabledText = "Hide GUI", disabledText = "Show GUI")]
        [NonSerialized]
        public bool showRFGUI;

        private void OnPartActionGuiDismiss(Part p)
        {
            if (p == part || p.isSymmetryCounterPart(part))
                showRFGUI = false;
        }

        public override void OnInactive()
        {
            if (!compatible)
                return;
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onPartActionUIDismiss.Remove(OnPartActionGuiDismiss);
        }

        private static Vector3 mousePos = Vector3.zero;
        private Rect guiWindowRect = new Rect(0, 0, 0, 0);
        private string myToolTip = string.Empty;
        private int counterTT;
        private bool editorLocked = false;

        private int toolTipWidth => EditorLogic.fetch.editorScreen == EditorScreen.Parts ? 220 : 300;
        private int toolTipHeight => (int)Styles.styleEditorTooltip.CalcHeight(new GUIContent(myToolTip), toolTipWidth);

        public void OnGUI()
        {
            if (!compatible || !isMaster || !HighLogic.LoadedSceneIsEditor || EditorLogic.fetch == null)
                return;

            bool inPartsEditor = EditorLogic.fetch.editorScreen == EditorScreen.Parts;
            if (!(showRFGUI && inPartsEditor) && !(EditorLogic.fetch.editorScreen == EditorScreen.Actions && EditorActionGroups.Instance.GetSelectedParts().Contains(part)))
            {
                EditorUnlock();
                return;
            }

            if (inPartsEditor && part.symmetryCounterparts.FirstOrDefault(p => p.persistentId < part.persistentId) is Part)
                return;

            if (guiWindowRect.width == 0)
            {
                int posAdd = inPartsEditor ? 256 : 0;
                int posMult = (offsetGUIPos == -1) ? (part.Modules.Contains("ModuleFuelTanks") ? 1 : 0) : offsetGUIPos;
                guiWindowRect = new Rect(posAdd + 430 * posMult, 365, 430, (Screen.height - 365));
            }

            mousePos = Input.mousePosition; //Mouse location; based on Kerbal Engineer Redux code
            mousePos.y = Screen.height - mousePos.y;
            if (guiWindowRect.Contains(mousePos))
                EditorLock();
            else
                EditorUnlock();

            myToolTip = myToolTip.Trim();
            if (!string.IsNullOrEmpty(myToolTip))
            {
                int offset = inPartsEditor ? -222 : 440;
                GUI.Label(new Rect(guiWindowRect.xMin + offset, mousePos.y - 5, toolTipWidth, toolTipHeight), myToolTip, Styles.styleEditorTooltip);
            }

            guiWindowRect = GUILayout.Window(unchecked((int)part.persistentId), guiWindowRect, EngineManagerGUI, "Configure " + part.partInfo.title, Styles.styleEditorPanel);
        }

        private void EditorLock()
        {
            if (!editorLocked)
            {
                EditorLogic.fetch.Lock(false, false, false, "RFGUILock");
                editorLocked = true;
                KSP.UI.Screens.Editor.PartListTooltipMasterController.Instance?.HideTooltip();
            }
        }

        private void EditorUnlock()
        {
            if (editorLocked)
            {
                EditorLogic.fetch.Unlock("RFGUILock");
                editorLocked = false;
            }
        }

        protected string GetCostString(ConfigNode node)
        {
            string costString = string.Empty;
            if (node.HasValue("cost"))
            {
                float curCost = scale * float.Parse(node.GetValue("cost"));

                if (techLevel != -1)
                {
                    curCost = CostTL(curCost, node) - CostTL(0f, node); // get purely the config cost difference
                }
                costString = $" ({((curCost < 0) ? string.Empty : "+")}{curCost:N0}f)";
            }
            return costString;
        }

        /// Normal apply action for the 'select <config>' button.
        protected void GUIApplyConfig(string configName)
        {
            SetConfiguration(configName, true);
            UpdateSymmetryCounterparts();
            MarkWindowDirty();
        }

        protected void DrawSelectButton(ConfigNode node, bool isSelected, Action<string> apply)
        {
            var nName = node.GetValue("name");
            var dispName = GetConfigDisplayName(node);
            var costString = GetCostString(node);
            var configInfo = GetConfigInfo(node);

            using (new GUILayout.HorizontalScope())
            {
                // For simulations, RP-1 will allow selecting all configs despite tech status or whether the entry cost has been paid.
                // The KCT that comes with RP-1 will call the Validate() method when the player tries to add a vessel to the build queue.
                if (Utilities.RP1Found)
                {
                    // Currently selected.
                    if (isSelected)
                    {
                        GUILayout.Label(new GUIContent($"Current config: {dispName}{costString}", configInfo));
                    }
                    else if (GUILayout.Button(new GUIContent($"Switch to {dispName}{costString}", configInfo)))
                        apply(nName);

                    if (!UnlockedConfig(node, part))
                    {
                        double upgradeCost = EntryCostManager.Instance.ConfigEntryCost(nName);
                        if (upgradeCost <= 0)
                        {
                            // Auto-buy.
                            EntryCostManager.Instance.PurchaseConfig(nName);
                        }

                        bool isConfigAvailable = CanConfig(node);
                        string tooltip = string.Empty;
                        if (!isConfigAvailable && techNameToTitle.TryGetValue(node.GetValue("techRequired"), out string techStr))
                        {
                            tooltip = $"Lacks tech for {techStr}";
                        }

                        GUI.enabled = isConfigAvailable;
                        if (GUILayout.Button(new GUIContent($"Purchase ({upgradeCost:N0}f)", tooltip), GUILayout.Width(145)))
                        {
                            if (EntryCostManager.Instance.PurchaseConfig(nName))
                                apply(nName);
                        }
                        GUI.enabled = true;
                    }
                }
                else
                {
                    // Currently selected.
                    if (isSelected)
                    {
                        GUILayout.Label(new GUIContent($"Current config: {dispName}{costString}", configInfo));
                        return;
                    }

                    // Locked.
                    if (!CanConfig(node))
                    {
                        if (techNameToTitle.TryGetValue(node.GetValue("techRequired"), out string techStr))
                            techStr = "\nRequires: " + techStr;
                        GUILayout.Label(new GUIContent($"Lacks tech for {dispName}", configInfo + techStr));
                        return;
                    }

                    // Available.
                    if (UnlockedConfig(node, part))
                    {
                        if (GUILayout.Button(new GUIContent($"Switch to {dispName}{costString}", configInfo)))
                            apply(nName);
                        return;
                    }

                    // Purchase.
                    double upgradeCost = EntryCostManager.Instance.ConfigEntryCost(nName);
                    if (upgradeCost > 0d)
                    {
                        costString = $" ({upgradeCost:N0}f)";
                        if (GUILayout.Button(new GUIContent($"Purchase {dispName}{costString}", configInfo)))
                        {
                            if (EntryCostManager.Instance.PurchaseConfig(nName))
                                apply(nName);
                        }
                    }
                    else
                    {
                        // Auto-buy.
                        EntryCostManager.Instance.PurchaseConfig(nName);
                        if (GUILayout.Button(new GUIContent($"Switch to {dispName}{costString}", configInfo)))
                            apply(nName);
                    }
                }
            }
        }

        virtual protected void DrawConfigSelectors(IEnumerable<ConfigNode> availableConfigNodes)
        {
            foreach (ConfigNode node in availableConfigNodes)
                DrawSelectButton(node, node.GetValue("name") == configuration, GUIApplyConfig);
        }

        virtual protected void DrawPartInfo()
        {
            // show current info, cost
            if (pModule != null && part.partInfo != null)
            {
                GUILayout.BeginHorizontal();
                string ratedBurnTime = string.Empty;
                if (config.HasValue("ratedBurnTime"))
                {
                    if (config.HasValue("ratedContinuousBurnTime"))
                        ratedBurnTime = $"Rated burn time: {config.GetValue("ratedContinuousBurnTime")}/{config.GetValue("ratedBurnTime")}s\n";
                    else
                        ratedBurnTime = $"Rated burn time: {config.GetValue("ratedBurnTime")}s\n";
                }
                string label = $"<b>Engine mass:</b> {part.mass:N3}t\n" +
                               $"{ratedBurnTime}" +
                               $"{pModule.GetInfo()}\n" +
                               $"{TLTInfo()}\n" +
                               $"Total cost: {part.partInfo.cost + part.GetModuleCosts(part.partInfo.cost):0}";
                GUILayout.Label(label);
                GUILayout.EndHorizontal();
            }
        }

        protected void DrawTechLevelSelector()
        {
            // NK Tech Level
            if (techLevel != -1)
            {
                GUILayout.BeginHorizontal();

                GUILayout.Label("Tech Level: ");
                string minusStr = "X";
                bool canMinus = false;
                if (TechLevel.CanTL(config, techNodes, engineType, techLevel - 1) && techLevel > minTechLevel)
                {
                    minusStr = "-";
                    canMinus = true;
                }
                if (GUILayout.Button(minusStr) && canMinus)
                {
                    techLevel--;
                    SetConfiguration();
                    UpdateSymmetryCounterparts();
                    MarkWindowDirty();
                }
                GUILayout.Label(techLevel.ToString());
                string plusStr = "X";
                bool canPlus = false;
                bool canBuy = false;
                string tlName = Utilities.GetPartName(part) + configuration;
                double tlIncrMult = (double)(techLevel + 1 - origTechLevel);
                if (TechLevel.CanTL(config, techNodes, engineType, techLevel + 1) && techLevel < maxTechLevel)
                {
                    if (UnlockedTL(tlName, techLevel + 1))
                    {
                        plusStr = "+";
                        canPlus = true;
                    }
                    else
                    {
                        double cost = EntryCostManager.Instance.TLEntryCost(tlName) * tlIncrMult;
                        double sciCost = EntryCostManager.Instance.TLSciEntryCost(tlName) * tlIncrMult;
                        bool autobuy = true;
                        plusStr = string.Empty;
                        if (cost > 0d)
                        {
                            plusStr += cost.ToString("N0") + "f";
                            autobuy = false;
                            canBuy = true;
                        }
                        if (sciCost > 0d)
                        {
                            if (cost > 0d)
                                plusStr += "/";
                            autobuy = false;
                            canBuy = true;
                            plusStr += sciCost.ToString("N1") + "s";
                        }
                        if (autobuy)
                        {
                            // auto-upgrade
                            EntryCostManager.Instance.SetTLUnlocked(tlName, techLevel + 1);
                            plusStr = "+";
                            canPlus = true;
                            canBuy = false;
                        }
                    }
                }
                if (GUILayout.Button(plusStr) && (canPlus || canBuy))
                {
                    if (!canBuy || EntryCostManager.Instance.PurchaseTL(tlName, techLevel + 1, tlIncrMult))
                    {
                        techLevel++;
                        SetConfiguration();
                        UpdateSymmetryCounterparts();
                        MarkWindowDirty();
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        private void EngineManagerGUI(int WindowID)
        {
            GUILayout.Space(20);

            GUILayout.BeginHorizontal();
            GUILayout.Label(EditorDescription);
            GUILayout.EndHorizontal();

            DrawConfigSelectors(FilteredDisplayConfigs(false));

            DrawTechLevelSelector();
            DrawPartInfo();

            if (!myToolTip.Equals(string.Empty) && GUI.tooltip.Equals(string.Empty))
            {
                if (counterTT > 4)
                {
                    myToolTip = GUI.tooltip;
                    counterTT = 0;
                }
                else
                {
                    counterTT++;
                }
            }
            else
            {
                myToolTip = GUI.tooltip;
                counterTT = 0;
            }

            GUI.DragWindow();
        }

        #endregion

        #region Helpers
        public int DoForEachSymmetryCounterpart(Action<ModuleEngineConfigsBase> action)
        {
            int i = 0;
            int mIdx = moduleIndex;
            if (engineID == string.Empty && mIdx < 0)
                mIdx = 0;

            foreach (Part p in part.symmetryCounterparts)
            {
                if (GetSpecifiedModule(p, engineID, mIdx, GetType().Name, false) is ModuleEngineConfigsBase engine)
                {
                    action(engine);
                    ++i;
                }
            }
            return i;
        }

        virtual public int UpdateSymmetryCounterparts()
        {
            return DoForEachSymmetryCounterpart((engine) =>
            {
                engine.techLevel = techLevel;
                engine.SetConfiguration(configuration);
            });
        }

        virtual protected void UpdateOtherModules(ConfigNode node)
        {
            if (node.HasNode("OtherModules"))
            {
                node = node.GetNode("OtherModules");
                for (int i = 0; i < node.values.Count; ++i)
                {
                    if (GetSpecifiedModule(part, node.values[i].name, -1, GetType().Name, false) is ModuleEngineConfigsBase otherM)
                    {
                        otherM.techLevel = techLevel;
                        otherM.SetConfiguration(node.values[i].value);
                    }
                }
            }
        }
        virtual public void CheckConfigs()
        {
            if (configs == null || configs.Count == 0)
                ConfigSaveLoad();
        }
        // run this to save/load non-serialized data
        protected void ConfigSaveLoad()
        {
            string partName = Utilities.GetPartName(part) + moduleIndex + engineID;
            if (configs.Count > 0)
            {
                if (!RFSettings.Instance.engineConfigs.ContainsKey(partName))
                {
                    if (configs.Count > 0)
                        RFSettings.Instance.engineConfigs[partName] = new List<ConfigNode>(configs);
                }
            }
            else if (RFSettings.Instance.engineConfigs.ContainsKey(partName))
                configs = new List<ConfigNode>(RFSettings.Instance.engineConfigs[partName]);
            else
                Debug.LogError($"*RFMEC* ERROR: could not find configs definition for {partName}");
        }

        protected static PartModule GetSpecifiedModule(Part p, string eID, int mIdx, string eType, bool weakType) => GetSpecifiedModules(p, eID, mIdx, eType, weakType).FirstOrDefault();

        private static readonly List<PartModule> _searchList = new List<PartModule>();
        protected static List<PartModule> GetSpecifiedModules(Part p, string eID, int mIdx, string eType, bool weakType)
        {
            int mCount = p.Modules.Count;
            int tmpIdx = 0;
            _searchList.Clear();

            for (int m = 0; m < mCount; ++m)
            {
                PartModule pM = p.Modules[m];
                bool test = false;
                if (weakType)
                {
                    if (eType.Contains("ModuleEngines"))
                        test = pM is ModuleEngines;
                    else if (eType.Contains("ModuleRCS"))
                        test = pM is ModuleRCS;
                }
                else
                    test = pM.GetType().Name.Equals(eType);

                if (test)
                {
                    if (mIdx >= 0)
                    {
                        if (tmpIdx == mIdx)
                        {
                            _searchList.Add(pM);
                        }
                        tmpIdx++;
                        continue; // skip the next test
                    }
                    else if (eID != string.Empty)
                    {
                        string testID = string.Empty;
                        if (pM is ModuleEngines)
                            testID = (pM as ModuleEngines).engineID;
                        else if (pM is ModuleEngineConfigsBase)
                            testID = (pM as ModuleEngineConfigsBase).engineID;

                        if (testID.Equals(eID))
                            _searchList.Add(pM);
                    }
                    else
                        _searchList.Add(pM);
                }
            }
            return _searchList;
        }

        internal void MarkWindowDirty()
        {
            if (UIPartActionController.Instance?.GetItem(part) is UIPartActionWindow action_window)
                action_window.displayDirty = true;
        }
        #endregion

        /// <summary>
        /// Called from RP0KCT when adding vessels to queue to validate whether all the currently selected configs are available and unlocked.
        /// </summary>
        /// <param name="validationError"></param>
        /// <param name="canBeResolved"></param>
        /// <param name="costToResolve"></param>
        /// <returns></returns>
        public virtual bool Validate(out string validationError, out bool canBeResolved, out float costToResolve)
        {
            validationError = null;
            canBeResolved = false;
            costToResolve = 0;

            ConfigNode node = GetConfigByName(configuration);

            if (UnlockedConfig(node, part)) return true;

            if (!CanConfig(node))
            {
                validationError = $"unlock tech {ResearchAndDevelopment.GetTechnologyTitle(config.GetValue("techRequired"))}";
                return false;
            }

            string nName = node.GetValue("name");
            double upgradeCost = EntryCostManager.Instance.ConfigEntryCost(nName);
            canBeResolved = true;
            costToResolve = (float)upgradeCost;
            validationError = $"pay entry cost";

            return false;
        }

        /// <summary>
        /// Called from RP0KCT to purchase configs that were returned as errors in the Validate() method.
        /// </summary>
        /// <returns></returns>
        public virtual bool ResolveValidationError()
        {
            ConfigNode node = GetConfigByName(configuration);
            string nName = node.GetValue("name");
            return EntryCostManager.Instance.PurchaseConfig(nName);
        }
    }
}
