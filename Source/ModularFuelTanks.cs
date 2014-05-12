//#define DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP;
using System.Collections.ObjectModel;
using KSPAPIExtensions;
using KSPAPIExtensions.PartMessage;

namespace RealFuels
{
	public class ModuleFuelTanks : ModularFuelPartModule
	{
        #region loading stuff from config files

        public static float massMult
        {
            get
            {
                return MFSSettings.Instance.useRealisticMass ? MFSSettings.Instance.tankMassMultiplier : 1.0f;
            }
        }

        // looks to see if we should ignore this fuel when creating an autofill for an engine
        public static bool IgnoreFuel(string name)
        {
            return MFSSettings.Instance.ignoreFuelsForFill.Contains(name);
        }

        #endregion

        #region FuelTank
        // A FuelTank is a single TANK {} entry from the part.cfg file.
		// it defines four properties:
		// name = the name of the resource that can be stored
		// utilization = how much of the tank is devoted to that resource (vs. how much is wasted in cryogenics or pumps)
		// mass = how much the part's mass is increased per volume unit of tank installed for this resource type
		// loss_rate = how quickly this resource type bleeds out of the tank

		public class FuelTank: IConfigNode
		{
			//------------------- fields
            [Persistent]
			public string name = "UnknownFuel";
            [Persistent]
            public string note = "";
            [Persistent]
            public float utilization = 1.0f;
            [Persistent]
            public float mass = 0.0f;
            [Persistent]
            public double loss_rate = 0.0;
            [Persistent]
            public float temperature = 300.0f;
            [Persistent]
            public bool fillable = true;

            public bool resourceAvailable = false;

            internal string amountExpression;
            internal string maxAmountExpression;

            [System.NonSerialized]
			private ModuleFuelTanks module;

			//------------------- virtual properties
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

            public double amount
            {
				get {
                    if (module == null)
                        throw new InvalidOperationException("Amount is not defined until instantiated in a tank");

                    if (resource == null)
						return 0.0;
					else
						return resource.amount;
				}
				set {
                    if (module == null)
                        throw new InvalidOperationException("Amount is not defined until instantiated in a tank");

                    PartResource resource = this.resource;
                    if (resource == null)
                        return;

                    if (value > resource.maxAmount)
                        value = resource.maxAmount;

                    if (value == resource.amount)
                        return;

                    amountExpression = null;
                    resource.amount = amount;
                    module.RaiseResourceInitialChanged(resource, amount);
                    // TODO: symmetry updates
                }
			}

			public double maxAmount {
				get 
                {
                    if (module == null)
                        throw new InvalidOperationException("Maxamount is not defined until instantiated in a tank");

                    if (resource == null)
						return 0.0f;
					return resource.maxAmount;
				}

				set 
                {
                    if (module == null)
                        throw new InvalidOperationException("Maxamount is not defined until instantiated in a tank");

                    PartResource resource = this.resource;
					if (resource != null && value <= 0.0) 
                    {
                        // Delete it
                        Debug.LogWarning("[MFT] Deleting tank from API " + name);
                        maxAmountExpression = null;

                        Destroy(resource);
                        part.Resources.list.Remove(resource);
                        module.RaiseResourceListChanged();
					} 
                    else if (resource != null) 
                    {
                        if (value > resource.maxAmount)
                        {
                            // If expanding, modify it to be less than overfull
                            double maxQty = module.availableVolume * utilization + resource.maxAmount;
                            if (maxQty < value)
                                value = maxQty;
                        }

                        // Do nothing if unchanged
                        if (value == resource.maxAmount)
                            return;

                        Debug.LogWarning("[MFT] Updating tank from API " + name + " amount: " + value);
                        maxAmountExpression = null;

                        // Keep the same fill fraction
                        double newAmount = value * fillFraction;

                        resource.maxAmount = value;
                        module.RaiseResourceMaxChanged(resource, value);

                        if (newAmount != resource.amount)
                        {
                            resource.amount = newAmount;
                            module.RaiseResourceInitialChanged(resource, newAmount);
                        }
					} 
                    else if(value > 0.0) 
                    {
                        Debug.LogWarning("[MFT] Adding tank from API " + name + " amount: " + value);
                        maxAmountExpression = null;

                        ConfigNode node = new ConfigNode("RESOURCE");
						node.AddValue ("name", name);
						node.AddValue ("amount", value);
						node.AddValue ("maxAmount", value);
#if DEBUG
						print (node.ToString ());
#endif
						resource = part.AddResource (node);
						resource.enabled = true;

                        module.RaiseResourceListChanged();
					}
                    // TODO: symmetry updates
                    module.CalculateMass();
				}

			}

            public double fillFraction
            {
                get
                {
                    return amount / maxAmount;
                }
                set
                {
                    amount = value * maxAmount;
                }
            }


			//------------------- implicit type conversions
			public override string ToString ()
			{
				if (name == null)
					return "NULL";
				return name;
			}

