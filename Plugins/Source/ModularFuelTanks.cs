//#define DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP;

namespace ModularFuelTanks
{
	public class ModuleFuelTanks : PartModule
	{
		public static float massMult = 1.0f;
		public static ConfigNode MFSSettings = null;
		private static bool initialized = false;
		public static Dictionary<string, ConfigNode> stageDefinitions;	// configuration for all parts of this type

		// A FuelTank is a single TANK {} entry from the part.cfg file.
		// it defines four properties:
		// name = the name of the resource that can be stored
		// utilization = how much of the tank is devoted to that resource (vs. how much is wasted in cryogenics or pumps)
		// mass = how much the part's mass is increased per volume unit of tank installed for this resource type
		// loss_rate = how quickly this resource type bleeds out of the tank

		public class FuelTank: IConfigNode
		{
			//------------------- fields
			public string name = "UnknownFuel";
			public string note = "";
			public float utilization = 1.0f;
			public float mass = 0.0f;
			public double loss_rate = 0.0;
			public float temperature = 300.0f;
			public bool fillable = true;

			[System.NonSerialized]
			public ModuleFuelTanks module;

			//------------------- virtual properties
			public int id
			{
				get {
					if(name == null)
						return 0;
					return name.GetHashCode ();
				}
			}

			public Part part
			{
				get {
					if(module == null)
						return null;
					return module.part;
				}
			}

			public PartResource resource
			{
				get {
					if (part == null)
						return null;
					return part.Resources[name];
				}
			}

			public double amount {
				get {
					if (resource == null)
						return 0.0;
					else
						return resource.amount;
				}
				set {
					double newAmount = value;
					if(newAmount > maxAmount)
						newAmount = maxAmount;

					if(resource != null && newAmount >= 0)
						resource.amount = newAmount;
				}
			}

			public double maxAmount {
				get {
					if (resource == null)
						return 0.0f;
					return resource.maxAmount;
				}

				set {
					double newMaxAmount = value;
					if (resource != null && newMaxAmount <= 0.0) {
						amount = 0.0;
						resource.amount = 0.0;
						resource.maxAmount = 0.0;
						PartResource res = resource;
						part.Resources.list.Remove(res);
						PartResource[] allR = part.GetComponents<PartResource>();
						foreach (PartResource r in allR)
							if (r.resourceName.Equals(name))
								DestroyImmediate(r);
						part.Resources.UpdateList();
					} else if (resource != null) {
						double maxQty = module.availableVolume * utilization + maxAmount;
						if (maxQty < newMaxAmount)
							newMaxAmount = maxQty;

						resource.maxAmount = newMaxAmount;
						if(amount > newMaxAmount)
							amount = newMaxAmount;
					} else if(newMaxAmount > 0.0) {
						ConfigNode node = new ConfigNode("RESOURCE");
						node.AddValue ("name", name);
						node.AddValue ("amount", newMaxAmount);
						node.AddValue ("maxAmount", newMaxAmount);
#if DEBUG
						print (node.ToString ());
#endif
						part.AddResource (node);
						resource.enabled = true;
					}
					// update mass here because C# is annoying.
					if (module.basemass >= 0) {
						module.basemass = module.basemassPV * module.volume;
						part.mass = module.basemass * massMult + module.tank_mass; // NK for realistic mass
					}
				}
			}

			//------------------- implicit type conversions
			public static implicit operator bool(FuelTank f)
			{
				return (f != null);
			}

			public static implicit operator string(FuelTank f)
			{
				return f.name;
			}

			public override string ToString ()
			{
				if (name == null)
					return "NULL";
				return name;
			}

