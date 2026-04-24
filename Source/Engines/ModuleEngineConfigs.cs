using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Linq;
using UnityEngine;
using RealFuels.Tanks;
using RealFuels.TechLevels;
using KSP.UI.Screens;
using KSP.Localization;
using Debug = UnityEngine.Debug;

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
                return $"{gimbalRange:0.#}°";
            if (new[] { gimbalRangeXP, gimbalRangeXN, gimbalRangeYP, gimbalRangeYN }.Distinct().Count() == 1)
                return $"{gimbalRangeXP:0.#}°";
            var ret = string.Empty;
            if (gimbalRangeXP == gimbalRangeXN)
                ret += $"{gimbalRangeXP:0.#}° pitch, ";
            else
                ret += $"+{gimbalRangeXP:0.#}°/-{gimbalRangeXN:0.#}° pitch, ";
            if (gimbalRangeYP == gimbalRangeYN)
                ret += $"{gimbalRangeYP:0.#}° yaw";
            else
                ret += $"+{gimbalRangeYP:0.#}°/-{gimbalRangeYN:0.#}° yaw";
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
            return node.GetValue(PatchNameKey); // Just show subconfig name without parent prefix
        }

        public override IEnumerable<ConfigRowDefinition> BuildConfigRows()
        {
            foreach (var node in FilteredDisplayConfigs(false))
            {
                string configName = node.GetValue("name");
                yield return new ConfigRowDefinition
                {
                    Node = node,
                    DisplayName = GetConfigDisplayName(node),
                    IsSelected = configName == configuration && activePatchName == "",
                    Indent = false,
                    Apply = () =>
                    {
                        activePatchName = "";
                        GUIApplyConfig(configName);
                    }
                };

                foreach (var patch in GetPatchesOfConfig(node))
                {
                    var patchedNode = PatchConfig(node, patch, false);
                    string patchName = patch.GetValue("name");
                    string patchedConfigName = configName;
                    yield return new ConfigRowDefinition
                    {
                        Node = patchedNode,
                        DisplayName = GetConfigDisplayName(patchedNode),
                        IsSelected = patchedConfigName == configuration && patchName == activePatchName,
                        Indent = true,
                        Apply = () =>
                        {
                            activePatchName = patchName;
                            GUIApplyConfig(patchedConfigName);
                        }
                    };
                }
            }
        }
    }

    public class ModuleEngineConfigsBase : PartModule, IPartCostModifier, IPartMassModifier
    {
        //protected const string groupName = "ModuleEngineConfigs";
        public const string groupName = ModuleEnginesRF.groupName;
        public const string groupDisplayName = "#RF_Engine_EngineConfigs"; // "Engine Configs"
        #region Fields
        internal bool compatible = true;

        [KSPField(isPersistant = true)]
        public string configuration = string.Empty;

        // For display purposes only.
        [KSPField(guiName = "#RF_Engine_Configuration", isPersistant = true, guiActiveEditor = true, guiActive = true, // Configuration
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

        public int? Ignitions { get; protected set; }

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
            field.guiName = Localizer.GetStringByTag("#RF_Engine_ThrusterPower"); // Thruster Power
            field.guiUnits = "kN";
            field.group = new BasePAWGroup(groupName, groupDisplayName, false);
        }

        internal List<ConfigNode> FilteredDisplayConfigs(bool update)
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
            {
                GameEvents.onPartActionUIDismiss.Add(OnPartActionGuiDismiss);
                GameEvents.onPartActionUIShown.Add(OnPartActionUIShown);
            }

            ConfigSaveLoad();

            Integrations.LoadB9PSModules();

            LoadDefaultGimbals();

            SetConfiguration();

            Fields[nameof(showRFGUI)].guiName = GUIButtonName;

            // Why is this here, if KSP will call this normally?
            part.Modules.GetModule("ModuleEngineIgnitor")?.OnStart(state);
        }

        public override void OnStartFinished(StartState state)
        {
            Integrations.HideB9PSVariantSelectors();
            if (pModule is ModuleRCS mrcs) RelocateRCSPawItems(mrcs);
        }
        #endregion

        #region Info Methods
        private string TLTInfo()
        {
            string retStr = string.Empty;
            if (engineID != string.Empty)
                retStr += $"{Localizer.Format("#RF_Engine_BoundToEngineID", engineID)}\n"; // (Bound to {engineID})
            if (moduleIndex >= 0)
                retStr += $"{Localizer.Format("#RF_Engine_BoundToModuleIndex", moduleIndex)}\n"; // (Bound to engine {moduleIndex} in part)
            if (techLevel != -1)
            {
                TechLevel cTL = new TechLevel();
                if (!cTL.Load(config, techNodes, engineType, techLevel))
                    cTL = null;

                if (!string.IsNullOrEmpty(configDescription))
                    retStr += configDescription + "\n";

                retStr += $"{Localizer.GetStringByTag("#RF_Engine_TLTInfo_Type")}: {engineType}. {Localizer.GetStringByTag("#RF_Engine_TLTInfo_TechLevel")}: {techLevel} ({origTechLevel}-{maxTechLevel})"; // TypeTech Level
                if (origMass > 0)
                    retStr += $", {Localizer.Format("#RF_Engine_TLTInfo_OrigMass", $"{part.mass:N3}", $"{origMass * RFSettings.Instance.EngineMassMultiplier:N3}")}"; // Mass: {part.mass:N3} (was {origMass * RFSettings.Instance.EngineMassMultiplier:N3})
                if (configThrottle >= 0)
                    retStr += $", {Localizer.GetStringByTag("#RF_Engine_TLTInfo_MinThrust")} {configThrottle:P0}"; // MinThr

                float gimbalR = -1f;
                if (config.HasValue("gimbalRange"))
                    gimbalR = float.Parse(config.GetValue("gimbalRange"), CultureInfo.InvariantCulture);
                else if (!gimbalTransform.Equals(string.Empty) || useGimbalAnyway)
                {
                    if (cTL != null)
                        gimbalR = cTL.GimbalRange;
                }
                if (gimbalR != -1f)
                    retStr += $", {Localizer.GetStringByTag("#RF_Engine_TLTInfo_Gimbal")} {gimbalR:N1}"; // Gimbal
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

            string info = TLTInfo() + $"\n{Localizer.GetStringByTag("#RF_Engine_AlternateConfigurations")}:\n"; // Alternate configurations

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
                info.Append($"  {Utilities.FormatThrust(scale * TechLevels.ThrustTL(config.GetValue(thrustRating), config))}");
                // add throttling info if present
                if (config.HasValue("minThrust"))
                    info.Append($", {Localizer.GetStringByTag("#RF_Engine_minThrustInfo")} {float.Parse(config.GetValue("minThrust"), CultureInfo.InvariantCulture) / float.Parse(config.GetValue(thrustRating), CultureInfo.InvariantCulture):P0}"); //min
                else if (config.HasValue("throttle"))
                    info.Append($", {Localizer.GetStringByTag("#RF_Engine_minThrustInfo")} {float.Parse(config.GetValue("throttle"), CultureInfo.InvariantCulture):P0}"); // min
            }
            else
                info.Append($"  {Localizer.GetStringByTag("#RF_Engine_UnknownThrust")}"); // Unknown Thrust

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
                info.Append($"  {Localizer.GetStringByTag("#RF_Engine_Isp")}: {isp.Evaluate(isp.maxTime)} - {isp.Evaluate(isp.minTime)}s\n"); // Isp
            }
            else if (config.HasValue("IspSL") && config.HasValue("IspV") && cTL != null)
            {
                float.TryParse(config.GetValue("IspSL"), out float ispSL);
                float.TryParse(config.GetValue("IspV"), out float ispV);
                ispSL *= ispSLMult * cTL.AtmosphereCurve.Evaluate(1);
                ispV *= ispVMult * cTL.AtmosphereCurve.Evaluate(0);
                info.Append($"  {Localizer.GetStringByTag("#RF_Engine_Isp")}: {ispSL:N0} - {ispV:N0}s\n"); // Isp
            }

            if (config.HasNode("PROPELLANT"))
            {
                var propellants = config.GetNodes("PROPELLANT")
                    .Select(node =>
                    {
                        string name = node.GetValue("name");
                        string ratioStr = null;
                        if (node.TryGetValue("ratio", ref ratioStr) && float.TryParse(ratioStr, out float ratio))
                            return $"{name} ({ratio:N3})";
                        return name;
                    })
                    .Where(name => !string.IsNullOrWhiteSpace(name));

                string propellantList = string.Join(", ", propellants);
                if (!string.IsNullOrWhiteSpace(propellantList))
                    info.Append($"  {Localizer.GetStringByTag("#RF_EngineRF_Propellant")}: {propellantList}\n");
            }

            if (config.HasValue("ratedBurnTime"))
            {
                if (config.HasValue("ratedContinuousBurnTime"))
                    info.Append($"  {Localizer.GetStringByTag("#RF_Engine_RatedBurnTime")}: {config.GetValue("ratedContinuousBurnTime")}/{config.GetValue("ratedBurnTime")}s\n"); // Rated burn time
                else
                    info.Append($"  {Localizer.GetStringByTag("#RF_Engine_RatedBurnTime")}: {config.GetValue("ratedBurnTime")}s\n"); // Rated burn time
            }

            if (part.HasModuleImplementing<ModuleGimbal>())
            {
                if (config.HasNode("GIMBAL"))
                {
                    foreach (KeyValuePair<string, Gimbal> kv in ExtractGimbals(config))
                    {
                        info.Append($"  {Localizer.GetStringByTag("#RF_Engine_TLTInfo_Gimbal")} ({kv.Key}): {kv.Value.Info()}\n"); // Gimbal
                    }
                }
                else if (config.HasValue("gimbalRange"))
                {
                    // The extracted gimbals contain `gimbalRange` et al. applied to either a specific
                    // transform or all the gimbal transforms on the part. Either way, the values
                    // are all the same, so just take the first one.
                    var gimbal = ExtractGimbals(config).Values.First();
                    info.Append($"  Gimbal {gimbal.Info()}\n"); // 
                }
            }

            if (config.HasValue("ullage") || config.HasValue("ignitions") || config.HasValue("pressureFed"))
            {
                info.Append("  ");
                bool comma = false;
                if (config.HasValue("ullage"))
                {
                    info.Append(config.GetValue("ullage").ToLower() == "true" ? Localizer.GetStringByTag("#RF_Engine_ullage") : Localizer.GetStringByTag("#RF_Engine_NoUllage")); // "ullage""no ullage"
                    comma = true;
                }
                if (config.HasValue("pressureFed") && config.GetValue("pressureFed").ToLower() == "true")
                {
                    if (comma)
                        info.Append(", ");
                    info.Append(Localizer.GetStringByTag("#RF_Engine_pressureFed")); // "pfed"
                    comma = true;
                }

                if (config.HasValue("ignitions"))
                {
                    if (int.TryParse(config.GetValue("ignitions"), out int ignitions))
                    {
                        if (comma)
                            info.Append(", ");
                        if (ignitions > 0)
                            info.Append(Localizer.Format("#RF_Engine_ignitionsleft", ignitions)); // $"{ignitions} ignition{(ignitions > 1 ? "s" : string.Empty)}"
                        else if (literalZeroIgnitions && ignitions == 0)
                            info.Append(Localizer.GetStringByTag("#RF_Engine_GroundIgnitionOnly")); // "ground ignition only"
                        else
                            info.Append(Localizer.GetStringByTag("#RF_Engine_unlignitions")); // "unl. ignitions"
                    }
                }
                info.Append("\n");
            }
            if (config.HasValue("cost") && float.TryParse(config.GetValue("cost"), out float cst))
                info.Append($"  ({scale * cst:N0}√ {Localizer.GetStringByTag("#RF_Engine_extraCost")} )\n"); // extra cost// FIXME should get cost from TL, but this should be safe

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

            if ((pModule = GetSpecifiedModule(part, engineID, moduleIndex, type, useWeakType)) is null)
            {
                Debug.LogError($"*RFMEC* Could not find appropriate module of type {type}, with ID={engineID} and index {moduleIndex}");
                return;
            }

            Type mType = pModule.GetType();
            config.SetValue("name", mType.Name);

            EngineConfigPropellants.ClearFloatCurves(mType, pModule, config, techLevel);
            EngineConfigPropellants.ClearPropellantGauges(mType, pModule);

            if (type.Equals("ModuleRCS") || type.Equals("ModuleRCSFX"))
                EngineConfigPropellants.ClearRCSPropellants(part, config, DoConfig);
            else
            { // is an ENGINE
                if (pModule is ModuleEngines mE && config.HasNode("PROPELLANT"))
                    mE.propellants.Clear();

                DoConfig(config);

                HandleEngineIgnitor(config);

                Ignitions = null;
                if (config.HasValue("ignitions"))
                {
                    if (int.TryParse(config.GetValue("ignitions"), out int tmpIgnitions))
                    {
                        Ignitions = TechLevels.ConfigIgnitions(tmpIgnitions);
                        config.SetValue("ignitions", Ignitions.Value);
                    }

                    if (HighLogic.LoadedSceneIsFlight && vessel?.situation != Vessel.Situations.PRELAUNCH)
                        config.RemoveValue("ignitions");
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

            if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch.ship != null)
            {
                EditorPartSetMaintainer.Instance.ScheduleUsedBySetsUpdate();
            }

            SetupFX();

            Integrations.UpdateB9PSVariants();

            Integrations.UpdateTFInterops(); // update TestFlight if it's installed

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

        #region SetConfiguration Tools
        internal Dictionary<string, Gimbal> ExtractGimbals(ConfigNode cfg)
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
                        ignitions = TechLevels.ConfigIgnitions(ignitions);
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
                gimbal = float.Parse(cfg.GetValue("gimbalRange"), CultureInfo.InvariantCulture);

            float cost = 0f;
            if (cfg.HasValue("cost"))
                cost = scale * float.Parse(cfg.GetValue("cost"), CultureInfo.InvariantCulture);

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
                    configHeat = TechLevels.MassTL(configHeat);

                // set thrust and throttle
                if (configMaxThrust >= 0)
                {
                    configMaxThrust = TechLevels.ThrustTL(configMaxThrust);
                    if (configMinThrust >= 0)
                        configMinThrust = TechLevels.ThrustTL(configMinThrust);
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
                        TLMassMult = TechLevels.MassTL(1.0f);
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
                        gimbal *= float.Parse(cfg.GetValue("gimbalMult"), CultureInfo.InvariantCulture);
                }

                // Cost (multiplier will be 1.0 if unspecified)
                cost = scale * TechLevels.CostTL(cost, cfg);
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
            // These previously used the format "0.0000" but that sets thrust to 0 for engines with < that in kN
            // so we'll just use default.
            if (configMaxThrust >= 0f)
                cfg.SetValue(thrustRating, configMaxThrust, true);
            if (configMinThrust >= 0f)
                cfg.SetValue("minThrust", configMinThrust, true); // will be ignored by RCS, so what.

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

        #endregion

        #region GUI
        public virtual string GUIButtonName => Localizer.GetStringByTag("#RF_Engine_ButtonName"); // "Engine"
        public virtual string EditorDescription => Localizer.GetStringByTag("#RF_Engine_ButtonName_desc"); // "Select a configuration for this engine."
        [KSPField(guiActiveEditor = true, guiName = "#RF_Engine_ButtonName", groupName = groupName), // Engine
         UI_Toggle(enabledText = "#RF_Engine_GUIHide", disabledText = "#RF_Engine_GUIShow")] // Hide GUIShow GUI
        [NonSerialized]
        public bool showRFGUI;

        // Track if the user manually closed this specific window (per-instance, not static)
        [NonSerialized]
        private bool userClosedWindow = false;

        // Track the currently open GUI to ensure only one is visible at a time
        private static ModuleEngineConfigsBase currentlyOpenGUI = null;

        private void OnPartActionGuiDismiss(Part p)
        {
            if (p == part || p.isSymmetryCounterPart(part))
            {
                showRFGUI = false;
                // Clear the currently open GUI tracker if it's this instance
                if (currentlyOpenGUI == this)
                    currentlyOpenGUI = null;
            }
        }

        private void OnPartActionUIShown(UIPartActionWindow window, Part p)
        {
            if (p == part && !userClosedWindow)
            {
                // Close any previously open GUI before opening this one
                if (currentlyOpenGUI != null && currentlyOpenGUI != this)
                {
                    currentlyOpenGUI.showRFGUI = false;
                }

                showRFGUI = isMaster;

                // Track this as the currently open GUI
                if (isMaster)
                    currentlyOpenGUI = this;
            }
        }

        public override void OnInactive()
        {
            if (!compatible)
                return;
            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onPartActionUIDismiss.Remove(OnPartActionGuiDismiss);
                GameEvents.onPartActionUIShown.Remove(OnPartActionUIShown);
            }
        }

        public void OnDestroy()
        {
            GameEvents.onPartActionUIDismiss.Remove(OnPartActionGuiDismiss);
            GameEvents.onPartActionUIShown.Remove(OnPartActionUIShown);

            // Note: We don't call EngineConfigTextures.Cleanup() here because textures
            // are shared across all instances. They'll be cleaned up when Unity unloads the scene.
        }

        // Tech level management - lazy initialization
        private EngineConfigTechLevels _techLevels;
        protected EngineConfigTechLevels TechLevels
        {
            get
            {
                if (_techLevels == null)
                    _techLevels = new EngineConfigTechLevels(this);
                return _techLevels;
            }
        }

        // Integration with B9PartSwitch and TestFlight - lazy initialization
        private EngineConfigIntegrations _integrations;
        internal EngineConfigIntegrations Integrations
        {
            get
            {
                if (_integrations == null)
                    _integrations = new EngineConfigIntegrations(this);
                return _integrations;
            }
        }

        // GUI rendering - lazy initialization
        private EngineConfigGUI _gui;
        private EngineConfigGUI GUI
        {
            get
            {
                if (_gui == null)
                    _gui = new EngineConfigGUI(this);
                return _gui;
            }
        }

        /// <summary>
        /// Struct for passing configuration row data to GUI
        /// </summary>
        public struct ConfigRowDefinition
        {
            public ConfigNode Node;
            public string DisplayName;
            public bool IsSelected;
            public bool Indent;
            public Action Apply;
        }

        /// <summary>
        /// Builds the list of configuration rows to display in the GUI.
        /// Virtual so derived classes can customize the row structure.
        /// </summary>
        public virtual IEnumerable<ConfigRowDefinition> BuildConfigRows()
        {
            foreach (var node in FilteredDisplayConfigs(false))
            {
                string configName = node.GetValue("name");
                yield return new ConfigRowDefinition
                {
                    Node = node,
                    DisplayName = GetConfigDisplayName(node),
                    IsSelected = configName == configuration,
                    Indent = false,
                    Apply = () => GUIApplyConfig(configName)
                };
            }
        }

        /// <summary>
        /// Draws the configuration selector UI elements.
        /// Virtual so derived classes can add custom UI before the config table.
        /// </summary>
        protected internal virtual void DrawConfigSelectors(IEnumerable<ConfigNode> availableConfigNodes)
        {
            // Default implementation - just draw the table
            // Derived classes can override to add custom UI
        }

        /// <summary>
        /// Internal callback for GUI to apply a selected configuration.
        /// </summary>
        internal void GUIApplyConfig(string configName)
        {
            SetConfiguration(configName);
            UpdateSymmetryCounterparts();
        }

        /// <summary>
        /// Hook point for external mod compatibility (e.g., RP-1 Harmony patches).
        /// Called by the GUI before rendering each config row to allow external mods
        /// to track context via Harmony prefix/postfix patches.
        /// Does not render anything - rendering is handled by EngineConfigGUI.
        /// </summary>
        internal void DrawSelectButton(ConfigNode node, bool isSelected, Action<string> applyCallback)
        {
            // Hook point for external mods (RP-1) to patch and track tech node context
            // RP-1's Harmony prefix sets techNode here, and postfix clears it after this method returns
            // So we must invoke the callback HERE, not after this returns, to keep techNode set
            string configName = node?.GetValue("name") ?? "null";

            // Invoke the callback while we're still inside this method (before RP-1's postfix clears techNode)
            applyCallback?.Invoke(configName);
        }

        private bool lastShowRFGUI = false;

        public void OnGUI()
        {
            if (isMaster && HighLogic.LoadedSceneIsEditor)
            {
                // If the user clicked the PAW button to show the GUI, clear the userClosedWindow flag
                if (showRFGUI && !lastShowRFGUI)
                {
                    userClosedWindow = false;

                    // Close any previously open GUI before opening this one
                    if (currentlyOpenGUI != null && currentlyOpenGUI != this)
                    {
                        currentlyOpenGUI.showRFGUI = false;
                    }

                    // Track this as the currently open GUI
                    currentlyOpenGUI = this;
                }
                // If the user clicked the PAW button to hide the GUI
                else if (!showRFGUI && lastShowRFGUI)
                {
                    // Clear the currently open GUI tracker if it's this instance
                    if (currentlyOpenGUI == this)
                        currentlyOpenGUI = null;
                }

                lastShowRFGUI = showRFGUI;

                GUI.OnGUI();
            }
        }

        internal void CloseWindow()
        {
            showRFGUI = false;
            userClosedWindow = true;

            // Clear the currently open GUI tracker if it's this instance
            if (currentlyOpenGUI == this)
                currentlyOpenGUI = null;
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
        internal static List<PartModule> GetSpecifiedModules(Part p, string eID, int mIdx, string eType, bool weakType)
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
        public virtual bool Validate(out string validationError, out bool canBeResolved, out float costToResolve, out string techToResolve)
        {
            validationError = null;
            canBeResolved = false;
            costToResolve = 0;
            techToResolve = null;

            ConfigNode node = GetConfigByName(configuration);

            if (EngineConfigTechLevels.UnlockedConfig(node, part)) return true;

            techToResolve = config.GetValue("techRequired");
            if (!EngineConfigTechLevels.CanConfig(node))
            {
                validationError = $"{Localizer.GetStringByTag("#RF_Engine_unlocktech")} {ResearchAndDevelopment.GetTechnologyTitle(techToResolve)}"; // unlock tech
                canBeResolved = false;
            }
            else
            {
                validationError = Localizer.GetStringByTag("#RF_Engine_PayEntryCost"); // $"pay entry cost"
                canBeResolved = true;
            }

            string nName = node.GetValue("name");
            double upgradeCost = EntryCostManager.Instance.ConfigEntryCost(nName);
            costToResolve = (float)upgradeCost;
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
            return EntryCostManager.Instance.PurchaseConfig(nName, node.GetValue("techRequired"));
        }
    }
}