			//------------------- IConfigNode implementation
			public void Load(ConfigNode node)
			{
                if (!(node.name.Equals("TANK") && node.HasValue("name")))
                    return;

                ConfigNode.LoadObjectFromConfig(this, node);
				if(node.HasValue ("efficiency") && !node.HasValue("utilization"))
					float.TryParse (node.GetValue("efficiency"), out utilization);

                amountExpression = node.GetValue("amount");
                maxAmountExpression = node.GetValue("maxAmount");

                InitializeAmounts();

                resourceAvailable = PartResourceLibrary.Instance.GetDefinition(name) != null;
			}

            private void InitializeAmounts()
            {
                if (module == null)
                    return;

                if (maxAmountExpression == null)
                {
                    maxAmount = 0;
                    amount = 0;
                    return;
                }

                double v;
                if (maxAmountExpression.Contains("%"))
                {
                    double.TryParse(maxAmountExpression.Replace("%", "").Trim(), out v);
                    maxAmount = v * utilization * module.volume * 0.01; // NK
                }
                else
                {
                    double.TryParse(maxAmountExpression, out v);
                    maxAmount = v;
                }

                if (amountExpression == null) 
                {
                    amount = maxAmount;
                    return;
                }

                if (amountExpression.Equals("full"))
                    amount = maxAmount;
                else if (amountExpression.Contains("%"))
                {
                    double.TryParse(amountExpression.Replace("%", "").Trim(), out v);
                    amount = v * maxAmount * 0.01;
                }
                else
                {
                    double.TryParse(amountExpression, out v);
                    amount = v;
                }
            }

			public void Save(ConfigNode node)
			{
                if (name == null)
                    return;
                ConfigNode.CreateConfigFromObject(this, node);

                if (module == null)
                {
                    node.AddValue("amount", amountExpression);
                    node.AddValue("maxAmount", maxAmountExpression);
                }
                else
                {
                    // TODO: I don't think this is necicary anymore.

                    // You would think we want to do this only in the editor, but
                    // as it turns out, KSP is terrible about consistently setting
                    // up resources between the editor and the launchpad.
                    node.AddValue("amount", amount.ToString("G17"));
                    node.AddValue("maxAmount", maxAmount.ToString("G17"));
                }
			}

			//------------------- Constructor
            public FuelTank(ConfigNode node)
            {
                Load(node);
            }

            public FuelTank(ModuleFuelTanks module, ConfigNode node)
			{
                this.module = module;
                Load(node);
			}

            public FuelTank CreateConcreteCopy(ModuleFuelTanks module)
            {
                FuelTank clone = (FuelTank)this.MemberwiseClone();
                clone.module = module;
                clone.InitializeAmounts();

                return clone;
            }
        }

        public class FuelTankList : KeyedCollection<string, ModuleFuelTanks.FuelTank>, IConfigNode
        {
            public FuelTankList()
            {
            }

            public FuelTankList(ConfigNode node)
            {
                Load(node);
            }

            public FuelTankList(ModuleFuelTanks module, ConfigNode node)
            {
                foreach (ConfigNode tankNode in node.GetNodes("TANK"))
                {
                    string resourceName = tankNode.GetValue("name");
                    Add(new ModuleFuelTanks.FuelTank(module, tankNode));
                }
            }

            protected override string GetKeyForItem(ModuleFuelTanks.FuelTank item)
            {
                return item.name;
            }

            public void CreateConcreteCopy(ModuleFuelTanks module, FuelTankList copyInto)
            {
                foreach (ModuleFuelTanks.FuelTank tank in this)
                {
                    copyInto.Remove(tank.name);
                    copyInto.Add(tank.CreateConcreteCopy(module));
                }
            }

            public void Load(ConfigNode node)
            {
                foreach (ConfigNode tankNode in node.GetNodes("TANK"))
                {
                    string resourceName = tankNode.GetValue("name");
                    Add(new ModuleFuelTanks.FuelTank(tankNode));
                }
            }

            public void Save(ConfigNode node)
            {
                foreach (ModuleFuelTanks.FuelTank tank in this)
                {
                    ConfigNode tankNode = new ConfigNode("TANK");
                    tank.Save(tankNode);
                    node.AddNode(tankNode);
                }
            }
        }



        #endregion

        #region KSP Callbacks
        public override void OnAwake()
        {
            base.OnAwake();
            PartMessageService.Register(this);
        }

        public override void OnActive()
        {
            GameEvents.onPartActionUIDismiss.Add(OnPartActionGuiDismiss);
        }

        public override void OnInactive()
        {
            GameEvents.onPartActionUIDismiss.Remove(OnPartActionGuiDismiss);
        }