			//------------------- IConfigNode implementation
			public void Load(ConfigNode node)
			{
				if (node.name.Equals ("TANK") && node.HasValue ("name")) {
					name = node.GetValue ("name");
					if(node.HasValue ("note"))
						note = node.GetValue ("note");
					if (node.HasValue("fillable"))
						bool.TryParse(node.GetValue("fillable"), out fillable);
					if(node.HasValue ("utilization"))
						float.TryParse (node.GetValue("utilization"), out utilization);
					else if(node.HasValue ("efficiency"))
						float.TryParse (node.GetValue("efficiency"), out utilization);
					if(node.HasValue ("temperature"))
						float.TryParse (node.GetValue("temperature"), out temperature);
					if(node.HasValue ("loss_rate"))
						double.TryParse (node.GetValue("loss_rate"), out loss_rate);
					if(node.HasValue ("mass"))
						float.TryParse (node.GetValue("mass"), out mass);
					if(node.HasValue ("maxAmount")) {
						double v;
						if(node.GetValue ("maxAmount").Contains ("%")) {
							double.TryParse(node.GetValue("maxAmount").Replace("%", "").Trim(), out v);
							maxAmount = v * utilization * module.volume * 0.01; // NK
						} else {
							double.TryParse(node.GetValue ("maxAmount"), out v);
							maxAmount = v;
						}
						if(node.HasValue ("amount")) {
                            string amt = node.GetValue("amount").Trim().ToLower();
							if(amt.Equals("full"))
								amount = maxAmount;
                            else if (amt.Contains("%"))
                            {
                                double.TryParse(amt.Replace("%", "").Trim(), out v);
                                amount = v * maxAmount * 0.01;
                            }
                            else
                            {
                                double.TryParse(node.GetValue("amount"), out v);
                                amount = v;
                            }
						} else {
							amount = 0;
						}
					} else {
						maxAmount = 0;
						amount = 0;
					}
				}
			}

			public void Save(ConfigNode node)
			{
				if (name != null) {
					node.AddValue ("name", name);
					node.AddValue ("utilization", utilization);
					node.AddValue ("mass", mass);
					node.AddValue ("temperature", temperature);
					node.AddValue ("loss_rate", loss_rate);
					node.AddValue ("fillable", fillable);
					node.AddValue ("note", note);

					// You would think we only want to do this in the editor, but
					// as it turns out, KSP is terrible about consistently setting
					// up resources between the editor and the launchpad.
					node.AddValue ("amount", amount);
					node.AddValue ("maxAmount", maxAmount);
				}
			}

			//------------------- Constructor
			public FuelTank()
			{
			}
		}

		public static string GetSetting(string setting, string dflt)
		{
			if (MFSSettings == null) {
				foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("MFSSETTINGS"))
					MFSSettings = n;
			}
			if (MFSSettings != null && MFSSettings.HasValue(setting)) {
				return MFSSettings.GetValue(setting);
			}
			return dflt;
		}

		private void InitMFS()
		{
			bool usereal = false;
			bool.TryParse(GetSetting("useRealisticMass", "false"), out usereal);
			if (!usereal)
				massMult = float.Parse(GetSetting("tankMassMultiplier", "1.0"));
			else
				massMult = 1.0f;

			initialized = true;

			stageDefinitions = new Dictionary<string, ConfigNode>();
		}


		//------------- this is all my non-KSP stuff

		public float usedVolume {
			get {
				double v = 0;
				foreach (FuelTank fuel in fuelList) {
					if(fuel.maxAmount > 0 && fuel.utilization > 0)
						v += fuel.maxAmount / fuel.utilization;
				}
				return (float) v;
			}
		}

		public float availableVolume {
			get {
				return volume - usedVolume;
			}
		}
		public float tank_massPV = 0.0f;

		public float tank_mass {
			get {
				float m = 0.0f;
				foreach (FuelTank fuel in fuelList) {
#if DEBUG2
					print(String.Format("{0} {1} {2} {3}", fuel.maxAmount, fuel.utilization, fuel.mass, massMult));
#endif
					if(fuel.maxAmount > 0 && fuel.utilization > 0)
						m += (float) fuel.maxAmount * fuel.mass / fuel.utilization * massMult; // NK for realistic masses
				}
				tank_massPV = m / volume;
				return m;
			}
		}

		//------------------- this is all KSP stuff

		[KSPField(isPersistant = true)]
		public double timestamp = 0.0;

		[KSPField(isPersistant = true)]
		public float basemass = 0.0f;

		[KSPField(isPersistant = true)]
		public float basemassPV = 0.0f;

		[KSPField(isPersistant = true)]
		public float volume = 0.0f;

		public ConfigNode stage;		// configuration for this part (instance)
		public List<FuelTank> fuelList;
        // for EngineIgnitor integration: store a public dictionary of all pressurized propellants
        public Dictionary<string, bool> pressurizedFuels;

