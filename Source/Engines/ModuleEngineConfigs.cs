using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Linq;
using UnityEngine;
using KSP;
using Debug = UnityEngine.Debug;
using RealFuels.TechLevels;
using SolverEngines;
using KSP.UI.Screens;

namespace RealFuels
{
    public class ModuleHybridEngine : ModuleEngineConfigs
    {
        ModuleEngines ActiveEngine = null;

        public override void OnStart (StartState state)
        {
            base.OnStart(state);
        }

        public override void OnInitialize()
        {
            if (!compatible)
                return;

            SetConfiguration();
        }
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            if (!isMaster)
            {
                Actions["SwitchAction"].active = false;
                Events["SwitchEngine"].guiActive = false;
                Events["SwitchEngine"].guiActiveEditor = false;
                Events["SwitchEngine"].guiActiveUnfocused = false;
            }
        }

        [KSPAction("Switch Engine Mode")]
        public void SwitchAction (KSPActionParam param)
        {
            SwitchEngine ();
        }

        [KSPEvent(guiActive=true, guiName="Switch Engine Mode")]
        public void SwitchEngine ()
        {
            ConfigNode currentConfig = configs.Find (c => c.GetValue ("name").Equals (configuration));
            string nextConfiguration = configs[(configs.IndexOf (currentConfig) + 1) % configs.Count].GetValue ("name");
            SetConfiguration(nextConfiguration);
            // TODO: Does Engine Ignitor get switched here?
        }