        public override void OnLoad(ConfigNode node)
        {
#if DEBUG
			print ("========ModuleFuelTanks.OnLoad called. Node is:=======");
			print (part.name);
#endif
            // no KSPField support for doubles
            if (node.HasValue("volume"))
                double.TryParse(node.GetValue("volume"), out volume);

            using (PartMessageService.Instance.Ignore(this, null, typeof(PartResourcesChanged)))
            {
                if (GameSceneFilter.Loading.IsLoaded())
                {
                    LoadTankListOverridesInLoading(node);

                    ParseBasemass(node);

                    typesAvailable = node.GetValues("typeAvailable");
                }
                else if (GameSceneFilter.Flight.IsLoaded())
                {
                    // We're in flight, load the concrete tanks directly
                    LoadConcreteTankList(node);

                    // Ensure the old type matches so what's set up doesn't get clobbered.
                    oldType = type;

                    // Setup the mass
                    part.mass = mass;
                    MassChanged(mass);
                }
                else if (GameSceneFilter.AnyEditor.IsLoaded())
                {
                    // We've loaded a stored vessel / assembly in the editor. 
                    // Initialize as per normal editor mode, but copy the fill fraction
                    // from the old data.
                    LoadConcreteTankList(node);

                    if (type != null)
                    {
                        FuelTankList loadedTanks = tankList;

                        UpdateTankType();

                        FuelTank newTank;
                        foreach (FuelTank oldTank in loadedTanks)
                            if(tankList.TryGet(oldTank.name, out newTank))
                                newTank.fillFraction = oldTank.fillFraction;
                    }
                }
            }
        }

        public override string GetInfo()
        {
            UpdateTankType();

            string info = "Modular Fuel Tank: \n"
                + "  Max Volume: " + volume.ToString() + "\n"
                    + "  Tank can hold:";
            foreach (FuelTank tank in tankList)
            {
                info += "\n   " + tank + " " + tank.note;
            }
            return info + "\n";
        }

        public override void OnStart(StartState state)
        {
            // This won't do anything if it's already been done in OnLoad (stored vessel/assem)
            if (GameSceneFilter.AnyEditor.IsLoaded())
                InitializeTankType();

            CalculateMass();
        }

		public override void OnSave (ConfigNode node)
		{
            base.OnSave(node);

            node.AddValue("volume", volume.ToString("G17")); // no KSPField support for doubles

#if DEBUG
			print ("========ModuleFuelTanks.OnSave called. Node is:=======");
			print (node.ToString ());
#endif
			foreach (FuelTank tank in tankList) {
				ConfigNode subNode = new ConfigNode("TANK");
				tank.Save (subNode);
#if DEBUG
				print ("========ModuleFuelTanks.OnSave adding subNode:========");
				print (subNode.ToString());
#endif
				node.AddNode (subNode);
			}
		}
        #endregion

        #region Update

        public void Update()
        {
            if (!GameSceneFilter.AnyEditor.IsLoaded())
                return;

            UpdateTankType();
        }

        public override void OnUpdate()
        {
            if (GameSceneFilter.AnyEditor.IsLoaded()) // I don't think this actually gets called in editor mode
                return;

            if (timestamp > 0)
                CalculateTankLossFunction(precisionDeltaTime);

            base.OnUpdate();            //Needs to be at the end to prevent weird things from happening during startup and to make handling persistance easy; this does mean that the effects are delayed by a frame, but since they're constant, that shouldn't matter here
        }

        private void CalculateTankLossFunction(double deltaTime)
        {
            foreach (FuelTank tank in tankList)
            {
                if (tank.loss_rate > 0 && tank.amount > 0)
                {
                    double deltaTemp = part.temperature - tank.temperature;
                    if (deltaTemp > 0)
                    {
                        double loss = tank.maxAmount * tank.loss_rate * deltaTemp * deltaTime; // loss_rate is calibrated to 300 degrees.
                        if (loss > tank.amount)
                            tank.amount = 0;
                        else
                            tank.amount -= loss;
                    }
                }
            }
        }
        #endregion

        #region Volume

        // no double support for KSPFields - [KSPField(isPersistant = true)]
        public double volume = 0.0f;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Volume")]
        public string volumeDisplay;

        /// <summary>
        /// Procedural parts sends messages with different volumes than we use. This is the scaling factor.
        /// </summary>
        [KSPField]
        public float ppVolumeScale = 867.7219117f;


        public double usedVolume
        {
            get
            {
                double v = 0;
                foreach (FuelTank fuel in tankList)
                {
                    if (fuel.maxAmount > 0 && fuel.utilization > 0)
                        v += fuel.maxAmount / fuel.utilization;
                }
                return v;
            }
        }

        public double availableVolume
        {
            get
            {
                return volume - usedVolume;
            }
        }

        [PartMessageListener(typeof(PartVolumeChanged), scenes: GameSceneFilter.AnyEditor)]
        private void PartVolumeChanged(string name, float volume)
        {
            if (name != PartVolumes.Tankage.ToString())
                return;

            // The event volume is in kL, we expect L
            double newVolume = Math.Round(volume * ppVolumeScale);

            if (newVolume == this.volume)
                return;

            double volumeRatio = newVolume / this.volume;
            this.volume = newVolume;

            // The used volume will rescale automatically when setting maxAmount
            foreach (FuelTank tank in tankList)
                tank.maxAmount *= volumeRatio;

            CalculateMass();
        }

