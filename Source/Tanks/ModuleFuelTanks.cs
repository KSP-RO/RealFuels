using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using System.Collections.ObjectModel;
using KSPAPIExtensions;
using KSPAPIExtensions.PartMessage;
using System.Reflection;

// ReSharper disable InconsistentNaming, CompareOfFloatsByEqualityOperator

namespace RealFuels.Tanks
{
    public class ModuleFuelTanks : PartModule, IModuleInfo, IPartCostModifier, IPartMassModifier, IAnalyticTemperatureModifier, IAnalyticPreview
	{
		bool compatible = true;
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
                    tfInterface.InvokeMember("AddInteropValue", tfBindingFlags, null, null, new System.Object[] { this.part, "tankType", type, "RealFuels" });
                }
                catch
                {
                }
            }
        }
        #endregion

        private static float MassMult
		{
			get {
				return MFSSettings.useRealisticMass ? 1.0f : MFSSettings.tankMassMultiplier;
			}
		}

		private static float defaultBaseCostPV
		{
			get {
				return MFSSettings.baseCostPV;
			}
		}

		public override void OnAwake ()
		{
			enabled = false;
			if (CompatibilityChecker.IsWin64 ()) {
				compatible = false;
				Events["HideUI"].active = false;
				Events["ShowUI"].active = false;
				return;
			}
            PartMessageService.Register(this);
			// Initialize utilization from the settings file
			utilization = MFSSettings.partUtilizationDefault;
		}

		public override void OnInactive ()
		{
			if (!compatible) {
				return;
			}
		}

		bool isDatabaseLoad
		{
			get {
				return (GameSceneFilter.Loading.IsLoaded () || GameSceneFilter.SpaceCenter.IsLoaded ());
			}
		}

		bool isEditor
		{
			get {
				return GameSceneFilter.AnyEditor.IsLoaded ();
			}
		}

		bool isEditorOrFlight
		{
			get {
				return GameSceneFilter.AnyEditorOrFlight.IsLoaded();
			}
		}

		void RecordTankTypeResources (HashSet<string> resources, string type)
		{
			TankDefinition def;
			if (!MFSSettings.tankDefinitions.Contains (type)) {
				return;
			}
			def = MFSSettings.tankDefinitions[type];

			for (int i = 0; i < def.tankList.Count; i++) {
				FuelTank tank = def.tankList[i];
				resources.Add (tank.name);
			}
		}

		void RecordManagedResources ()
		{
			HashSet<string> resources = new HashSet<string> ();

			RecordTankTypeResources (resources, type);
			if (typesAvailable != null) {
				for (int i = 0; i < typesAvailable.Count(); i++) {
					RecordTankTypeResources (resources, typesAvailable[i]);
				}
			}
			MFSSettings.managedResources[part.name] = resources;
		}

		public override void OnLoad (ConfigNode node)
		{
			if (!compatible) {
				return;
			}

			if (MFSSettings.tankDefinitions == null) {
				MFSSettings.Initialize ();
			}

			// Load the volume. If totalVolume is specified, use that to calc the volume
			// otherwise scale up the provided volume. No KSPField support for doubles
			if (node.HasValue ("totalVolume") && double.TryParse (node.GetValue ("totalVolume"), out totalVolume)) {
				ChangeTotalVolume (totalVolume);
			} else if (node.HasValue ("volume") && double.TryParse (node.GetValue ("volume"), out volume)) {
				totalVolume = volume * 100d / utilization;
			}
			using (PartMessageService.Instance.Ignore(this, null, typeof(PartResourcesChanged))) {
				if (isDatabaseLoad) {
					MFSSettings.SaveOverrideList(part, node.GetNodes("TANK"));
					ParseBaseMass(node);
					ParseBaseCost(node);
                    ParseInsulationFactor(node);
					typesAvailable = node.GetValues ("typeAvailable");
					RecordManagedResources ();
				} else if (isEditorOrFlight) {
					// The amounts initialized flag is there so that the tank type loading doesn't
					// try to set up any resources. They'll get loaded directly from the save.
					UpdateTankType (false);
					// Destroy any resources still hanging around from the LOADING phase
					for (int i = part.Resources.Count - 1; i >= 0; --i) {
						PartResource partResource = part.Resources[i];
						if (!tankList.Contains (partResource.resourceName))
							continue;
						part.Resources.list.RemoveAt (i);
						DestroyImmediate (partResource);
					}
					RaiseResourceListChanged ();
					// Setup the mass
					part.mass = mass;
					MassChanged (mass);
					// compute massDelta based on prefab, if available.
					if ((object)(part.partInfo) != null)
						if ((object)(part.partInfo.partPrefab) != null)
							massDelta = part.mass - part.partInfo.partPrefab.mass;
				}
			}
		}

		public override string GetInfo ()
		{
			if (!compatible) {
				return "";
			}

			UpdateTankType ();

			StringBuilder info = new StringBuilder ();
			info.AppendLine ("Modular Fuel Tank:");
			info.Append ("	Max Volume: ").AppendLine (volume.ToStringSI (unit: MFSSettings.unitLabel));
			info.AppendLine ("	Tank can hold:");
			for (int i = 0; i < tankList.Count; i++) {
				FuelTank tank = tankList[i];
				info.Append ("		").Append (tank).Append (" ").AppendLine (tank.note);
			}
			return info.ToString ();
		}

		public string GetPrimaryField ()
		{
			return String.Format ("Max Volume: {0}, {1}{2}",
							volume.ToStringSI (unit: MFSSettings.unitLabel),
							type,
							(typesAvailable != null && typesAvailable.Length > 1) ? "*" : "");
		}

		public Callback<Rect> GetDrawModulePanelCallback ()
		{
			return null;
		}

		public string GetModuleTitle ()
		{
			return "Modular Fuel Tank";
		}

		void OnActionGroupEditorOpened ()
		{
			Events["HideUI"].active = false;
			Events["ShowUI"].active = false;
		}

		void OnActionGroupEditorClosed ()
		{
			Events["HideUI"].active = false;
			Events["ShowUI"].active = true;
		}

        public void Start() // not just when activated
        {
            if (!compatible) {
				return;
			}
            enabled = true;
            UpdateTFInterops();
        }

		public override void OnStart (StartState state)
		{
			if (!compatible) {
				return;
			}
            enabled = true; // just in case...

			Events["HideUI"].active = false;
			Events["ShowUI"].active = true;

#if DEBUG
            Fields["debug1Display"].guiActive = true;
            Fields["debug2Display"].guiActive = true;
#endif


            if (isEditor)
            {
                GameEvents.onPartActionUIDismiss.Add (OnPartActionGuiDismiss);
                TankWindow.OnActionGroupEditorOpened.Add (OnActionGroupEditorOpened);
                TankWindow.OnActionGroupEditorClosed.Add (OnActionGroupEditorClosed);
                if (part.symmetryCounterparts.Count > 0)
                {
                    UpdateTankType (false);
                    massDirty = true;
                }

                InitializeTankType ();
                InitializeUtilization ();
            }
			CalculateMass ();
            CalculateTankArea(out tankArea);
            part.heatConductivity = Math.Min(part.heatConductivity, outerInsulationFactor);
            part.skinInternalConductionMult = Math.Min(part.skinInternalConductionMult, outerInsulationFactor);
		}

		public override void OnSave (ConfigNode node)
		{
			if (!compatible) {
				return;
			}

			node.AddValue ("volume", volume.ToString ("G17")); // no KSPField support for doubles
			tankList.Save (node);
		}

		private void OnPartActionGuiDismiss(Part p)
		{
			if (p == part) {
				HideUI ();
			}
		}

		public void Update ()
		{
            if (!compatible || !HighLogic.LoadedSceneIsEditor)
            {
				return;
			}
			UpdateTankType ();
			UpdateUtilization ();
			CalculateMass ();

			EditorLogic editor = EditorLogic.fetch;
			if (editor.editorScreen == EditorScreen.Actions && EditorActionGroups.Instance.GetSelectedParts ().Contains (part)) {
				TankWindow.ShowGUI (this);
			}
		}

		public void FixedUpdate ()
		{
			if (!compatible) {
				return;
			}
			//print ("[Real Fuels]" + Time.time.ToString ());
			if (HighLogic.LoadedSceneIsFlight)
            {
#if DEBUG
                //debug1Display = part.skinInternalConductionMult.ToString ("F12");
                //debug2Display = FormatFlux (part.skinToInternalFlux * (part.skinTemperature - part.temperature));
                debug1Display = "";
                debug2Display = "";
#endif
                //debug1Display = (part.thermalRadiationFlux / part.radiativeArea).ToString("F");
                if (tankArea == 0d)
                    CalculateTankArea(out tankArea);
                // Don't call tank loss function if we're in analytic mode. That's what the interface is for.
                //if (TimeWarp.CurrentRate <= PhysicsGlobals.ThermalMaxIntegrationWarp)
                    StartCoroutine(CalculateTankLossFunction (TimeWarp.fixedDeltaTime));
			}
		}

        public double ConductionFactors { get { return MFSSettings.globalConductionCompensation == true ? PhysicsGlobals.ConductionFactor * PhysicsGlobals.SkinInternalConductionFactor : 1d; }} 
        protected float tankArea;
        double boiloffMass = 0d;

        public double BoiloffMassRate { get { return boiloffMass; } }

        private IEnumerator CalculateTankLossFunction (float deltaTime, bool analyticalMode = false)
		{
			// Need to ensure that all heat compensation (radiators, heat pumps, etc) run first.
			yield return new WaitForFixedUpdate();
            boiloffMass = 0d;

            double minTemp = part.temperature;
            for (int i = tankList.Count - 1; i >= 0; --i)
            {
                FuelTank tank = tankList[i];
                if (tank.amount > 0d && (tank.vsp > 0.0 || tank.loss_rate > 0d))
                    minTemp = Math.Min(minTemp, tank.temperature);
            }

            part.radiatorMax = (minTemp * 0.9d) / part.maxTemp;
            if (vessel != null && vessel.situation == Vessel.Situations.PRELAUNCH)
            {
                part.temperature = minTemp;
                part.skinTemperature = minTemp;
            }
            else
            {
				partPrevTemperature = part.temperature;

                double deltaTimeRecip = 1d / deltaTime;
                for (int i = tankList.Count - 1; i >= 0; --i)
                {
                    FuelTank tank = tankList[i];

					if (tank.amount > 0d)
					{
						if (tank.vsp > 0.0)
						{
                            // Opposite of original boil off code. Finds massLost first.
                            double massLost = 0.0;
                            double deltaTemp = part.temperature - tank.temperature;

#if DEBUG
                            if (debug2Display != "")
                                debug2Display += " / ";

                            if (debug1Display != "")
                                debug1Display += " / ";
#endif
                            if (deltaTemp > 0)
                            {

                                //double tankConductivity = 0.03999680026; // Equal to 10cm aluminum + 10cm polyurethane insulation. Conductivity 250 and 0.02.
                                //Equation: (0.2/ 0.1/205 + 0.1/0.02)

								double tankRatio = tank.maxAmount / volume;

								double area = tankArea * tankRatio;

                                double q = deltaTemp / ((tank.wallThickness / ((tank.wallConduction / ConductionFactors) * area)) + (tank.insulationThickness / ((tank.insulationConduction / ConductionFactors) * area)));
                                Debug.Log (part.name + " area: " + area);
								if (MFSSettings.ferociousBoilOff)
                                    q *= (part.thermalMass / (part.thermalMass - part.resourceThermalMass)) * tankRatio;

                                //q /= ConductionFactors;

                                q *= 0.001d; // convert to kilowatts

                                massLost = q / tank.vsp;
#if DEBUG
                                // Only do debugging displays if compiled for debugging.
                                debug1Display += FormatFlux(q);
                                debug2Display += (massLost * 1000 * 3600).ToString("F4") + "kg/hr";
                                //debug2Display += area.ToString("F2");

                                //debug1Display = tank.wallThickness + " / " + tank.wallConduction;
                                //debug2Display = tank.insulationThickness + " / " + tank.insulationConduction;

#endif
                                massLost *= deltaTime; // Frame scaling
                            }

							double lossAmount = massLost / tank.density;
							boiloffMass += massLost;

                            if (double.IsNaN(lossAmount))
                                Debug.Log("[MFT] " + tank.name + " lossAmount is NaN!");
                            else
                            {
                                if (lossAmount > tank.amount)
                                {
                                    tank.amount = 0d;
                                } 
                                else
                                {
                                    tank.amount -= lossAmount;

                                    double fluxLost = -massLost * tank.vsp;

                                    fluxLost *= ConductionFactors;

                                    part.AddThermalFlux(fluxLost * deltaTimeRecip);

                                    // subtract heat from boiloff
                                    // TODO Fix analytic mode behavior or remove this. (currently unused as analyticMode is always false)
                                    if (analyticalMode)
                                        previewInternalFluxAdjust += fluxLost;
                                    else
                                        part.AddThermalFlux(fluxLost * deltaTimeRecip);
                                }
                            }
						}
						else if (tank.loss_rate > 0 && tank.amount > 0)
						{
							double deltaTemp = part.temperature - tank.temperature;
							if (deltaTemp > 0)
	                        {
	                            double lossAmount = tank.maxAmount * tank.loss_rate * deltaTemp * deltaTime;
	                            if(lossAmount > tank.amount)
	                            {
	                                lossAmount = -tank.amount;
	                                tank.amount = 0d;
	                            }
	                            else
	                            {
	                                lossAmount = -lossAmount;
	                                tank.amount += lossAmount;
	                            }
	                            double massLost = tank.density * lossAmount;
	                            boiloffMass += massLost;
	                        }
	                    }
					}
                }
            }
		}

        // Analytic Interface
        public void SetAnalyticTemperature(FlightIntegrator fi, double analyticTemp, double toBeInternal, double toBeSkin)
        {
            analyticSkinTemp = toBeSkin;
        }

        public double GetSkinTemperature(out bool lerp)
        {
            lerp = true;
            return analyticSkinTemp;
        }

        public double GetInternalTemperature(out bool lerp)
        {
            lerp = true;
            //if (partPrevTemperature == -1)
                return part.temperature;
            //else
            //    return partPrevTemperature;
        }

        // Analytic Preview Interface
        public void AnalyticInfo(FlightIntegrator fi, double sunAndBodyIn, double backgroundRadiation, double radArea, double internalFlux, double convCoeff, double ambientTemp, double maxPartTemp)
        {
            //analyticalInternalFlux = internalFlux;
            float deltaTime = (float)(Planetarium.GetUniversalTime() - vessel.lastUT);
            //CalculateTankLossFunction(deltaTime, true);
        }

        public double InternalFluxAdjust()
        {
            return previewInternalFluxAdjust;
        }



		// The active fuel tanks. This will be the list from the tank type, with any overrides from the part file.
		internal FuelTankList tankList = new FuelTankList ();

		[KSPField (isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Tank Type"), UI_ChooseOption (scene = UI_Scene.Editor)]
		public string type;
		private string oldType;

		public string[] typesAvailable;

		// for EngineIgnitor integration: store a public list of the fuel tanks, and
		[NonSerialized]
		public List<FuelTank> fuelList = new List<FuelTank> ();
		// for EngineIgnitor integration: store a public dictionary of all pressurized propellants
		[NonSerialized]
		public Dictionary<string, bool> pressurizedFuels = new Dictionary<string, bool> ();
        [KSPField(guiActiveEditor = true, guiName = "Highly Pressurized?")]
        public bool highlyPressurized = false;

        public double outerInsulationFactor = 1.0;

		private void InitializeTankType ()
		{
			if (typesAvailable == null || typesAvailable.Length <= 1) {
				Fields["type"].guiActiveEditor = false;
			} else {
                List<string> typesTech = new List<string>();
                foreach (string curType in typesAvailable)
                {
                    TankDefinition def;
                    if (!MFSSettings.tankDefinitions.Contains(curType))
                    {
                        string loadedTypes = "";
                        foreach (TankDefinition d2 in MFSSettings.tankDefinitions)
                            loadedTypes += " " + d2.name;
                        Debug.LogError("Unable to find tank definition for type \"" + curType + "\". Available types are:" + loadedTypes);
                        continue;
                    }
                    def = MFSSettings.tankDefinitions[curType];
                    if (def.canHave)
                        typesTech.Add(curType);
                }
                if (typesTech.Count > 0)
                {
                    UI_ChooseOption typeOptions = (UI_ChooseOption)Fields["type"].uiControlEditor;
                    typeOptions.options = typesTech.ToArray();
                }
                else
                    Fields["type"].guiActiveEditor = false;
			}

			UpdateTankType ();
		}

		private void UpdateEngineIgnitor (TankDefinition def)
		{
			// collect pressurized propellants for EngineIgnitor
			// XXX Dirty hack until engine ignitor is fixed
			fuelList.Clear ();				//XXX
			fuelList.AddRange (tankList);	//XXX

			pressurizedFuels.Clear ();
			for (int i = 0; i < tankList.Count; i++) {
				FuelTank f = tankList[i];
				pressurizedFuels[f.name] = def.highlyPressurized || f.note.ToLower ().Contains ("pressurized");
			}
		}

		private void UpdateTankType (bool initializeAmounts = true)
		{
			if (oldType == type || type == null) {
				return;
			}

			// Copy the tank list from the tank definitiion
			TankDefinition def;
			if (!MFSSettings.tankDefinitions.Contains (type)) {
				Debug.LogError ("Unable to find tank definition for type \"" + type + "\" reverting.");
				type = oldType;
				return;
			}
			def = MFSSettings.tankDefinitions[type];
            if (!def.canHave)
            {
                type = oldType;
                if(oldType != null && oldType != "") // we have an old type
                {
                    def = MFSSettings.tankDefinitions[type];
                    if (def.canHave)
                        return; // go back to old type
                }
                // else find one that does work
                foreach (TankDefinition newDef in MFSSettings.tankDefinitions)
                {
                    if (newDef.canHave)
                    {
                        def = newDef;
                        type = newDef.name;
                        break;
                    }
                }
                if (type == oldType) // if we didn't find a new one
                {
                    Debug.LogError("Unable to find a type that is tech-available for part " + part.name);
                    return;
                }
            }

			oldType = type;

            // Get pressurization
            highlyPressurized = def.highlyPressurized;

			// Build the new tank list.
			tankList = new FuelTankList ();
			for (int i = 0; i < def.tankList.Count; i++) {
				FuelTank tank = def.tankList[i];
				// Pull the override from the list of overrides
				ConfigNode overNode = MFSSettings.GetOverrideList(part).FirstOrDefault(n => n.GetValue("name") == tank.name);

				tankList.Add (tank.CreateCopy (this, overNode, initializeAmounts));
            }
            tankList.TechAmounts(); // update for current techs

			// Destroy any managed resources that are not in the new type.
			HashSet<string> managed = MFSSettings.managedResources[part.name];	// if this throws, we have some big fish to fry
			bool needsMesage = false;
			for (int i = part.Resources.Count - 1; i >= 0; --i) {
				PartResource partResource = part.Resources[i];
				string resname = partResource.resourceName;
				if (!managed.Contains(resname) || tankList.Contains(resname))
					continue;
				part.Resources.list.RemoveAt (i);
				DestroyImmediate (partResource);
				needsMesage = true;
			}
			if (needsMesage) {
				RaiseResourceListChanged ();
			}
			if (!basemassOverride) {
				ParseBaseMass (def.basemass);
			}
			if (!baseCostOverride) {
				ParseBaseCost (def.baseCost);
			}

            ParseInsulationFactor(def.outerInsulationFactor);

            UpdateTFInterops();

			if (isDatabaseLoad) {
				// being called in the SpaceCenter scene is assumed to be a database reload
				//FIXME is this really needed?
				return;
			}

			UpdateEngineIgnitor (def);

			massDirty = true;
		}

		// The total tank volume. This is prior to utilization
		public double totalVolume;

		[KSPField (isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Utilization", guiUnits = "%", guiFormat = "F0"),
		 UI_FloatEdit (minValue = 1, maxValue = 100, incrementSlide = 1, scene = UI_Scene.Editor)]
		public float utilization = -1;
		private float oldUtilization = -1;

		[KSPField]
		public bool utilizationTweakable = false;

		// no double support for KSPFields - [KSPField (isPersistant = true)]
		public double volume;

		[KSPField (isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Volume")]
		public string volumeDisplay;

        // Note that the following two fields are highly variable and subject to the whims of whatever RF dev that might want to change them.
        // (they only show up if this code is compiled in debug mode and are currently for debugging boiloff system)
        [KSPField (isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "Heat Penetration", guiUnits = "")]
		public string debug1Display;

        [KSPField (isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "Boil-off Loss", guiUnits = "")]
		public string debug2Display;

        [KSPField(isPersistant = true)]
		public double partPrevTemperature = -1;
		
        private double analyticSkinTemp;

        private double previewInternalFluxAdjust;

		public double UsedVolume
		{
			get; private set;
		}

		public double AvailableVolume
		{
			get {
				return volume - UsedVolume;
			}
		}

		static string FormatFlux(double flux, bool scale = false)
		{
			string prefix = "";
			if (flux < 0.0)
			{
				flux *= -1.0;
				prefix = "-";
			}
            // TODO If scale then it should display in joules not watts
			if (scale)
				flux *= TimeWarp.fixedDeltaTime;
			if (flux >= 1000000000000000.0)
				return prefix + (flux / 1000000000000000.0).ToString("F2") + " EW";
			else if (flux >= 1000000000000.0)
				return prefix + (flux / 1000000000000.0).ToString("F2") + " PW";
			else if (flux >= 1000000000.0)
				return prefix + (flux / 1000000000.0).ToString("F2") + " TW";
			else if (flux >= 1000000.0)
				return prefix + (flux / 1000000.0).ToString("F2") + " GW";
			else if (flux >= 1000.0)
				return prefix + (flux / 1000.0).ToString("F2") + " MW";
			else if (flux >= 1.0)
				return prefix + (flux).ToString("F2") + " kW";
			else if (flux < 1.0)
				return prefix + (flux * 1000.0).ToString("F2") + " W";
			else
				return "ERROR";
		}


		// Conversion between tank volume in kL, and whatever units this tank uses.
		// Default to 1000 for RF. Varies for MFT. Needed to interface with PP.
		[KSPField]
		public float tankVolumeConversion = 1000;

		[PartMessageListener (typeof (PartVolumeChanged), scenes: GameSceneFilter.AnyEditor)]
		public void PartVolumeChanged (string volName, float newTotalVolume)
		{
			if (volName != PartVolumes.Tankage.ToString ()) {
				return;
			}

			double newTotalVolue = newTotalVolume * tankVolumeConversion;

			if (newTotalVolue == totalVolume) {
				return;
			}

			ChangeTotalVolume (newTotalVolue);
		}

		//called by StretchyTanks
		public void ChangeVolume (double newVolume)
		{
			ChangeTotalVolume (newVolume * 100 / utilization);
		}

		protected void ChangeResources (double volumeRatio, bool propagate = false)
		{
			// The used volume will rescale automatically when setting maxAmount
			for (int i = 0; i < tankList.Count; i++) {
				FuelTank tank = tankList[i];

				bool save_propagate = tank.propagate;
				tank.propagate = propagate;

				tank.maxAmount *= volumeRatio;

				tank.propagate = save_propagate;
			}
		}

		public void ChangeTotalVolume (double newTotalVolume, bool propagate = false)
		{
			double newVolume = Math.Round (newTotalVolume * utilization * 0.01d, 4);
			double volumeRatio = newVolume / volume;

			bool doResources = false;

			if (totalVolume > newTotalVolume) {
				ChangeResources (volumeRatio, propagate);
			} else {
				doResources = true;
			}
			totalVolume = newTotalVolume;
			volume = newVolume;
			if (propagate) {
				foreach (Part p in part.symmetryCounterparts) {
					// FIXME: Not safe, assumes only 1 MFT on the part.
					ModuleFuelTanks m = (ModuleFuelTanks)p.Modules["ModuleFuelTanks"];
					m.totalVolume = newTotalVolume;
					m.volume = newVolume;
				}
			}
			if (doResources) {
				ChangeResources (volumeRatio, propagate);
			}
			massDirty = true;
		}

		public void ChangeVolumeRatio (double ratio, bool propagate = false)
		{
			ChangeTotalVolume (totalVolume * ratio, propagate);
		}

		private void UpdateUtilization ()
		{
			if (oldUtilization == utilization) {
				return;
			}

			oldUtilization = utilization;

			ChangeTotalVolume (totalVolume);
		}

		private void InitializeUtilization ()
		{
			Fields["utilization"].guiActiveEditor = MFSSettings.partUtilizationTweakable || utilizationTweakable;
		}

		[KSPField (isPersistant = true)]
		public float mass;
		internal bool massDirty = true;

		[KSPField (isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Mass")]
		public string massDisplay;

		// public so they copy
		public bool basemassOverride;
		public bool baseCostOverride;
		public float basemassPV;
		public float baseCostPV;
		public float basemassConst;
		public float baseCostConst;

		[PartMessageEvent]
		public event PartMassChanged MassChanged;

		public static string FormatMass (float mass)
		{
			if (mass < 1.0f) {
				return mass.ToStringSI (4, 6, "g");
			}
			return mass.ToStringSI (4, unit:"t");
		}

		private void ParseBaseMass (ConfigNode node)
		{
			if (!node.HasValue ("basemass")) {
				return;
			}

			string baseMass = node.GetValue ("basemass");
			ParseBaseMass (baseMass);
			basemassOverride = true;
		}

		private void ParseBaseMass (string baseMass)
		{
			if (baseMass.Contains ("*") && baseMass.Contains ("volume")) {
				if (float.TryParse (baseMass.Replace ("volume", "").Replace ("*", "").Trim (), out basemassPV)) {
					basemassConst = 0;
					return;
				}
			} else if (float.TryParse (baseMass.Trim (), out basemassConst)) {
				basemassPV = 0f;
				return;
			}
			Debug.LogWarning ("[MFT] Unable to parse basemass \"" + baseMass + "\"");
		}

		private void ParseBaseCost (ConfigNode node)
		{
			if (!node.HasValue ("baseCost")) {
				return;
			}

			string baseCost = node.GetValue ("baseCost");
			ParseBaseCost (baseCost);
			baseCostOverride = true;
		}

		private void ParseBaseCost (string baseCost)
		{
			if (baseCost == null) {
				baseCost = "";
			}
			if (baseCost.Contains ("*") && baseCost.Contains ("volume")) {
				if (float.TryParse (baseCost.Replace ("volume", "").Replace ("*", "").Trim (), out baseCostPV)) {
					baseCostConst = 0f;
					return;
				}
			} else if (float.TryParse (baseCost.Trim (), out baseCostConst)) {
				baseCostPV = 0f;
				return;
			}
			if (baseCost != "") {
				Debug.LogWarning ("[MFT] Unable to parse baseCost \"" + baseCost + "\"");
			} else {
				baseCostPV = defaultBaseCostPV;
			}
		}

        private void ParseInsulationFactor(ConfigNode node)
        {
            if (!node.HasValue("outerInsulationFactor"))
                return;

            string insulationFactor = node.GetValue("outerInsulationFactor");
			ParseInsulationFactor (insulationFactor);
        }

        private void ParseInsulationFactor(string insulationFactor)
        {
            if (!double.TryParse(insulationFactor, out outerInsulationFactor))
                Debug.LogWarning("[MFT] Unable to parse outerInsulationFactor");
        }

		public void CalculateMass ()
		{
			if (tankList == null || !massDirty) {
				return;
			}
			massDirty = false;

			double basemass = basemassConst + basemassPV * (MFSSettings.basemassUseTotalVolume ? totalVolume : volume);

			if (basemass >= 0) {
				double tankDryMass = tankList
					.Where (fuel => fuel.maxAmount > 0 && fuel.utilization > 0)
					.Sum (fuel => (float)fuel.maxAmount * fuel.mass / fuel.utilization);

				mass = (float) (basemass + tankDryMass) * MassMult;

				if (part.mass != mass) {
					part.mass = mass;
					MassChanged (mass);
					// compute massDelta based on prefab, if available.
					if ((object)(part.partInfo) != null)
						if ((object)(part.partInfo.partPrefab) != null)
							massDelta = part.mass - part.partInfo.partPrefab.mass;
				}
			} else {
				mass = part.mass; // display dry mass even in this case.
			}

			if (isEditor) {
				UsedVolume = tankList
					.Where (fuel => fuel.maxAmount > 0 && fuel.utilization > 0)
					.Sum (fuel => fuel.maxAmount/fuel.utilization);

				SIPrefix pfx = volume.GetSIPrefix ();
				Func<double, string> formatter = pfx.GetFormatter (volume);
				volumeDisplay = "Avail: " + formatter (AvailableVolume) + pfx.PrefixString () + MFSSettings.unitLabel + " / Tot: " + formatter (volume) + pfx.PrefixString () + MFSSettings.unitLabel;

				double resourceMass = part.Resources.Cast<PartResource> ().Sum (r => r.maxAmount*r.info.density);

				double wetMass = mass + resourceMass;
				massDisplay = "Dry: " + FormatMass (mass) + " / Wet: " + FormatMass ((float)wetMass);

				UpdateTweakableMenu ();
			}
		}

        public void CalculateTankArea(out float totalTankArea)
        {
            totalTankArea = 0f;

            for (int i = 0; i < 6; ++i)
            {
                totalTankArea += part.DragCubes.WeightedArea[i];
            }
            Debug.Log("[MFT] Part WeightedArea: " + part.name + " = " + totalTankArea.ToString("F2"));
            Debug.Log("[MFT] Part Area: " + part.name + " = " + part.DragCubes.Area.ToString("F2"));
        }

		// mass-change interface, so Engineer's Report / Pad limit checking is correct.
		public float massDelta = 0f; // assigned whenever part.mass is changed.
		public float GetModuleMass(float defaultMass)
		{
			return massDelta;
		}

        private void UpdateTweakableMenu ()
        {
            if (!compatible) {
                return;
			}

            BaseEvent empty = Events["Empty"];
            if (empty != null) {
                empty.guiActiveEditor = (UsedVolume != 0);
			}

            bool activeEditor = (AvailableVolume >= 0.001);

            bool activeChanged = false;
            for (int i = 0; i < Events.Count; ++i) {
                BaseEvent evt = Events.GetByIndex (i);
                if (!evt.name.StartsWith ("MFT")) {
                    continue;
				}
                if (evt.guiActiveEditor != activeEditor) {
                    activeChanged = true;
				}
                evt.guiActiveEditor = activeEditor;
            }
            if (activeChanged) {
                MarkWindowDirty ();
			}
        }

        [PartMessageListener (typeof (PartResourceListChanged))]
        internal void MarkWindowDirty ()
        {
            if (_myWindow == null) {
                _myWindow = part.FindActionWindow ();
			}
            if (_myWindow == null) {
                return;
			}
            _myWindow.displayDirty = true;
        }
        private UIPartActionWindow _myWindow;

		public float GetModuleCost (float defaultCost)
		{
			double cst = 0;
			if (baseCostPV >= 0) {
				cst = volume * baseCostPV;
				if (PartResourceLibrary.Instance != null && tankList != null) {
					for (int i = 0; i < tankList.Count; i++) {
						FuelTank t = tankList[i];
						if (t.resource != null) {
							PartResourceDefinition d = PartResourceLibrary.Instance.GetDefinition (t.resource.resourceName);
							if (d != null) {
								cst += t.maxAmount * (d.unitCost + t.cost / t.utilization);
							}
						}
					}
				}
			}
			return (float)cst;
		}

		[PartMessageEvent]
		public event PartResourceInitialAmountChanged ResourceInitialChanged;
		[PartMessageEvent]
		public event PartResourceMaxAmountChanged ResourceMaxChanged;
		[PartMessageEvent]
		public event PartResourceListChanged ResourceListChanged;

		internal void RaiseResourceInitialChanged (PartResource resource, double amount)
		{
			ResourceInitialChanged (resource, amount);
		}
		internal void RaiseResourceMaxChanged (PartResource resource, double amount)
		{
			ResourceMaxChanged (resource, amount);
		}
		internal void RaiseResourceListChanged ()
		{
			ResourceListChanged ();
		}

		[PartMessageListener (typeof (PartResourcesChanged))]
		public void PartResourcesChanged ()
		{
			// We'll need to update the volume display regardless
			massDirty = true;
		}

		[KSPEvent (guiActiveEditor = true, guiName = "Hide UI", active = false)]
		public void HideUI ()
		{
			TankWindow.HideGUI ();
			UpdateMenus (false);
		}

		[KSPEvent (guiActiveEditor = true, guiName = "Show UI", active = false)]
		public void ShowUI ()
		{
			TankWindow.ShowGUI (this);
			UpdateMenus (true);
		}

		void UpdateMenus (bool visible)
		{
			Events["HideUI"].active = visible;
			Events["ShowUI"].active = !visible;
		}

		[KSPEvent (guiName = "Remove All Tanks", guiActive = false, guiActiveEditor = true, name = "Empty")]
		public void Empty ()
		{
			using (PartMessageService.Instance.Consolidate (this)) {
				for (int i = 0; i < tankList.Count; i++) {
					tankList[i].maxAmount = 0;
				}
			}
			GameEvents.onEditorShipModified.Fire (EditorLogic.fetch.ship);
		}

		// looks to see if we should ignore this fuel when creating an autofill for an engine
		private static bool IgnoreFuel (string name)
		{
			return MFSSettings.ignoreFuelsForFill.Contains (name);
		}

		[PartMessageListener (typeof (PartChildAttached), relations: PartRelationship.AnyPart, scenes: GameSceneFilter.AnyEditor)]
		[PartMessageListener (typeof (PartChildDetached), relations: PartRelationship.AnyPart, scenes: GameSceneFilter.AnyEditor)]
		public void VesselAttachmentsChanged (Part childPart)
		{
			UpdateUsedBy ();
		}

		/*[PartMessageListener (typeof (PartEngineConfigChanged), relations: PartRelationship.AnyPart, scenes: GameSceneFilter.AnyEditor)]
		public void EngineConfigsChanged ()
		{
			UpdateUsedBy ();
		}*/

		internal readonly Dictionary<string, FuelInfo> usedBy = new Dictionary<string, FuelInfo>();

        private void UpdateFuelInfo(FuelInfo f, string title)
        {
            FuelInfo found;
            if (!usedBy.TryGetValue(f.Label, out found))
            {
                usedBy.Add(f.Label, f);
            }
            else if (!found.names.Contains(title))
            {
                found.names += ", " + title;
            }
        }

		public void UpdateUsedBy ()
		{
			//print ("*RK* Updating UsedBy");

			usedBy.Clear ();

            // Get part list
            List<Part> parts;
            if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch.ship != null)
                parts = EditorLogic.fetch.ship.parts;
            else if (HighLogic.LoadedSceneIsFlight && vessel != null)
                parts = vessel.parts;
            else parts = new List<Part>();

            FuelInfo f;
            string title;
            PartModule m;
            for(int i = 0; i < parts.Count; ++i)
            {
                title = parts[i].partInfo.title;
                for(int j = 0; j < parts[i].Modules.Count; ++j)
                {
                    m = parts[i].Modules[j];
                    if (m is ModuleEngines)
                    {
                        f = new FuelInfo((m as ModuleEngines).propellants, this, title);
                        if(f.ratioFactor > 0d)
                            UpdateFuelInfo(f, title);
                    }
                    else if (m is ModuleRCS)
                    {
                        f = new FuelInfo((m as ModuleRCS).propellants, this, title);
                        if (f.ratioFactor > 0d)
                            UpdateFuelInfo(f, title);
                    }
                }
            }

			// Need to update the tweakable menu too
			if (HighLogic.LoadedSceneIsEditor) {
				Events.RemoveAll (button => button.name.StartsWith ("MFT"));

				bool activeEditor = (AvailableVolume >= 0.001);

				int idx = 0;
				foreach (FuelInfo info in usedBy.Values) {
					KSPEvent kspEvent = new KSPEvent {
						name = "MFT" + idx++,
						guiActive = false,
						guiActiveEditor = activeEditor,
						guiName = info.Label
					};
					FuelInfo info1 = info;
					BaseEvent button = new BaseEvent (Events, kspEvent.name, () => ConfigureFor (info1), kspEvent) {
						guiActiveEditor = activeEditor
					};
					Events.Add (button);
				}
				MarkWindowDirty ();
			}
		}

		public void ConfigureFor (Part engine)
		{
			foreach (PartModule engine_module in engine.Modules) {
				List<Propellant> propellants = GetEnginePropellants (engine_module);
				if ((object)propellants != null) {
					ConfigureFor (new FuelInfo (propellants, this, engine.partInfo.title));
					break;
				}
			}
		}

		internal void ConfigureFor (FuelInfo fi)
		{
			if (fi.ratioFactor == 0.0 || fi.efficiency == 0) // can't configure for this engine
				return;

			double availableVolume = AvailableVolume;
			foreach (Propellant tfuel in fi.propellants) {
				if (PartResourceLibrary.Instance.GetDefinition (tfuel.name).resourceTransferMode != ResourceTransferMode.NONE) {
					FuelTank tank;
					if (tankList.TryGet (tfuel.name, out tank)) {
						double amt = availableVolume * tfuel.ratio / fi.efficiency;
						tank.maxAmount += amt;
						tank.amount += amt;
					}
				}
			}
		}

        List<Propellant> GetEnginePropellants(PartModule engine)
        {
            if (engine is ModuleEngines)
                return (engine as ModuleEngines).propellants;
            else if (engine is ModuleRCS)
                return (engine as ModuleRCS).propellants;
            return null;
        }
	}
}
