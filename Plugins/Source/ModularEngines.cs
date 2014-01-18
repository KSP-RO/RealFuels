using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;
using KSP;

namespace ModularFuelTanks
{

	public class ModuleHybridEngine : ModuleEngineConfigs
	{

		public override void OnStart (StartState state)
		{
			if(configs.Count == 0 && part.partInfo != null
			   && part.partInfo.partPrefab.Modules.Contains ("ModuleHybridEngine")) {
				ModuleHybridEngine prefab = (ModuleHybridEngine) part.partInfo.partPrefab.Modules["ModuleHybridEngine"];
				configs = prefab.configs;
			}
			SetConfiguration (configuration);
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
		}
		override public void SetConfiguration(string newConfiguration = null)
		{
            if (newConfiguration == null)
                newConfiguration = configuration;
			ConfigNode newConfig = configs.Find (c => c.GetValue ("name").Equals (newConfiguration));
			if (newConfig == null)
				return;
			Fields ["configuration"].guiActive = true;
			Fields ["configuration"].guiName = "Current Mode";

			configuration = newConfiguration;
			config = new ConfigNode ("MODULE");
			newConfig.CopyTo (config);
			config.name = "MODULE";
			config.SetValue ("name", "ModuleEngines");

			bool engineActive = ActiveEngine.getIgnitionState;
			ActiveEngine.EngineIgnited = false;

			//  remove all fuel gauges
			ClearMeters ();
			propellants.Clear ();

			//  clear the old engine state
			ActiveEngine.atmosphereCurve = new FloatCurve();
			ActiveEngine.velocityCurve = new FloatCurve ();

            DoConfig(config); // from MEC

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

		public ModuleEngines ActiveEngine {
			get {
				type = "ModuleEngines";
				return (ModuleEngines)part.Modules ["ModuleEngines"];
			}

		}

		new public void FixedUpdate ()
		{
			SetThrust ();
			if (ActiveEngine.getIgnitionState) { // engine is active, render fuel gauges
				foreach (Propellant propellant in propellants) {
					if (!meters.ContainsKey (propellant.name)) // how did we miss one?
						meters.Add (propellant.name, NewMeter (propellant.name));

					double amount = 0d;
					double maxAmount = 0d;

					List<PartResource> sources = new List<PartResource> ();
					part.GetConnectedResources (propellant.id, sources);

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
					part.GetConnectedResources (propellant.id, sources);

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

	public class ModuleEngineConfigs : PartModule
	{

		[KSPField(isPersistant = true)]
		public string configuration = "";

        // Tech Level stuff
        [KSPField(isPersistant = true)]
        public int techLevel = -1; // default: disable

        public static float massMult = 1.0f;

        public int origTechLevel = 1; // default TL
        public float origMass = -1;

        public int maxTechLevel = -1;
        public int minTechLevel = 1;

        public string engineType = "L"; // default = lower stage
        public float throttle = 0.0f; // default min throttle level
        public float curThrottle = 0.0f;

        public ConfigNode techNodes = new ConfigNode();

        public static ConfigNode MFSSettings = null;


		[KSPField(isPersistant = true)]
		public string type = "ModuleEngines";

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


        public bool localCorrectThrust = true;
        public float configMaxThrust = 1.0f;
        public float configMinThrust = 0.0f;
        public float configMassMult = 1.0f;

        // *NEW* TL Handling
        public class TechLevel
        {
            public FloatCurve atmosphereCurve;
            public FloatCurve velocityCurve;
            public double TWR;
            public double thrustMultiplier;
            public double massMultiplier;
            public double minThrottleMultiplier;
            string techRequired;

            // CONSTRUCTORS
            public TechLevel()
            {
                atmosphereCurve = new FloatCurve();
                velocityCurve = new FloatCurve();
                TWR = -1;
                thrustMultiplier = -1;
                massMultiplier = -1;
                minThrottleMultiplier = -1;
                techRequired = "";
            }
            public TechLevel(TechLevel t)
            {
                atmosphereCurve = t.atmosphereCurve;
                velocityCurve = t.velocityCurve;
                TWR = t.TWR;
                thrustMultiplier = t.thrustMultiplier;
                massMultiplier = t.massMultiplier;
                techRequired = t.techRequired;
                minThrottleMultiplier = t.minThrottleMultiplier;
            }
            public TechLevel(ConfigNode node)
            {
                Load(node);
            }

            // LOADERS
            // loads from an override node
            public bool Load(ConfigNode node)
            {
                if (node.HasNode("atmosphereCurve"))
                    atmosphereCurve.Load(node.GetNode("atmosphereCurve"));
                else
                {
                    atmosphereCurve = null;
                    return false;
                }

                if (node.HasNode("velocityCurve"))
                    velocityCurve.Load(node.GetNode("velocityCurve"));
                else
                    velocityCurve = null;

                if (node.HasValue("TWR"))
                    TWR = double.Parse(node.GetValue("TWR"));
                else
                    TWR = -1;

                if (node.HasValue("thrustMultiplier"))
                    thrustMultiplier = double.Parse(node.GetValue("thrustMultiplier"));
                else
                    thrustMultiplier = -1;

                if (node.HasValue("massMultiplier"))
                    massMultiplier = double.Parse(node.GetValue("massMultiplier"));
                else
                    massMultiplier = -1;

                if (node.HasValue("minThrottleMultiplier"))
                    minThrottleMultiplier = double.Parse(node.GetValue("minThrottleMultiplier"));
                else
                    minThrottleMultiplier = -1;

                if (node.HasValue("techRequired"))
                    techRequired = node.GetValue("techRequired");
                else
                    techRequired = "";

                return true;
            }

            // loads a given techlevel from global techlevels-style node
            public bool Load(ConfigNode node, int level)
            {
                var tLs = node.GetNodes("TECHLEVEL");
                if (tLs.Count() > 0)
                {
                    foreach(ConfigNode n in tLs)
                        if (n.HasValue("name") && n.GetValue("name").Trim().Equals(level.ToString()))
                            return Load(n);
                    return false;
                }

                if (node.HasValue("techLevelType"))
                    return Load(node.GetValue("techLevelType"), level);

                if (node.HasNode("TLISP" + level))
                    atmosphereCurve.Load(node.GetNode("TLISP" + level));
                else
                {
                    atmosphereCurve = null;
                    return false;
                }

                if (node.HasNode("TLVC" + level))
                    velocityCurve.Load(node.GetNode("TLVC" + level));
                else
                    velocityCurve = null;

                if (node.HasValue("TLTWR" + level))
                    TWR = double.Parse(node.GetValue("TLTWR" + level));
                else
                    TWR = 60;

                if (node.HasValue("TLTHROTTLE" + level))
                    minThrottleMultiplier = double.Parse(node.GetValue("TLTHROTTLE" + level));
                else
                    minThrottleMultiplier = 0.0;

                if (node.HasValue("TLTECH"+level))
                    techRequired = node.GetValue("TLTECH"+level);
                else
                    techRequired = "";

                return true;
            }

            // loads from global techlevels
            public bool Load(string type, int level)
            {
                if (MFSSettings == null || MFSSettings.GetNode("MFS_TECHLEVELS") == null)
                    return false;

                foreach (ConfigNode node in MFSSettings.GetNode("MFS_TECHLEVELS").GetNodes("ENGINETYPE"))
                {
                    if (node.HasValue("name") && node.GetValue("name").Equals(type))
                        return Load(node, level);
                }
                return false;
            }

            // loads from anything
            public bool Load(ConfigNode cfg, ConfigNode mod, string type, int level)
            {
                // check local techlevel configs
                if (cfg != null)
                {
                    var tLs = cfg.GetNodes("TECHLEVEL");
                    if (tLs.Count() > 0)
                    {
                        foreach (ConfigNode n in tLs)
                            if (n.HasValue("name") && n.GetValue("name").Equals(level.ToString()))
                                return Load(n);
                        return false;
                    }
                    if (cfg.HasValue("techLevelType"))
                        return Load(cfg.GetValue("techLevelType"), level);
                }

                // check module techlevel configs
                if (mod != null)
                {
                    var tLs = mod.GetNodes("TECHLEVEL");
                    if (tLs.Count() > 0)
                    {
                        foreach (ConfigNode n in tLs)
                            if (n.HasValue("name") && n.GetValue("name").Equals(level.ToString()))
                                return Load(n);
                        return false;
                    }
                }

                // check global
                //print("*MFS* Fallback to global for type " + type + ", TL " + level);
                return Load(type, level);
            }

            // MULTIPLIERS
            public double Thrust(TechLevel oldTL, bool constantMass = false)
            {
                if (oldTL.thrustMultiplier > 0 && thrustMultiplier > 0)
                    return thrustMultiplier / oldTL.thrustMultiplier;

                if(constantMass)
                    return TWR / oldTL.TWR;
                else
                    return TWR / oldTL.TWR * oldTL.atmosphereCurve.Evaluate(0) / atmosphereCurve.Evaluate(0);
            }

            public double Mass(TechLevel oldTL, bool constantThrust = false)
            {
                if (oldTL.massMultiplier > 0 && massMultiplier > 0)
                    return massMultiplier / oldTL.massMultiplier;

                if (constantThrust)
                    return oldTL.TWR / TWR;
                else
                    return oldTL.atmosphereCurve.Evaluate(0) / atmosphereCurve.Evaluate(0);
            }

            public double Throttle()
            {
                if(minThrottleMultiplier < 0)
                    return 0.0;
                if (minThrottleMultiplier > 1.0)
                    return 1.0;
                return minThrottleMultiplier;
            }

            // looks up in global techlevels
            public static int MaxTL(string type)
            {
                int max = -1;
                if (MFSSettings == null || MFSSettings.GetNode("MFS_TECHLEVELS") == null)
                    return max;
                foreach (ConfigNode node in MFSSettings.GetNode("MFS_TECHLEVELS").GetNodes("ENGINETYPE"))
                {
                    if (node.HasValue("name") && node.GetValue("name").Equals(type))
                    {
                        var tLs = node.GetNodes("TECHLEVEL");
                        if (tLs.Count() > 0)
                        {
                            return MaxTL(node);
                        }
                        foreach (ConfigNode.Value val in node.values)
                        {
                            string stmp = val.name;
                            stmp = stmp.Replace("TLTWR", "");
                            int itmp;
                            if (int.TryParse(stmp.Trim(), out itmp))
                                if (itmp > max)
                                    max = itmp;
                        }
                    }
                }
                return max;
            }

            // looks up in global techlevels
            public static int MinTL(string type)
            {
                int min = int.MaxValue;
                if (MFSSettings == null || MFSSettings.GetNode("MFS_TECHLEVELS") == null)
                    return min;
                foreach (ConfigNode node in MFSSettings.GetNode("MFS_TECHLEVELS").GetNodes("ENGINETYPE"))
                {
                    if (node.HasValue("name") && node.GetValue("name").Equals(type))
                    {
                        var tLs = node.GetNodes("TECHLEVEL");
                        if (tLs.Count() > 0)
                        {
                            return MinTL(node);
                        }
                        foreach (ConfigNode.Value val in node.values)
                        {
                            string stmp = val.name;
                            stmp = stmp.Replace("TLTWR", "");
                            int itmp;
                            if (int.TryParse(stmp.Trim(), out itmp))
                                if (itmp < min)
                                    min = itmp;
                        }
                    }
                }
                return min;
            }

            // local check, with optional fallback to global
            public static int MaxTL(ConfigNode node, string type = "")
            {
                int max = -1;
                if (node != null)
                {
                    foreach (ConfigNode n in node.GetNodes("TECHLEVEL"))
                    {
                        int itmp;
                        if (n.HasValue("name") && int.TryParse(n.GetValue("name").Trim(), out itmp))
                            if (itmp > max)
                                max = itmp;
                    }
                }
                if (max < 0 && !type.Equals(""))
                    max = MaxTL(type);
                return max;
            }

            // local check, with optional fallback to global
            public static int MinTL(ConfigNode node, string type = "")
            {
                int min = int.MaxValue;
                if (node != null)
                {
                    foreach (ConfigNode n in node.GetNodes("TECHLEVEL"))
                    {
                        int itmp;
                        if (n.HasValue("name") && int.TryParse(n.GetValue("name").Trim(), out itmp))
                            if (itmp < min)
                                min = itmp;
                    }
                }
                if (min >= int.MaxValue && !type.Equals(""))
                    min = MinTL(type);
                return min;
            }

            // full check
            public static int MaxTL(ConfigNode cfg, ConfigNode mod, string type)
            {
                if (cfg.GetNodes("TECHLEVEL").Count() > 0)
                    return MaxTL(cfg, type);
                else if (cfg.HasValue("techLevelType"))
                    return MaxTL(cfg.GetValue("techLevelType"));
                else
                    return MaxTL(mod, type);
            }

            // full check
            public static int MinTL(ConfigNode cfg, ConfigNode mod, string type)
            {
                if (cfg.GetNodes("TECHLEVEL").Count() > 0)
                    return MinTL(cfg, type);
                else if (cfg.HasValue("techLevelType"))
                    return MinTL(cfg.GetValue("techLevelType"));
                else
                    return MinTL(mod, type);
            }

            // Check if can switch to TL
            public static bool CanTL(ConfigNode cfg, ConfigNode mod, string type, int level)
            {
                TechLevel nTL = new TechLevel();
                if (!nTL.Load(cfg, mod, type, level))
                    return false;
                return HighLogic.CurrentGame.Mode != Game.Modes.CAREER || nTL.techRequired.Equals("") || ResearchAndDevelopment.GetTechnologyState(nTL.techRequired) == RDTech.State.Available;
            }
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
            print("*MFS* Loading Engine Settings!\n");

            if (MFSSettings.HasValue("useRealisticMass"))
            {
                bool usereal = false;
                bool.TryParse(MFSSettings.GetValue("useRealisticMass"), out usereal);
                if (!usereal)
                    massMult = float.Parse(MFSSettings.GetValue("engineMassMultiplier"));
                else
                    massMult = 1.0f;
            }
            else
                massMult = 1.0f;
            if (MFSSettings.HasValue("heatMultiplier"))
            {
                if (!float.TryParse(MFSSettings.GetValue("heatMultiplier"), out heatMult))
                    heatMult = 1.0f;
            }
            else
                heatMult = 1.0f;
        }

		public override void OnAwake ()
		{
			if(configs == null)
				configs = new List<ConfigNode>();
            if (MFSSettings == null)
            {
                foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("MFSSETTINGS"))
                    MFSSettings = node;
                if(MFSSettings == null)
                    throw new UnityException("*MFS* MFSSettings not found!");
                FillSettings();
            }
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

        private string TLTInfo()
        {
            string retStr = "";
            if(techLevel != -1)
            {
                retStr =  "Type: " + engineType + ". Tech Level: " + techLevel + " (" + origTechLevel + "-" + maxTechLevel + ")";
                if (origMass > 0)
                    retStr += ", Mass: " + part.mass.ToString("N3") + " (was " + (origMass * massMult).ToString("N3") + ")";
                if (curThrottle >= 0)
                    retStr += ", MinThr " + (100f * curThrottle).ToString("N0") + "%";
                return retStr;
            }
            else
                return "";
        }

		public override string GetInfo ()
		{
			if (configs.Count < 2)
				return TLTInfo();

			string info = TLTInfo() + "\nAlternate configurations:\n";

            TechLevel moduleTLInfo = new TechLevel();
            if (techNodes != null)
                moduleTLInfo.Load(techNodes, techLevel);
            else
                moduleTLInfo = null;

			foreach (ConfigNode config in configs) {
				if(!config.GetValue ("name").Equals (configuration)) {
					info += "   " + config.GetValue ("name") + "\n";
					if(config.HasValue (thrustRating))
						info += "    (" + ThrustTL(config.GetValue (thrustRating), config).ToString("0.00") + " Thrust";
					else
						info += "    (Unknown Thrust";

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
                        TechLevel cTL = new TechLevel();
                        if (cTL.Load(config, techNodes, engineType, techLevel))
                        {
                            ispSL *= ispSLMult * cTL.atmosphereCurve.Evaluate(1);
                            ispV *= ispVMult * cTL.atmosphereCurve.Evaluate(0);
                            info += ", " + ispSL.ToString("0") + "-" + ispV.ToString("0") + "Isp";
                        }
                    }
					info += ")\n";
				}


			}
			return info;
		}

		public void OnGUI()
		{
			EditorLogic editor = EditorLogic.fetch;
			if (!HighLogic.LoadedSceneIsEditor || !editor || editor.editorScreen != EditorLogic.EditorScreen.Actions) {
				return;
			}

			if (EditorActionGroups.Instance.GetSelectedParts ().Contains (part)) {
				Rect screenRect = new Rect(part.Modules.Contains("ModuleFuelTanks") ? 430 : 0, 365, 430, (Screen.height - 365)); // NK allow both MFT and MEC to work
				GUILayout.Window (part.name.GetHashCode ()+1, screenRect, engineManagerGUI, "Configure " + part.partInfo.title);
			}
		}

		public override void OnLoad (ConfigNode node)
		{
			base.OnLoad (node);

            techNodes = new ConfigNode();
            var tLs = node.GetNodes("TECHLEVEL");
            foreach (ConfigNode n in tLs)
                techNodes.AddNode(n);

            if (node.HasValue("engineType"))
                engineType = node.GetValue("engineType");

            if (node.HasValue("throttle"))
                float.TryParse(node.GetValue("throttle"), out throttle);

            if(node.HasValue("origTechLevel"))
               int.TryParse(node.GetValue("origTechLevel"), out origTechLevel);
            if (node.HasValue("maxTechLevel"))
                int.TryParse(node.GetValue("maxTechLevel"), out maxTechLevel);
            else
                { if (techLevel != -1) { maxTechLevel = TechLevel.MaxTL(node, engineType); } }

            if (node.HasValue("minTechLevel"))
            {
                if (!int.TryParse(node.GetValue("minTechLevel"), out minTechLevel))
                    minTechLevel = origTechLevel;
            }
            else
                minTechLevel = origTechLevel;

            if (node.HasValue("origMass"))
            {
                float.TryParse(node.GetValue("origMass"), out origMass);
                part.mass = origMass * massMult;
            }
            /*else -- already defaults to -1 anyway. And this was breaking stuff.
            {
                //print("*MFS* OnLoad: Missing origMass for " + part.name);
                origMass = -1;
            }*/

            localCorrectThrust = true;
            if (node.HasValue("localCorrectThrust"))
                bool.TryParse(node.GetValue("localCorrectThrust"), out localCorrectThrust);
            localCorrectThrust = localCorrectThrust && correctThrust;

			if (configs == null)
				configs = new List<ConfigNode> ();
			else
				configs.Clear ();

			foreach (ConfigNode subNode in node.GetNodes ("CONFIG")) {
				ConfigNode newNode = new ConfigNode("CONFIG");
				subNode.CopyTo (newNode);
				configs.Add (newNode);
			}

            // same as OnStart
            if (configs.Count == 0 && part.partInfo != null
               && part.partInfo.partPrefab.Modules.Contains("ModuleEngineConfigs"))
            {
                ModuleEngineConfigs prefab = (ModuleEngineConfigs)part.partInfo.partPrefab.Modules["ModuleEngineConfigs"];
                configs = prefab.configs;
            }
            SetConfiguration(configuration);
		}

		public override void OnSave (ConfigNode node)
		{
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
                    ispSL *= ispSLMult;
                    ispV *= ispVMult;
                    FloatCurve aC = new FloatCurve();
                    aC = Mod(cTL.atmosphereCurve, ispSL, ispV);
                    aC.Save(curve);
                    cfg.AddNode(curve);
                }

                // set heatProduction and dissipation
                if (heat > 0)
                {
                    cfg.SetValue("heatProduction", MassTL(heat).ToString("0"));
                    part.heatDissipation = 0.12f / MassTL(1.0f);
                }

                // load throttle (for later)
                curThrottle = throttle;
                if(cfg.HasValue("throttle"))
                    float.TryParse(cfg.GetValue("throttle"), out curThrottle);

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
                }

                // mass change
                if (origMass > 0)
                {
                    float ftmp;
                    configMassMult = 1.0f;
                    if (cfg.HasValue("massMult"))
                        if (float.TryParse(cfg.GetValue("massMult"), out ftmp))
                            configMassMult = ftmp;

                    part.mass = MassTL(origMass * configMassMult);
                }
            }
        }

        public PartModule pModule = null;

		virtual public void SetConfiguration(string newConfiguration = null)
		{
            if (newConfiguration == null)
                newConfiguration = configuration;
			ConfigNode newConfig = configs.Find (c => c.GetValue ("name").Equals (newConfiguration));
			if (newConfig != null) {

                // for asmi
                if (useConfigAsTitle)
                    part.partInfo.title = configuration;

				configuration = newConfiguration;
				config = new ConfigNode ("MODULE");
				newConfig.CopyTo (config);
				config.name = "MODULE";
				config.SetValue ("name", type);
				#if DEBUG
				print ("replacing " + type + " with:");
				print (newConfig.ToString ());
				#endif

                pModule = null;
                bool rcsSounds = (type.Equals("ModuleRCS") && part.Modules.Contains("RcsSounds"));
                if (part.Modules.Contains(type) || rcsSounds)
                {
                    if(rcsSounds)
                        pModule = part.Modules["RcsSounds"];
                    else
                        pModule = part.Modules[type];

                    // clear all FloatCurves
                    Type mType = pModule.GetType();
                    foreach (FieldInfo field in mType.GetFields())
                    {
                        if (field.FieldType == typeof(FloatCurve) && (field.Name.Equals("atmosphereCurve") || field.Name.Equals("velocityCurve")))
                        {
                            //print("*MFS* resetting curve " + field.Name);
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
                                    print("*MFS* Trying to remove info box: " + e.Message);
                                }
                            }
                            boxes.Clear();
                        }
                    }
                }
				if(type.Equals ("ModuleRCS") || type.Equals("RcsSounds")) {
					ModuleRCS rcs = (ModuleRCS) pModule;
                    
                    string resource = config.GetValue("resourceName");
                    if (rcs != null)
                    {
                        rcs.resourceName = resource;
                    }
                    DoConfig(config);
                    if (rcs != null)
                    {
                        rcs.SetResource(resource);
                        pModule.Load(config);
                        rcs.resourceName = resource;
                        rcs.SetResource(resource);
                    }
				} else { // is an ENGINE
                    if (type.Equals("ModuleEngines"))
                    {
                        ModuleEngines mE = (ModuleEngines)pModule;
                        if (mE != null)
                        {
                            configMaxThrust = mE.maxThrust;
                            configMinThrust = mE.minThrust;
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
                                    ignitions = techLevel - ignitions;
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
			}
		}

        //called by StretchyTanks StretchySRB
        virtual public void ChangeThrust(float newThrust)
        {
            //print("*MFS* For " + part.name + ", Setting new max thrust " + newThrust.ToString());
            foreach(ConfigNode c in configs)
            {
                c.SetValue("maxThrust", newThrust.ToString());
            }
            SetConfiguration(configuration);
            //print("New max thrust: " + ((ModuleEngines)part.Modules["ModuleEngines"]).maxThrust);
        }

		public override void OnStart (StartState state)
		{
			if(configs.Count == 0 && part.partInfo != null
			   && part.partInfo.partPrefab.Modules.Contains ("ModuleEngineConfigs")) {
				ModuleEngineConfigs prefab = (ModuleEngineConfigs) part.partInfo.partPrefab.Modules["ModuleEngineConfigs"];
				configs = prefab.configs;
			}
			SetConfiguration (configuration);
            if (part.Modules.Contains("ModuleEngineIgnitor"))
                part.Modules["ModuleEngineIgnitor"].OnStart(state);
		}

        public override void OnInitialize()
        {
            SetConfiguration(configuration);
        }

		public void FixedUpdate ()
		{
			if (vessel == null)
				return;
			SetThrust ();
		}

		public void SetThrust()
		{
            if (!localCorrectThrust)
				return;
            if (type.Equals("ModuleEngines"))
            {
                ModuleEngines engine = (ModuleEngines)part.Modules["ModuleEngines"];
                if (config != null)
                {
                    bool throttleCut = vessel != null && vessel.ctrlState.mainThrottle <= 0;
                    if (engine.realIsp > 0)
                    {
                        float multiplier = (engine.realIsp / Mathf.Lerp(ispSLMult, ispVMult, (float)part.vessel.staticPressure)) / (engine.atmosphereCurve.Evaluate(0) / ispVMult);
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
            else if (type.Equals("ModuleRCS") || type.Equals("RcsSounds"))
            {
                ModuleRCS engine = (ModuleRCS)pModule;

                if (config != null && engine != null && engine.realISP > 0)
                {
                    float multiplier = engine.realISP / engine.atmosphereCurve.Evaluate(0);
                    engine.thrusterPower = configMaxThrust * multiplier;
                }
            }
		}

		private void engineManagerGUI(int WindowID)
		{
			foreach (ConfigNode node in configs) {
				GUILayout.BeginHorizontal();
				if(node.GetValue ("name").Equals (configuration))
					GUILayout.Label ("Current configuration: " + configuration);
				else if(GUILayout.Button ("Configure to " + node.GetValue ("name"))) {
					SetConfiguration(node.GetValue ("name"));
					UpdateSymmetryCounterparts();
				}
				GUILayout.EndHorizontal ();
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
                    //if (techLevel > origTechLevel)
                    //{
                    techLevel--;
                    SetConfiguration(configuration);
                    UpdateSymmetryCounterparts();
                    //}
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
                    //if (techLevel < TechLevel.MaxTL(config, techNodes, engineType))
                    //{
                    techLevel++;
                    SetConfiguration(configuration);
                    UpdateSymmetryCounterparts();
                    //}
                }
                GUILayout.EndHorizontal();
            }

			GUILayout.BeginHorizontal();
            GUILayout.Label(pModule.GetInfo() + "\n" + TLTInfo()); //part.Modules[type].GetInfo()
			GUILayout.EndHorizontal ();
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