        //called by StretchyTanks
#if deprecated
        public void ChangeVolume(double newVolume)
        {
            print("*MFS* Setting new volume " + newVolume);

            double oldUsedVolume = volume;
            if (availableVolume > 0.0001)
                oldUsedVolume = volume - availableVolume;

            double volRatio = newVolume / volume;
            double availVol = availableVolume * volRatio;
            if (availVol < 0.0001)
                availVol = 0;
            double newUsedVolume = newVolume - availVol;

            if (volume < newVolume)
                volume = newVolume; // do it now only if we're resizing up, else we'll fail to resize tanks.

            double ratio = newUsedVolume / oldUsedVolume;
            for (int i = 0; i < tankList.Count; i++)
            {
                ModuleFuelTanks.FuelTank tank = tankList[i];
                double oldMax = tank.maxAmount;
                double oldAmt = tank.amount;
                tank.maxAmount = oldMax * ratio;
                tank.amount = tank.maxAmount * ratio;
            }

            volume = newVolume; // update volume after tank resizes to avoid case where tank resizing clips new volume

            if (textFields != null)
                textFields.Clear();
            CalculateMass();
        }
#endif

        #endregion

        #region Mass

        [KSPField(isPersistant = true)]
        public float mass;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Mass")]
        public string massDisplay;

        // public so they copy
        public bool basemassOverride = false;
        public float basemassPV = 0.0f;
        public float basemassConst = 0.0f;

        [PartMessageEvent]
        public event PartMassChanged MassChanged;

        private static string FormatMass(float mass)
        {
            if (mass < 1.0f)
                return mass.ToStringSI(4, 6, "g");
            else
                return mass.ToStringSI(4, unit:"t");
        }

        private bool ParseBasemass(ConfigNode node)
        {
            if (!node.HasValue("basemass"))
                return false;

            string base_mass = node.GetValue("basemass");
            return ParseBasemass(base_mass);
        }

        private bool ParseBasemass(string base_mass)
        {
            if (base_mass.Contains("*") && base_mass.Contains("volume"))
            {
                if (float.TryParse(base_mass.Replace("volume", "").Replace("*", "").Trim(), out basemassPV))
                {
                    basemassConst = 0;
                    return true;
                }
            }
            else if (float.TryParse(base_mass.Trim(), out basemassPV))
            {
                basemassPV = (float)(basemassPV / volume);
                basemassConst = 0;
                return true;
            }
            Debug.LogWarning("[MFT] Unable to parse basemass \"" + base_mass + "\"");
            return false;
        }

        public void CalculateMass()
        {
            if (tankList == null)
                return;

            double basemass = basemassConst + basemassPV * volume;

            double tankDryMass = 0;
            if (basemass >= 0)
                foreach (FuelTank fuel in tankList)
                    if (fuel.maxAmount > 0 && fuel.utilization > 0)
                        tankDryMass += (float)fuel.maxAmount * fuel.mass / fuel.utilization * massMult; // NK for realistic masses

            mass = (float)(basemass + tankDryMass) * massMult;
            if (part.mass != mass)
            {
                part.mass = mass;
                MassChanged(mass);
            }

            if (GameSceneFilter.AnyEditor.IsLoaded())
            {
                Func<double, string> Formatter = volume.GetSIPrefix().GetFormatter(volume);
                volumeDisplay = "Avail: " + Formatter(availableVolume) + "L / Tot: " + Formatter(volume) + "L";

                double resourceMass = 0;
                foreach (PartResource r in part.Resources)
                    resourceMass += r.maxAmount * r.info.density;

                double wetMass = mass + resourceMass;
                massDisplay = "Dry: " + FormatMass(mass) + " / Wet: " + FormatMass((float)wetMass);

                UpdateTweakableMenu();
            }
        }

        #endregion

        #region Tank Type and Tank List management

        // The active fuel tanks. This will be the list from the tank type, with any overrides from the part file.
        public FuelTankList tankList = new FuelTankList();

        // List of tanks overriden in the part file (ie: not comming from the tank definition)
        public FuelTankList overrideList;

        // List of override nodes as defined in the part file. This is here so that it can get reconstituted after a clone
        public ConfigNode overrideListNodes;

        // TODO: make this switchable

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Tank Type"), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string type = null;
        private string oldType = null;

        public string[] typesAvailable;

        // for EngineIgnitor integration: store a public dictionary of all pressurized propellants
        public Dictionary<string, bool> pressurizedFuels;

        // Load the list of TANK overrides from the part file
        private void LoadTankListOverridesInLoading(ConfigNode node)
        {
            overrideList = new FuelTankList();
            overrideListNodes = new ConfigNode();

            foreach (ConfigNode tankNode in node.GetNodes("TANK"))
            {
                // we don't give it the back-ref to the module, because it's still an abstract tank
                FuelTank tank = new FuelTank(tankNode);
                overrideList.Add(tank);
                overrideListNodes.AddNode(tankNode);
            }
        }