        override public void SetConfiguration(string newConfiguration = null, bool resetTechLevels = false)
        {
            if (ActiveEngine == null)
                ActiveEngine = GetSpecifiedModule(part, engineID, moduleIndex, type, useWeakType) as ModuleEngines;

            bool engineActive = ActiveEngine.getIgnitionState;
            ActiveEngine.EngineIgnited = false;

            base.SetConfiguration(newConfiguration, resetTechLevels);

            if (engineActive)
                ActiveEngine.Actions["ActivateAction"].Invoke(new KSPActionParam(KSPActionGroup.None, KSPActionType.Activate));
        }
    }

    public class ModuleEngineConfigs : PartModule, IPartCostModifier, IPartMassModifier
    {
        #region Fields
        protected bool compatible = true;

        [KSPField(isPersistant = true)]
        public string configuration = string.Empty;

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
        // For TestFlight integration, only ONE ModuleEngineConfigs (or child class) can be
        // the master module on a part.


        // - dunno why ialdabaoth had this persistent. [KSPField(isPersistant = true)]
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
        protected static bool tfFound = false;
        protected static Type tfInterface = null;
        protected static BindingFlags tfBindingFlags = BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Static;

        public void UpdateTFInterops()
        {
            // Grab a pointer to the TestFlight interface if its installed
            if (!tfChecked)
            {
                tfInterface = Type.GetType("TestFlightCore.TestFlightInterface, TestFlightCore", false);
                if (tfInterface != null)
                    tfFound = true;
            }
            // update TestFlight if its installed
            if (tfFound)
            {
                try
                {
                    tfInterface.InvokeMember("AddInteropValue", tfBindingFlags, null, null, new System.Object[] { this.part, isMaster ? "engineConfig" : "vernierConfig", configuration, "RealFuels" });
                }
                catch
                {
                }
            }
        }
        #endregion

        #region Callbacks
        public float GetModuleCost(float stdCost, ModifierStagingSituation sit)
        {
            return configCost;
        }
        public ModifierChangeWhen GetModuleCostChangeWhen() { return ModifierChangeWhen.FIXED; }

        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit)
        {
            return massDelta;
        }
        public ModifierChangeWhen GetModuleMassChangeWhen() { return ModifierChangeWhen.FIXED; }

        [KSPEvent(guiActive = false, active = true)]
        void OnPartScaleChanged(BaseEventDetails data)
        {
            float factorAbsolute = data.Get<float>("factorAbsolute");
            float factorRelative = data.Get<float>("factorRelative");
            scale = factorAbsolute * factorAbsolute; // quadratic
            SetConfiguration();
            /*Debug.Log("PartMessage: OnPartScaleChanged:"
                + "\npart=" + part.name
                + "\nfactorRelative=" + factorRelative.ToString()
                + "\nfactorAbsolute=" + factorAbsolute.ToString());*/
        }
        #endregion

        #region PartModule Overrides
        public override void OnAwake ()
        {
            techNodes = new ConfigNode();
            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onPartActionUIDismiss.Add(OnPartActionGuiDismiss);
                string fullPath = KSPUtil.ApplicationRootPath + HighLogic.CurrentGame.Parameters.Career.TechTreeUrl;

                ConfigNode fileNode = ConfigNode.Load(fullPath);
                if (fileNode.HasNode("TechTree"))
                {
                    techNameToTitle.Clear();

                    ConfigNode treeNode = fileNode.GetNode("TechTree");
                    ConfigNode[] ns = treeNode.GetNodes("RDNode");
                    foreach (ConfigNode n in ns)
                    {
                        if (n.HasValue("id") && n.HasValue("title"))
                            techNameToTitle[n.GetValue("id")] = n.GetValue("title");
                    }
                }
            }

            if(configs == null)
                configs = new List<ConfigNode>();
        }

        public override void OnLoad(ConfigNode node)
        {
            if (!compatible)
                return;
            base.OnLoad (node);
            if (!isMaster)
            {
                Fields["showRFGUI"].guiActiveEditor = false;
                //Events["NextEngine"].guiActiveEditor = false;
                //Events["NextTech"].guiActiveEditor = false;
            }


            if (techLevel != -1)
            {
                if (maxTechLevel < 0)
                    maxTechLevel = TechLevel.MaxTL(node, engineType);
                if (minTechLevel < 0)
                    minTechLevel = (origTechLevel < techLevel ? origTechLevel : techLevel);
            }

            if(origMass > 0)
            {
                massDelta = 0;
                part.mass = origMass * RFSettings.Instance.EngineMassMultiplier;
                if ((object)(part.partInfo) != null)
                    if ((object)(part.partInfo.partPrefab) != null)
                        massDelta = part.mass - part.partInfo.partPrefab.mass;
            }

            if (configs == null)
                configs = new List<ConfigNode>();

            ConfigNode[] cNodes = node.GetNodes("CONFIG");
            if (cNodes != null && cNodes.Length > 0)
            {
                configs.Clear();

                foreach (ConfigNode subNode in cNodes) {
                    //Debug.Log("*RFMEC* Load Engine Configs. Part " + part.name + " has config " + subNode.GetValue("name"));
                    ConfigNode newNode = new ConfigNode("CONFIG");
                    subNode.CopyTo(newNode);
                    configs.Add(newNode);
                }
            }


            techNodes = new ConfigNode();
            ConfigNode[] tLs = node.GetNodes("TECHLEVEL");
            foreach (ConfigNode n in tLs)
                techNodes.AddNode(n);

            ConfigSaveLoad();

            SetConfiguration();
        }

        public override void OnSave (ConfigNode node)
        {
            if (!compatible)
                return;
            /*if (configs == null)
                configs = new List<ConfigNode> ();
            foreach (ConfigNode c in configs)
            {
                ConfigNode subNode = new ConfigNode("CONFIG");
                c.CopyTo(subNode);
                node.AddNode(subNode);
            }*/
        }

        public override void OnStart(StartState state)
        {
            if (!compatible)
                return;
            this.enabled = true;

            ConfigSaveLoad();

            SetConfiguration();

            if (part.Modules.Contains("ModuleEngineIgnitor"))
                part.Modules["ModuleEngineIgnitor"].OnStart(state);
        }

        public override void OnInitialize()
        {
            if (!compatible)
                return;

            SetConfiguration();
        }
        #endregion

        #region Info Methods
        private string TLTInfo()
        {
            string retStr = string.Empty;
            if (engineID != string.Empty)
                retStr += "(Bound to " + engineID + ")\n";
            if(moduleIndex >= 0)
                retStr += "(Bound to engine " + moduleIndex + " in part)\n";
            if(techLevel != -1)
            {
                TechLevel cTL = new TechLevel();
                if (!cTL.Load(config, techNodes, engineType, techLevel))
                    cTL = null;

                if (!string.IsNullOrEmpty(configDescription))
                    retStr += configDescription + "\n";

                retStr +=  "Type: " + engineType + ". Tech Level: " + techLevel + " (" + origTechLevel + "-" + maxTechLevel + ")";
                if (origMass > 0)
                    retStr += ", Mass: " + part.mass.ToString("N3") + " (was " + (origMass * RFSettings.Instance.EngineMassMultiplier).ToString("N3") + ")";
                if (configThrottle >= 0)
                    retStr += ", MinThr " + (100f * configThrottle).ToString("N0") + "%";

                float gimbalR = -1f;
                if (config.HasValue("gimbalRange"))
                    gimbalR = float.Parse(config.GetValue("gimbalRange"));
                else if (!gimbalTransform.Equals(string.Empty) || useGimbalAnyway)
                {
                    if (cTL != null)
                        gimbalR = cTL.GimbalRange;
                }
                if (gimbalR != -1f)
                    retStr += ", Gimbal " + gimbalR.ToString("N1");

                return retStr;
            }
            else
                return string.Empty;
        }

        public override string GetInfo ()
        {
            if (!compatible)
                return string.Empty;
            if (configs.Count < 2)
                return TLTInfo();

            string info = TLTInfo() + "\nAlternate configurations:\n";

            //Unused as yet
            /*TechLevel moduleTLInfo = new TechLevel();
            if (techNodes != null)
                moduleTLInfo.Load(techNodes, techLevel);
            else
                moduleTLInfo = null;*/

            foreach (ConfigNode config in configs)
                if(!config.GetValue ("name").Equals (configuration))
                    info += GetConfigInfo(config);

            return info;
        }

        public string GetConfigInfo(ConfigNode config)
        {
            TechLevel cTL = new TechLevel();
            if (!cTL.Load(config, techNodes, engineType, techLevel))
                cTL = null;

            string info = "   " + config.GetValue("name") + "\n";
            if (config.HasValue("description"))
                info += "    " + config.GetValue("description") + "\n";
            if (config.HasValue("tfRatedBurnTime"))
                info += "    " + config.GetValue("tfRatedBurnTime") + "\n";
            if (config.HasValue(thrustRating))
            {
                info += "    " + Utilities.FormatThrust(scale * ThrustTL(config.GetValue(thrustRating), config));
                // add throttling info if present
                if (config.HasValue("minThrust"))
                    info += ", min " + (float.Parse(config.GetValue("minThrust")) / float.Parse(config.GetValue(thrustRating)) * 100f).ToString("N0") + "%";
                else if (config.HasValue("throttle"))
                    info += ", min " + (float.Parse(config.GetValue("throttle")) * 100f).ToString("N0") + "%";
            }
            else
                info += "    Unknown Thrust";

            if (origMass > 0f)
            {
                float cMass = scale;
                float ftmp;
                if (config.HasValue("massMult"))
                    if (float.TryParse(config.GetValue("massMult"), out ftmp))
                        cMass *= ftmp;

                cMass = origMass * cMass * RFSettings.Instance.EngineMassMultiplier;

                info += ", " + cMass.ToString("N3") + "t";
            }
            info += "\n";

            FloatCurve isp = new FloatCurve();
            if (config.HasNode("atmosphereCurve"))
            {
                isp.Load(config.GetNode("atmosphereCurve"));
                info += "    Isp: "
                    + isp.Evaluate(isp.maxTime).ToString() + " - "
                      + isp.Evaluate(isp.minTime).ToString() + "s\n";
            }
            else if (config.HasValue("IspSL") && config.HasValue("IspV"))
            {
                float ispSL = 1.0f, ispV = 1.0f;
                float.TryParse(config.GetValue("IspSL"), out ispSL);
                float.TryParse(config.GetValue("IspV"), out ispV);
                if (cTL != null)
                {
                    ispSL *= ispSLMult * cTL.AtmosphereCurve.Evaluate(1);
                    ispV *= ispVMult * cTL.AtmosphereCurve.Evaluate(0);
                    info += "    Isp: " + ispSL.ToString("0") + " - " + ispV.ToString("0") + "s\n";
                }
            }
            float gimbalR = -1f;
            if (config.HasValue("gimbalRange"))
                gimbalR = float.Parse(config.GetValue("gimbalRange"));
            // Don't do per-TL checks here, they're misleading.
            /*else if (!gimbalTransform.Equals(string.Empty) || useGimbalAnyway)
            {
                if (cTL != null)
                    gimbalR = cTL.GimbalRange;
            }*/
            if (gimbalR != -1f)
                info += "    Gimbal " + gimbalR.ToString("N1") + "d\n";

            if (config.HasValue("ullage") || config.HasValue("ignitions") || config.HasValue("pressureFed"))
            {
                info += "    ";
                bool comma = false;
                if (config.HasValue("ullage"))
                {
                    info += (config.GetValue("ullage").ToLower() == "true" ? "ullage" : "no ullage");
                    comma = true;
                }
                if (config.HasValue("pressureFed") && config.GetValue("pressureFed").ToLower() == "true")
                {
                    if (comma)
                        info += ", ";
                    info += "pfed";
                    comma = true;
                }

                if (config.HasValue("ignitions"))
                {
                    int ignitions;
                    if (int.TryParse(config.GetValue("ignitions"), out ignitions))
                    {
                        if (comma)
                            info += ", ";
                        if (ignitions > 0)
                            info += ignitions + " ignition" + (ignitions > 1 ? "s" : string.Empty);
                        else if (literalZeroIgnitions && ignitions == 0)
                            info += "ground ignition only";
                        else
                            info += "unl. ignitions";
                    }
                }
                info += "\n";
            }
            float cst;
            if (config.HasValue("cost") && float.TryParse(config.GetValue("cost"), out cst))
                info += "    (" + (scale * cst).ToString("N0") + " extra cost)\n"; // FIXME should get cost from TL, but this should be safe

            return info;
        }
        #endregion

        #region FX handling
        string[] effectsToStop;
        public void SetupFX()
        {
            List<string> ourFX = new List<string>();

            // grab all effects
            List<string> effectsNames = new List<string>();
            effectsNames.Add("runningEffectName");
            effectsNames.Add("powerEffectName");
            effectsNames.Add("directThrottleEffectName");
            effectsNames.Add("disengageEffectName");
            effectsNames.Add("engageEffectName");
            effectsNames.Add("flameoutEffectName");

            // will be pushed to effectsToStop when done
            Dictionary<string, bool> otherCfgsFX = new Dictionary<string, bool>();

            // for each config and effect name, apply to dict if not us, else add to list of ours.
            foreach (ConfigNode cfg in configs)
            {
                bool ourConfig = cfg.GetValue("name").Equals(configuration);
                foreach (string fxName in effectsNames)
                {
                    if (cfg.HasValue(fxName))
                    {
                        string val = cfg.GetValue(fxName);
                        if (ourConfig)
                            ourFX.Add(val);
                        else
                            otherCfgsFX[val] = true;

                    }
                }
            }
            foreach (string s in ourFX)
            {
                otherCfgsFX.Remove(s);
            }
            effectsToStop = new string[otherCfgsFX.Keys.Count];
            otherCfgsFX.Keys.CopyTo(effectsToStop, 0);
        }
        public void StopFX()
        {
            //if(HighLogic.LoadedSceneIsFlight)
            if (part != null && effectsToStop != null)
            {
                for (int i = effectsToStop.Length - 1; i >= 0; --i)
                    part.Effect(effectsToStop[i], 0f);
            }
        }
        #endregion

        #region MonoBehaviour Methods
        /*virtual public void FixedUpdate()
        {
            if (!compatible)
                return;
            if (vessel == null)
                return;

            StopFX();
        }*/
        #endregion

        #region Configuration
        public PartModule pModule = null;

        virtual public void SetConfiguration(string newConfiguration = null, bool resetTechLevels = false)
        {

            if (newConfiguration == null)
                newConfiguration = configuration;

            ConfigSaveLoad();

            ConfigNode newConfig = configs.Find (c => c.GetValue ("name").Equals (newConfiguration));
            if (newConfig != null)
            {
                if (configuration != newConfiguration)
                {
                    if(resetTechLevels)
                        techLevel = origTechLevel;

                    while (techLevel > 0)
                    {
                        if (TechLevel.CanTL(newConfig, techNodes, engineType, techLevel))
                            break;
                        else
                            --techLevel;
                    }
                }

                // for asmi
                if (useConfigAsTitle)
                    part.partInfo.title = configuration;

                configuration = newConfiguration;
                config = new ConfigNode("MODULE");
                newConfig.CopyTo(config);
                config.name = "MODULE";

#if DEBUG
                print ("replacing " + type + " with:");
                print (newConfig.ToString ());
#endif

                pModule = null;
                // get correct module
                pModule = GetSpecifiedModule(part, engineID, moduleIndex, type, useWeakType);

                if ((object)pModule == null)
                {
                    Debug.LogError("*RFMEC* Could not find appropriate module of type " + type + ", with ID=" + engineID + " and index " + moduleIndex);
                    return;
                }

                Type mType = pModule.GetType();
                config.SetValue("name", mType.Name);

                // clear all FloatCurves we need to clear (i.e. if our config has one, or techlevels are enabled)
                bool delAtmo = config.HasNode("atmosphereCurve") || techLevel >= 0;
                bool delDens = config.HasNode("atmCurve") || techLevel >= 0;
                bool delVel = config.HasNode("velCurve") || techLevel >= 0;
                foreach (FieldInfo field in mType.GetFields())
                {
                    if (field.FieldType == typeof(FloatCurve) &&
                        ((field.Name.Equals("atmosphereCurve") && delAtmo)
                        || (field.Name.Equals("atmCurve") && delDens)
                        || (field.Name.Equals("velCurve") && delVel)))
                    {
                        field.SetValue(pModule, new FloatCurve());
                    }
                }
                // clear propellant gauges
                foreach (FieldInfo field in mType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (field.FieldType == typeof(Dictionary<Propellant, ProtoStageIconInfo>))
                    {
                        Dictionary<Propellant, ProtoStageIconInfo> boxes = (Dictionary<Propellant, ProtoStageIconInfo>)(field.GetValue(pModule));
                        if (boxes == null)
                            continue;
                        foreach (ProtoStageIconInfo v in boxes.Values)
                        {
                            if (v == null) //just in case...
                                continue;
                            try
                            {
                                part.stackIcon.RemoveInfo(v);
                            }
                            catch (Exception e)
                            {
                                Debug.LogError("*RFMEC* Trying to remove info box: " + e.Message);
                            }
                        }
                        boxes.Clear();
                    }
                }
                if (type.Equals("ModuleRCS") || type.Equals("ModuleRCSFX"))
                {
                    // Changed this to find ALL RCS modules on the part to address SSTU case where MUS with only aft RCS is not handled.
                    List<ModuleRCS> RCSModules = part.Modules.OfType<ModuleRCS>().ToList();

                    if (RCSModules.Count > 0)
                    {
                        DoConfig(config);
                        for (int i = 0; i < RCSModules.Count; i++)
                        {
                            if (config.HasNode("PROPELLANT"))
                            {
                                RCSModules[i].propellants.Clear();
                            }
                            RCSModules[i].Load(config);
                        }
                    }
                }
                else
                { // is an ENGINE
                    ModuleEngines mE = (ModuleEngines)pModule;
                    if (mE != null)
                    {
                        if (config.HasNode("PROPELLANT"))
                        {
                            mE.propellants.Clear();
                        }
                    }

                    DoConfig(config);

                    // Handle Engine Ignitor
                    if (config.HasNode("ModuleEngineIgnitor"))
                    {
                        if (part.Modules.Contains("ModuleEngineIgnitor"))
                        {
                            ConfigNode eiNode = config.GetNode("ModuleEngineIgnitor");
                            if (eiNode.HasValue("ignitionsAvailable"))
                            {
                                int ignitions;
                                if (int.TryParse(eiNode.GetValue("ignitionsAvailable"), out ignitions))
                                {
                                    ignitions = ConfigIgnitions(ignitions);

                                    eiNode.SetValue("ignitionsAvailable", ignitions.ToString());
                                    if (eiNode.HasValue("ignitionsRemained"))
                                        eiNode.SetValue("ignitionsRemained", ignitions.ToString());
                                    else
                                        eiNode.AddValue("ignitionsRemained", ignitions.ToString());
                                }
                            }
                            if (!HighLogic.LoadedSceneIsEditor && !(HighLogic.LoadedSceneIsFlight && vessel != null && vessel.situation == Vessel.Situations.PRELAUNCH)) // fix for prelaunch
                            {
                                int remaining = (int)(part.Modules["ModuleEngineIgnitor"].GetType().GetField("ignitionsRemained").GetValue(part.Modules["ModuleEngineIgnitor"]));
                                if (eiNode.HasValue("ignitionsRemained"))
                                    eiNode.SetValue("ignitionsRemained", remaining.ToString());
                                else
                                    eiNode.AddValue("ignitionsRemained", remaining.ToString());
                            }
                            ConfigNode tNode = new ConfigNode("MODULE");
                            eiNode.CopyTo(tNode);
                            tNode.SetValue("name", "ModuleEngineIgnitor", true);
                            part.Modules["ModuleEngineIgnitor"].Load(tNode);
                        }
                        else // backwards compatible with EI nodes when using RF ullage etc.
                        {
                            ConfigNode eiNode = config.GetNode("ModuleEngineIgnitor");
                            if (eiNode.HasValue("ignitionsAvailable") && !config.HasValue("ignitions"))
                            {
                                config.AddValue("ignitions", eiNode.GetValue("ignitionsAvailable"));
                            }
                            if (eiNode.HasValue("useUllageSimulation") && !config.HasValue("ullage"))
                                config.AddValue("ullage", eiNode.GetValue("useUllageSimulation"));
                            if (eiNode.HasValue("isPressureFed") && !config.HasValue("pressureFed"))
                                config.AddValue("pressureFed", eiNode.GetValue("isPressureFed"));
                            if (!config.HasNode("IGNITOR_RESOURCE"))
                                foreach (ConfigNode resNode in eiNode.GetNodes("IGNITOR_RESOURCE"))
                                    config.AddNode(resNode);
                        }
                    }
                    if (config.HasValue("ignitions"))
                    {
                        int ignitions;
                        if ((!HighLogic.LoadedSceneIsFlight || (vessel != null && vessel.situation == Vessel.Situations.PRELAUNCH)))
                        {
                            if (int.TryParse(config.GetValue("ignitions"), out ignitions))
                            {
                                ignitions = ConfigIgnitions(ignitions);
                                config.SetValue("ignitions", ignitions.ToString());
                            }
                        }
                        else
                            config.RemoveValue("ignitions");
                    }

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

                // set gimbal
                if (config.HasValue("gimbalRange"))
                {

                    float newGimbal = float.Parse(config.GetValue("gimbalRange"));

                    float newGimbalXP = -1;
                    float newGimbalXN = -1;
                    float newGimbalYP = -1;
                    float newGimbalYN = -1;

                    if (config.HasValue("gimbalRangeXP"))
                        newGimbalXP = float.Parse(config.GetValue("gimbalRangeXP"));
                    if (config.HasValue("gimbalRangeXN"))
                        newGimbalXN = float.Parse(config.GetValue("gimbalRangeXN"));
                    if (config.HasValue("gimbalRangeYP"))
                        newGimbalYP = float.Parse(config.GetValue("gimbalRangeYP"));
                    if (config.HasValue("gimbalRangeYN"))
                        newGimbalYN = float.Parse(config.GetValue("gimbalRangeYN"));

                    if (newGimbalXP < 0)
                        newGimbalXP = newGimbal;
                    if (newGimbalXN < 0)
                        newGimbalXN = newGimbal;
                    if (newGimbalYP < 0)
                        newGimbalYP = newGimbal;
                    if (newGimbalYN < 0)
                        newGimbalYN = newGimbal;

                    for (int m = 0; m < part.Modules.Count; ++m)
                    {
                        if (part.Modules[m] is ModuleGimbal)
                        {
                            ModuleGimbal g = part.Modules[m] as ModuleGimbal;
                            if (gimbalTransform.Equals(string.Empty) || g.gimbalTransformName.Equals(gimbalTransform))
                            {
                                g.gimbalRange = newGimbal;
                                g.gimbalRangeXN = newGimbalXN;
                                g.gimbalRangeXP = newGimbalXP;
                                g.gimbalRangeYN = newGimbalYN;
                                g.gimbalRangeYP = newGimbalYP;
                            }
                        }
                    }
                }
                if (config.HasValue("cost"))
                    configCost = float.Parse(config.GetValue("cost"));
                else
                    configCost = 0f;

                UpdateOtherModules(config);

                // GUI disabled for now - UpdateTweakableMenu();

                // Finally, fire the modified event
                // more trouble than it is worth...
                /*if((object)(EditorLogic.fetch) != null && (object)(EditorLogic.fetch.ship) != null && HighLogic.LoadedSceneIsEditor)
                    GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);*/

                // fire config modified event
                /*if(HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)
                    EngineConfigChanged();*/
                // do it manually
                List<Part> parts;
                if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch.ship != null)
                    parts = EditorLogic.fetch.ship.parts;
                else if (HighLogic.LoadedSceneIsFlight && vessel != null)
                    parts = vessel.parts;
                else parts = new List<Part>();
                for (int i = parts.Count - 1; i >= 0; --i)
                    parts[i].SendMessage("UpdateUsedBy", SendMessageOptions.DontRequireReceiver);

                SetupFX();

                UpdateTFInterops(); // update TestFlight if it's installed

                if (config.HasValue("description"))
                    configDescription = config.GetValue("description");
                else
                    configDescription = string.Empty;
            }
            else
            {
                Debug.LogWarning("*RFMEC* WARNING could not find configuration of name " + configuration + " for part " + part.name + ": Attempting to locate fallback configuration.");
                if (configs.Count > 0)
                {
                    configuration = configs[0].GetValue("name");
                    SetConfiguration();
                }
                else
                    Debug.LogError("*RFMEC* ERROR unable to locate any fallbacks for configuration " + configuration + ",\n Current nodes:" + Utilities.PrintConfigs(configs));
            }

            StopFX();
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

        virtual public void DoConfig(ConfigNode cfg)
        {
            configMaxThrust = configMinThrust = configHeat = -1f;
            // Get thrusts
            if (config.HasValue(thrustRating))
            {
                float thr;
                if (float.TryParse(config.GetValue(thrustRating), out thr))
                    configMaxThrust = scale * thr;
            }
            if (config.HasValue("minThrust"))
            {
                float thr;
                if (float.TryParse(config.GetValue("minThrust"), out thr))
                    configMinThrust = scale * thr;
            }

            // Get, multiply heat
            if (cfg.HasValue("heatProduction"))
            {
                float heat;
                if(float.TryParse(cfg.GetValue("heatProduction"), out heat))
                    configHeat = (float)Math.Round(heat * RFSettings.Instance.heatMultiplier, 0);
            }

            // load throttle (for later)
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
                    float ispSL = 1f, ispV = 1f;
                    float.TryParse(cfg.GetValue("IspSL"), out ispSL);
                    float.TryParse(cfg.GetValue("IspV"), out ispV);

                    // Mod the curve by the multipliers
                    FloatCurve newAtmoCurve = new FloatCurve();
                    newAtmoCurve = Utilities.Mod(cTL.AtmosphereCurve, ispSL, ispV);
                    newAtmoCurve.Save(curve);

                    cfg.AddNode(curve);
                }

                // set heatProduction
                if (configHeat > 0)
                {
                    configHeat = MassTL(configHeat);
                }

                // set thrust and throttle
                if (configMaxThrust >= 0)
                {
                    configMaxThrust = ThrustTL(configMaxThrust);
                    if (configMinThrust >= 0)
                    {
                        configMinThrust = ThrustTL(configMinThrust);
                    }
                    else if (thrustRating.Equals("thrusterPower"))
                    {
                        configMinThrust = configMaxThrust * 0.5f;
                    }
                    else
                    {
                        configMinThrust = configMaxThrust;
                        if (configThrottle > 1.0f)
                        {
                            if (techLevel >= configThrottle)
                                configThrottle = 1.0f;
                            else
                                configThrottle = -1.0f;
                        }
                        if (configThrottle >= 0.0f)
                        {
                            configThrottle = (float)((double)configThrottle * cTL.Throttle());
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
            if(configMaxThrust >= 0f)
                cfg.SetValue(thrustRating, configMaxThrust.ToString("0.0000"), true);
            if(configMinThrust >= 0f)
                cfg.SetValue("minThrust", configMinThrust.ToString("0.0000"), true); // will be ignored by RCS, so what.

            // heat update
            if(configHeat >= 0f)
                cfg.SetValue("heatProduction", configHeat.ToString("0"), true);

            // mass change
            if (origMass > 0)
            {
                float ftmp;
                configMassMult = scale;
                if (cfg.HasValue("massMult"))
                    if (float.TryParse(cfg.GetValue("massMult"), out ftmp))
                        configMassMult *= ftmp;

                part.mass = origMass * configMassMult * RFSettings.Instance.EngineMassMultiplier * TLMassMult;
                massDelta = 0;
                if ((object)(part.partInfo) != null)
                    if ((object)(part.partInfo.partPrefab) != null)
                        massDelta = part.mass - part.partInfo.partPrefab.mass;
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
            {
                // apply module-wide gimbal mult on top of any local ones
                cfg.AddValue("gimbalRange", (gimbal * gimbalMult).ToString("N4"));
            }
            if (cost != 0f)
            {
                if (cfg.HasValue("cost"))
                    cfg.SetValue("cost", cost.ToString("N3"));
                else
                    cfg.AddValue("cost", cost.ToString("N3"));
            }
        }

        /*[PartMessageEvent]
        public event PartEngineConfigChanged EngineConfigChanged;*/


        //called by StretchyTanks StretchySRB and ProcedrualParts
        virtual public void ChangeThrust(float newThrust)
        {
            foreach(ConfigNode c in configs)
            {
                c.SetValue("maxThrust", newThrust.ToString());
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
            if ((object)config == null)
                return false;
            if (!config.HasValue("name"))
                return false;
            if (EntryCostManager.Instance != null && HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode != Game.Modes.SANDBOX)
                return EntryCostManager.Instance.ConfigUnlocked((RFSettings.Instance.usePartNameInConfigUnlock ? Utilities.GetPartName(p) : string.Empty) + config.GetValue("name"));
            return true;
        }
        public static bool CanConfig(ConfigNode config)
        {
            if ((object)config == null)
                return false;
            if (!config.HasValue("techRequired") || (object)HighLogic.CurrentGame == null)
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
                if (!oldTL.Load(cfg == null ? config : cfg, techNodes, engineType, origTechLevel))
                    return 1.0;
                if (!newTL.Load(cfg == null ? config : cfg, techNodes, engineType, techLevel))
                    return 1.0;

                return newTL.Thrust(oldTL);
            }
            return 1.0;
        }

        private float ThrustTL(float thrust, ConfigNode cfg = null)
        {
            return (float)Math.Round((double)thrust * ThrustTL(cfg), 6);
        }

        private float ThrustTL(string thrust, ConfigNode cfg = null)
        {
            float tmp = 1.0f;
            float.TryParse(thrust, out tmp);
            return ThrustTL(tmp, cfg);
        }

        private double MassTL(ConfigNode cfg = null)
        {
            if (techLevel != -1)
            {
                TechLevel oldTL = new TechLevel(), newTL = new TechLevel();
                if (!oldTL.Load(cfg == null ? config : cfg, techNodes, engineType, origTechLevel))
                    return 1.0;
                if (!newTL.Load(cfg == null ? config : cfg, techNodes, engineType, techLevel))
                    return 1.0;

                return newTL.Mass(oldTL, engineType.Contains("S"));
            }
            return 1.0;
        }

        private float MassTL(float mass)
        {
            return (float)Math.Round((double)mass * MassTL(), 6);
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
        /*[KSPEvent(guiActive = false, guiActiveEditor = true, name = "NextEngine", guiName = "Current Configuration")]
        public void NextEngine()
        {
            bool nextEngine = false;
            foreach (ConfigNode node in configs)
            {
                if (node.GetValue("name").Equals(configuration))
                    nextEngine = true; // flag to use the next config
                else if (nextEngine == true) // flag set
                {
                    SetConfiguration(configuration = node.GetValue("name"));
                    UpdateSymmetryCounterparts();
                    return;
                }
            }
            SetConfiguration(configs[0].GetValue("name"));
            UpdateSymmetryCounterparts();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = true, name = "NextTech", guiName = "Tech Level")]
        public void NextTech()
        {
            if (techLevel == -1)
                return;
            else if (TechLevel.CanTL(config, techNodes, engineType, techLevel + 1) && techLevel < maxTechLevel)
            {
                techLevel++;
            }
            else while (TechLevel.CanTL(config, techNodes, engineType, techLevel - 1) && techLevel > minTechLevel)
                {
                    techLevel--;
                }
            SetConfiguration(configuration);
            UpdateSymmetryCounterparts();
        }*/

        [KSPField(isPersistant = false, guiActiveEditor = true, guiActive = false, guiName = "Engine"),
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
        private bool styleSetup = false;
        private bool editorLocked = false;

        public void OnGUI()
        {
            if (!compatible || !isMaster || !HighLogic.LoadedSceneIsEditor || EditorLogic.fetch == null)
                return;

            bool inPartsEditor = EditorLogic.fetch.editorScreen == EditorScreen.Parts;
            if (!(showRFGUI && inPartsEditor) && !(EditorLogic.fetch.editorScreen == EditorScreen.Actions && EditorActionGroups.Instance.GetSelectedParts().Contains(part)))
            {
                editorUnlock();
                return;
            }

            if (inPartsEditor)
            {
                List<Part> symmetryParts = part.symmetryCounterparts;
                for(int i = 0; i < symmetryParts.Count; i++)
                {
                    if (symmetryParts[i].persistentId < part.persistentId)
                        return;
                }
            }

            if (!styleSetup)
            {
                styleSetup = true;
                Styles.InitStyles ();
            }

            if (guiWindowRect.width == 0)
            {
                int posAdd = inPartsEditor ? 256 : 0;
                int posMult = (offsetGUIPos == -1) ? (part.Modules.Contains("ModuleFuelTanks") ? 1 : 0) : offsetGUIPos;
                guiWindowRect = new Rect(posAdd + 430 * posMult, 365, 430, (Screen.height - 365));
            }

            mousePos = Input.mousePosition; //Mouse location; based on Kerbal Engineer Redux code
            mousePos.y = Screen.height - mousePos.y;
            if (guiWindowRect.Contains(mousePos))
                editorLock();
            else
                editorUnlock();

            myToolTip = myToolTip.Trim ();
            if (!String.IsNullOrEmpty(myToolTip))
            {
                int offset = inPartsEditor ? -222 : 440;
                int width = inPartsEditor ? 220 : 300;
                GUI.Label(new Rect(guiWindowRect.xMin + offset, mousePos.y - 5, width, 200), myToolTip, Styles.styleEditorTooltip);
            }

            guiWindowRect = GUILayout.Window(unchecked((int)part.persistentId), guiWindowRect, engineManagerGUI, "Configure " + part.partInfo.title, Styles.styleEditorPanel);
        }

        private void editorLock() {
            if (!editorLocked)
            {
                EditorLogic.fetch.Lock(false, false, false, "RFGUILock");
                editorLocked = true;
                if (KSP.UI.Screens.Editor.PartListTooltipMasterController.Instance != null)
                    KSP.UI.Screens.Editor.PartListTooltipMasterController.Instance.HideTooltip();
            }
        }

        private void editorUnlock() {
            if (editorLocked)
            {
                EditorLogic.fetch.Unlock("RFGUILock");
                editorLocked = false;
            }
        }

        /*private int oldTechLevel = -1;
        private string oldConfiguration;
        public void UpdateTweakableMenu()
        {
            if (!compatible)
                return;
            if (!HighLogic.LoadedSceneIsEditor)
                return;

            if (configs.Count < 2)
                Events["NextEngine"].guiActiveEditor = false;
            else
                Events["NextEngine"].guiName = configuration;

            if (TechLevel.CanTL(config, techNodes, engineType, techLevel + 1) && techLevel < maxTechLevel)
                Events["NextTech"].guiName = "Tech Level: " + techLevel.ToString() + "            +";
            else if (TechLevel.CanTL(config, techNodes, engineType, techLevel - 1) && techLevel > minTechLevel)
                Events["NextTech"].guiName = "Tech Level: " + techLevel.ToString() + "            -";
            else
                Events["NextTech"].guiActiveEditor = false;

            // Sorry about this dirty hack. Otherwise we end up with loops. Will try to get something tidier
            // some time in the future.
            if (oldConfiguration != null && (techLevel != oldTechLevel || oldConfiguration != configuration))
            {
                oldConfiguration = configuration;
                oldTechLevel = techLevel;
                EngineConfigChanged();
            }
            else
            {
                oldConfiguration = configuration;
                oldTechLevel = techLevel;
            }
        }*/

        private void engineManagerGUI(int WindowID)
        {
            GUILayout.Space (20);
            foreach (ConfigNode node in configs)
            {
                string nName = node.GetValue("name");
                GUILayout.BeginHorizontal();

                // get cost
                string costString = string.Empty;
                if (node.HasValue("cost"))
                {
                    float curCost = scale * float.Parse(node.GetValue("cost"));

                    if (techLevel != -1)
                    {
                        curCost = CostTL(curCost, node) - CostTL(0f, node); // get purely the config cost difference
                    }
                    costString = " (" + ((curCost < 0) ? string.Empty : "+") + curCost.ToString("N0") + "f)";
                }

                if (nName.Equals(configuration))
                {
                    GUILayout.Label(new GUIContent("Current config: " + nName + costString, GetConfigInfo(node)));
                }
                else
                {
                    if (CanConfig(node))
                    {
                        if (UnlockedConfig(node, part))
                        {
                            if (GUILayout.Button(new GUIContent("Switch to " + nName + costString, GetConfigInfo(node))))
                            {
                                SetConfiguration(nName, true);
                                UpdateSymmetryCounterparts();
                                MarkWindowDirty();
                            }
                        }
                        else
                        {
                            double upgradeCost = EntryCostManager.Instance.ConfigEntryCost(nName);
                            costString = string.Empty;
                            if (upgradeCost > 0d)
                            {
                                costString = "(" + upgradeCost.ToString("N0") + "f)";
                                if (GUILayout.Button(new GUIContent("Purchase " + nName + costString, GetConfigInfo(node))))
                                {
                                    if (EntryCostManager.Instance.PurchaseConfig(nName))
                                    {
                                        SetConfiguration(nName, true);
                                        UpdateSymmetryCounterparts();
                                        MarkWindowDirty();
                                    }
                                }
                            }
                            else
                            {
                                // autobuy
                                EntryCostManager.Instance.PurchaseConfig(nName);
                                if (GUILayout.Button(new GUIContent("Switch to " + nName + costString, GetConfigInfo(node))))
                                {
                                    SetConfiguration(nName, true);
                                    UpdateSymmetryCounterparts();
                                    MarkWindowDirty();
                                }
                            }
                        }
                    }
                    else
                    {
                        string techStr = string.Empty;
                        if (techNameToTitle.TryGetValue(node.GetValue("techRequired"), out techStr))
                            techStr = "\nRequires: " + techStr;
                        GUILayout.Label(new GUIContent("Lack tech for " + nName, GetConfigInfo(node) + techStr));
                    }
                }
                GUILayout.EndHorizontal();
            }
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

            // show current info, cost
            if (pModule != null && part.partInfo != null)
            {
                GUILayout.BeginHorizontal();
                var ratedBurnTime = string.Empty;
                if (config.HasValue("tfRatedBurnTime"))
                {
                    ratedBurnTime += config.GetValue("tfRatedBurnTime") + "\n";
                }
                GUILayout.Label(ratedBurnTime + "<b>Engine mass:</b> " + part.mass.ToString("N3") + "t\n" + pModule.GetInfo() + "\n" + TLTInfo() + "\n" + "Total cost: " + (part.partInfo.cost + part.GetModuleCosts(part.partInfo.cost)).ToString("0"));
                GUILayout.EndHorizontal();
            }

            if (!(myToolTip.Equals(string.Empty)) && GUI.tooltip.Equals(string.Empty))
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
        virtual public int UpdateSymmetryCounterparts()
        {
            int i = 0;
            if (part.symmetryCounterparts == null)
                return i;

            int mIdx = moduleIndex;
            if (engineID == string.Empty && mIdx < 0)
                mIdx = 0;

            int pCount = part.symmetryCounterparts.Count;
            for (int j = 0; j < pCount; ++j)
            {
                if (part.symmetryCounterparts[j] == part)
                    continue;
                ModuleEngineConfigs engine = GetSpecifiedModule(part.symmetryCounterparts[j], engineID, mIdx, this.GetType().Name, false) as ModuleEngineConfigs;
                engine.techLevel = techLevel;
                engine.SetConfiguration(configuration);
                ++i;
            }
            return i;
        }

        virtual protected void UpdateOtherModules(ConfigNode node)
        {
            if (node.HasNode("OtherModules"))
            {
                node = node.GetNode("OtherModules");
                int nCount = node.values.Count;
                for (int i = 0; i < nCount; ++i)
                {
                    ModuleEngineConfigs otherM = GetSpecifiedModule(part, node.values[i].name, -1, this.GetType().Name, false) as ModuleEngineConfigs;
                    if (otherM != null)
                    {
                        otherM.techLevel = techLevel;
                        otherM.SetConfiguration(node.values[i].value);
                    }
                }
            }
        }
        virtual public void CheckConfigs()
        {
            if(configs == null || configs.Count == 0)
                ConfigSaveLoad();
        }
        // run this to save/load non-serialized data
        protected void ConfigSaveLoad()
        {
            string partName = Utilities.GetPartName(part);
            partName += moduleIndex + engineID;
            //Debug.Log("*RFMEC* Saveload " + partName);
            if (configs == null)
                configs = new List<ConfigNode>();
            if (configs.Count > 0)
            {
                if (!RFSettings.Instance.engineConfigs.ContainsKey(partName))
                {
                    RFSettings.Instance.engineConfigs[partName] = new List<ConfigNode>(configs);
                    /*Debug.Log("*RFMEC* Saved " + configs.Count + " configs");
                    Debug.Log("Current nodes:" + Utilities.PrintConfigs(configs));*/
                }
                else
                {
                    /*Debug.Log("*RFMEC* ERROR: part " + partName + " already in database! Current count = " + configs.Count + ", db count = " + RFSettings.Instance.engineConfigs[partName].Count);
                    Debug.Log("DB nodes:" + Utilities.PrintConfigs(RFSettings.Instance.engineConfigs[partName]));
                    Debug.Log("Current nodes:" + Utilities.PrintConfigs(configs));*/
                    //configs = RFSettings.Instance.engineConfigs[partName]; // just in case.
                }

            }
            else
            {
                if (RFSettings.Instance.engineConfigs.ContainsKey(partName))
                {
                    configs = new List<ConfigNode>(RFSettings.Instance.engineConfigs[partName]);
                    /*Debug.Log("Found " + configs.Count + " configs!");
                    Debug.Log("Current nodes:" + Utilities.PrintConfigs(configs));*/
                }
                else
                    Debug.Log("*RFMEC* ERROR: could not find configs definition for " + partName);
            }
        }

        protected static PartModule GetSpecifiedModule(Part p, string eID, int mIdx, string eType, bool weakType)
        {
            int mCount = p.Modules.Count;
            int tmpIdx = 0;

            for(int m = 0; m < mCount; ++m)
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
                            return pM;
                        }
                        tmpIdx++;
                        continue; // skip the next test
                    }
                    else if (eID != string.Empty)
                    {
                        string testID = string.Empty;
                        if (pM is ModuleEngines)
                            testID = (pM as ModuleEngines).engineID;
                        else if (pM is ModuleEngineConfigs)
                            testID = (pM as ModuleEngineConfigs).engineID;

                        if (testID.Equals(eID))
                            return pM;
                    }
                    else
                        return pM;
                }
            }
            return null;
        }

        protected static List<PartModule> GetSpecifiedModules(Part p, string eID, int mIdx, string eType, bool weakType)
        {
            int mCount = p.Modules.Count;
            int tmpIdx = 0;
            List<PartModule> pMs = new List<PartModule>();

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
                            pMs.Add(pM);
                        }
                        tmpIdx++;
                        continue; // skip the next test
                    }
                    else if (eID != string.Empty)
                    {
                        string testID = string.Empty;
                        if (pM is ModuleEngines)
                            testID = (pM as ModuleEngines).engineID;
                        else if (pM is ModuleEngineConfigs)
                            testID = (pM as ModuleEngineConfigs).engineID;

                        if (testID.Equals(eID))
                            pMs.Add(pM);
                    }
                    else
                        pMs.Add(pM);
                }
            }
            return pMs;
        }

        internal void MarkWindowDirty()
        {
            UIPartActionWindow action_window;
            if (UIPartActionController.Instance == null)
            {
                // no controller means no window to mark dirty
                return;
            }
            action_window = UIPartActionController.Instance.GetItem(part);
            if (action_window == null)
            {
                return;
            }
            action_window.displayDirty = true;
        }
        #endregion
    }
}
