using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using System.Collections.ObjectModel;
using KSPAPIExtensions;
using KSPAPIExtensions.PartMessage;

// ReSharper disable InconsistentNaming, CompareOfFloatsByEqualityOperator

namespace RealFuels
{
    public class ModuleFuelTanks : ModularFuelPartModule
    {
        #region loading stuff from config files

        private static float MassMult
        {
            get
            {
                return MFSSettings.Instance.useRealisticMass ? 1.0f : MFSSettings.Instance.tankMassMultiplier;
            }
        }

        // looks to see if we should ignore this fuel when creating an autofill for an engine
        private static bool IgnoreFuel(string name)
        {
            return MFSSettings.Instance.ignoreFuelsForFill.Contains(name);
        }

        private static MFSSettings Settings
        {
            get { return MFSSettings.Instance; }
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

            public bool resourceAvailable;

            internal string amountExpression;
            internal string maxAmountExpression;

            [NonSerialized]
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
                    return resource.amount;
                }
                set {
                    if (module == null)
                        throw new InvalidOperationException("Amount is not defined until instantiated in a tank");

                    PartResource partResource = resource;
                    if (partResource == null)
                        return;

                    if (value > partResource.maxAmount)
                        value = partResource.maxAmount;

                    if (value == partResource.amount)
                        return;

                    amountExpression = null;
                    partResource.amount = value;
                    if (HighLogic.LoadedSceneIsEditor)
                    {
                        module.RaiseResourceInitialChanged(partResource, amount);
                        foreach (Part sym in part.symmetryCounterparts)
                        {
                            PartResource symResc = sym.Resources[name];
                            symResc.amount = value;
                            PartMessageService.Send<PartResourceInitialAmountChanged>(this, sym, symResc, amount);
                        }
                        
                    }
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

                    PartResource partResource = resource;
                    if (partResource != null && value <= 0.0) 
                    {
                        // Delete it
                        //Debug.LogWarning("[MFT] Deleting tank from API " + name);
                        maxAmountExpression = null;

                        Destroy(partResource);
                        part.Resources.list.Remove(partResource);
                        module.RaiseResourceListChanged();

                        // Update symmetry counterparts.
                        if (HighLogic.LoadedSceneIsEditor)
                            foreach (Part sym in part.symmetryCounterparts)
                            {
                                PartResource symResc = sym.Resources[name];
                                Destroy(symResc);
                                sym.Resources.list.Remove(symResc);
                                PartMessageService.Send<PartResourceListChanged>(this, sym);
                            }
                    } 
                    else if (partResource != null) 
                    {
                        if (value > partResource.maxAmount)
                        {
                            // If expanding, modify it to be less than overfull
                            double maxQty = module.AvailableVolume * utilization + partResource.maxAmount;
                            if (maxQty < value)
                                value = maxQty;
                        }

                        // Do nothing if unchanged
                        if (value == partResource.maxAmount)
                            return;

                        //Debug.LogWarning("[MFT] Updating tank from API " + name + " amount: " + value);
                        maxAmountExpression = null;

                        // Keep the same fill fraction
                        double newAmount = value * fillFraction;

                        partResource.maxAmount = value;
                        module.RaiseResourceMaxChanged(partResource, value);

                        if (newAmount != partResource.amount)
                        {
                            partResource.amount = newAmount;
                            module.RaiseResourceInitialChanged(partResource, newAmount);
                        }

                        // Update symmetry counterparts.
                        if (HighLogic.LoadedSceneIsEditor)
                            foreach (Part sym in part.symmetryCounterparts)
                            {
                                PartResource symResc = sym.Resources[name];
                                symResc.maxAmount = value;
                                PartMessageService.Send<PartResourceMaxAmountChanged>(this, sym, symResc, value);

                                if (newAmount != symResc.amount)
                                {
                                    symResc.amount = newAmount;
                                    PartMessageService.Send<PartResourceInitialAmountChanged>(this, sym, symResc, newAmount);
                                }
                            }

                    } 
                    else if(value > 0.0) 
                    {
                        //Debug.LogWarning("[MFT] Adding tank from API " + name + " amount: " + value);
                        maxAmountExpression = null;

                        ConfigNode node = new ConfigNode("RESOURCE");
                        node.AddValue ("name", name);
                        node.AddValue ("amount", value);
                        node.AddValue ("maxAmount", value);
#if DEBUG
                        print (node.ToString ());
#endif
                        partResource = part.AddResource (node);
                        partResource.enabled = true;

                        module.RaiseResourceListChanged();

                        // Update symmetry counterparts.
                        if (HighLogic.LoadedSceneIsEditor)
                            foreach (Part sym in part.symmetryCounterparts)
                            {
                                PartResource symResc = sym.AddResource(node);
                                symResc.enabled = true;
                                PartMessageService.Send<PartResourceListChanged>(this, sym);
                            }

                    }
                    module.massDirty = true;
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

                amountExpression = node.GetValue("amount") ?? amountExpression;
                maxAmountExpression = node.GetValue("maxAmount") ?? maxAmountExpression;

                resourceAvailable = PartResourceLibrary.Instance.GetDefinition(name) != null;
            }