        // Load the list of overrides as defined in the part file, in the editor.
        private void LoadTankListOverridesInEditor()
        {
            if (overrideList != null)
                return;
            // we don't give it the back-ref to the module, because it's still an abstract tank
            overrideList = new FuelTankList(overrideListNodes);
        }

        private void LoadConcreteTankList(ConfigNode node)
        {
            tankList = new FuelTankList(this, node);
        }

        private void InitializeTankType()
        {
            if(typesAvailable == null || typesAvailable.Length <= 1) 
            {
                Fields["type"].guiActiveEditor = false;
            }
            else
            {
                UI_ChooseOption typeOptions = (UI_ChooseOption)Fields["type"].uiControlEditor;
                typeOptions.options = typesAvailable;
            }

            Debug.LogWarning("TankTypes: " + string.Join(", ", typesAvailable));

            UpdateTankType();
        }

        private void UpdateTankType()
        {
            if (oldType == type || type == null)
                return;
            oldType = type;

            // Copy the tank list from the tank definitiion
            MFSSettings.TankDefinition def;
            try
            {
                def = MFSSettings.Instance.tankDefinitions[type];
            }
            catch (KeyNotFoundException)
            {
                Debug.LogError("Unable to find tank definition for type \"" + type + "\" reverting.");
                type = oldType;
                return;
            }

            tankList = new FuelTankList();
            def.tankList.CreateConcreteCopy(this, tankList);

            LoadTankListOverridesInEditor();
            overrideList.CreateConcreteCopy(this, tankList);

            // Update the basemass
            if (!basemassOverride)
                ParseBasemass(def.basemass);

            CalculateMass();

            if (GameSceneFilter.Loading.IsLoaded())
                return;

            // Clear the resource list
            foreach (PartResource res in part.Resources)
                Destroy(res);
            part.Resources.list.Clear();
            RaiseResourceListChanged();

            // for EngineIgnitor integration: store a public dictionary of all pressurized propellants
            pressurizedFuels = new Dictionary<string, bool>();
            foreach (FuelTank f in tankList)
                pressurizedFuels[f.name] = def.name == "ServiceModule" || f.note.ToLower().Contains("pressurized");
        }

        #endregion

        #region Resource messages

        [PartMessageEvent]
        public event PartResourceInitialAmountChanged ResourceInitialChanged;
        [PartMessageEvent]
        public event PartResourceMaxAmountChanged ResourceMaxChanged;
        [PartMessageEvent]
        public event PartResourceListChanged ResourceListChanged;

        internal void RaiseResourceInitialChanged(PartResource resource, double amount)
        {
            ResourceInitialChanged(resource, amount);
        }
        internal void RaiseResourceMaxChanged(PartResource resource, double amount)
        {
            ResourceMaxChanged(resource, amount);
        }
        internal void RaiseResourceListChanged()
        {
            ResourceListChanged();
        }

        [PartMessageListener(typeof(PartResourcesChanged))]
        private void PartResourcesChanged()
        {
            // We'll need to update the volume display regardless
            CalculateMass();
        }

        [KSPEvent(guiName = "Remove All Tanks", guiActive = false, guiActiveEditor = true, name = "Empty")]
        public void Empty()
        {
            using (PartMessageService.Instance.Consolidate(this))
            {
                foreach (ModuleFuelTanks.FuelTank tank in tankList)
                    tank.maxAmount = 0;
            }
        }

        #endregion

        #region Stuff I Haven't got to yet

        [KSPField(isPersistant = true)]
        public bool dedicated = false;

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

        #endregion

        #region GUI Display

        [KSPField(isPersistant = false, guiActiveEditor = true, guiActive = false, guiName = "Real Fuels"),
         UI_Toggle(enabledText = "GUI", disabledText = "GUI")]
        public bool showRFGUI = false;

        private static GUIStyle unchanged = null;
        private static GUIStyle changed = null;
        private static GUIStyle greyed = null;
        private static GUIStyle overfull = null;
        public static string myToolTip = "";

        private List<string> mixtures = new List<string>();

		private int counterTT = 0;
        private Vector2 scrollPos;

        private void OnPartActionGuiDismiss(Part p)
        {
            if (p == part)
                showRFGUI = false;
        }

        [PartMessageListener(typeof(PartResourceListChanged))]
        private void MarkWindowDirty()
        {
            if (_myWindow == null)
                _myWindow = part.FindActionWindow();
            if(_myWindow == null)
                return;
            _myWindow.displayDirty = true;
        }
        private UIPartActionWindow _myWindow;

