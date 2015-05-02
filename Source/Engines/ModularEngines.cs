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

namespace RealFuels
{

    public enum ModuleType
    {
        MODULEENGINES,
        MODULEENGINESFX,
        MODULERCS,
        MODULERCSFX
    }

    public class ModuleHybridEngine : ModuleEngineConfigs
    {
        public override void OnStart (StartState state)
        {
            if (!compatible)
                return;
            if(configs.Count == 0 && part.partInfo != null
               && part.partInfo.partPrefab.Modules.Contains ("ModuleHybridEngine")) {
                ModuleHybridEngine prefab = (ModuleHybridEngine) part.partInfo.partPrefab.Modules["ModuleHybridEngine"];
                configs = new List<ConfigNode>();
                foreach (ConfigNode subNode in prefab.configs)
                {
                    ConfigNode newNode = new ConfigNode("CONFIG");
                    subNode.CopyTo(newNode);
                    configs.Add(newNode);
                }
            }
            if (type.Equals("ModuleEnginesFX"))
                ActiveEngine = new EngineWrapper((ModuleEnginesFX)part.Modules[type]);
            else if (type.Equals("ModuleEngines"))
                ActiveEngine = new EngineWrapper((ModuleEngines)part.Modules[type]);
            else
                print("*RF* trying to start " + part.name + " but is neither ME nor MEFX! (type = " + type + ")");

            SetConfiguration(configuration);
            if (part.Modules.Contains("ModuleEngineIgnitor"))
                part.Modules["ModuleEngineIgnitor"].OnStart(state);
        }