		public static bool ResourceExists (string name)
		{
			return PartResourceLibrary.Instance.GetDefinition (name) != null;
		}

		public static ConfigNode CheckTankResources (ConfigNode tankdef)
		{
			foreach (var tank in tankdef.GetNodes ("TANK")) {
				if (!ResourceExists (tank.GetValue ("name"))) {
					tankdef.nodes.Remove (tank);
				}
			}
			return tankdef;
		}

		public static ConfigNode TankDefinition(string name)
		{
			foreach (ConfigNode tank in GameDatabase.Instance.GetConfigNodes ("TANK_DEFINITION")) {
				if(tank.HasValue ("name") && tank.GetValue ("name").Equals (name))
					return CheckTankResources (tank);
			}
			return null;
		}

		private void CopyConfigValue(ConfigNode src, ConfigNode dst, string key)
		{
			if (src.HasValue(key)) {
				if(dst.HasValue(key))
					dst.SetValue(key, src.GetValue(key));
				else
					dst.AddValue(key, src.GetValue(key));
			}
		}

		public override void OnInitialize()
		{
#if DEBUG
			print("========ModuleFuelTanks.OnInitialize=======" + (part.vessel != null ? " for " + part.vessel.name : ""));
#endif
			if (fuelList == null || fuelList.Count == 0) {
				fuelList = new List<FuelTank>();

				if (stage == null) {	// OnLoad does not get called in the VAB or SPH
#if DEBUG
					print("copying from stageDefinitions");
#endif
					string part_name = part.name;
					if (part_name.Contains("_"))
						part_name = part_name.Remove(part_name.LastIndexOf("_"));
					if (part_name.Contains("(Clone)"))
						part_name = part_name.Remove(part_name.LastIndexOf("(Clone)"));

					stage = new ConfigNode();
					stageDefinitions[part.name].CopyTo(stage);
				}
				foreach (ConfigNode tankNode in stage.GetNodes("TANK")) {
#if DEBUG
					print("loading FuelTank from node " + tankNode.ToString());
#endif
					FuelTank tank = new FuelTank();
					tank.module = this;
					tank.Load(tankNode);
					fuelList.Add(tank);
				}
                // for EngineIgnitor integration: store a public dictionary of all pressurized propellants
                pressurizedFuels = new Dictionary<string, bool>();
                foreach(FuelTank f in fuelList)
                    pressurizedFuels[f.name] = stage.name.Equals("ServiceModule") || f.note.ToLower().Contains("pressurized");
#if DEBUG
				print("ModuleFuelTanks.onLoad loaded " + fuelList.Count + " fuels");
#endif
			}
		}

		public override void OnLoad(ConfigNode node)
		{
#if DEBUG
			print ("========ModuleFuelTanks.OnLoad called. Node is:=======");
			print (part.name);
#endif
			if (!initialized)
				InitMFS ();

			string part_name = part.name;
			if (part_name.Contains("_"))
				part_name = part_name.Remove(part_name.LastIndexOf("_"));

			stage = new ConfigNode ();

			bool needInitialize = false;
			// Only the part config nodes "type", so missing "type" implies a persistence file or saved craft
			// "volume" is required for part config nodes, but optional for the others
			if (node.HasValue ("type") && node.HasValue ("volume")) {
				string tank_type = node.GetValue ("type");
				ConfigNode tankDef = TankDefinition (tank_type);
				if (tankDef != null)
					tankDef.CopyTo (stage);
				CopyConfigValue (node, stage, "volume");
				CopyConfigValue (node, stage, "basemass");

				stageDefinitions[part_name] = stage;
				needInitialize = true;
			} else {
				stageDefinitions[part_name].CopyTo (stage);
			}
#if DEBUG
			print (stage);
#endif
			// Override tank definitions
			foreach (var tank in node.GetNodes("TANK")) {
				string tank_name = tank.GetValue("name");
				// don't allow tanks for resources that don't exist, unless this is from a saved game.
				if (needInitialize && !ResourceExists (tank_name)) {
					print (String.Format("dropping {0}", tank_name));
					continue;
				}
				ConfigNode stageTank = stage.GetNodes("TANK").FirstOrDefault(p => p.GetValue("name") == tank_name);
				if (stageTank == null) {
					stageTank = stage.AddNode("TANK");
				}
				CopyConfigValue(tank, stageTank, "name");
				CopyConfigValue(tank, stageTank, "fillable");
				CopyConfigValue(tank, stageTank, "utilization");
				CopyConfigValue(tank, stageTank, "mass");
				CopyConfigValue(tank, stageTank, "temperature");
				CopyConfigValue(tank, stageTank, "loss_rate");
				CopyConfigValue(tank, stageTank, "amount");
				CopyConfigValue(tank, stageTank, "maxAmount");
				CopyConfigValue(tank, stageTank, "note");
			}

			// NK use custom basemass
			if (stage.HasValue("basemass")) {
				string base_mass = stage.GetValue("basemass");
#if DEBUG
				print (String.Format("basemass: {0} {1}", basemass, base_mass));
#endif
				if (base_mass.Contains("*") && base_mass.Contains("volume")) {
					float.TryParse(base_mass.Replace("volume", "").Replace("*", "").Trim(), out basemass);
					basemassPV = basemass;
					basemass = basemass * volume;
				} else {
					// NK allow static basemass
					float.TryParse(base_mass.Trim(), out basemass);
					basemassPV = basemass / volume;
				}
			}

			if (needInitialize) {
				OnInitialize ();
				UpdateMass ();
			}
#if DEBUG
			print ("ModuleFuelTanks loaded. ");
#endif
		}

