using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Linq;
using KSPAPIExtensions.PartMessage;
using UnityEngine;
using KSP;
using KSPAPIExtensions.Utils;
using Debug = UnityEngine.Debug;
using RealFuels.TechLevels;
using SolverEngines;

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
        public string configuration = "";

        // Tech Level stuff
        [KSPField(isPersistant = true)]
        public int techLevel = -1; // default: disable

        [KSPField]
        public int origTechLevel = 1; // default TL

        [KSPField]
        public float origMass = -1;
        protected float massDelta = 0;

        [KSPField]
        public string gimbalTransform = "";
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

        public ConfigNode techNodes = new ConfigNode();

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
        public string engineID = "";

        [KSPField]
        public int moduleIndex = -1;

        [KSPField]
        public int offsetGUIPos = -1;

        [KSPField(isPersistant = true)]
        public string thrustRating = "maxThrust";

        [KSPField(isPersistant = true)]
        public bool modded = false;

        public List<ConfigNode> configs;
        public ConfigNode config;

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
                    tfInterface.InvokeMember("AddInteropValue", tfBindingFlags, null, null, new System.Object[] { this.part, "engineConfig", configuration, "RealFuels" });
                }
                catch
                {
                }
            }
        }
        #endregion

        #region Callbacks
        public float GetModuleCost(float stdCost)
        {
            return configCost;
        }

        public float GetModuleMass(float defaultMass)
        {
            return massDelta;
        }
        #endregion

        #region PartModule Overrides
        public override void OnAwake ()
        {
            if (!CompatibilityChecker.IsAllCompatible())
            {
                compatible = false;
                return;
            }
            PartMessageService.Register(this);
            if(HighLogic.LoadedSceneIsEditor)
                GameEvents.onPartActionUIDismiss.Add(OnPartActionGuiDismiss);

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
                    minTechLevel = origTechLevel;
            }

            if(origMass >= 0)
            {
                massDelta = 0;
                part.mass = origMass * RFSettings.Instance.EngineMassMultiplier;
                if ((object)(part.partInfo) != null)
                    if ((object)(part.partInfo.partPrefab) != null)
                        massDelta = part.mass - part.partInfo.partPrefab.mass;
            }


            if (configs == null)
                configs = new List<ConfigNode>();
            else
                configs.Clear();

            foreach (ConfigNode subNode in node.GetNodes ("CONFIG")) {
                //Debug.Log("*RFMEC* Load Engine Configs. Part " + part.name + " has config " + subNode.GetValue("name"));
                ConfigNode newNode = new ConfigNode("CONFIG");
                subNode.CopyTo (newNode);
                configs.Add (newNode);
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
            string retStr = "";
            if (engineID != "")
                retStr += "Bound to " + engineID;
            if(moduleIndex >= 0)
                retStr += "Bound to engine " + moduleIndex + " in part";
            if(techLevel != -1)
            {
                TechLevel cTL = new TechLevel();
                if (!cTL.Load(config, techNodes, engineType, techLevel))
                    cTL = null;

                retStr =  "Type: " + engineType + ". Tech Level: " + techLevel + " (" + origTechLevel + "-" + maxTechLevel + ")";
                if (origMass > 0)
                    retStr += ", Mass: " + part.mass.ToString("N3") + " (was " + (origMass * RFSettings.Instance.EngineMassMultiplier).ToString("N3") + ")";
                if (configThrottle >= 0)
                    retStr += ", MinThr " + (100f * configThrottle).ToString("N0") + "%";

                float gimbalR = -1f;
                if (config.HasValue("gimbalRange"))
                    gimbalR = float.Parse(config.GetValue("gimbalRange"));
                else if (!gimbalTransform.Equals("") || useGimbalAnyway)
                {
                    if (cTL != null)
                        gimbalR = cTL.GimbalRange;
                }
                if (gimbalR != -1f)
                    retStr += ", Gimbal " + gimbalR.ToString("N1");

                return retStr;
            }
            else
                return "";
        }

        public override string GetInfo ()
        {
            if (!compatible)
                return "";
            if (configs.Count < 2)
                return TLTInfo();

            string info = TLTInfo() + "\nAlternate configurations:\n";

            //Unused as yet
            /*TechLevel moduleTLInfo = new TechLevel();
            if (techNodes != null)
                moduleTLInfo.Load(techNodes, techLevel);
            else
                moduleTLInfo = null;*/

            foreach (ConfigNode config in configs) {
                
                TechLevel cTL = new TechLevel();
                if (!cTL.Load(config, techNodes, engineType, techLevel))
                    cTL = null;

                if(!config.GetValue ("name").Equals (configuration)) {
                    info += "   " + config.GetValue ("name") + "\n";
                    if(config.HasValue (thrustRating))
                        info += "    (" + (scale * ThrustTL(config.GetValue (thrustRating), config)).ToString("0.00") + " Thrust";
                    else
                        info += "    (Unknown Thrust";
                    float cst;
                    if(config.HasValue("cost") && float.TryParse(config.GetValue("cost"), out cst))
                        info += "    (" + (scale*cst).ToString("N0") + " extra cost)"; // FIXME should get cost from TL, but this should be safe
                    // because it will always be the cost for the original TL, and thus unmodified.

                    FloatCurve isp = new FloatCurve();
                    if(config.HasNode ("atmosphereCurve")) {
                        isp.Load (config.GetNode ("atmosphereCurve"));
                        info  += ", "
                            + isp.Evaluate (isp.maxTime).ToString() + "-"
                              + isp.Evaluate (isp.minTime).ToString() + "Isp";
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
                            info += ", " + ispSL.ToString("0") + "-" + ispV.ToString("0") + "Isp";
                        }
                    }
                    float gimbalR = -1f;
                    if (config.HasValue("gimbalRange"))
                        gimbalR = float.Parse(config.GetValue("gimbalRange"));
                    // Don't do per-TL checks here, they're misleading.
                    /*else if (!gimbalTransform.Equals("") || useGimbalAnyway)
                    {
                        if (cTL != null)
                            gimbalR = cTL.GimbalRange;
                    }*/
                    if (gimbalR != -1f)
                        info += ", Gimbal " + gimbalR.ToString("N1");
                    info += ")\n";
                }


            }
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
                for (int i = effectsToStop.Length - 1; i >= 0 ; --i)
                    part.Effect(effectsToStop[i], 0f);
        }
        #endregion

        #region MonoBehaviour Methods
        virtual public void FixedUpdate()
        {
            if (!compatible)
                return;
            if (vessel == null)
                return;

            StopFX();
        }
        #endregion

        #region Configuration
        public PartModule pModule = null;

        virtual public void SetConfiguration(string newConfiguration = null, bool resetTechLevels = false)
        {

            if (newConfiguration == null)
                newConfiguration = configuration;
            
            ConfigSaveLoad();

            ConfigNode newConfig = configs.Find (c => c.GetValue ("name").Equals (newConfiguration));
            if (!UnlockedConfig(newConfig, part))
            {
                if(newConfig == null)
                    Debug.Log("*RFMEC* ERROR Can't find configuration " + newConfiguration + ", falling back to first tech-available config.");

                foreach(ConfigNode cfg in configs)
                    if (UnlockedConfig(cfg, part))
                    {
                        newConfig = cfg;
                        newConfiguration = cfg.GetValue("name");
                        break;
                    }
            }
            if (newConfig != null)
            {
                if (configuration != newConfiguration && resetTechLevels)
                    techLevel = origTechLevel;

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
                    Debug.Log("*RFMEC* Could not find appropriate module of type " + type + ", with ID=" + engineID + " and index " + moduleIndex);
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
                    if (field.FieldType == typeof(Dictionary<Propellant, VInfoBox>))
                    {
                        Dictionary<Propellant, VInfoBox> boxes = (Dictionary<Propellant, VInfoBox>)(field.GetValue(pModule));
                        if (boxes == null)
                            continue;
                        foreach (VInfoBox v in boxes.Values)
                        {
                            if (v == null) //just in case...
                                continue;
                            try
                            {
                                part.stackIcon.RemoveInfo(v);
                            }
                            catch (Exception e)
                            {
                                Debug.Log("*RFMEC* Trying to remove info box: " + e.Message);
                            }
                        }
                        boxes.Clear();
                    }
                }
                if (type.Equals("ModuleRCS") || type.Equals("ModuleRCSFX"))
                {
                    ModuleRCS rcs = (ModuleRCS)pModule;
                    if (rcs != null)
                    {
                        DoConfig(config);
                        if (config.HasNode("PROPELLANT"))
                        {
                            rcs.propellants.Clear();
                        }
                        pModule.Load(config);
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
                    if (pModule is ModuleEnginesRF)
                        (pModule as ModuleEnginesRF).SetScale(1d);
                    pModule.Load(config);

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
                                    if (ignitions < 0)
                                    {
                                        ignitions = techLevel + ignitions;
                                        if (ignitions < 1)
                                            ignitions = 1;
                                    }
                                    else if (ignitions == 0)
                                        ignitions = -1;

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
                            tNode.SetValue("name", "ModuleEngineIgnitor");
                            part.Modules["ModuleEngineIgnitor"].Load(tNode);
                        }
                        else // backwards compatible with EI nodes when using RF ullage etc.
                        {
                            ConfigNode eiNode = config.GetNode("ModuleEngineIgnitor");
                            int ignitions = -1;
                            string ignitionsString = "";
                            bool writeIgnitions = false;
                            if (config.HasValue("ignitions"))
                            {
                                ignitionsString = config.GetValue("ignitions");
                                config.RemoveValue("ignitions");
                            }
                            else if (eiNode.HasValue("ignitionsAvailable"))
                            {
                                ignitionsString = eiNode.GetValue("ignitionsAvailable");
                            }
                            if (!string.IsNullOrEmpty(ignitionsString) && int.TryParse(ignitionsString, out ignitions))
                            {
                                if (ignitions < 0)
                                {
                                    ignitions = techLevel + ignitions;
                                    if (ignitions < 1)
                                        ignitions = 1;
                                }
                                else if (ignitions == 0)
                                    ignitions = -1;
                                writeIgnitions = true;
                            }
                            if (eiNode.HasValue("useUllageSimulation") && !config.HasValue("ullage"))
                                config.AddValue("ullage", eiNode.GetValue("useUllageSimulation"));
                            if (eiNode.HasValue("isPressureFed") && !config.HasValue("pressureFed"))
                                config.AddValue("pressureFed", eiNode.GetValue("isPressureFed"));
                            if(!config.HasNode("IGNITOR_RESOURCE"))
                                foreach (ConfigNode resNode in eiNode.GetNodes("IGNITOR_RESOURCE"))
                                    config.AddNode(resNode);

                            if (writeIgnitions && (!HighLogic.LoadedSceneIsFlight || (vessel != null && vessel.situation == Vessel.Situations.PRELAUNCH)))
                                config.AddValue("ignitions", ignitions);
                                
                        }
                    }
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
                    for (int m = 0; m < part.Modules.Count; ++m)
                    {
                        if (part.Modules[m] is ModuleGimbal)
                        {
                            ModuleGimbal g = part.Modules[m] as ModuleGimbal;
                            if (gimbalTransform.Equals("") || g.gimbalTransformName.Equals(gimbalTransform))
                            {
                                g.gimbalRange = newGimbal;
                                break;
                            }
                        }
                    }
                }
                if (config.HasValue("cost"))
                    configCost = scale * float.Parse(config.GetValue("cost"));
                else
                    configCost = 0f;

                UpdateOtherModules(config);

                // GUI disabled for now - UpdateTweakableMenu();

                // Finally, fire the modified event
                // more trouble than it is worth...
                /*if((object)(EditorLogic.fetch) != null && (object)(EditorLogic.fetch.ship) != null && HighLogic.LoadedSceneIsEditor)
                    GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);*/

                SetupFX();

                UpdateTFInterops(); // update TestFlight if it's installed
            }
            else
            {
                Debug.Log("*RFMEC* ERROR could not find configuration of name " + configuration + " and could find no fallback config.");
                Debug.Log("For part " + part.name + ", Current nodes:" + Utilities.PrintConfigs(configs));
            }
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
                if (gimbal < 0 && (!gimbalTransform.Equals("") || useGimbalAnyway))
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
                cfg.SetValue(thrustRating, configMaxThrust.ToString("0.0000"));
            if(configMinThrust >= 0f)
                cfg.SetValue("minThrust", configMinThrust.ToString("0.0000")); // will be ignored by RCS, so what.

            // heat update
            if(configHeat >= 0f)
                cfg.SetValue("heatProduction", configHeat.ToString("0"));
            
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
            if (RFUpgradeManager.Instance != null && HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode != Game.Modes.SANDBOX)
                return RFUpgradeManager.Instance.ConfigUnlocked((RFSettings.Instance.usePartNameInConfigUnlock ? Utilities.GetPartName(p) : "") + config.GetValue("name"));
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
            if (RFUpgradeManager.Instance != null && HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode != Game.Modes.SANDBOX)
                return RFUpgradeManager.Instance.TLUnlocked(tlName) >= newTL;
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
            if (p == part)
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
        public void OnGUI()
        {
            if (!compatible)
                return;
            bool cursorInGUI = false; // nicked the locking code from Ferram
            mousePos = Input.mousePosition; //Mouse location; based on Kerbal Engineer Redux code
            mousePos.y = Screen.height - mousePos.y;
            EditorLogic editor = EditorLogic.fetch;
            if (!HighLogic.LoadedSceneIsEditor || !editor || !isMaster)
            {
                return;
            }

            int posMult = 0;
            if (offsetGUIPos != -1)
                posMult = offsetGUIPos;
            if (editor.editorScreen == EditorScreen.Actions && EditorActionGroups.Instance.GetSelectedParts().Contains(part))
            {
                if (offsetGUIPos == -1 && part.Modules.Contains("ModuleFuelTanks"))
                    posMult = 1;
                if (guiWindowRect.width == 0)
                    guiWindowRect = new Rect(430 * posMult, 365, 430, (Screen.height - 365));
                cursorInGUI = guiWindowRect.Contains(mousePos);
                if (cursorInGUI)
                {
                    editor.Lock(false, false, false, "RFGUILock");
                    EditorTooltip.Instance.HideToolTip();
                }
                else
                {
                    editor.Unlock("RFGUILock");
                }
            }
            else if (showRFGUI && editor.editorScreen == EditorScreen.Parts)
            {
                if (guiWindowRect.width == 0)
                    guiWindowRect = new Rect(256 + 430 * posMult, 365, 430, (Screen.height - 365));
                cursorInGUI = guiWindowRect.Contains(mousePos);
                if (cursorInGUI)
                {
                    editor.Lock(false, false, false, "RFGUILock");
                    EditorTooltip.Instance.HideToolTip();
                }
                else
                {
                    editor.Unlock("RFGUILock");
                }
            }
            else
            {
                showRFGUI = false;
                editor.Unlock("RFGUILock");
                return;
            }

            guiWindowRect = GUILayout.Window(part.name.GetHashCode() + 1, guiWindowRect, engineManagerGUI, "Configure " + part.partInfo.title);
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
            foreach (ConfigNode node in configs)
            {
                string nName = node.GetValue("name");
                GUILayout.BeginHorizontal();

                // get cost
                string costString = "";
                if (node.HasValue("cost"))
                {
                    float curCost = scale * float.Parse(node.GetValue("cost"));

                    if (techLevel != -1)
                    {
                        curCost = CostTL(curCost, node) - CostTL(0f, node); // get purely the config cost difference
                    }
                    costString = " (" + ((curCost < 0) ? "" : "+") + curCost.ToString("N0") + "f)";
                }

                if (nName.Equals(configuration))
                {
                    GUILayout.Label("Current config: " + nName + costString);
                }
                else
                {
                    if (CanConfig(node))
                    {
                        if (UnlockedConfig(node, part))
                        {
                            if (GUILayout.Button("Switch to " + nName + costString))
                            {
                                SetConfiguration(nName, true);
                                UpdateSymmetryCounterparts();
                            }
                        }
                        else
                        {
                            double upgradeCost = RFUpgradeManager.Instance.ConfigEntryCost(nName);
                            double sciCost = RFUpgradeManager.Instance.ConfigSciEntryCost(nName);
                            costString = "";
                            bool foundCost = false;
                            if (upgradeCost > 0d)
                            {
                                costString = "(" + upgradeCost.ToString("N0") + "f";
                                foundCost = true;
                            }
                            if (sciCost > 0d)
                            {
                                if (foundCost)
                                    costString += "/";
                                costString += sciCost.ToString("N1") + "s";
                                foundCost = true;
                            }
                            if (foundCost)
                            {
                                costString += ")";
                                if (GUILayout.Button("Purchase " + nName + costString))
                                {
                                    RFUpgradeManager.Instance.PurchaseConfig(nName);
                                    SetConfiguration(nName, true);
                                    UpdateSymmetryCounterparts();
                                }
                            }
                            else
                            {
                                // autobuy
                                RFUpgradeManager.Instance.PurchaseConfig(nName);
                                if (GUILayout.Button("Switch to " + nName + costString))
                                {
                                    SetConfiguration(nName, true);
                                    UpdateSymmetryCounterparts();
                                }
                            }
                        }
                    }
                    else
                    {
                        GUILayout.Label("Lack tech for " + nName);
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
                        double cost = RFUpgradeManager.Instance.TLEntryCost(tlName) * tlIncrMult;
                        double sciCost = RFUpgradeManager.Instance.TLSciEntryCost(tlName) * tlIncrMult;
                        bool autobuy = true;
                        plusStr = "";
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
                            RFUpgradeManager.Instance.SetTLUnlocked(tlName, techLevel + 1);
                            plusStr = "+";
                            canPlus = true;
                            canBuy = false;
                        }
                    }
                }
                if (GUILayout.Button(plusStr) && (canPlus || canBuy))
                {
                    if (canBuy)
                        RFUpgradeManager.Instance.PurchaseTL(tlName, techLevel + 1, tlIncrMult);

                    techLevel++;
                    SetConfiguration();
                    UpdateSymmetryCounterparts();
                }
                GUILayout.EndHorizontal();
            }

            // show current info, cost
            if (pModule != null && part.partInfo != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(pModule.GetInfo() + "\n" + TLTInfo() + "\n" + "Total cost: " + (part.partInfo.cost + part.GetModuleCosts(part.partInfo.cost)).ToString("0"));
                GUILayout.EndHorizontal();
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
            if (engineID == "" && mIdx < 0)
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
                    RFSettings.Instance.engineConfigs[partName] = configs;
                    /*Debug.Log("*RFMEC* Saved " + configs.Count + " configs");
                    Debug.Log("Current nodes:" + Utilities.PrintConfigs(configs));*/
                }
                else
                {
                    /*Debug.Log("*RFMEC* ERROR: part " + partName + " already in database! Current count = " + configs.Count + ", db count = " + RFSettings.Instance.engineConfigs[partName].Count);
                    Debug.Log("DB nodes:" + Utilities.PrintConfigs(RFSettings.Instance.engineConfigs[partName]));
                    Debug.Log("Current nodes:" + Utilities.PrintConfigs(configs));*/
                    configs = RFSettings.Instance.engineConfigs[partName]; // just in case.
                }

            }
            else
            {
                if (RFSettings.Instance.engineConfigs.ContainsKey(partName))
                {
                    configs = RFSettings.Instance.engineConfigs[partName];
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
                    else if (eID != "")
                    {
                        string testID = "";
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
        #endregion
    }
}