        public override void OnInitialize()
        {
            if (!compatible)
                return;
            if (type.Equals("ModuleEnginesFX"))
                ActiveEngine = new EngineWrapper((ModuleEnginesFX)part.Modules[type]);
            else if (type.Equals("ModuleEngines"))
                ActiveEngine = new EngineWrapper((ModuleEngines)part.Modules[type]);
            else
                print("*RF* trying to start " + part.name + " but is neither ME nor MEFX! (type = " + type + ")");
            SetConfiguration(configuration);
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
        override public void SetConfiguration(string newConfiguration = null)
        {

            if (newConfiguration == null)
                newConfiguration = configuration;
            ConfigNode newConfig = configs.Find (c => c.GetValue ("name").Equals (newConfiguration));
            pModule = part.Modules[type];
            if (newConfig == null || pModule == null)
                return;

            // fix for HotRockets etc.
            if (type.Equals("ModuleEngines") && part.Modules.Contains("ModuleEnginesFX") && !part.Modules.Contains("ModuleEngines"))
                type = "ModuleEnginesFX";

            if (type.Equals("ModuleEnginesFX"))
                ActiveEngine = new EngineWrapper((ModuleEnginesFX)part.Modules[type]);
            else if (type.Equals("ModuleEngines"))
                ActiveEngine = new EngineWrapper((ModuleEngines)part.Modules[type]);
            else
                print("*RF* trying to start " + part.name + " but is neither ME nor MEFX! (type = " + type + ")");

            ActiveEngine.g = 9.80665f;

            Fields ["configuration"].guiActive = true;
            Fields ["configuration"].guiName = "Current Mode";

            configuration = newConfiguration;
            config = new ConfigNode ("MODULE");
            newConfig.CopyTo (config);
            config.name = "MODULE";
            config.SetValue ("name", type);

            // clear all relevant FloatCurves
            Type mType = pModule.GetType();
            foreach (FieldInfo field in mType.GetFields())
            {
                if (field.FieldType == typeof(FloatCurve) && (field.Name.Equals("atmosphereCurve") || field.Name.Equals("velocityCurve")))
                {
                    //print("*RFEng* resetting curve " + field.Name);
                    field.SetValue(pModule, new FloatCurve());
                }
            }
            // clear propellant gauges Squad made
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
                            print("*RFEng* Trying to remove info box: " + e.Message);
                        }
                    }
                    boxes.Clear();
                }
            }
            

            bool engineActive = ActiveEngine.getIgnitionState;
            ActiveEngine.EngineIgnited = false;

            //  remove all fuel gauges we made
            ClearMeters ();
            propellants.Clear ();

            if (type.Equals("ModuleEngines"))
            {
                ModuleEngines mE = (ModuleEngines)pModule;
                if (mE != null)
                {
                    configMaxThrust = mE.maxThrust;
                    configMinThrust = mE.minThrust;
                    fastEngines = mE;
                    fastType = ModuleType.MODULEENGINES;
                }
                if (config.HasValue("maxThrust"))
                {
                    float thr;
                    if (float.TryParse(config.GetValue("maxThrust"), out thr))
                        configMaxThrust = thr;
                }
                if (config.HasValue("minThrust"))
                {
                    float thr;
                    if (float.TryParse(config.GetValue("minThrust"), out thr))
                        configMinThrust = thr;
                }
            }
            else if (type.Equals("ModuleEnginesFX"))
            {
                ModuleEnginesFX mE = (ModuleEnginesFX)pModule;
                if (mE != null)
                {
                    configMaxThrust = mE.maxThrust;
                    configMinThrust = mE.minThrust;
                    fastEnginesFX = mE;
                    fastType = ModuleType.MODULEENGINESFX;
                }
                if (config.HasValue("maxThrust"))
                {
                    float thr;
                    if (float.TryParse(config.GetValue("maxThrust"), out thr))
                        configMaxThrust = thr;
                }
                if (config.HasValue("minThrust"))
                {
                    float thr;
                    if (float.TryParse(config.GetValue("minThrust"), out thr))
                        configMinThrust = thr;
                }
            }

            DoConfig(config); // from MEC

            //  load the new engine state
            pModule.Load(config);

            // I'd think the load, above, would do this already. So maybe unnecessary?
            if (config.HasValue ("useVelocityCurve") && (config.GetValue ("useVelocityCurve").ToLowerInvariant () == "true")) {
                ActiveEngine.velocityCurve.Load (config.GetNode ("velocityCurve"));
            } else {
                ActiveEngine.useVelocityCurve = false;
            }

            //  set up propellants
            foreach (Propellant propellant in ActiveEngine.propellants) {
                if(propellant.drawStackGauge) { // we need to handle fuel gauges ourselves
                    propellant.drawStackGauge = false;
                    propellants.Add (propellant);
                }
            }
            ActiveEngine.SetupPropellant ();

            if (engineActive)
                ActiveEngine.Actions ["ActivateAction"].Invoke (new KSPActionParam (KSPActionGroup.None, KSPActionType.Activate));

            UpdateTweakableMenu();

            SetupFX();

            UpdateTFInterops(); // update TestFlight if it's installed
        }

        public EngineWrapper ActiveEngine = null;

        new public void FixedUpdate ()
        {
            if (!compatible)
                return;
            SetThrust ();
            if (ActiveEngine.getIgnitionState) { // engine is active, render fuel gauges
                foreach (Propellant propellant in propellants) {
                    if (!meters.ContainsKey (propellant.name)) // how did we miss one?
                        meters.Add (propellant.name, NewMeter (propellant.name));

                    double amount = 0d;
                    double maxAmount = 0d;

                    List<PartResource> sources = new List<PartResource> ();
                    part.GetConnectedResources (propellant.id, propellant.GetFlowMode(), sources);

                    foreach (PartResource source in sources) {
                        amount += source.amount;
                        maxAmount += source.maxAmount;
                    }

                    if (propellant.name.Equals ("IntakeAir")) {
                        double minimum = (from modules in vessel.Parts
                                          from module in modules.Modules.OfType<ModuleEngines> ()
                                          from p in module.propellants
                                          where p.name == "IntakeAir"
                                          select module.ignitionThreshold * p.currentRequirement).Sum ();

                        // linear scale
                        meters ["IntakeAir"].SetValue ((float)((amount - minimum) / (maxAmount - minimum)));
                    } else {
                        meters [propellant.name].SetValue ((float)(amount / maxAmount));
                    }
                }
            } else if(meters.Count > 0) { // engine is shut down, remove all fuel gauges
                ClearMeters();
            }
            StopFX();
        }

        List<Propellant> _props;
        List<Propellant> propellants
        {
            get {
                if(_props == null)
                    _props = new List<Propellant>();
                return _props;
            }
        }
        private Dictionary<string, VInfoBox> _meters;
        public Dictionary<string, VInfoBox> meters
        {
            get {
                if(_meters == null)
                    _meters = new Dictionary<string, VInfoBox>();
                return _meters;
            }
        }

        public void ClearMeters() {
            foreach(VInfoBox meter in meters.Values) {
                part.stackIcon.RemoveInfo (meter);
            }
            meters.Clear ();
        }

        VInfoBox NewMeter (string resourceName)
        {
            VInfoBox meter = part.stackIcon.DisplayInfo ();
            if (resourceName == "IntakeAir") {
                meter.SetMessage ("Air");
                meter.SetProgressBarColor (XKCDColors.White);
                meter.SetProgressBarBgColor (XKCDColors.Grey);
            } else {
                meter.SetMessage (resourceName);
                meter.SetMsgBgColor (XKCDColors.DarkLime);
                meter.SetMsgTextColor (XKCDColors.ElectricLime);
                meter.SetProgressBarColor (XKCDColors.Yellow);
                meter.SetProgressBarBgColor (XKCDColors.DarkLime);
            }
            meter.SetLength (2f);
            meter.SetValue (0f);

            return meter;
        }

        override public int UpdateSymmetryCounterparts()
        {
            int i = 0;
            foreach (Part sPart in part.symmetryCounterparts) {
                ModuleHybridEngine engine = (ModuleHybridEngine)sPart.Modules ["ModuleHybridEngine"];
                if (engine) {
                    i++;
                    engine.techLevel = techLevel;
                    engine.SetConfiguration (configuration);
                }
            }
            return i;
        }


    }

    public class ModuleHybridEngines : PartModule
    { // originally developed from HybridEngineController from careo / ExsurgentEngineering.

        [KSPField(isPersistant=false)]
        public ConfigNode
            primaryEngine;

        [KSPField(isPersistant=false)]
        public ConfigNode
            secondaryEngine;

        [KSPField(isPersistant=false)]
        public string
            primaryModeName = "Primary";

        [KSPField(isPersistant=false)]
        public string
            secondaryModeName = "Secondary";

        [KSPField(guiActive=true, isPersistant=true, guiName="Current Mode")]
        public string
            currentMode;

        [KSPField]
        public bool localCorrectThrust = true;

        public FloatCurve t;

        [KSPAction("Switch Engine Mode")]
        public void SwitchAction (KSPActionParam param)
        {
            SwitchEngine ();
        }

        public override void OnLoad (ConfigNode node)
        {

            if (node.HasNode ("primaryEngine")) {
                primaryEngine = node.GetNode ("primaryEngine");
                secondaryEngine = node.GetNode ("secondaryEngine");
            } else {
                var prefab = (ModuleHybridEngines)part.partInfo.partPrefab.Modules ["ModuleHybridEngines"];
                primaryEngine = prefab.primaryEngine;
                secondaryEngine = prefab.secondaryEngine;
            }
            if (currentMode == null) {
                currentMode = primaryModeName;
            }
            if (ActiveEngine == null) {
                if (currentMode == primaryModeName)
                    AddEngine (primaryEngine);
                else
                    AddEngine (secondaryEngine);
            }
        }

        public override void OnStart (StartState state)
        {
            base.OnStart (state);
            if (state == StartState.Editor)
                return;
            SwitchEngine();
            SwitchEngine();


        }

        public void SetEngine(ConfigNode config)
        {
            bool engineActive = ActiveEngine.getIgnitionState;
            ActiveEngine.EngineIgnited = false;

            //  remove all fuel gauges
            ClearMeters ();
            propellants.Clear ();

            //  clear the old engine state
            ActiveEngine.atmosphereCurve = new FloatCurve();
            ActiveEngine.velocityCurve = new FloatCurve ();

            //  load the new engine state
            ActiveEngine.Load (config);

            if (config.HasValue ("useVelocityCurve") && (config.GetValue ("useVelocityCurve").ToLowerInvariant () == "true")) {
                ActiveEngine.velocityCurve.Load (config.GetNode ("velocityCurve"));
            } else {
                ActiveEngine.useVelocityCurve = false;
            }

            //  set up propellants
            foreach (Propellant propellant in ActiveEngine.propellants) {
                if(propellant.drawStackGauge) { // we need to handle fuel gauges ourselves
                    propellant.drawStackGauge = false;
                    propellants.Add (propellant);
                }
            }
            ActiveEngine.SetupPropellant ();

            if (engineActive)
                ActiveEngine.Actions ["ActivateAction"].Invoke (new KSPActionParam (KSPActionGroup.None, KSPActionType.Activate));
        }
        bool AddEngine (ConfigNode config)
        {
            part.AddModule ("ModuleEngines");
            if (!ActiveEngine)
                return false;
            SetEngine (config);
            return true;
        }

        public ModuleEngines ActiveEngine {
            get { return (ModuleEngines)part.Modules ["ModuleEngines"]; }

        }

        [KSPEvent(guiActive=true, guiName="Switch Engine Mode")]
        public void SwitchEngine ()
        {
            if (currentMode == primaryModeName) {
                currentMode = secondaryModeName;
                SetEngine(secondaryEngine);
            } else {
                currentMode = primaryModeName;
                SetEngine(primaryEngine);
            }
        }

        public void FixedUpdate ()
        {
            if (ActiveEngine.getIgnitionState) { // engine is active, render fuel gauges
                SetThrust ((float) vessel.atmDensity);
                foreach (Propellant propellant in propellants) {
                    if (!meters.ContainsKey (propellant.name)) // how did we miss one?
                        meters.Add (propellant.name, NewMeter (propellant.name));

                    double amount = 0d;
                    double maxAmount = 0d;

                    List<PartResource> sources = new List<PartResource> ();
                    part.GetConnectedResources (propellant.id, propellant.GetFlowMode(), sources);

                    foreach (PartResource source in sources) {
                        amount += source.amount;
                        maxAmount += source.maxAmount;
                    }

                    if (propellant.name.Equals ("IntakeAir")) {
                        double minimum = (from modules in vessel.Parts
                                          from module in modules.Modules.OfType<ModuleEngines> ()
                                          from p in module.propellants
                                          where p.name == "IntakeAir"
                                          select module.ignitionThreshold * p.currentRequirement).Sum ();

                        // linear scale
                        meters ["IntakeAir"].SetValue ((float)((amount - minimum) / (maxAmount - minimum)));
                    } else {
                        meters [propellant.name].SetValue ((float)(amount / maxAmount));
                    }
                }
            } else if(meters.Count > 0) { // engine is shut down, remove all fuel gauges
                ClearMeters();
            }
        }

        private void SetThrust(float density)
        {
            ConfigNode config;
            if (currentMode == primaryModeName) {
                config = primaryEngine;
            } else {
                config = secondaryEngine;
            }

            float maxThrust = 0;
            float.TryParse (config.GetValue ("maxThrust"), out maxThrust);
            if(localCorrectThrust)
                maxThrust *= ActiveEngine.atmosphereCurve.Evaluate (density) / ActiveEngine.atmosphereCurve.Evaluate (0); // NK scale from max, not min, thrust.
            ActiveEngine.maxThrust = maxThrust;
        }

        List<Propellant> _props;
        List<Propellant> propellants
        {
            get {
                if(_props == null)
                    _props = new List<Propellant>();
                return _props;
            }
        }
        private Dictionary<string, VInfoBox> _meters;
        public Dictionary<string, VInfoBox> meters
        {
            get {
                if(_meters == null)
                    _meters = new Dictionary<string, VInfoBox>();
                return _meters;
            }
        }

        public void ClearMeters() {
            foreach(VInfoBox meter in meters.Values) {
                part.stackIcon.RemoveInfo (meter);
            }
            meters.Clear ();
        }

        VInfoBox NewMeter (string resourceName)
        {
            VInfoBox meter = part.stackIcon.DisplayInfo ();
            if (resourceName == "IntakeAir") {
                meter.SetMessage ("Air");
                meter.SetProgressBarColor (XKCDColors.White);
                meter.SetProgressBarBgColor (XKCDColors.Grey);
            } else {
                meter.SetMessage (resourceName);
                meter.SetMsgBgColor (XKCDColors.DarkLime);
                meter.SetMsgTextColor (XKCDColors.ElectricLime);
                meter.SetProgressBarColor (XKCDColors.Yellow);
                meter.SetProgressBarBgColor (XKCDColors.DarkLime);
            }
            meter.SetLength (2f);
            meter.SetValue (0f);

            return meter;
        }

    }

    public class ModuleEngineConfigs : PartModule, IPartCostModifier
    {
        protected bool compatible = true;
        [KSPField(isPersistant = true)]
        public string configuration = "";

        // Tech Level stuff
        [KSPField(isPersistant = true)]
        public int techLevel = -1; // default: disable

        public static float massMult = 1.0f;

        [KSPField]
        public int origTechLevel = 1; // default TL
        public float origMass = -1;
        public float massDelta = 0;
        [KSPField]
        public string gimbalTransform = "";
        [KSPField]
        public float gimbalMult = 1f;
        [KSPField]
        public bool useGimbalAnyway = false;

        [KSPField]
        public int maxTechLevel = -1;
        [KSPField]
        public int minTechLevel = -1;

        [KSPField]
        public string engineType = "L"; // default = lower stage

        [KSPField]
        public float throttle = 0.0f; // default min throttle level
        public float curThrottle = 0.0f;

        public ConfigNode techNodes = new ConfigNode();

        public static ConfigNode RFEngSettings = null;

        [KSPField]
        public bool isMaster = true; //is this Module the "master" module on the part?
        // For TestFlight integration, only ONE ModuleEngineConfigs (or child class) can be
        // the master module on a part.


        // - dunno why ialdabaoth had this persistent. [KSPField(isPersistant = true)]
        [KSPField]
        public string type = "ModuleEngines";

        [KSPField]
        public string engineID = "";

        [KSPField]
        public int moduleIndex = -1;

        [KSPField]
        public int offsetGUIPos = -1;

        public ModuleType fastType = ModuleType.MODULEENGINES;
        public ModuleEngines fastEngines = null;
        public ModuleEnginesFX fastEnginesFX = null;
        public ModuleRCS fastRCS = null;

        [KSPField(isPersistant = true)]
        public string thrustRating = "maxThrust";

        [KSPField(isPersistant = true)]
        public bool modded = false;

        public List<ConfigNode> configs;
        public ConfigNode config;

        // KIDS integration
        public static float ispSLMult = 1.0f;
        public static float ispVMult = 1.0f;
        public static bool correctThrust = true;

        public static float heatMult = 1.0f;

        [KSPField]
        public bool useConfigAsTitle = false;

        [KSPField]
        public bool localCorrectThrust = true;
        public float configMaxThrust = 1.0f;
        public float configMinThrust = 0.0f;
        public float configMassMult = 1.0f;
        public float configHeat = 0.0f;
        public float configCost = 0f;

        public bool useThrustCurve = false;
        public FloatCurve configThrustCurve = null;
        public string curveResource = "";
        public int curveProp = -1;
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "% Rated Thrust", guiUnits = "%", guiFormat = "F3")]
        public float thrustCurveDisplay = 100f;
        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "Fuel Ratio", guiUnits = "%", guiFormat = "F3")]
        public float thrustCurveRatio = 1f;

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

        public float GetModuleCost(float stdCost)
        {
            return configCost;
        }

        public float GetModuleMass(float defaultMass)
        {
            return massDelta;
        }

        public static FloatCurve Mod(FloatCurve fc, float sMult, float vMult)
        {
            //print("Modding this");
            ConfigNode curve = new ConfigNode("atmosphereCurve");
            fc.Save(curve);
            foreach (ConfigNode.Value k in curve.values)
            {
                string[] val = k.value.Split(' ');
                //print("Got string !" + k.value + ", split into " + val.Count() + " elements");
                float atmo = float.Parse(val[0]);
                float isp = float.Parse(val[1]);
                isp = isp * ((sMult * atmo) + (vMult * (1f - atmo))); // lerp between vac and SL
                val[1] = Math.Round(isp,1).ToString(); // round for neatness
                string newVal = "";
                foreach (string s in val)
                    newVal += s + " ";
                k.value = newVal;
            }
            FloatCurve retCurve = new FloatCurve();
            retCurve.Load(curve);
            return retCurve;
        }

        private static void FillSettings()
        {
            print("*RFEng* Loading Engine Settings!\n");

            if (RFEngSettings.HasValue("useRealisticMass"))
            {
                bool usereal = false;
                bool.TryParse(RFEngSettings.GetValue("useRealisticMass"), out usereal);
                if (!usereal)
                    massMult = float.Parse(RFEngSettings.GetValue("engineMassMultiplier"));
                else
                    massMult = 1.0f;
            }
            else
                massMult = 1.0f;
            if (RFEngSettings.HasValue("heatMultiplier"))
            {
                if (!float.TryParse(RFEngSettings.GetValue("heatMultiplier"), out heatMult))
                    heatMult = 1.0f;
            }
            else
                heatMult = 1.0f;
        }


        public override void OnAwake ()
        {
            if (CompatibilityChecker.IsWin64 ())
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
                    retStr += ", Mass: " + part.mass.ToString("N3") + " (was " + (origMass * massMult).ToString("N3") + ")";
                if (curThrottle >= 0)
                    retStr += ", MinThr " + (100f * curThrottle).ToString("N0") + "%";

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

            TechLevel moduleTLInfo = new TechLevel();
            if (techNodes != null)
                moduleTLInfo.Load(techNodes, techLevel);
            else
                moduleTLInfo = null;

            foreach (ConfigNode config in configs) {
                
                TechLevel cTL = new TechLevel();
                if (!cTL.Load(config, techNodes, engineType, techLevel))
                    cTL = null;

                if(!config.GetValue ("name").Equals (configuration)) {
                    info += "   " + config.GetValue ("name") + "\n";
                    if(config.HasValue (thrustRating))
                        info += "    (" + ThrustTL(config.GetValue (thrustRating), config).ToString("0.00") + " Thrust";
                    else
                        info += "    (Unknown Thrust";
                    if(config.HasValue("cost"))
                        info += "    (" + config.GetValue("cost") + " extra cost)"; // FIXME should get cost from TL, but this should be safe
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


        [KSPEvent(guiActive=false,guiActiveEditor=true, name = "NextEngine", guiName = "Current Configuration")]
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
            if(techLevel == -1)
                return;
            else if(TechLevel.CanTL(config, techNodes, engineType, techLevel + 1) && techLevel < maxTechLevel)
            {
                techLevel++;
            }
            else while (TechLevel.CanTL(config, techNodes, engineType, techLevel - 1) && techLevel > minTechLevel)
            {
                techLevel--;
            }
            SetConfiguration(configuration);
            UpdateSymmetryCounterparts();
        }

        [KSPField(isPersistant = false, guiActiveEditor = true, guiActive = false, guiName = "Show Engine "),
         UI_Toggle(enabledText = "GUI", disabledText = "GUI")]
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
            if(HighLogic.LoadedSceneIsEditor)
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
            if (!HighLogic.LoadedSceneIsEditor || !editor)
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

        public override void OnLoad(ConfigNode node)
        {
            if (!compatible)
                return;
            base.OnLoad (node);


            if (techLevel != -1)
            {
                if (maxTechLevel < 0)
                    maxTechLevel = TechLevel.MaxTL(node, engineType);
                if (minTechLevel < 0)
                    minTechLevel = origTechLevel;
            }

            if (node.HasValue("origMass"))
            {
                float.TryParse(node.GetValue("origMass"), out origMass);
                massDelta = 0;
                part.mass = origMass * massMult;
                if ((object)(part.partInfo) != null)
                    if ((object)(part.partInfo.partPrefab) != null)
                        massDelta = part.mass - part.partInfo.partPrefab.mass;
            }


            if (configs == null)
                configs = new List<ConfigNode>();
            else
                configs.Clear();

            foreach (ConfigNode subNode in node.GetNodes ("CONFIG")) {
                ConfigNode newNode = new ConfigNode("CONFIG");
                subNode.CopyTo (newNode);
                configs.Add (newNode);
            }


            techNodes = new ConfigNode();
            ConfigNode[] tLs = node.GetNodes("TECHLEVEL");
            foreach (ConfigNode n in tLs)
                techNodes.AddNode(n);

            // same as OnStart
            if (configs.Count == 0 && part.partInfo != null
               && part.partInfo.partPrefab.Modules.Contains("ModuleEngineConfigs"))
            {
                // get the correct prefab
                ModuleEngineConfigs prefab = null;
                if (part.partInfo != null && part.partInfo.partPrefab != null)
                {
                    foreach (PartModule p in part.partInfo.partPrefab.Modules)
                    {
                        if (p is ModuleEngineConfigs)
                        {
                            ModuleEngineConfigs m = (ModuleEngineConfigs)p;
                            if (m != null && m.engineID == engineID && m.moduleIndex == moduleIndex)
                            {
                                prefab = m;
                                break;
                            }
                        }
                    }
                }
                if((object)prefab != null)
                {
                    configs = new List<ConfigNode>();
                    foreach (ConfigNode subNode in prefab.configs)
                    {
                        ConfigNode newNode = new ConfigNode("CONFIG");
                        subNode.CopyTo(newNode);
                        configs.Add(newNode);
                    }
                    techNodes = new ConfigNode();
                    foreach (ConfigNode n in prefab.techNodes.nodes)
                        techNodes.AddNode(n);
                }
            }
            SetConfiguration(configuration);
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

        virtual public void DoConfig(ConfigNode cfg)
        {
            // fix propellant ratios to not be rounded
            if (cfg.HasNode("PROPELLANT"))
            {
                foreach (ConfigNode pNode in cfg.GetNodes("PROPELLANT"))
                {
                    if (pNode.HasValue("ratio"))
                    {
                        double dtmp;
                        if (double.TryParse(pNode.GetValue("ratio"), out dtmp))
                            pNode.SetValue("ratio", (dtmp * 100.0).ToString());
                    }
                }
            }
            float heat = -1;
            if (cfg.HasValue("heatProduction")) // ohai amsi: allow heat production to be changed by multiplier
            {
                heat = (float)Math.Round(float.Parse(cfg.GetValue("heatProduction")) * heatMult, 0);
                cfg.SetValue("heatProduction", heat.ToString());
            }

            // load throttle (for later)
            curThrottle = throttle;
            if (cfg.HasValue("throttle"))
                float.TryParse(cfg.GetValue("throttle"), out curThrottle);
            else if(cfg.HasValue("minThrust") && cfg.HasValue("maxThrust"))
                curThrottle = float.Parse(cfg.GetValue("minThrust")) / float.Parse(cfg.GetValue("maxThrust"));
            float TLMassMult = 1.0f;

            float gimbal = -1f;
            if (cfg.HasValue("gimbalRange"))
                gimbal = float.Parse(cfg.GetValue("gimbalRange"));

            float cost = 0f;
            if(cfg.HasValue("cost"))
                cost = float.Parse(cfg.GetValue("cost"));

            if (techLevel != -1)
            {
                // load techlevels
                TechLevel cTL = new TechLevel();
                //print("For engine " + part.name + ", config " + configuration + ", max TL: " + TechLevel.MaxTL(cfg, techNodes, engineType));
                cTL.Load(cfg, techNodes, engineType, techLevel);
                TechLevel oTL = new TechLevel();
                oTL.Load(cfg, techNodes, engineType, origTechLevel);


                // set atmosphereCurve
                if (cfg.HasValue("IspSL") && cfg.HasValue("IspV"))
                {
                    cfg.RemoveNode("atmosphereCurve");
                    ConfigNode curve = new ConfigNode("atmosphereCurve");
                    float ispSL, ispV;
                    float.TryParse(cfg.GetValue("IspSL"), out ispSL);
                    float.TryParse(cfg.GetValue("IspV"), out ispV);
                    FloatCurve aC = new FloatCurve();
                    aC = Mod(cTL.AtmosphereCurve, ispSL, ispV);
                    aC.Save(curve);
                    cfg.AddNode(curve);
                }

                // set heatProduction and dissipation
                if (heat > 0)
                {
                    cfg.SetValue("heatProduction", MassTL(heat).ToString("0"));
                    part.heatDissipation = 0.12f / MassTL(1.0f);
                }

                // set thrust and throttle
                if (cfg.HasValue(thrustRating))
                {
                    float thr;
                    float.TryParse(cfg.GetValue(thrustRating), out thr);
                    configMaxThrust = ThrustTL(thr);
                    cfg.SetValue(thrustRating, configMaxThrust.ToString("0.0000"));
                    if (cfg.HasValue("minThrust"))
                    {
                        float.TryParse(cfg.GetValue("minThrust"), out thr);
                        configMinThrust = ThrustTL(thr);
                        cfg.SetValue("minThrust", configMinThrust.ToString("0.0000"));
                    }
                    else
                    {
                        if (thrustRating.Equals("thrusterPower"))
                        {
                            configMinThrust = configMaxThrust * 0.5f;
                        }
                        else
                        {
                            configMinThrust = configMaxThrust;
                            if (curThrottle > 1.0f)
                            {
                                if (techLevel >= curThrottle)
                                    curThrottle = 1.0f;
                                else
                                    curThrottle = -1.0f;
                            }
                            if (curThrottle >= 0.0f)
                            {
                                curThrottle = (float)((double)curThrottle * cTL.Throttle());
                                configMinThrust *= curThrottle;
                            }
                            cfg.SetValue("minThrust", configMinThrust.ToString("0.0000"));
                        }
                    }
                    curThrottle = configMinThrust / configMaxThrust;
                    if(origMass > 0)
                         TLMassMult =  MassTL(1.0f);
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
                cost = CostTL(cost, cfg);
            }
            else
            {
                if(cfg.HasValue(thrustRating) && curThrottle > 0f && !cfg.HasValue("minThrust"))
                {
                    configMinThrust = curThrottle * float.Parse(cfg.GetValue(thrustRating));
                    cfg.SetValue("minThrust", configMinThrust.ToString("0.0000"));
                }
            }
            // mass change
            if (origMass > 0)
            {
                float ftmp;
                configMassMult = 1.0f;
                if (cfg.HasValue("massMult"))
                    if (float.TryParse(cfg.GetValue("massMult"), out ftmp))
                        configMassMult = ftmp;

                part.mass = origMass * configMassMult * massMult * TLMassMult;
                massDelta = 0;
                if((object)(part.partInfo) != null)
                    if((object)(part.partInfo.partPrefab) != null)
                        massDelta = part.mass - part.partInfo.partPrefab.mass;
            }
            // KIDS integration
            if(cfg.HasNode("atmosphereCurve"))
            {
                ConfigNode newCurveNode = new ConfigNode("atmosphereCurve");
                FloatCurve oldCurve = new FloatCurve();
                oldCurve.Load(cfg.GetNode("atmosphereCurve"));
                FloatCurve newCurve = Mod(oldCurve, ispSLMult, ispVMult);
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
                for (int i = 0; i < effectsToStop.Length; i++)
                    part.Effect(effectsToStop[i], 0f);
        }

        public PartModule pModule = null;

        virtual public void SetConfiguration(string newConfiguration = null)
        {

            if (newConfiguration == null)
                newConfiguration = configuration;
            ConfigNode newConfig = configs.Find (c => c.GetValue ("name").Equals (newConfiguration));
            if (!CanConfig(newConfig))
            {
                foreach(ConfigNode cfg in configs)
                    if (CanConfig(cfg))
                    {
                        newConfig = cfg;
                        newConfiguration = cfg.GetValue("name");
                        break;
                    }
            }
            if (newConfig != null) {

                // for asmi
                if (useConfigAsTitle)
                    part.partInfo.title = configuration;

                configuration = newConfiguration;
                config = new ConfigNode ("MODULE");
                newConfig.CopyTo (config);
                config.name = "MODULE";

                // fix for HotRockets etc.
                if (type.Equals("ModuleEngines") && part.Modules.Contains("ModuleEnginesFX") && !part.Modules.Contains("ModuleEngines"))
                    type = "ModuleEnginesFX";
                if (type.Equals("ModuleEnginesFX") && part.Modules.Contains("ModuleEngines") && !part.Modules.Contains("ModuleEnginesFX"))
                    type = "ModuleEngines";
                // fix for ModuleRCSFX etc
                if (type.Equals("ModuleRCS") && part.Modules.Contains("ModuleRCSFX") && !part.Modules.Contains("ModuleRCS"))
                    type = "ModuleRCSFX";
                if (type.Equals("ModuleRCSFX") && part.Modules.Contains("ModuleRCS") && !part.Modules.Contains("ModuleRCSFX"))
                    type = "ModuleRCS";

                config.SetValue("name", type);

                #if DEBUG
                print ("replacing " + type + " with:");
                print (newConfig.ToString ());
                #endif

                pModule = null;
                if (part.Modules.Contains(type))
                {
                    if (type.Equals("ModuleEnginesFX"))
                    {
                        if (engineID != "")
                        {
                            foreach (ModuleEnginesFX mFX in part.Modules.OfType<ModuleEnginesFX>())
                            {
                                if (mFX.engineID.Equals(engineID))
                                    pModule = (PartModule)mFX;
                            }
                        }
                        else if (moduleIndex >= 0)
                        {
                            int tmpIdx = 0;
                            pModule = null;
                            foreach (PartModule pM in part.Modules)
                            {
                                if (pM.GetType().Equals(type))
                                {
                                    if (tmpIdx == moduleIndex)
                                        pModule = pM;
                                    tmpIdx++;
                                }
                            }
                        }
                        else
                            pModule = part.Modules[type];
                    }
                    else
                        pModule = part.Modules[type];

                    if ((object)pModule == null)
                    {
                        Debug.Log("*RF* Could not find appropriate module of type " + type + ", with ID=" + engineID + " and index " + moduleIndex);
                        return;
                    }
                    // clear all FloatCurves
                    Type mType = pModule.GetType();
                    foreach (FieldInfo field in mType.GetFields())
                    {
                        if (field.FieldType == typeof(FloatCurve) && (field.Name.Equals("atmosphereCurve") || field.Name.Equals("velocityCurve")))
                        {
                            //print("*RFEng* resetting curve " + field.Name);
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
                                    print("*RFEng* Trying to remove info box: " + e.Message);
                                }
                            }
                            boxes.Clear();
                        }
                    }
                }
                if (type.Equals("ModuleRCS") || type.Equals("ModuleRCSFX"))
                {
                    ModuleRCS rcs = (ModuleRCS)pModule;
                    if (rcs != null)
                    {
                        rcs.G = 9.80665f;
                        /*bool oldRes = config.HasValue("resourceName");
                        string resource = "";
                        if (oldRes)
                        {
                            resource = config.GetValue("resourceName");
                            rcs.resourceName = resource;
                        }*/
                        DoConfig(config);
                        if (config.HasNode("PROPELLANT"))
                        {
                            rcs.propellants.Clear();
                        }
                        pModule.Load(config);
                        /*if (oldRes)
                        {
                            rcs.resourceName = resource;
                            rcs.SetResource(resource);
                        }*/
                        // PROPELLANT handling is automatic.
                        fastRCS = rcs;
                        if(type.Equals("ModuleRCS") && !part.Modules.Contains("ModuleRCSFX"))
                            fastType = ModuleType.MODULERCS;
                        else
                            fastType = ModuleType.MODULERCSFX;
                    }
                }
                else
                { // is an ENGINE
                    if (type.Equals("ModuleEngines"))
                    {
                        ModuleEngines mE = (ModuleEngines)pModule;
                        if (mE != null)
                        {
                            configMaxThrust = mE.maxThrust;
                            configMinThrust = mE.minThrust;
                            fastEngines = mE;
                            fastType = ModuleType.MODULEENGINES;
                            mE.g = 9.80665f;
                            if (config.HasNode("PROPELLANT"))
                            {
                                mE.propellants.Clear();
                            }
                        }
                        if (config.HasValue("maxThrust"))
                        {
                            float thr;
                            if(float.TryParse(config.GetValue("maxThrust"), out thr))
                                configMaxThrust = thr;
                        }
                        if (config.HasValue("minThrust"))
                        {
                            float thr;
                            if(float.TryParse(config.GetValue("minThrust"), out thr))
                                configMinThrust = thr;
                        }
                    }
                    else if (type.Equals("ModuleEnginesFX"))
                    {
                        ModuleEnginesFX mE = (ModuleEnginesFX)pModule;
                        if (mE != null)
                        {
                            configMaxThrust = mE.maxThrust;
                            configMinThrust = mE.minThrust;
                            fastEnginesFX = mE;
                            fastType = ModuleType.MODULEENGINESFX;
                            mE.g = 9.80665f;
                            if (config.HasNode("PROPELLANT"))
                            {
                                mE.propellants.Clear();
                            }
                        }
                        if (config.HasValue("maxThrust"))
                        {
                            float thr;
                            if (float.TryParse(config.GetValue("maxThrust"), out thr))
                                configMaxThrust = thr;
                        }
                        if (config.HasValue("minThrust"))
                        {
                            float thr;
                            if (float.TryParse(config.GetValue("minThrust"), out thr))
                                configMinThrust = thr;
                        }
                    }
                    DoConfig(config);
                    if(pModule != null)
                        pModule.Load (config);
                    if (config.HasNode("ModuleEngineIgnitor") && part.Modules.Contains("ModuleEngineIgnitor"))
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
                            if(eiNode.HasValue("ignitionsRemained"))
                                eiNode.SetValue("ignitionsRemained", remaining.ToString());
                            else
                                eiNode.AddValue("ignitionsRemained", remaining.ToString());
                        }
                        ConfigNode tNode = new ConfigNode("MODULE");
                        eiNode.CopyTo(tNode);
                        tNode.SetValue("name", "ModuleEngineIgnitor");
                        part.Modules["ModuleEngineIgnitor"].Load(tNode);
                    }
                }
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
                    configCost = float.Parse(config.GetValue("cost"));
                else
                    configCost = 0f;
                UpdateTweakableMenu();
                // Check for and enable the thrust curve
                useThrustCurve = false;
                Fields["thrustCurveDisplay"].guiActive = false;
                if (config.HasNode("thrustCurve") && config.HasValue("curveResource"))
                {
                    curveResource = config.GetValue("curveResource");
                    if (curveResource != "")
                    {
                        double ratio = 0.0;
                        switch (fastType)
                        {
                            case ModuleType.MODULEENGINES:
                                configHeat = fastEngines.heatProduction;
                                for (int i = 0; i < fastEngines.propellants.Count; i++ )
                                    if (fastEngines.propellants[i].name.Equals(curveResource))
                                        curveProp = i;
                                if (curveProp >= 0)
                                    ratio = fastEngines.propellants[curveProp].totalResourceAvailable / fastEngines.propellants[curveProp].totalResourceCapacity;
                                break;

                            case ModuleType.MODULEENGINESFX:
                                configHeat = fastEnginesFX.heatProduction;
                                for (int i = 0; i < fastEnginesFX.propellants.Count; i++)
                                    if (fastEnginesFX.propellants[i].name.Equals(curveResource))
                                        curveProp = i;
                                if (curveProp >= 0)
                                    ratio = fastEnginesFX.propellants[curveProp].totalResourceAvailable / fastEnginesFX.propellants[curveProp].totalResourceCapacity;
                                break;

                            case ModuleType.MODULERCS:
                                for (int i = 0; i < fastRCS.propellants.Count; i++)
                                    if (fastRCS.propellants[i].name.Equals(curveResource))
                                        curveProp = i;
                                if (curveProp >= 0)
                                    ratio = fastRCS.propellants[curveProp].totalResourceAvailable / fastRCS.propellants[curveProp].totalResourceCapacity;
                                break;
                        }
                        if (curveProp != -1)
                        {
                            useThrustCurve = true;
                            configThrustCurve = new FloatCurve();
                            configThrustCurve.Load(config.GetNode("thrustCurve"));
                            print("*RF* Found thrust curve for " + part.name);
                            Fields["thrustCurveDisplay"].guiActive = true;
                        }
                        
                    }
                }
                if((object)(EditorLogic.fetch) != null && (object)(EditorLogic.fetch.ship) != null && HighLogic.LoadedSceneIsEditor)
                    GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }
            SetupFX();

            UpdateTFInterops(); // update TestFlight if it's installed
        }

        private int oldTechLevel = -1;
        private string oldConfiguration;

        [PartMessageEvent]
        public event PartEngineConfigChanged EngineConfigChanged;

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
        }

        //called by StretchyTanks StretchySRB and ProcedrualParts
        virtual public void ChangeThrust(float newThrust)
        {
            //print("*RFEng* For " + part.name + (part.parent!=null? " parent " + part.parent.name:"") + ", Setting new max thrust " + newThrust.ToString());
            foreach(ConfigNode c in configs)
            {
                c.SetValue("maxThrust", newThrust.ToString());
            }
            SetConfiguration(configuration);
            //print("New max thrust: " + ((ModuleEngines)part.Modules["ModuleEngines"]).maxThrust);
        }

        // Used by ProceduralParts
        public void ChangeEngineType(string newEngineType)
        {
            engineType = newEngineType;
            SetConfiguration(configuration);
        }

        public override void OnStart (StartState state)
        {
            if (!compatible)
                return;
            this.enabled = true;
            if(configs.Count == 0 && part.partInfo != null
               && part.partInfo.partPrefab.Modules.Contains ("ModuleEngineConfigs")) {
                ModuleEngineConfigs prefab = (ModuleEngineConfigs) part.partInfo.partPrefab.Modules["ModuleEngineConfigs"];

                configs = new List<ConfigNode>();
                foreach (ConfigNode subNode in prefab.configs)
                {
                    ConfigNode newNode = new ConfigNode("CONFIG");
                    subNode.CopyTo(newNode);
                    configs.Add(newNode);
                }
            }
            SetConfiguration (configuration);
            if (part.Modules.Contains("ModuleEngineIgnitor"))
                part.Modules["ModuleEngineIgnitor"].OnStart(state);
            if (state != StartState.Editor && type.Contains("ModuleEngines"))
            {
                if (part.Modules.Contains("ModuleEngines"))
                    ((ModuleEngines)part.Modules["ModuleEngines"]).minThrust = 0f;
                else if (part.Modules.Contains("ModuleEnginesFX"))
                    ((ModuleEnginesFX)part.Modules["ModuleEnginesFX"]).minThrust = 0f;
            }
        }

        public override void OnInitialize()
        {
            if (!compatible)
                return;
            SetConfiguration(configuration);
        }

        public void FixedUpdate ()
        {
            if (!compatible)
                return;
            if (vessel == null)
                return;
            SetThrust ();
            StopFX();
        }

        public void SetThrust()
        {
            if (fastType == ModuleType.MODULEENGINES)
            {
                ModuleEngines engine = fastEngines;
                if ((object)config != null && (object)engine != null)
                {
                    bool throttleCut = (object)vessel != null;
                    if (throttleCut)
                        throttleCut = throttleCut && vessel.ctrlState.mainThrottle <= 0;
                    if (engine.realIsp > 0)
                    {
                        float multiplier = 1.0f;
                        if (useThrustCurve)
                        {
                            thrustCurveRatio = (float)((engine.propellants[curveProp].totalResourceAvailable / engine.propellants[curveProp].totalResourceCapacity));
                            thrustCurveDisplay = configThrustCurve.Evaluate(thrustCurveRatio);
                            multiplier *= thrustCurveDisplay;
                            engine.heatProduction = configHeat * thrustCurveDisplay;
                            thrustCurveDisplay *= 100f;
                        }
                        if (localCorrectThrust && correctThrust)
                        {
                            float refIsp = Mathf.Lerp(ispVMult, ispSLMult, (float)part.vessel.staticPressure) / ispVMult * engine.atmosphereCurve.Evaluate(0);
                            float frameIsp = engine.atmosphereCurve.Evaluate((float)vessel.staticPressure);
                            multiplier *= frameIsp / refIsp;
                        }
                        engine.maxThrust = configMaxThrust * multiplier;
                        if (throttleCut)
                            engine.minThrust = 0;
                        else
                            engine.minThrust = configMinThrust * multiplier;
                    }
                    else if(throttleCut)
                        engine.minThrust = 0;
                }
                if(!engine.EngineIgnited)
                    engine.SetRunningGroupsActive(false); // fix for SQUAD bug
            }
            else if (fastType == ModuleType.MODULEENGINESFX)
            {
                ModuleEnginesFX engine = fastEnginesFX;
                if ((object)config != null)
                {
                    bool throttleCut = (object)vessel != null;
                    if (throttleCut)
                        throttleCut = throttleCut && vessel.ctrlState.mainThrottle <= 0;
                    if (engine.realIsp > 0)
                    {
                        float multiplier = 1.0f;
                        if (useThrustCurve)
                        {
                            thrustCurveRatio = (float)((engine.propellants[curveProp].totalResourceAvailable / engine.propellants[curveProp].totalResourceCapacity));
                            thrustCurveDisplay = configThrustCurve.Evaluate(thrustCurveRatio);
                            multiplier *= thrustCurveDisplay;
                            engine.heatProduction = configHeat * thrustCurveDisplay;
                            thrustCurveDisplay *= 100f;
                        }
                        if (localCorrectThrust && correctThrust)
                        {
                            float refIsp = Mathf.Lerp(ispVMult, ispSLMult, (float)part.vessel.staticPressure) / ispVMult * engine.atmosphereCurve.Evaluate(0);
                            float frameIsp = engine.atmosphereCurve.Evaluate((float)vessel.staticPressure);
                            multiplier *= frameIsp / refIsp;
                        }

                        engine.maxThrust = configMaxThrust * multiplier;
                        if (throttleCut)
                            engine.minThrust = 0;
                        else
                            engine.minThrust = configMinThrust * multiplier;
                    }
                    else if (throttleCut)
                        engine.minThrust = 0;
                }
            }
            else if (fastType == ModuleType.MODULERCS || fastType == ModuleType.MODULERCSFX) // cast either to ModuleRCS
            {
                ModuleRCS engine = fastRCS;
                if ((object)config != null && (object)engine != null && engine.realISP > 0)
                {
                    float multiplier = 1.0f;
                    if (useThrustCurve)
                    {
                        thrustCurveRatio = (float)((engine.propellants[curveProp].totalResourceAvailable / engine.propellants[curveProp].totalResourceCapacity));
                        thrustCurveDisplay = configThrustCurve.Evaluate(thrustCurveRatio);
                        multiplier *= thrustCurveDisplay;
                        thrustCurveDisplay *= 100f;
                    }
                    if (fastType != ModuleType.MODULERCSFX && localCorrectThrust && correctThrust)
                    {
                        float refIsp = Mathf.Lerp(ispVMult, ispSLMult, (float)part.vessel.staticPressure) / ispVMult * engine.atmosphereCurve.Evaluate(0);
                        float frameIsp = engine.atmosphereCurve.Evaluate((float)vessel.staticPressure);
                        multiplier *= frameIsp / refIsp;
                    }
                    engine.thrusterPower = configMaxThrust * multiplier;
                }
            }
        }

        private bool CanConfig(ConfigNode config)
        {
            if ((object)config == null)
                return true;
            if (!config.HasValue("techRequired") || (object)HighLogic.CurrentGame == null)
                return true;
            if (HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX || ResearchAndDevelopment.GetTechnologyState(config.GetValue("techRequired")) == RDTech.State.Available)
                return true;
            return false;
        }

        private void engineManagerGUI(int WindowID)
        {
            foreach (ConfigNode node in configs)
            {
                GUILayout.BeginHorizontal();
                string costString = "";
                if (node.HasValue("cost"))
                {
                    float curCost = float.Parse(node.GetValue("cost"));

                    if (techLevel != -1)
                    {
                        curCost = CostTL(curCost, node) - CostTL(0f, node); // get purely the config cost difference
                    }
                    costString = " (" + ((curCost < 0) ? "" : "+") + curCost.ToString("0") + "f)";
                }
                if (node.GetValue("name").Equals(configuration))
                    GUILayout.Label("Current config: " + configuration + costString);
                else if (CanConfig(node))
                {
                    if (GUILayout.Button("Switch to " + node.GetValue("name") + costString))
                    {
                        SetConfiguration(node.GetValue("name"));
                        UpdateSymmetryCounterparts();
                    }
                }
                else
                {
                    GUILayout.Label("Lack tech for " + node.GetValue("name"));
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
                    SetConfiguration(configuration);
                    UpdateSymmetryCounterparts();
                }
                GUILayout.Label(techLevel.ToString());
                string plusStr = "X";
                bool canPlus = false;
                if (TechLevel.CanTL(config, techNodes, engineType, techLevel + 1) && techLevel < maxTechLevel)
                {
                    plusStr = "+";
                    canPlus = true;
                }
                if (GUILayout.Button(plusStr) && canPlus)
                {
                    techLevel++;
                    SetConfiguration(configuration);
                    UpdateSymmetryCounterparts();
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(pModule.GetInfo() + "\n" + TLTInfo() + "\n" + "Total cost: " + (part.partInfo.cost + part.GetModuleCosts(part.partInfo.cost)).ToString("0"));
            GUILayout.EndHorizontal();

            if(showRFGUI)
                GUI.DragWindow();
        }

        virtual public int UpdateSymmetryCounterparts()
        {
            int i = 0;
            if (part.symmetryCounterparts == null)
                return i;
            foreach (Part sPart in part.symmetryCounterparts) {
                try
                {
                    ModuleEngineConfigs engine = (ModuleEngineConfigs)sPart.Modules["ModuleEngineConfigs"];
                    if (engine)
                    {
                        i++;
                        engine.techLevel = techLevel;
                        engine.SetConfiguration(configuration);
                    }
                }
                catch
                {
                }
            }
            return i;
        }
    }
}