		public override void OnSave (ConfigNode node)
		{
#if DEBUG
			print ("========ModuleFuelTanks.OnSave called. Node is:=======");
			print (node.ToString ());
#endif
			if (fuelList == null)
				fuelList = new List<FuelTank> ();
			foreach (FuelTank tank in fuelList) {
				ConfigNode subNode = new ConfigNode("TANK");
				tank.Save (subNode);
#if DEBUG
				print ("========ModuleFuelTanks.OnSave adding subNode:========");
				print (subNode.ToString());
#endif
				node.AddNode (subNode);
				tank.module = this;
			}
		}

		private void UsePrefab()
		{
			Part prefab = null;
			prefab = part.symmetryCounterparts.Find(pf => pf.Modules.Contains("ModuleFuelTanks")
														  && ((ModuleFuelTanks)pf.Modules["ModuleFuelTanks"]).fuelList != null
														  && ((ModuleFuelTanks)pf.Modules["ModuleFuelTanks"]).fuelList.Count > 0);
#if DEBUG
			print ("ModuleFuelTanks.OnStart: copying from a symmetryCounterpart with a ModuleFuelTanks PartModule");
#endif
			ModuleFuelTanks pModule = (ModuleFuelTanks) prefab.Modules["ModuleFuelTanks"];
			if(pModule == this) {
				print ("ModuleFuelTanks.OnStart: Copying from myself won't do any good.");
			} else {
				ConfigNode node = new ConfigNode("MODULE");
				pModule.OnSave (node);
#if DEBUG
				print ("ModuleFuelTanks.OnStart node from prefab:" + node);
#endif
				OnLoad (node);
			}
		}

		private void ResourcesModified (Part part)
		{
			BaseEventData data = new BaseEventData (BaseEventData.Sender.USER);
			data.Set<Part> ("part", part);
			part.SendEvent ("OnResourcesModified", data, 0);
		}

		private void MassModified (Part part, float oldmass)
		{
			BaseEventData data = new BaseEventData (BaseEventData.Sender.USER);
			data.Set<Part> ("part", part);
			data.Set<float> ("oldmass", oldmass);
			part.SendEvent ("OnMassModified", data, 0);
		}

		public override void OnStart (StartState state)
		{
#if DEBUG
			print ("========ModuleFuelTanks.OnStart( State == " + state.ToString () + ")=======");
#endif

			if (basemass == 0 && part != null)
				basemass = part.mass;
			if(fuelList == null || fuelList.Count == 0) {
				// In the editor, OnInitialize doesn't get called for the root part (KSP bug?)
				// First check if it's a counterpart.
				if(HighLogic.LoadedSceneIsEditor
				   && part.symmetryCounterparts.Count > 0) {
					UsePrefab();
				} else {
					if(fuelList != null)
						foreach (FuelTank tank in fuelList)
							tank.module = this;

					OnInitialize();
				}
			}
			UpdateMass();

			ResourcesModified (part);

			if(HighLogic.LoadedSceneIsEditor) {
				UpdateSymmetryCounterparts();
					// if we detach and then re-attach a configured tank with symmetry on, make sure the copies are configured.
			}
		}