            internal void InitializeAmounts()
            {
                if (module == null)
                    return;

                double v;
                if (maxAmountExpression == null)
                {
                    maxAmount = 0;
                    amount = 0;
                    return;
                }

                if (maxAmountExpression.Contains("%") && double.TryParse(maxAmountExpression.Replace("%", "").Trim(), out v))
                    maxAmount = v * utilization * module.volume * 0.01; // NK
                else if (double.TryParse(maxAmountExpression, out v))
                    maxAmount = v;
                else
                {
                    Debug.LogError("Unable to parse max amount expression: " + maxAmountExpression + " for tank " + name);
                    maxAmount = 0;
                    amount = 0;
                    maxAmountExpression = null;
                    return;
                }
                maxAmountExpression = null;

                if (amountExpression == null) 
                {
                    amount = maxAmount;
                    return;
                }

                if (amountExpression.Equals("full"))
                    amount = maxAmount;
                else if (amountExpression.Contains("%") && double.TryParse(amountExpression.Replace("%", "").Trim(), out v))
                    amount = v * maxAmount * 0.01;
                else if (double.TryParse(amountExpression, out v))
                    amount = v;
                else
                {
                    amount = maxAmount;
                    Debug.LogError("Unable to parse amount expression: " + amountExpression + " for tank " + name);
                }
                amountExpression = null;
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
            }

            //------------------- Constructor
            public FuelTank(ConfigNode node)
            {
                Load(node);
            }

            internal FuelTank CreateCopy(ModuleFuelTanks toModule, ConfigNode overNode, bool initializeAmounts)
            {
                FuelTank clone = (FuelTank)MemberwiseClone();
                clone.module = toModule;

                if (overNode != null)
                    clone.Load(overNode);

                if(initializeAmounts)
                    clone.InitializeAmounts();
                else
                    clone.amountExpression = clone.maxAmountExpression = null;

                return clone;
            }
        }

        public class FuelTankList : KeyedCollection<string, FuelTank>, IConfigNode
        {
            public FuelTankList()
            {
            }

            public FuelTankList(ConfigNode node)
            {
                Load(node);
            }

            protected override string GetKeyForItem(FuelTank item)
            {
                return item.name;
            }

            public void Load(ConfigNode node)
            {
                if (node == null)
                    return;
                foreach (ConfigNode tankNode in node.GetNodes("TANK"))
                {
                    Add(new FuelTank(tankNode));
                }
            }

