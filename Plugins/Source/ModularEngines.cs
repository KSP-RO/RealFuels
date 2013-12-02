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
			foreach (ModuleEngines.Propellant propellant in ActiveEngine.propellants) {
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
				foreach (ModuleEngines.Propellant propellant in propellants) {
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
		
		List<ModuleEngines.Propellant> _props; 
		List<ModuleEngines.Propellant> propellants 
		{
			get {
				if(_props == null)
					_props = new List<ModuleEngines.Propellant>();
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

        public float ispMultSL, ispMult;
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
			foreach (ModuleEngines.Propellant propellant in ActiveEngine.propellants) {
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
				foreach (ModuleEngines.Propellant propellant in propellants) {
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

		List<ModuleEngines.Propellant> _props; 
		List<ModuleEngines.Propellant> propellants 
		{
			get {
				if(_props == null)
					_props = new List<ModuleEngines.Propellant>();
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

        private const float THRUSTMULT = 1.00f; // TL thrust mod. Thrust *= this each TL increase
        // now done by mass change.
        private static int MAXTL = 0;
        public int maxTechLevel = -1;

        public string engineType = "L"; // default = lower stage

        public static Dictionary<string, List<FloatCurve>> TLTIsps;
        public static Dictionary<string, List<float>> TLTTWRs;

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

        private static void FillTechLevels()
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

            int max = -1;
            ConfigNode node = MFSSettings.GetNode("MFS_TECHLEVELS");
            foreach(ConfigNode n in node.nodes) // like ENGINETYPE[G+]
            {
                int num = 0;
                List<FloatCurve> ispList = new List<FloatCurve>();
                List<float> twrList = new List<float>();
                string typeName = n.GetValue("name");
                foreach(ConfigNode c in n.nodes) // like TL0
                {
                    FloatCurve isp = new FloatCurve();
                    isp.Load(c);
                    ispList.Add(isp);
                    float twr = 60;
                    float.TryParse(n.GetValue("TLTWR"+num), out twr);
                    twrList.Add(twr);
                    print("Added for type " + typeName + ": " + c.name + ": " + isp.Evaluate(1) + "-" + isp.Evaluate(0) + ", TWR " + twr + "\n");
                    num++;
                }
                if (max == -1 || num < max)
                    max = num;
                TLTIsps.Add(typeName, ispList);
                TLTTWRs.Add(typeName, twrList);
            }
            MAXTL = (max - 1);
        }
		
		public override void OnAwake ()
		{
			if(configs == null)
				configs = new List<ConfigNode>();
            if(TLTIsps == null)
            {
                foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("MFSSETTINGS"))
                    MFSSettings = node;
                if(MFSSettings == null)
                    throw new UnityException("*MFS* MFSSettings not found!");

                TLTIsps = new Dictionary<string, List<FloatCurve>>();
                TLTTWRs = new Dictionary<string, List<float>>();
                FillTechLevels();
            }
		}

        private float ThrustTL(float thrust)
        {
            if (techLevel != -1 && !engineType.Contains("S"))
                return thrust * TLTTWRs[engineType][techLevel] / TLTTWRs[engineType][origTechLevel] / TLTIsps[engineType][techLevel].Evaluate(0) * TLTIsps[engineType][origTechLevel].Evaluate(0);
            return thrust;
        }

        private float ThrustTL(string thrust)
        {
            float tmp = 1.0f;
            float.TryParse(thrust, out tmp);
            return ThrustTL(tmp);
        }

        private string TLTInfo()
        {
            string retStr = "";
            if(techLevel != -1)
            {
                retStr =  "Type: " + engineType + ". Tech Level: " + techLevel + " (" + origTechLevel + "-" + maxTechLevel + ")";
                if (origMass > 0)
                    retStr += ", Mass: " + part.mass.ToString("N3") + " (was " + (origMass * massMult).ToString("N3") + ")";
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
			foreach (ConfigNode config in configs) {
				if(!config.GetValue ("name").Equals (configuration)) {
					info += "   " + config.GetValue ("name") + "\n";
					if(config.HasValue (thrustRating))
						info += "    (" + ThrustTL(config.GetValue (thrustRating)).ToString("0.00") + " Thrust";
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
                        float ispSL, ispV;
                        float.TryParse(config.GetValue("IspSL"), out ispSL);
                        float.TryParse(config.GetValue("IspV"), out ispV);
                        ispSL *= TLTIsps[engineType][techLevel].Evaluate(1);
                        ispV *= TLTIsps[engineType][techLevel].Evaluate(0);
                        info += ", " + ispSL.ToString("0") + "-" + ispV.ToString("0") + "Isp";
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
            if(node.HasValue("origTechLevel"))
               int.TryParse(node.GetValue("origTechLevel"), out origTechLevel);
            if (node.HasValue("maxTechLevel"))
                int.TryParse(node.GetValue("maxTechLevel"), out maxTechLevel);
            else
                { if (techLevel != -1) { maxTechLevel = MAXTL; } }
            if (node.HasValue("origMass"))
            {
                float.TryParse(node.GetValue("origMass"), out origMass);
                part.mass = origMass * massMult;
            }
            else
                origMass = -1;
            
            if (node.HasValue("engineType"))
                engineType = node.GetValue("engineType");

            if (node.HasValue("localCorrectThrust"))
                bool.TryParse(node.GetValue("localCorrectThrust"), out localCorrectThrust);

			if (configs == null)
				configs = new List<ConfigNode> ();
			else
				configs.Clear ();
			
			foreach (ConfigNode subNode in node.GetNodes ("CONFIG")) {
				ConfigNode newNode = new ConfigNode("CONFIG");
				subNode.CopyTo (newNode);
				configs.Add (newNode);
			}
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

        virtual public void DoConfig(ConfigNode Config)
        {
            // fix propellant ratios to not be rounded
            foreach (ConfigNode pNode in config.GetNodes("PROPELLANT"))
            {
                if (pNode.HasValue("ratio"))
                {
                    double dtmp;
                    if (double.TryParse(pNode.GetValue("ratio"), out dtmp))
                        pNode.SetValue("ratio", (dtmp * 100.0).ToString());
                }
            }

            if (config.HasValue("heatProduction")) // ohai amsi
                config.SetValue("heatProduction", ((float)Math.Round(float.Parse(config.GetValue("heatProduction")) * heatMult,0)).ToString());
            if (techLevel != -1)
            {
                if (config.HasValue("IspSL") && config.HasValue("IspV"))
                {
                    config.RemoveNode("atmosphereCurve");
                    ConfigNode curve = new ConfigNode("atmosphereCurve");
                    float ispSL, ispV;
                    float.TryParse(config.GetValue("IspSL"), out ispSL);
                    float.TryParse(config.GetValue("IspV"), out ispV);
                    ispSL *= ispSLMult * TLTIsps[engineType][techLevel].Evaluate(1);
                    ispV *= ispVMult * TLTIsps[engineType][techLevel].Evaluate(0);
                    curve.AddValue("key", "0 " + ispV.ToString("0"));
                    curve.AddValue("key", "1 " + ispSL.ToString("0"));
                    config.AddNode(curve);
                }
                if (config.HasValue("maxThrust"))
                {
                    float thr;
                    float.TryParse(config.GetValue("maxThrust"), out thr);
                    float newThr = ThrustTL(thr);
                    config.SetValue("maxThrust", newThr.ToString("0.00"));
                    // mass change
                    if (origMass > 0)
                    {
                        if (!engineType.Contains("S"))
                            part.mass = (float)Math.Round(origMass / TLTIsps[engineType][techLevel].Evaluate(0) * TLTIsps[engineType][origTechLevel].Evaluate(0) * massMult, 3);
                        else
                            part.mass = (float)Math.Round(origMass * TLTTWRs[engineType][origTechLevel] / TLTTWRs[engineType][techLevel], 3);
                    }
                }
            }
        }

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

				// clear all FloatCurves
				foreach(FieldInfo field in part.Modules[type].GetType().GetFields()) { 
					if(field.FieldType == typeof(FloatCurve))
						field.SetValue (part.Modules[type], new FloatCurve());
				}
				if(type.Equals ("ModuleRCS")) {
					ModuleRCS rcs = (ModuleRCS) part.Modules["ModuleRCS"];
					string resource = config.GetValue ("resourceName");
					rcs.Load (config);
					rcs.SetResource (resource);
				} else { // is an ENGINE

                    DoConfig(config);
					part.Modules[type].Load (config);
					part.Modules[type].OnStart (StartState.None);
                    //TODO: CONFIG AS TITLE
				}
                /*
                print("*MFS* part " + part.name + " has effects: ");
                
                foreach (FXGroup fxg in part.fxGroups)
                {
                    if(fxg.name.Equals("running"))
                    {
                        for(int i = fxg.fxEmitters.Count - 1; i >= 0; i--)
                        {
                            GameObject e = fxg.fxEmitters[i];
                            if(e.name.Equals("fx_exhaustFlame_blue") || e.name.Equals("fx_exhaustFlame_blue_small")
                                || e.name.Equals("fx_exhaustFlame_white_tiny") || e.name.Equals("fx_exhaustFlame_yellow")
                                || e.name.Equals("fx_exhaustFlame_yellow_small") || e.name.Equals("fx_exhaustFlame_yellow_tiny")
                                || e.name.Equals("fx_exhaustLight_blue") || e.name.Equals("fx_exhaustLight_yellow")
                                || e.name.Equals("fx_gasJet_white") || e.name.Equals("fx_gasJet_tiny"))
                            {
                                fxg.fxEmitters.RemoveAt(i);
                            }
                            
                        }
                    }
                    
                    //add new
                }
                //print(tmp.ToString());
                */
			}
		}

        //called by StretchyTanks StretchySRB
        virtual public void ChangeThrust(float newThrust)
        {
            //print("*MFS* Setting new max thrust " + newThrust.ToString());
            foreach(ConfigNode c in configs)
            {
                c.SetValue("maxThrust", newThrust.ToString());
            }
            SetConfiguration(configuration);
        }

		public override void OnStart (StartState state)
		{
			if(configs.Count == 0 && part.partInfo != null 
			   && part.partInfo.partPrefab.Modules.Contains ("ModuleEngineConfigs")) {
				ModuleEngineConfigs prefab = (ModuleEngineConfigs) part.partInfo.partPrefab.Modules["ModuleEngineConfigs"];
				configs = prefab.configs;
			}
			SetConfiguration (configuration);

		}

		public void FixedUpdate ()
		{
			if (!type.Equals ("ModuleEngines"))
				return;
			if (vessel == null || part.Modules["ModuleEngines"] == null)
				return;
			SetThrust ();
		}

		public void SetThrust()
		{
            if (!type.Equals("ModuleEngines") || !correctThrust || !localCorrectThrust)
				return;
			ModuleEngines engine = (ModuleEngines) part.Modules["ModuleEngines"];
			ConfigNode config = configs.Find (c => c.GetValue ("name").Equals (configuration));			
			if (config != null && engine.realIsp > 0) {
				float maxThrust = 0;
				float.TryParse (config.GetValue ("maxThrust"), out maxThrust);
                maxThrust *= engine.realIsp / engine.atmosphereCurve.Evaluate(0); // NK scale from max, not min, thrust.
                maxThrust = ThrustTL(maxThrust);
				engine.maxThrust = maxThrust;
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
            GUILayout.BeginHorizontal();
            GUILayout.Label("Tech Level: ");
            if(GUILayout.Button("-"))
            {
                if (techLevel > origTechLevel)
                {
                    techLevel--;
                    SetConfiguration(configuration);
                    UpdateSymmetryCounterparts();
                }
            }
            GUILayout.Label(techLevel.ToString());
            if(GUILayout.Button("+"))
            {
                if (techLevel < maxTechLevel)
                {
                    techLevel++;
                    SetConfiguration(configuration);
                    UpdateSymmetryCounterparts();
                }

            }
            GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal();
            GUILayout.Label(part.Modules[type].GetInfo() + "\n" + TLTInfo());
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