		public void CheckSymmetry()
		{
#if DEBUG
			print ("ModuleFuelTanks.CheckSymmetry for " + part.partInfo.name);
#endif
			EditorLogic editor = EditorLogic.fetch;
			if (editor != null && editor.editorScreen == EditorLogic.EditorScreen.Parts && part.symmetryCounterparts.Count > 0) {
#if DEBUG
				print ("ModuleFuelTanks.CheckSymmetry: updating " + part.symmetryCounterparts.Count + " other parts.");
#endif
				UpdateSymmetryCounterparts();
			}
#if DEBUG
			print ("ModuleFuelTanks checked symmetry");
#endif
		}

		public override void OnUpdate ()
		{
			if (HighLogic.LoadedSceneIsEditor) {
				return;
			}
			if (timestamp > 0) {
				double delta_t = Planetarium.GetUniversalTime () - timestamp;
				foreach (FuelTank tank in fuelList) {
					if (tank.amount > 0 && tank.loss_rate > 0 && part.temperature > tank.temperature) {
						double loss = tank.maxAmount * tank.loss_rate * (part.temperature - tank.temperature) * delta_t; // loss_rate is calibrated to 300 degrees.
						if (loss > tank.amount)
							tank.amount = 0;
						else
							tank.amount -= loss;
					}
				}
			}
			timestamp = Planetarium.GetUniversalTime ();
		}

		public override string GetInfo ()
		{
			string info = "Modular Fuel Tank: \n"
				+ "  Max Volume: " + volume.ToString () + "\n"
					+ "  Tank can hold:";
			foreach(FuelTank tank in fuelList) {
				info += "\n   " + tank + " " + tank.note;
			}
			return info + "\n";
		}

		// looks to see if we should ignore this fuel when creating an autofill for an engine
		public bool IgnoreFuel(string name)
		{
			ConfigNode fNode = MFSSettings.GetNode("IgnoreFuelsForFill");
			if (fNode != null) {
				foreach (ConfigNode.Value v in fNode.values)
					if (v.name.Equals(name))
						return true;
			}
			return false;
		}

		public static string myToolTip = "";
		int counterTT = 0;
		public void OnGUI()
		{
			EditorLogic editor = EditorLogic.fetch;
			if (!HighLogic.LoadedSceneIsEditor || !editor || editor.editorScreen != EditorLogic.EditorScreen.Actions) {
				return;
			}

			if (EditorActionGroups.Instance.GetSelectedParts ().Contains (part)) {
				//Rect screenRect = new Rect(0, 365, 430, (Screen.height - 365));
				Rect screenRect = new Rect(0, 365, 438, (Screen.height - 365));
				//Color reset = GUI.backgroundColor;
				//GUI.backgroundColor = Color.clear;
				GUILayout.Window (part.name.GetHashCode (), screenRect, fuelManagerGUI, "Fuel Tanks for " + part.partInfo.title);
				//GUI.backgroundColor = reset;

				//if(!(myToolTip.Equals("")))
				GUI.Label(new Rect(440, Screen.height - Input.mousePosition.y, 300, 20), myToolTip);
			}
		}

		static GUIStyle unchanged = null;
		static GUIStyle changed = null;
		static GUIStyle greyed = null;
		static GUIStyle overfull = null;