            public void Save(ConfigNode node)
            {
                foreach (FuelTank tank in this)
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
            try
            {
                base.OnAwake();
                PartMessageService.Register(this);
                this.RegisterOnUpdateEditor(OnUpdateEditor);
                if(HighLogic.LoadedSceneIsEditor)
                    GameEvents.onPartActionUIDismiss.Add(OnPartActionGuiDismiss);

                // Initialize utilization from the settings file
                utilization = Settings.partUtilizationDefault;

                // This will be removed soon.
                oldmass = part.mass;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public override void OnInactive()
        {
            try
            {
                if (HighLogic.LoadedSceneIsEditor)
                    GameEvents.onPartActionUIDismiss.Remove(OnPartActionGuiDismiss);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            try
            {
                // Load the volume. If totalVolume is specified, use that to calc the volume
                // otherwise scale up the provided volume. No KSPField support for doubles
                if (node.HasValue("totalVolume") && double.TryParse(node.GetValue("totalVolume"), out totalVolume))
                {
                    ChangeTotalVolume(totalVolume);
                } 
                else if (node.HasValue("volume") && double.TryParse(node.GetValue("volume"), out volume))
                {
                    totalVolume = volume * 100 / utilization;
                }

                using (PartMessageService.Instance.Ignore(this, null, typeof(PartResourcesChanged)))
                {
                    if (GameSceneFilter.Loading.IsLoaded())
                    {
                        LoadTankListOverridesInLoading(node);

                        ParseBasemass(node);

                        typesAvailable = node.GetValues("typeAvailable");
                    }
                    else if (GameSceneFilter.AnyEditorOrFlight.IsLoaded())
                    {
                        // The amounts initialized flag is there so that the tank type loading doesn't
                        // try to set up any resources. They'll get loaded directly from the save.
                        UpdateTankType(false);
                        
                        // Destroy any resources still hanging around from the LOADING phase
                        for (int i = part.Resources.Count - 1; i >= 0; --i)
                        {
                            PartResource partResource = part.Resources[i];
                            if (!tankList.Contains(partResource.resourceName))
                                continue;
                            DestroyImmediate(partResource);
                            part.Resources.list.RemoveAt(i);
                        }
                        RaiseResourceListChanged();

                        // Setup the mass
                        part.mass = mass;
                        MassChanged(mass);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public override string GetInfo()
        {
            try
            {
                UpdateTankType();


                if (dedicated)
                    return string.Empty;

                StringBuilder info = new StringBuilder();
                info.AppendLine("Modular Fuel Tank:");
                info.Append("  Max Volume: ").Append(volume.ToStringSI(unit: "L"));
                info.AppendLine("  Tank can hold:");
                foreach (FuelTank tank in tankList)
                    info.Append("   ").Append(tank).Append(" ").AppendLine(tank.note);
                return info.ToString();

            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return string.Empty;
            }
        }

        // This flag lets us know if this is a symmetry copy or clone in the vab.
        public override void OnStart(StartState state)
        {
            // This won't do anything if it's already been done in OnLoad (stored vessel/assem)
            if (GameSceneFilter.AnyEditor.IsLoaded())
            {
                if (part.isClone)
                {
                    UpdateTankType(false);
                    massDirty = true;
                }

                InitializeTankType();
                InitializeUtilization();

                if (dedicated)
                {
                    Events["Empty"].guiActiveEditor = false;
                    Fields["showRFGUI"].guiActiveEditor = false;
                }
            }

            CalculateMass();
        }

        public override void OnSave (ConfigNode node)
        {
            try
            {
                base.OnSave(node);

                node.AddValue("volume", volume.ToString("G17")); // no KSPField support for doubles
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
        #endregion

        #region Update

        public void OnUpdateEditor()
        {
            try
            {
                UpdateTankType();
                UpdateUtilization();
                CalculateMass();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                isEnabled = enabled = false;
            }
        }

        public override void OnUpdate()
        {
            try
            {
                if (timestamp > 0)
                    CalculateTankLossFunction(precisionDeltaTime);
                // Needs to be at the end to prevent weird things from happening during startup and to make handling persistance easy; 
                // this does mean that the effects are delayed by a frame, but since they're constant, that shouldn't matter here
                base.OnUpdate();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                isEnabled = enabled = false;
            }
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

        #region Tank Type and Tank List management

        // The active fuel tanks. This will be the list from the tank type, with any overrides from the part file.
        internal FuelTankList tankList = new FuelTankList();

        // List of override nodes as defined in the part file. This is here so that it can get reconstituted after a clone
        public ConfigNode [] overrideListNodes;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Tank Type"), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string type;
        private string oldType;

        public string[] typesAvailable;

        // for EngineIgnitor integration: store a public list of the fuel tanks, and 
        [NonSerialized]
        public List<FuelTank> fuelList = new List<FuelTank>();
        // for EngineIgnitor integration: store a public dictionary of all pressurized propellants
        [NonSerialized]
        public Dictionary<string, bool> pressurizedFuels = new Dictionary<string, bool>();

        // Load the list of TANK overrides from the part file
        private void LoadTankListOverridesInLoading(ConfigNode node)
        {
            overrideListNodes = node.GetNodes("TANK");
        }

        private void InitializeTankType()
        {
            if (typesAvailable == null || typesAvailable.Length <= 1)
            {
                Fields["type"].guiActiveEditor = false;
            }
            else
            {
                UI_ChooseOption typeOptions = (UI_ChooseOption)Fields["type"].uiControlEditor;
                typeOptions.options = typesAvailable;
            }

            UpdateTankType();
        }

        private void UpdateTankType(bool initializeAmounts = true)
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

            FuelTankList oldList = tankList;

            // Build the new tank list.
            tankList = new FuelTankList();
            foreach (FuelTank tank in def.tankList)
            {
                // Pull the override from the list of overrides
                ConfigNode overNode = overrideListNodes.FirstOrDefault(n => n.GetValue("name") == tank.name);

                tankList.Add(tank.CreateCopy(this, overNode, initializeAmounts));
            }

            // Destroy resources that are in either the new or the old type.
            bool needsMesage = false;
            for (int i = part.Resources.Count - 1; i >= 0; --i)
            {
                PartResource partResource = part.Resources[i];
                if (!tankList.Contains(partResource.name) || oldList == null || !oldList.Contains(partResource.name))
                    continue;
                Destroy(partResource);
                part.Resources.list.RemoveAt(i);
                needsMesage = true;
            }
            if(needsMesage)
                RaiseResourceListChanged();

            // Update the basemass
            if (!basemassOverride)
                ParseBasemass(def.basemass);

            if (GameSceneFilter.Loading.IsLoaded())
                return;

            // for EngineIgnitor integration: store a public list of all pressurized propellants
            // Dirty hack until engine ignitor is fixed
            fuelList.Clear();
            fuelList.AddRange(tankList);
            // for EngineIgnitor integration: store a public dictionary of all pressurized propellants
            pressurizedFuels.Clear();
            foreach (FuelTank f in tankList)
                pressurizedFuels[f.name] = def.name == "ServiceModule" || f.note.ToLower().Contains("pressurized");

            massDirty = true;
        }

        #endregion

        #region Volume

        // The total tank volume. This is prior to utilization
        public double totalVolume;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Utilization", guiUnits = "%", guiFormat = "F0"),
         UI_FloatEdit(minValue = 0, maxValue = 100, incrementSlide = 1, scene = UI_Scene.Editor)]
        public float utilization = -1;
        private float oldUtilization = -1;


        // no double support for KSPFields - [KSPField(isPersistant = true)]
        public double volume;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Volume")]
        public string volumeDisplay;

        public double UsedVolume
        {
            get; private set;
        }

        public double AvailableVolume
        {
            get
            {
                return volume - UsedVolume;
            }
        }

        // Conversion between tank volume in kL, and whatever units this tank uses.
        // Default to 1000 for RF. Varies for MFT. Needed to interface with PP.
        [KSPField]
        public float tankVolumeConversion = 1000;

        [PartMessageListener(typeof(PartVolumeChanged), scenes: GameSceneFilter.AnyEditor)]
        public void PartVolumeChanged(string volName, float newTotalVolume)
        {
            if (volName != PartVolumes.Tankage.ToString())
                return;

            double newTotalVolue = newTotalVolume * tankVolumeConversion;

            if (newTotalVolue == totalVolume)
                return;

            ChangeTotalVolume(newTotalVolue);
        }

        //called by StretchyTanks
        public void ChangeVolume(double newVolume)
        {
            ChangeTotalVolume(newVolume * 100 / utilization);
        }

        public void ChangeTotalVolume(double newTotalVolume)
        {
            totalVolume = newTotalVolume;

            double newVolume = Math.Round(newTotalVolume * utilization / 100);

            double volumeRatio = newVolume / volume;
            volume = newVolume;

            // The used volume will rescale automatically when setting maxAmount
            foreach (FuelTank tank in tankList)
                tank.maxAmount *= volumeRatio;

            massDirty = true;
        }

        private void UpdateUtilization()
        {
            if (oldUtilization == utilization)
                return;

            oldUtilization = utilization;

            ChangeTotalVolume(totalVolume);            
        }

        private void InitializeUtilization() 
        {
            Fields["utilization"].guiActiveEditor = Settings.partUtilizationTweakable;
        }

        #endregion

        #region Mass

        [KSPField(isPersistant = true)]
        public float mass;
        internal bool massDirty = true;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Mass")]
        public string massDisplay;

        // public so they copy
        public bool basemassOverride;
        public float basemassPV;
        public float basemassConst;

        [PartMessageEvent]
        public event PartMassChanged MassChanged;

        private static string FormatMass(float mass)
        {
            if (mass < 1.0f)
                return mass.ToStringSI(4, 6, "g");
            return mass.ToStringSI(4, unit:"t");
        }

        private void ParseBasemass(ConfigNode node)
        {
            if (!node.HasValue("basemass"))
                return;

            string baseMass = node.GetValue("basemass");
            ParseBasemass(baseMass);
            basemassOverride = true;
        }

        private void ParseBasemass(string baseMass)
        {
            if (baseMass.Contains("*") && baseMass.Contains("volume"))
            {
                if (float.TryParse(baseMass.Replace("volume", "").Replace("*", "").Trim(), out basemassPV))
                {
                    basemassConst = 0;
                    return;
                }
            }
            else if (float.TryParse(baseMass.Trim(), out basemassPV))
            {
                basemassPV = (float)(basemassPV / volume);
                basemassConst = 0;
                return;
            }
            Debug.LogWarning("[MFT] Unable to parse basemass \"" + baseMass + "\"");
        }

        public void CalculateMass()
        {
            if (tankList == null || !massDirty)
                return;
            massDirty = false;

            double basemass = basemassConst + basemassPV * volume;

            if (basemass >= 0)
            {
                double tankDryMass = tankList
                    .Where(fuel => fuel.maxAmount > 0 && fuel.utilization > 0)
                    .Sum(fuel => (float)fuel.maxAmount * fuel.mass / fuel.utilization);

                mass = (float)(basemass + tankDryMass) * MassMult;

                if (part.mass != mass)
                {
                    part.mass = mass;
                    MassChanged(mass);
                }
            }
            else
                mass = part.mass; // display dry mass even in this case.

            if (GameSceneFilter.AnyEditor.IsLoaded())
            {
                UsedVolume = tankList
                    .Where(fuel => fuel.maxAmount > 0 && fuel.utilization > 0)
                    .Sum(fuel => fuel.maxAmount/fuel.utilization);

                SIPrefix pfx = volume.GetSIPrefix();
                Func<double, string> formatter = pfx.GetFormatter(volume);
                volumeDisplay = "Avail: " + formatter(AvailableVolume) + pfx.PrefixString() + "L / Tot: " + formatter(volume) + pfx.PrefixString() + "L";

                double resourceMass = part.Resources.Cast<PartResource>().Sum(r => r.maxAmount*r.info.density);

                double wetMass = mass + resourceMass;
                massDisplay = "Dry: " + FormatMass(mass) + " / Wet: " + FormatMass((float)wetMass);

                UpdateTweakableMenu();
            }
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
        public void PartResourcesChanged()
        {
            // We'll need to update the volume display regardless
            massDirty = true;
        }

        [KSPEvent(guiName = "Remove All Tanks", guiActive = false, guiActiveEditor = true, name = "Empty")]
        public void Empty()
        {
            using (PartMessageService.Instance.Consolidate(this))
            {
                foreach (FuelTank tank in tankList)
                    tank.maxAmount = 0;
            }
        }

        #endregion

        #region Message passing for EPL

        // Extraplanetary launchpads needs these messages sent.
        // From the next update of EPL, this won't be required.

        [PartMessageListener(typeof(PartResourcesChanged))]
        public void ResourcesModified()
        {
            BaseEventData data = new BaseEventData (BaseEventData.Sender.USER);
            data.Set ("part", part);
            part.SendEvent ("OnResourcesModified", data, 0);
        }

        private float oldmass;

        [PartMessageListener(typeof(PartMassChanged))]
        public void MassModified(float newMass)
        {
            BaseEventData data = new BaseEventData (BaseEventData.Sender.USER);
            data.Set ("part", part);
            data.Set<float> ("oldmass", oldmass);
            part.SendEvent ("OnMassModified", data, 0);

            oldmass = newMass;
        }

        #endregion

        #region GUI Display

        [KSPField(isPersistant = true)]
        public bool dedicated = false;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiActive = false, guiName = "Show Tank"),
         UI_Toggle(enabledText = "Tank GUI", disabledText = "GUI")]
        [NonSerialized]
        public bool showRFGUI;

        private static GUIStyle unchanged;
        private static GUIStyle changed;
        private static GUIStyle greyed;
        private static GUIStyle overfull;
        public static string myToolTip = "";

        private int counterTT;
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
        public void VesselAttachmentsChanged(Part childPart)
        {
            UpdateUsedBy();
        }

        [PartMessageListener(typeof (PartEngineConfigChanged), relations: PartRelationship.AnyPart, scenes: GameSceneFilter.AnyEditor)]
        public void EngineConfigsChanged()
        {
            UpdateUsedBy();
        }

        public void OnGUI()
        {
            EditorLogic editor = EditorLogic.fetch;
            if (!HighLogic.LoadedSceneIsEditor || !editor || dedicated) {
                return;
            }

            //UpdateMixtures();

            Rect screenRect;
            Rect tooltipRect;
            if (editor.editorScreen == EditorLogic.EditorScreen.Actions && EditorActionGroups.Instance.GetSelectedParts ().Contains (part)) 
            {
                screenRect = new Rect(0, 365, 438, (Screen.height - 365));
                tooltipRect = new Rect(440, Screen.height - Input.mousePosition.y, 300, 20);
            }
            else if (showRFGUI && editor.editorScreen == EditorLogic.EditorScreen.Parts)
            {
                screenRect = new Rect((Screen.width - 438), 365, 438, (Screen.height - 365));
                tooltipRect = new Rect(Screen.width - 660, Screen.height - Input.mousePosition.y, 220, 20);
            }
            else 
            {
                showRFGUI = false;
                return;
            }

            GUI.Label(tooltipRect, myToolTip);
            GUILayout.Window(part.name.GetHashCode(), screenRect, GUIWindow, "Fuel Tanks for " + part.partInfo.title);
        }

        public void GUIWindow(int windowID)
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
                if (Math.Round(AvailableVolume, 4) < 0)
                    GUILayout.Label("Volume: " + volumeDisplay, overfull);
                else
                    GUILayout.Label("Volume: " + volumeDisplay);
                GUILayout.EndHorizontal();

                scrollPos = GUILayout.BeginScrollView(scrollPos);

                GUITanks();

                GUIEngines();

                GUILayout.EndScrollView();
                GUILayout.Label(MFSSettings.GetVersion ());
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
            foreach (FuelTank tank in tankList)
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
                        tank.maxAmountExpression = tank.maxAmount.ToString();
                        //Debug.LogWarning("[MFT] Adding tank from API " + tank.name + " amount: " + tank.maxAmountExpression ?? "null");
                    }
                    else if (tank.maxAmountExpression.Length > 0 && tank.maxAmountExpression != tank.maxAmount.ToString())
                    {
                        style = changed;
                    }

                    tank.maxAmountExpression = GUILayout.TextField(tank.maxAmountExpression, style, GUILayout.Width(140));

                    if (GUILayout.Button("Update", GUILayout.Width(60)))
                    {
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
                            //Debug.LogWarning("[MFT] Removing tank as empty input " + tank.name + " amount: " + tank.maxAmountExpression ?? "null");
                        }
                        else
                        {
                            double tmp;
                            if (MathUtils.TryParseExt(trimmed, out tmp))
                            {
                                tank.maxAmount = tmp;
                                
                                if (tmp != 0)
                                {
                                    tank.amount = tank.fillable ? tank.maxAmount : 0;

                                    // Need to round-trip the value
                                    tank.maxAmountExpression = tank.maxAmount.ToString();
                                    //Debug.LogWarning("[MFT] Updating maxAmount " + tank.name + " amount: " + tank.maxAmountExpression ?? "null");
                                }
                            }
                        }
                    }
                    if (GUILayout.Button("Remove", GUILayout.Width(60)))
                    {
                        tank.maxAmount = 0;
                        //Debug.LogWarning("[MFT] Removing tank from button " + tank.name + " amount: " + tank.maxAmountExpression ?? "null");
                    }
                }
                else if (AvailableVolume >= 0.001)
                {
                    string extraData = "Max: " + (AvailableVolume * tank.utilization).ToStringExt("S3") + "L (+" + FormatMass((float)(AvailableVolume * tank.mass)) + " )";

                    GUILayout.Label(extraData, GUILayout.Width(150));

                    if (GUILayout.Button("Add", GUILayout.Width(130)))
                    {
                        tank.maxAmount = AvailableVolume * tank.utilization;
                        tank.amount = tank.fillable ? tank.maxAmount : 0;

                        tank.maxAmountExpression = tank.maxAmount.ToString();
                        //Debug.LogWarning("[MFT] Adding tank " + tank.name + " maxAmount: " + tank.maxAmountExpression ?? "null");
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
            if (usedBy.Count > 0 && AvailableVolume >= 0.001)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Configure remaining volume for " + engineCount + " engines:");
                GUILayout.EndHorizontal();

                foreach (FuelInfo info in usedBy.Values)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(new GUIContent(info.Label, info.names)))
                    {
                        ConfigureFor(info);
                    }
                    GUILayout.EndHorizontal();
                }
            }
        }

        #endregion

        #region Engine bits

        private readonly Dictionary<string, FuelInfo> usedBy = new Dictionary<string, FuelInfo>();
        private int engineCount;

        private void UpdateUsedBy()
        {
            if (dedicated)
            {
                Empty();
                UsedVolume = 0;
                ConfigureFor(part);
                MarkWindowDirty();
                return;
            }


            usedBy.Clear();

            List<Part> enginesList = GetEnginesFedBy(part);
            engineCount = enginesList.Count;

            foreach (Part engine in enginesList)
            {
                FuelInfo f = new FuelInfo(engine, this);
                if (f.ratioFactor > 0.0)
                {
                    FuelInfo found;
                    if (!usedBy.TryGetValue(f.Label, out found))
                    {
                        usedBy.Add(f.Label, f);
                    }
                    else if (!found.names.Contains(engine.partInfo.title))
                    {
                        found.names += ", " + engine.partInfo.title;
                    }
                }
            }

            // Need to update the tweakable menu too
            if (HighLogic.LoadedSceneIsEditor)
            {
                Events.RemoveAll(button => button.name.StartsWith("MFT"));

                bool activeEditor = (AvailableVolume >= 0.001);

                int idx = 0;
                foreach (FuelInfo info in usedBy.Values)
                {
                    KSPEvent kspEvent = new KSPEvent
                    {
                        name = "MFT" + idx++,
                        guiActive = false,
                        guiActiveEditor = activeEditor,
                        guiName = info.Label
                    };
                    FuelInfo info1 = info;
                    BaseEvent button = new BaseEvent(Events, kspEvent.name, () => ConfigureFor(info1), kspEvent)
                    {
                        guiActiveEditor = activeEditor
                    };
                    Events.Add(button);
                }
                MarkWindowDirty();
            }
        }

        private void UpdateTweakableMenu()
        {
            if (dedicated)
                return;

            BaseEvent empty = Events["Empty"];
            if (empty != null)
                empty.guiActiveEditor = (UsedVolume != 0);

            bool activeEditor = (AvailableVolume >= 0.001);

            bool activeChanged = false;
            for (int i = 0; i < Events.Count; ++i)
            {
                BaseEvent evt = Events.GetByIndex(i);
                if (!evt.name.StartsWith("MFT"))
                    continue;
                if (evt.guiActiveEditor != activeEditor)
                    activeChanged = true;
                evt.guiActiveEditor = activeEditor;
            }
            if (activeChanged)
                MarkWindowDirty();
        }

        private class FuelInfo
        {
            public string names;
            public readonly List<Propellant> propellants;
            public readonly double efficiency;
            public readonly double ratioFactor;

            public string Label
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
                            label += Math.Round(100 * tfuel.ratio / ratioFactor, 0) + "% " + tfuel.name;
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

                ratioFactor = 0.0;
                efficiency = 0.0;

                if (engine.Modules.Contains("ModuleEnginesFX"))
                {
                    ModuleEnginesFX e = (ModuleEnginesFX)engine.Modules["ModuleEnginesFX"];
                    propellants = e.propellants;
                }
                else if (engine.Modules.Contains("ModuleEngines"))
                {
                    ModuleEngines e = (ModuleEngines)engine.Modules["ModuleEngines"];
                    propellants = e.propellants;
                }

                foreach (Propellant tfuel in propellants)
                {
                    if (PartResourceLibrary.Instance.GetDefinition(tfuel.name) == null)
                    {
                        print("Unknown RESOURCE {" + tfuel.name + "}");
                        ratioFactor = 0.0;
                        break;
                    }
                    if (tank.dedicated || PartResourceLibrary.Instance.GetDefinition(tfuel.name).resourceTransferMode != ResourceTransferMode.NONE)
                    {
                        FuelTank t;
                        if (tank.tankList.TryGet(tfuel.name, out t))
                        {
                            efficiency += tfuel.ratio/t.utilization;
                            ratioFactor += tfuel.ratio;
                        }
                        else if (!IgnoreFuel(tfuel.name))
                        {
                            ratioFactor = 0.0;
                            break;
                        }
                    }
                }
                names = "Used by: " + engine.partInfo.title;
            }
        }

        public void ConfigureFor(Part engine)
        {
            ConfigureFor(new FuelInfo(engine, this));
        }

        private void ConfigureFor(FuelInfo fi)
        {
            if (fi.ratioFactor == 0.0 || fi.efficiency == 0) // can't configure for this engine
                return;

            double availableVolume = AvailableVolume;
            foreach (Propellant tfuel in fi.propellants)
            {
                if (dedicated || PartResourceLibrary.Instance.GetDefinition(tfuel.name).resourceTransferMode != ResourceTransferMode.NONE)
                {
                    FuelTank tank;
                    if (tankList.TryGet(tfuel.name, out tank))
                    {
                        double amt = availableVolume * tfuel.ratio / fi.efficiency;
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