        [PartMessageListener(typeof(PartChildAttached), relations: PartRelationship.AnyPart, scenes: GameSceneFilter.AnyEditor)]
        [PartMessageListener(typeof(PartChildDetached), relations: PartRelationship.AnyPart, scenes: GameSceneFilter.AnyEditor)]
        private void VesselAttachmentsChanged(Part part)
        {
            UpdateTweakableMenu();
        }

        public void OnGUI()
		{
			EditorLogic editor = EditorLogic.fetch;
            if (!HighLogic.LoadedSceneIsEditor || !editor) {
                return;
            }

            //UpdateMixtures();

            Rect screenRect;
            if (editor.editorScreen == EditorLogic.EditorScreen.Actions && EditorActionGroups.Instance.GetSelectedParts ().Contains (part)) 
            {
				//Rect screenRect = new Rect(0, 365, 430, (Screen.height - 365));
				screenRect = new Rect(0, 365, 438, (Screen.height - 365));
			}
            else if (showRFGUI && editor.editorScreen == EditorLogic.EditorScreen.Parts)
            {
                screenRect = new Rect((Screen.width - 438), 365, 438, (Screen.height - 365));
            }
            else 
            {
                showRFGUI = false;
                return;
            }

            //Color reset = GUI.backgroundColor;
            //GUI.backgroundColor = Color.clear;
            GUILayout.Window(part.name.GetHashCode(), screenRect, GUIWindow, "Fuel Tanks for " + part.partInfo.title);
            //GUI.backgroundColor = reset;

            //if(!(myToolTip.Equals("")))
            GUI.Label(new Rect(440, Screen.height - Input.mousePosition.y, 300, 20), myToolTip);
		}

        public void GUIWindow(int WindowID)
        {
            InitializeStyles();

            GUILayout.BeginVertical();
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Mass: " + massDisplay);
                GUILayout.EndHorizontal();

                if (tankList.Count == 0)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("This fuel tank cannot hold resources.");
                    GUILayout.EndHorizontal();
                    return;
                }

                GUILayout.BeginHorizontal();
                if (Math.Round(availableVolume, 4) < 0)
                    GUILayout.Label("Volume: " + volumeDisplay, overfull);
                else
                    GUILayout.Label("Volume: " + volumeDisplay);
                GUILayout.EndHorizontal();

                scrollPos = GUILayout.BeginScrollView(scrollPos);

                GUITanks();

                GUIEngines();

                GUILayout.EndScrollView();
            }
            GUILayout.EndVertical();