		Vector2 scrollPos;
		private List<string> textFields;
		struct FuelInfo
		{
			public string names;
			public ModuleEngines thruster;
			public double efficiency;
			public double ratio_factor;
		}
		private void fuelManagerGUI(int WindowID)
		{
			if (unchanged == null) {
				unchanged = new GUIStyle(GUI.skin.textField);
				unchanged.normal.textColor = Color.white;
				unchanged.active.textColor = Color.white;
				unchanged.focused.textColor = Color.white;
				unchanged.hover.textColor = Color.white;

				changed = new GUIStyle(GUI.skin.textField);
				changed.normal.textColor = Color.yellow;
				changed.active.textColor = Color.yellow;
				changed.focused.textColor = Color.yellow;
				changed.hover.textColor = Color.yellow;

				greyed = new GUIStyle(GUI.skin.textField);
				greyed.normal.textColor = Color.gray;

				overfull = new GUIStyle(GUI.skin.label);
				overfull.normal.textColor = Color.red;
			}

			GUILayout.BeginVertical ();

			GUILayout.BeginHorizontal();
			GUILayout.Label ("Current mass: " + Math.Round(part.mass + part.GetResourceMass(),4) + " Ton(s)");
			GUILayout.Label("Dry mass: " + Math.Round(part.mass,4) + " Ton(s)");
			GUILayout.EndHorizontal ();

			if (fuelList.Count == 0) {

				GUILayout.BeginHorizontal();
				GUILayout.Label ("This fuel tank cannot hold resources.");
				GUILayout.EndHorizontal ();
				return;
			}

			GUILayout.BeginHorizontal();
			if (availableVolume < 0) {
				GUILayout.Label ("Available volume: " + availableVolume + " / " + volume, overfull);
			} else {
				GUILayout.Label ("Available volume: " + availableVolume + " / " + volume);
			}
			GUILayout.EndHorizontal ();

			scrollPos = GUILayout.BeginScrollView(scrollPos);

			int text_field = 0;
			if (textFields == null)
				textFields = new List<string> ();

			foreach (ModuleFuelTanks.FuelTank tank in fuelList) {
				GUILayout.BeginHorizontal();
				int amountField = text_field;
				text_field++;
				if(textFields.Count < text_field) {
					textFields.Add (tank.amount.ToString());
				}
				int maxAmountField = text_field;
				text_field++;
				if(textFields.Count < text_field) {
					textFields.Add (tank.maxAmount.ToString());
				}
				GUILayout.Label(" " + tank, GUILayout.Width (120));
				if(part.Resources.Contains(tank) && part.Resources[tank].maxAmount > 0) {
					GUIStyle style;

					if(tank.fillable) {
						style = unchanged;
						if (textFields[amountField] != tank.amount.ToString()) {
							style = changed;
						}
						textFields[amountField] = GUILayout.TextField(textFields[amountField], style, GUILayout.Width (65));
					} else {
						GUILayout.Label ("None", greyed, GUILayout.Width (65));
					}
					GUILayout.Label("/", GUILayout.Width (5));

					style = unchanged;
					if (textFields[maxAmountField] != tank.maxAmount.ToString()) {
						style = changed;
					}
					textFields[maxAmountField] = GUILayout.TextField(textFields[maxAmountField], style, GUILayout.Width (65));

					GUILayout.Label(" ", GUILayout.Width (5));

					if(GUILayout.Button ("Update", GUILayout.Width (60))) {
						double newMaxAmount = tank.maxAmount;
						double newAmount = tank.amount;

						if (textFields[maxAmountField].Trim() == "") {
							newMaxAmount = 0;
						} else {
							double tmp;
							if(double.TryParse (textFields[maxAmountField], out tmp))
								newMaxAmount = tmp;
						}

						if(!tank.fillable || textFields[amountField].Trim() == "") {
							newAmount = 0;
							print("empty amount");
						} else {
							double tmp;
							if(double.TryParse(textFields[amountField], out tmp))
								newAmount = tmp;
							print("amount " + textFields[amountField] + " " + newAmount.ToString());
						}

						tank.maxAmount = newMaxAmount;
						tank.amount = newAmount;

						textFields[amountField] = tank.amount.ToString();
						textFields[maxAmountField] = tank.maxAmount.ToString();

						ResourcesModified (part);
						if(part.symmetryCounterparts.Count > 0)
							UpdateSymmetryCounterparts();
					}
					if(GUILayout.Button ("Remove", GUILayout.Width (60))) {
						tank.maxAmount = 0;
						textFields[amountField] = "0";
						textFields[maxAmountField] = "0";
						ResourcesModified (part);
						if(part.symmetryCounterparts.Count > 0)
							UpdateSymmetryCounterparts();
					}
				} else if(availableVolume >= 0.001) {
					string extraData = "Max: " + Math.Round(availableVolume * tank.utilization,2) + " (+" + Math.Round(availableVolume * tank.utilization * tank.mass,4) + " tons)" ;

					GUILayout.Label(extraData, GUILayout.Width (150));

					if(GUILayout.Button("Add", GUILayout.Width (130))) {
						tank.maxAmount = availableVolume * tank.utilization;
						if(tank.fillable)
							tank.amount = tank.maxAmount;
						else
							tank.amount = 0;

						textFields[amountField] = tank.amount.ToString();
						textFields[maxAmountField] = tank.maxAmount.ToString();

						ResourcesModified (part);
						if(part.symmetryCounterparts.Count > 0)
							UpdateSymmetryCounterparts();

					}
				} else {
					GUILayout.Label ("  No room for tank.", GUILayout.Width (150));

				}
				GUILayout.EndHorizontal ();

			}

			GUILayout.BeginHorizontal();
			if(GUILayout.Button ("Remove All Tanks")) {
				textFields.Clear ();
				foreach(ModuleFuelTanks.FuelTank tank in fuelList)
					tank.maxAmount = 0;
				ResourcesModified (part);
				if(part.symmetryCounterparts.Count > 0)
					UpdateSymmetryCounterparts();

			}
			GUILayout.EndHorizontal();
			if(GetEnginesFedBy(part).Count > 0 && availableVolume >= 0.001) {
				Dictionary<string, FuelInfo> usedBy = new Dictionary<string, FuelInfo>();

				GUILayout.BeginHorizontal();
				GUILayout.Label ("Configure remaining volume for engines:");
				GUILayout.EndHorizontal();

				foreach (Part engine in GetEnginesFedBy(part)) {
					double ratio_factor = 0.0;
					double efficiency = 0.0;
					ModuleEngines thruster = (ModuleEngines)engine.Modules["ModuleEngines"];

					// tank math:
					// efficiency = sum[utilization * ratio]
					// then final volume per fuel = fuel_ratio / fuel_utilization / efficciency


					foreach (Propellant tfuel in thruster.propellants) {
						if (PartResourceLibrary.Instance.GetDefinition(tfuel.name) == null) {
							print("Unknown RESOURCE {" + tfuel.name + "}");
							ratio_factor = 0.0;
							break;
						} else if (PartResourceLibrary.Instance.GetDefinition(tfuel.name).resourceTransferMode == ResourceTransferMode.NONE) {
							//ignore this propellant, since it isn't serviced by fuel tanks
						} else {
							ModuleFuelTanks.FuelTank tank = fuelList.Find(f => f.ToString().Equals(tfuel.name));
							if (tank) {
								efficiency += tfuel.ratio / tank.utilization;
								ratio_factor += tfuel.ratio;
							} else if(!IgnoreFuel(tfuel.name)) {
								ratio_factor = 0.0;
								break;
							}
						}
					}
					if (ratio_factor > 0.0) {
						string label = "";
						foreach (Propellant tfuel in thruster.propellants) {
							if (PartResourceLibrary.Instance.GetDefinition(tfuel.name).resourceTransferMode != ResourceTransferMode.NONE) {
								if (label.Length > 0)
									label += " / ";
								label += Math.Round(100 * tfuel.ratio / ratio_factor,1).ToString() + "% " + tfuel.name;
							}

						}
						if (!usedBy.ContainsKey(label)) {
							FuelInfo f = new FuelInfo();
							f.names = "Used by: " + thruster.part.partInfo.title;
							f.efficiency = efficiency;
							f.ratio_factor = ratio_factor;
							f.thruster = thruster;
							usedBy.Add(label, f);
						} else {
							if (!usedBy[label].names.Contains(thruster.part.partInfo.title)) {
								FuelInfo f = usedBy[label];
								f.names += ", " + thruster.part.partInfo.title;
								usedBy[label] = f;
							}
						}
					}
				}
				if (usedBy.Count > 0) {
					foreach (string label in usedBy.Keys) {
						GUILayout.BeginHorizontal();
						if (GUILayout.Button(new GUIContent(label, usedBy[label].names))) {
							textFields.Clear();

							double total_volume = availableVolume;
							foreach (Propellant tfuel in usedBy[label].thruster.propellants) {
								if (PartResourceLibrary.Instance.GetDefinition(tfuel.name).resourceTransferMode != ResourceTransferMode.NONE) {
									ModuleFuelTanks.FuelTank tank = fuelList.Find(t => t.name.Equals(tfuel.name));
									if (tank) {
										double amt = total_volume * tfuel.ratio / usedBy[label].efficiency;
										tank.maxAmount += amt;
										tank.amount += amt;
									}
								}
							}
							ResourcesModified (part);
							if (part.symmetryCounterparts.Count > 0)
								UpdateSymmetryCounterparts();
						}
						GUILayout.EndHorizontal();
					}
				}
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical ();
			if(!(myToolTip.Equals("")) && GUI.tooltip.Equals("")) {
				if(counterTT > 4) {
					myToolTip = GUI.tooltip;
					counterTT = 0;
				} else {
					counterTT++;
				}
			} else {
				myToolTip = GUI.tooltip;
				counterTT = 0;
			}
			//print("GT: " + GUI.tooltip);
		}

		public static List<Part> GetEnginesFedBy(Part part)
		{
			Part ppart = part;
			while (ppart.parent != null && ppart.parent != ppart)
				ppart = ppart.parent;

			return new List<Part>(ppart.FindChildParts<Part> (true)).FindAll (p => p.Modules.Contains ("ModuleEngines"));
		}

        //called by StretchyTanks
        public void ChangeVolume(float newVolume)
        {
            //print("*MFS* Setting new volume " + newVolume);
            double volRatio = (double)newVolume / (double)volume;
            double availVol = availableVolume * volRatio;
            if (availVol < 0.0001)
                availVol = 0;
            volume = newVolume;
            List<double> amtratios = new List<double>();
            List<double> maxes = new List<double>();
            double totalAmt = 0;
            double newVol = ((double)newVolume - availVol);
            for (int i = 0; i < fuelList.Count; i++)
            {
                ModuleFuelTanks.FuelTank tank = fuelList[i];
                totalAmt += tank.maxAmount;
                double amtRatio = tank.amount / tank.maxAmount;
                if (amtRatio > 0.9999)
                    amtRatio = 1.0;
                else if (amtRatio < 0.0001)
                    amtRatio = 0.0;
                amtratios.Add(amtRatio);
                maxes.Add(tank.maxAmount);
            }
            double ratio = newVol / totalAmt;
            for (int i = 0; i < fuelList.Count; i++)
            {
                ModuleFuelTanks.FuelTank tank = fuelList[i];
                double newMax = maxes[i] * ratio;
                tank.maxAmount = newMax;
                tank.amount = amtratios[i] * newMax;
            }
            if (textFields != null)
                textFields.Clear();
            UpdateMass();
        }

		public void UpdateMass()
		{
#if DEBUG
			print ("=== MFS: UpdateMass: " + basemass.ToString() + " , " + basemassPV.ToString() + " , " + volume.ToString() + " , " + massMult.ToString() + " , " + tank_mass.ToString());
#endif
			float oldmass = part.mass;
			if (basemass >= 0) {
				basemass = basemassPV * volume;
				part.mass = basemass * massMult + tank_mass; // NK for realistic mass
			}
			MassModified (part, oldmass);
		}

		public int UpdateSymmetryCounterparts()
		{
			int i = 0;

			if (part.symmetryCounterparts == null)
				return i;
			foreach(Part sPart in part.symmetryCounterparts) {
				if (sPart.Modules.Contains("ModuleFuelTanks")) {
					ModuleFuelTanks fuel = (ModuleFuelTanks)sPart.Modules["ModuleFuelTanks"];
					if (fuel) {
						i++;
						if (fuel.fuelList == null)
							continue;
						foreach (ModuleFuelTanks.FuelTank tank in fuel.fuelList) {
							tank.amount = 0;
							tank.maxAmount = 0;
						}
						foreach (ModuleFuelTanks.FuelTank tank in this.fuelList) {
							if (tank.maxAmount > 0) {
								ModuleFuelTanks.FuelTank pTank = fuel.fuelList.Find(t => t.name.Equals(tank.name));
								if (pTank) {
									pTank.maxAmount = tank.maxAmount;
									if(tank.maxAmount > 0)
										pTank.amount = tank.amount;
								}
							}
						}
						ResourcesModified (fuel.part);
						fuel.UpdateMass();
					}
				}
			}
			return i;
		}
	}
}