            if (!(myToolTip.Equals("")) && GUI.tooltip.Equals(""))
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
            //print("GT: " + GUI.tooltip);
        }

        private static void InitializeStyles()
        {
            if (unchanged == null)
            {
                if (GUI.skin == null)
                {
                    unchanged = new GUIStyle();
                    changed = new GUIStyle();
                    greyed = new GUIStyle();
                    overfull = new GUIStyle();
                }
                else
                {
                    unchanged = new GUIStyle(GUI.skin.textField);
                    changed = new GUIStyle(GUI.skin.textField);
                    greyed = new GUIStyle(GUI.skin.textField);
                    overfull = new GUIStyle(GUI.skin.label);
                }

                unchanged.normal.textColor = Color.white;
                unchanged.active.textColor = Color.white;
                unchanged.focused.textColor = Color.white;
                unchanged.hover.textColor = Color.white;

                changed.normal.textColor = Color.yellow;
                changed.active.textColor = Color.yellow;
                changed.focused.textColor = Color.yellow;
                changed.hover.textColor = Color.yellow;

                greyed.normal.textColor = Color.gray;

                overfull.normal.textColor = Color.red;
            }
        }

        private void GUITanks()
        {
            foreach (ModuleFuelTanks.FuelTank tank in tankList)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(" " + tank, GUILayout.Width(120));



                // So our states here are:
                //   Not being edited currently(empty):    maxAmountExpression = null, maxAmount = 0
                //   Have updated the field, no user edit: maxAmountExpression == maxAmount.ToStringExt
                //   Other non UI updated maxAmount:       maxAmountExpression = null(set), maxAmount = non-zero
                //   User has updated the field:           maxAmountExpression != null, maxAmountExpression != maxAmount.ToStringExt

                if (part.Resources.Contains(tank.name) && part.Resources[tank.name].maxAmount > 0)
                {
                    GUILayout.Label(" ", GUILayout.Width(5));

                    GUIStyle style = unchanged;
                    if (tank.maxAmountExpression == null)
                    {
                        tank.maxAmountExpression = tank.maxAmount.ToStringExt("S4");
                        Debug.LogWarning("[MFT] Adding tank from API " + tank.name + " amount: " + tank.maxAmountExpression ?? "null");
                    }
                    else if (tank.maxAmountExpression.Length > 0 && tank.maxAmountExpression != tank.maxAmount.ToStringExt("S4"))
                    {
                        style = changed;
                    }

                    tank.maxAmountExpression = GUILayout.TextField(tank.maxAmountExpression, style, GUILayout.Width(140));

                    if (GUILayout.Button("Update", GUILayout.Width(60)))
                    {
                        double tmp;
                        string trimmed = tank.maxAmountExpression.Trim();

                        // TODO: Allow for expressions in mass, volume, or percentage
#if false
                        {
                            char unit = 'L';
                            if (trimmed.Length > 0)
                                switch (unit = trimmed[trimmed.Length - 1])
                                {
                                    case 'l':
                                    case 'L':
                                        unit = 'L'; // Liters defaults to uppercase
                                        trimmed = trimmed.Substring(0, trimmed.Length - 1);
                                        break;
                                    case 't':
                                    case 'T':
                                        unit = 't'; // Tons defaults to lowercase
                                        trimmed = trimmed.Substring(0, trimmed.Length - 1);
                                        break;
                                    case 'g':
                                        // Cannot use 'G' as collides with giga. And anyhow that would be daft.
                                        trimmed = trimmed.Substring(0, trimmed.Length - 1);
                                        break;
                                }
                        }
#endif

                        if (trimmed == "")
                        {
                            tank.maxAmount = 0;
                            Debug.LogWarning("[MFT] Removing tank as empty input " + tank.name + " amount: " + tank.maxAmountExpression ?? "null");
                        }
                        else
                        {
                            if (MathUtils.TryParseExt(trimmed, out tmp))
                            {
                                tank.maxAmount = tmp;
                                if (tmp != 0)
                                {
                                    if (!tank.fillable)
                                        tank.amount = 0;
                                    else
                                        tank.amount = tank.maxAmount;

                                    // Need to round-trip the value
                                    tank.maxAmountExpression = tank.maxAmount.ToStringExt("S4");
                                    Debug.LogWarning("[MFT] Updating maxAmount " + tank.name + " amount: " + tank.maxAmountExpression ?? "null");
                                }
                            }
                        }
                    }
                    if (GUILayout.Button("Remove", GUILayout.Width(60)))
                    {
                        tank.maxAmount = 0;
                        Debug.LogWarning("[MFT] Removing tank from button " + tank.name + " amount: " + tank.maxAmountExpression ?? "null");
                    }
                }
                else if (availableVolume >= 0.001)
                {
                    string extraData = "Max: " + (availableVolume * tank.utilization).ToStringExt("S3") + "L (+" + FormatMass((float)(availableVolume * tank.mass)) + " )";

                    GUILayout.Label(extraData, GUILayout.Width(150));

                    if (GUILayout.Button("Add", GUILayout.Width(130)))
                    {
                        tank.maxAmount = availableVolume * tank.utilization;
                        if (tank.fillable)
                            tank.amount = tank.maxAmount;
                        else
                            tank.amount = 0;

                        tank.maxAmountExpression = tank.maxAmount.ToStringExt("S4");
                        Debug.LogWarning("[MFT] Adding tank " + tank.name + " maxAmount: " + tank.maxAmountExpression ?? "null");
                    }
                }
                else
                {
                    GUILayout.Label("  No room for tank.", GUILayout.Width(150));

                }
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Remove All Tanks"))
            {
                Empty();
            }
            GUILayout.EndHorizontal();
        }

        private void GUIEngines()
        {
            List<Part> enginesList = GetEnginesFedBy(part);

            if (enginesList.Count > 0 && availableVolume >= 0.001)
            {
                Dictionary<string, FuelInfo> usedBy = new Dictionary<string, FuelInfo>();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Configure remaining volume for " + enginesList.Count + " engines:");
                GUILayout.EndHorizontal();

                foreach (Part engine in enginesList)
                {
                    FuelInfo f = new FuelInfo(engine, this);
                    if (f.ratio_factor > 0.0)
                    {
                        if (!usedBy.ContainsKey(f.label))
                        {
                            usedBy.Add(f.label, f);
                        }
                        else if (!usedBy[f.label].names.Contains(engine.partInfo.title))
                        {
                            usedBy[f.label].names += ", " + engine.partInfo.title;
                        }
                    }
                }
                if (usedBy.Count > 0)
                {
                    foreach (string label in usedBy.Keys)
                    {
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button(new GUIContent(label, usedBy[label].names)))
                        {
                            ConfigureFor(usedBy[label]);
                        }
                        GUILayout.EndHorizontal();
                    }
                }
            }
        }


        private void UpdateMixtures()
        {
            bool dirty = false;
            List<string> new_mixtures = new List<string>();
            foreach (Part engine in GetEnginesFedBy(part))
            {
                FuelInfo fi = new FuelInfo(engine, this);
                if (fi.ratio_factor > 0 && !new_mixtures.Contains(fi.label))
                {
                    new_mixtures.Add(fi.label);
                    if (!mixtures.Contains(fi.label))
                        dirty = true;
                }
            }
            foreach (string label in mixtures)
            {
                if (!new_mixtures.Contains(label))
                    dirty = true;
            }
            if (dirty)
            {
                UpdateTweakableMenu();
                mixtures = new_mixtures;
            }
        }

        public class FuelInfo
		{
			public string names;
			public List<Propellant> propellants;
			public double efficiency;
			public double ratio_factor;

            public string label
            {
                get
                {
                    string label = "";
                    foreach (Propellant tfuel in propellants)
                    {
                        if (PartResourceLibrary.Instance.GetDefinition(tfuel.name).resourceTransferMode != ResourceTransferMode.NONE)
                        {
                            if (label.Length > 0)
                                label += " / ";
                            label += Math.Round(100 * tfuel.ratio / ratio_factor, 0).ToString() + "% " + tfuel.name;
                        }
                    }
                    return label;
                }
            }

            public FuelInfo(Part engine, ModuleFuelTanks tank)
            {
                // tank math:
                // efficiency = sum[utilization * ratio]
                // then final volume per fuel = fuel_ratio / fuel_utilization / efficiency

                ratio_factor = 0.0;
                efficiency = 0.0;

                propellants = new List<Propellant>();
                if (engine.Modules.Contains("ModuleEnginesFX"))
                {
                    ModuleEnginesFX e = (ModuleEnginesFX)engine.Modules["ModuleEnginesFX"];
                    foreach (Propellant p in e.propellants)
                        propellants.Add(p);
                }
                else if (engine.Modules.Contains("ModuleEngines"))
                {
                    ModuleEngines e = (ModuleEngines)engine.Modules["ModuleEngines"];
                    foreach (Propellant p in e.propellants)
                        propellants.Add(p);
                }

                foreach (Propellant tfuel in propellants)
                {
                    if (PartResourceLibrary.Instance.GetDefinition(tfuel.name) == null)
                    {
                        print("Unknown RESOURCE {" + tfuel.name + "}");
                        ratio_factor = 0.0;
                        break;
                    }
                    else if (PartResourceLibrary.Instance.GetDefinition(tfuel.name).resourceTransferMode == ResourceTransferMode.NONE)
                    {
                        //ignore this propellant, since it isn't serviced by fuel tanks
                    }
                    else
                    {
                        FuelTank t;
                        if (tank.tankList.TryGet(tfuel.name, out t))
                        {
                            efficiency += tfuel.ratio / t.utilization;
                            ratio_factor += tfuel.ratio;
                        }
                        else if (!IgnoreFuel(tfuel.name))
                        {
                            ratio_factor = 0.0;
                            break;
                        }
                    }
                }
                this.names = "Used by: " + engine.partInfo.title;
            }
		}

        private void UpdateTweakableMenu()
        {
            Events["Empty"].guiActiveEditor = (usedVolume != 0);

            if (HighLogic.LoadedSceneIsEditor && !dedicated)
            {
                Events.RemoveAll(button => button.name.StartsWith("MFT"));                

                if (availableVolume >= 0.001)
                {
                    List<string> labels = new List<string>();
                    foreach(Part engine in GetEnginesFedBy(part))
                    {
                        FuelInfo f = new FuelInfo(engine, this);
                        int i = 0;
                        if (f.ratio_factor > 0.0)
                        {
                            if (!labels.Contains(f.label))
                            {
                                labels.Add(f.label);
                                KSPEvent kspEvent = new KSPEvent();
                                kspEvent.name = "MFT" + (++i).ToString();
                                kspEvent.guiActive = false;
                                kspEvent.guiActiveEditor = true;
                                kspEvent.guiName = f.label;
                                BaseEvent button = new BaseEvent(Events, kspEvent.name, () =>
                                {
                                    ConfigureFor(engine);
                                }, kspEvent);
                                button.guiActiveEditor = true;
                                Events.Add(button);
                            }
                        }
                    }
                }
                MarkWindowDirty();
            }
        }

        public void ConfigureFor(Part engine)
        {
            ConfigureFor(new FuelInfo(engine, this));
        }

        public void ConfigureFor(FuelInfo fi)
        {
            if (fi.ratio_factor == 0.0 || fi.efficiency == 0) // can't configure for this engine
                return;

            double total_volume = availableVolume;
            foreach (Propellant tfuel in fi.propellants)
            {
                if (PartResourceLibrary.Instance.GetDefinition(tfuel.name).resourceTransferMode != ResourceTransferMode.NONE)
                {
                    ModuleFuelTanks.FuelTank tank;
                    if (tankList.TryGet(tfuel.name, out tank))
                    {
                        double amt = total_volume * tfuel.ratio / fi.efficiency;
                        tank.maxAmount += amt;
                        tank.amount += amt;
                    }
                }
            }
        }

		public static List<Part> GetEnginesFedBy(Part part)
		{
			Part ppart = part;
			while (ppart.parent != null && ppart.parent != ppart)
				ppart = ppart.parent;

            return new List<Part>(ppart.FindChildParts<Part>(true)).FindAll(p => (p.Modules.Contains("ModuleEngines") || p.Modules.Contains("ModuleEnginesFX")));
		}

        #endregion
	}
}
