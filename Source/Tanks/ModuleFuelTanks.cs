using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using System.Collections.ObjectModel;
using System.Reflection;

using KSP.UI.Screens;

// ReSharper disable InconsistentNaming, CompareOfFloatsByEqualityOperator

namespace RealFuels.Tanks
{
    public partial class ModuleFuelTanks : PartModule, IModuleInfo, IPartCostModifier, IPartMassModifier
    {
        public class UnmanagedResource
        {

            public UnmanagedResource(string name, double amount, double maxAmount)
            {
                this.name = name;
                this.amount = amount;
                this.maxAmount = maxAmount;
            }

            public string name;
            public double amount;
            public double maxAmount;
        }

        public Dictionary<string, UnmanagedResource> unmanagedResources;

        bool compatible = true;
        bool started;
        private bool windowDirty = false;

        public bool fueledByLaunchClamp = false;

        private const string guiGroupName = "RealFuels";
        private const string guiGroupDisplayName = "Real Fuels";

        private static double MassMult
        {
            get {
                return MFSSettings.useRealisticMass ? 1.0 : MFSSettings.tankMassMultiplier;
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
            MFSSettings.TryInitialize();
            enabled = false;

            InitUtilization();

            unmanagedResources = new Dictionary<string, UnmanagedResource>();
            if (part.partInfo != null && part.partInfo.partPrefab != null)
            {
                if (unmanagedResources.Count == 0)
                {
                    unmanagedResources = ((ModuleFuelTanks)part.partInfo.partPrefab.Modules["ModuleFuelTanks"]).unmanagedResources;
                    //Debug.Log("[ModuleFuelTanks.OnAwake()] unmanagedResources was initialized with count = " + unmanagedResources.Count.ToString());
                }
            }
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
                return (HighLogic.LoadedScene == GameScenes.SPACECENTER
                        || HighLogic.LoadedScene == GameScenes.LOADING || HighLogic.LoadedScene == GameScenes.MAINMENU);
            }
        }

        bool isEditor
        {
            get {
                return HighLogic.LoadedSceneIsEditor;
            }
        }

        bool isEditorOrFlight
        {
            get {
                return (HighLogic.LoadedSceneIsEditor
                        || HighLogic.LoadedSceneIsFlight);
            }
        }

        protected void InitUtilization()
        {
            // Initialize utilization from the settings file
            if (utilization == -1)
                utilization = MFSSettings.partUtilizationDefault;

            UI_FloatRange f = (UI_FloatRange)(Fields["utilization"].uiControlEditor);
            f.minValue = minUtilization;
            f.maxValue = maxUtilization;
            utilization = Mathf.Clamp(utilization, minUtilization, maxUtilization);
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

        void CleanResources ()
        {
            // Destroy any resources still hanging around from the LOADING phase
            for (int i = part.Resources.Count - 1; i >= 0; --i) {
                PartResource partResource = part.Resources[i];
                // Do not remove any resources not managed by MFT
                if (!tankList.Contains (partResource.resourceName))
                    continue;
                part.Resources.Remove(partResource.info.id);
                part.SimulationResources.Remove(partResource.info.id);
            }
            RaiseResourceListChanged ();
            // Setup the mass
            massDirty = true;
            CalculateMass();
        }

        public override void OnCopy (PartModule fromModule)
        {
            //Debug.Log ($"[ModuleFuelTanks] OnCopy: {fromModule}");

            var prefab = fromModule as ModuleFuelTanks;
            utilization = prefab.utilization;
            totalVolume = prefab.totalVolume;
            volume = prefab.volume;
            type = prefab.type;
            UpdateTankType (false);
            CleanResources ();
            tankList.Clear ();
            for (int i = 0; i < prefab.tankList.Count; i++) {
                var tank = prefab.tankList[i];
                //Debug.Log ($"    {tank.name} {tank.amount} {tank.maxAmount}");
                tankList.Add (tank.CreateCopy (this, null, false));
                tankList[i].maxAmount = tank.maxAmount;
                tankList[i].amount = tank.amount;
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            if (!compatible)
            {
                return;
            }

            // Make sure this isn't an upgrade node because if we got here during an upgrade application
            // then RaiseResourceListChanged will throw an error when it hits SendEvent()
            if (node.name == "CURRENTUPGRADE")
            {
                // If there's ever a need for special upgrade handling, put that code here.

                // Special handling for adding tank types via upgrade system
                string[] typeAvailableUpgrades = node.GetValues("typeAvailable");
                if (typeAvailableUpgrades.Count() > 0)
                {
                    for (int i = 0; i < typeAvailableUpgrades.Count(); i++)
                        typesAvailable.AddUnique(typeAvailableUpgrades[i]);
                    if (typesAvailable.Count() > 0 && !typesAvailable.Contains(type))
                        typesAvailable.Add(type);
                    InitializeTankType();
                }
            }
            else
            {
                ConfigNode[] unmanagedResourceNodes = node.GetNodes("UNMANAGED_RESOURCE");
                //Debug.Log("[ModuleFuelTanks.OnLoad()] " + unmanagedResourceNodes.Count() + " UNMANAGED_RESOURCE nodes found");
                for (int i = unmanagedResourceNodes.Count() - 1; i >= 0; --i)
                {
                    string name = "";
                    double amount = 0;
                    double maxAmount = 0;
                    // we're going to be strict and demand all of these be present
                    if (!unmanagedResourceNodes[i].HasValue("name") || !unmanagedResourceNodes[i].HasValue("amount") || !unmanagedResourceNodes[i].HasValue("maxAmount"))
                    {
                        Debug.Log("[ModuleFuelTanks.OnLoad()] was missing either name, amount or maxAmount for UNMANAGED_RESOURCE: " + name);
                        continue;
                    }
                    name = unmanagedResourceNodes[i].GetValue("name");
                    if (PartResourceLibrary.Instance.GetDefinition(name) == null)
                    {
                        Debug.Log("[ModuleFuelTanks.OnLoad()] could not find resource by the name of " + name);
                        continue;
                    }
                    double.TryParse(unmanagedResourceNodes[i].GetValue("amount"), out amount);
                    double.TryParse(unmanagedResourceNodes[i].GetValue("maxAmount"), out maxAmount);
                    amount = Math.Max(amount, 0d);
                    maxAmount = Math.Max(amount, maxAmount);
                    if (!unmanagedResources.ContainsKey(name))
                    {
                        if (maxAmount > 0)
                        {
                            unmanagedResources.Add(name, new UnmanagedResource(name, amount, maxAmount));
                            Debug.Log("[ModuleFuelTanks.OnLoad()] added new UnmanagedResource " + name + " with " + amount + "/" + maxAmount);
                            if (!part.Resources.Contains(name))
                            {
                                ConfigNode resNode = new ConfigNode("RESOURCE");
                                resNode.AddValue("name", name);
                                resNode.AddValue("amount", amount);
                                resNode.AddValue("maxAmount", maxAmount);
                                part.AddResource(resNode);
                            }
                        }
                        else
                            Debug.Log("[ModuleFuelTanks.OnLoad()] did not add new UnmanagedResource; maxAmount = 0");
                    }
                    else
                    {
                        if (maxAmount > 0)
                        {
                            unmanagedResources[name].amount += amount;
                            unmanagedResources[name].maxAmount += maxAmount;
                            //Debug.Log("[ModuleFuelTanks.OnLoad()] modified UnmanagedResource: " + name + "; amount = " + amount + " / maxAmount = " + maxAmount);

                            // this should be safe; if we're here then we previously would have added this resource if missing.
                            part.Resources[name].amount = Math.Max(part.Resources[name].amount, unmanagedResources[name].amount);
                            part.Resources[name].maxAmount = Math.Max(part.Resources[name].maxAmount, unmanagedResources[name].maxAmount);
                        }
                        else
                            Debug.Log("[ModuleFuelTanks.OnLoad()] did not add new UnmanagedResource; maxAmount = 0");
                    }
                }

                if (isDatabaseLoad)
                {
                    InitUtilization();
                    InitVolume(node);

                    MFSSettings.SaveOverrideList(part, node.GetNodes("TANK"));
                    ParseBaseMass(node);
                    ParseBaseCost(node);
                    ParseInsulationFactor(node);
                    typesAvailable.AddRange(node.GetValues("typeAvailable"));
                    if (typesAvailable.Count() > 0 && !typesAvailable.Contains(type))
                        typesAvailable.Add(type);
                    RecordManagedResources();
                }
                else if (isEditorOrFlight)
                {
                    // The amounts initialized flag is there so that the tank type loading doesn't
                    // try to set up any resources. They'll get loaded directly from the save.
                    UpdateTankType(false);

                    InitUtilization();
                    InitVolume(node);

                    CleanResources();

                    // Destroy any resources still hanging around from the LOADING phase
                    for (int i = part.Resources.Count - 1; i >= 0; --i)
                    {
                        PartResource partResource = part.Resources[i];
                        if (!tankList.Contains(partResource.resourceName) && !unmanagedResources.ContainsKey(partResource.resourceName))
                        {
                            part.Resources.Remove(partResource.info.id);
                            part.SimulationResources.Remove(partResource.info.id);
                        }
                    }
                    RaiseResourceListChanged();

                    // Setup the mass
                    massDirty = true;
                    CalculateMass();
                }
                OnLoadRF(node);
            }
        }

        private void InitVolume(ConfigNode node)
        {
            // Load the volume. If totalVolume is specified, use that to calc the volume
            // otherwise scale up the provided volume. No KSPField support for doubles
            if (node.HasValue("totalVolume") && double.TryParse(node.GetValue("totalVolume"), out totalVolume))
            {
                ChangeTotalVolume(totalVolume);
            }
            else if (node.HasValue("volume") && double.TryParse(node.GetValue("volume"), out volume))
            {
                totalVolume = volume * 100d / utilization;
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
            info.Append ("  Max Volume: ").AppendLine (KSPUtil.PrintSI (volume, MFSSettings.unitLabel));
            info.AppendLine ("  Tank can hold:");
            for (int i = 0; i < tankList.Count; i++) {
                FuelTank tank = tankList[i];
                info.Append ("      ").Append (tank).Append (" ").AppendLine (tank.note);
            }
            return info.ToString ();
        }

        public string GetPrimaryField ()
        {
            return String.Format ("Max Volume: {0}, {1}{2}",
                            KSPUtil.PrintSI (volume, MFSSettings.unitLabel),
                            type,
                            (typesAvailable != null && typesAvailable.Count() > 1) ? "*" : "");
        }

        public Callback<Rect> GetDrawModulePanelCallback ()
        {
            return null;
        }

        public string GetModuleTitle ()
        {
            return "Modular Fuel Tank";
        }

        public void Start() // not just when activated
        {
            if (!compatible) {
                return;
            }
            enabled = true;
        }

        public override void OnStart(StartState state)
        {
            if (!compatible) {
                return;
            }
            enabled = true; // just in case...

            if (isEditor)
            {
                GameEvents.onPartAttach.Add(onPartAttach);
                GameEvents.onPartRemove.Add(onPartRemove);
                GameEvents.onEditorShipModified.Add(onEditorShipModified);
                GameEvents.onPartActionUIDismiss.Add(OnPartActionGuiDismiss);
                GameEvents.onPartActionUIShown.Add(OnPartActionUIShown);

                if (part.symmetryCounterparts.Count > 0) {
                    UpdateTankType(false);
                }

                InitializeTankType();
                InitializeUtilization();
            }

            OnStartRF(state);

            massDirty = true;
            CalculateMass ();

            UpdateTestFlight();
            started = true;
        }

        void OnDestroy ()
        {
            GameEvents.onPartAttach.Remove (onPartAttach);
            GameEvents.onPartRemove.Remove (onPartRemove);
            GameEvents.onEditorShipModified.Remove (onEditorShipModified);
            GameEvents.onPartActionUIDismiss.Remove (OnPartActionGuiDismiss);
            GameEvents.onPartActionUIShown.Remove(OnPartActionUIShown);
            TankWindow.HideGUI();
        }

        public override void OnSave (ConfigNode node)
        {
            if (!compatible) {
                return;
            }

            node.AddValue ("volume", volume.ToString ("G17")); // no KSPField support for doubles
            tankList.Save (node, false);
        }

        const int wait_frames = 2;
        int update_wait_frames = 0;

        private IEnumerator WaitAndUpdate (ShipConstruct ship)
        {
            while (--update_wait_frames > 0) {
                yield return null;
            }

            PartResourcesChanged ();
        }

        private void onEditorShipModified (ShipConstruct ship)
        {
            if (this == null)
                return;

            // some parts/modules fire the event before doing things
            if (update_wait_frames == 0) {
                update_wait_frames = wait_frames;
                StartCoroutine (WaitAndUpdate (ship));
            } else {
                update_wait_frames = wait_frames;
            }
        }

        int updateusedby_wait_frames = 0;

        private IEnumerator WaitAndUpdateUsedBy ()
        {
            while (--updateusedby_wait_frames > 0) {
                yield return null;
            }

            UpdateUsedBy ();
        }

        private void onPartAttach (GameEvents.HostTargetAction<Part, Part> hostTarget)
        {
            if (this == null)
                return;

            if (updateusedby_wait_frames == 0) {
                updateusedby_wait_frames = wait_frames;
                StartCoroutine (WaitAndUpdateUsedBy ());
            } else {
                updateusedby_wait_frames = wait_frames;
            }
        }

        private void onPartRemove (GameEvents.HostTargetAction<Part, Part> hostTarget)
        {
            if (this == null)
                return;

            if (updateusedby_wait_frames == 0) {
                updateusedby_wait_frames = wait_frames;
                StartCoroutine (WaitAndUpdateUsedBy ());
            } else {
                updateusedby_wait_frames = wait_frames;
            }
        }

        private void OnPartActionUIShown(UIPartActionWindow window, Part p)
        {
            if (p == part && windowDirty)
            {
                windowDirty = false;        // Un-flag state
                window.displayDirty = true; // Signal refresh
                //MonoUtilities.RefreshPartContextWindow(part);
            }
        }

        private void OnPartActionGuiDismiss(Part p)
        {
            if (p == part)
                showUI = false;
        }

        public void Update ()
        {
            if (compatible && HighLogic.LoadedSceneIsEditor)
            {
                UpdateTankType();
                UpdateUtilization();
                CalculateMass();

                bool inEditorActionsScreen = (EditorLogic.fetch?.editorScreen == EditorScreen.Actions);
                bool partIsSelectedInActionsScreen = inEditorActionsScreen && (EditorActionGroups.Instance?.GetSelectedParts().Contains(part) ?? false);

                if (partIsSelectedInActionsScreen || showUI)
                    TankWindow.ShowGUI(this);
                else
                    TankWindow.HideGUIForModule(this);
            }
            UpdateRF();
        }

        // The active fuel tanks. This will be the list from the tank type, with any overrides from the part file.
        internal FuelTankList tankList = new FuelTankList ();

        [KSPField (isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Tank Type", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName), UI_ChooseOption (scene = UI_Scene.Editor)]
        public string type = "Default";
        private string oldType;

        public List<string> typesAvailable = new List<string>(); 

        // for EngineIgnitor integration: store a public list of the fuel tanks, and
        [NonSerialized]
        public List<FuelTank> fuelList = new List<FuelTank> ();

        private void InitializeTankType ()
        {
            Fields["type"].guiActiveEditor = true;
            if (typesAvailable == null || typesAvailable.Count() <= 1) {
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
                if (!string.IsNullOrEmpty(oldType)) // we have an old type
                {
                    def = MFSSettings.tankDefinitions[type];
                    if (def.canHave)
                        return; // go back to old type
                }
                // else find one that does work
                if (typesAvailable != null)
                {
                    for (int i = 0; i < typesAvailable.Count(); i++)
                    {
                        string tn = typesAvailable[i];
                        TankDefinition newDef = MFSSettings.tankDefinitions.Contains(tn) ? MFSSettings.tankDefinitions[tn] : null;
                        if (newDef != null && newDef.canHave)
                        {
                            def = newDef;
                            type = newDef.name;
                            break;
                        }
                    }
                }
                if (type == oldType) // if we didn't find a new one
                {
                    Debug.LogError("Unable to find a type that is tech-available for part " + part.name);
                    return;
                }
            }

            oldType = type;

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
            HashSet<string> managed = MFSSettings.managedResources[part.name];  // if this throws, we have some big fish to fry
            bool needsMesage = false;
            for (int i = part.Resources.Count - 1; i >= 0; --i) {
                PartResource partResource = part.Resources[i];
                string resname = partResource.resourceName;
                if (!managed.Contains(resname) || tankList.Contains(resname) || unmanagedResources.ContainsKey(resname))
                    continue;
                part.Resources.Remove (partResource.info.id);
                part.SimulationResources.Remove (partResource.info.id);
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


            if (!isDatabaseLoad) {
                // being called in the SpaceCenter scene is assumed to be a database reload
                //FIXME is this really needed?
                
                massDirty = true;
            }

            UpdateTankTypeRF(def);
            UpdateTestFlight();
        }

        // The total tank volume. This is prior to utilization
        public double totalVolume;

        [KSPField (isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Utilization", guiUnits = "%", guiFormat = "F0", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName),
         UI_FloatRange (minValue = 1, maxValue = 100, stepIncrement = 1, scene = UI_Scene.Editor)]
        public float utilization = -1;
        private float oldUtilization = -1;

        [KSPField]
        public bool utilizationTweakable = false;

        [KSPField]
        public float minUtilization = 1f;

        [KSPField]
        public float maxUtilization = 100f;

        // no double support for KSPFields - [KSPField (isPersistant = true)]
        public double volume;

        [KSPField (isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Volume", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName)]
        public string volumeDisplay;

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

        // Conversion between tank volume in kL, and whatever units this tank uses.
        // Default to 1000 for RF. Varies for MFT. Needed to interface with PP.
        [KSPField]
        public float tankVolumeConversion = 1000;

        [KSPEvent (guiActive=false, active = true)]
        void OnPartVolumeChanged (BaseEventDetails data)
        {
            string volName = data.Get<string> ("volName");
            double newTotalVolume = data.Get<double> ("newTotalVolume") * tankVolumeConversion;
            if (volName == "Tankage") {
                ChangeTotalVolume (newTotalVolume);
            }
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

            if (Double.IsInfinity(newVolume / volume))
            {
                totalVolume = newTotalVolume;
                volume = newVolume;
                Debug.LogWarning("[ModularFuelTanks] caught DIV/0 in ChangeTotalVolume. Setting volume/totalVolume and exiting function");
                return;
            }
            double volumeRatio = newVolume / volume;

            bool doResources = false;

            if (volume > newVolume) {
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

        [KSPField (isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Mass", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName)]
        public string massDisplay;

        // public so they copy
        public bool basemassOverride;
        public bool baseCostOverride;
        public float basemassPV;
        public float baseCostPV;
        public float basemassConst;
        public float baseCostConst;

        public static string FormatMass (float mass)
        {
            if (mass < 1.0f) {
                return KSPUtil.PrintSI (mass * 1e6, "g", 4);
            }
            return KSPUtil.PrintSI (mass, "t", 4);
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
                baseCostConst = 0f;
            }
        }

        public void CalculateMass ()
        {
            if (tankList == null || !massDirty)
            {
                return;
            }
            massDirty = false;

            double basemass = basemassConst + basemassPV * (MFSSettings.basemassUseTotalVolume ? totalVolume : volume);
            CalculateMassRF(ref basemass);

            if (basemass >= 0)
            {
                double tankDryMass = 0;
                for (int i = 0; i < tankList.Count; i++)
                {
                    var tank = tankList[i];
                    tankDryMass += tank.maxAmount * tank.mass / tank.utilization;

                }
                mass = (float) ((basemass + tankDryMass) * MassMult);

                // compute massDelta based on prefab, if available.
                if (part.partInfo == null || part.partInfo.partPrefab == null)
                {
                    part.mass = mass;
                    massDelta = 0;
                }
                else
                {
                    massDelta = mass - part.partInfo.partPrefab.mass;
                }
            }
            else
            {
                mass = part.mass; // display dry mass even in this case.
                massDelta = 0f;
            }

            if (isEditor) {
                UsedVolume = tankList
                    .Where (fuel => fuel.maxAmount > 0 && fuel.utilization > 0)
                    .Sum (fuel => fuel.maxAmount/fuel.utilization);

                double availRounded = AvailableVolume;
                if (Math.Abs(availRounded) < 0.001d)
                    availRounded = 0d;
                string availVolStr = KSPUtil.PrintSI (availRounded, MFSSettings.unitLabel);
                string volStr = KSPUtil.PrintSI (volume, MFSSettings.unitLabel);
                volumeDisplay = "Avail: " + availVolStr + " / Tot: " + volStr;

                double resourceMass = part.Resources.Cast<PartResource> ().Sum (partResource => partResource.maxAmount* partResource.info.density);

                double wetMass = mass + resourceMass;
                massDisplay = "Dry: " + FormatMass (mass) + " / Wet: " + FormatMass ((float)wetMass);

                UpdateTweakableMenu ();
            }
        }

        // mass-change interface, so Engineer's Report / Pad limit checking is correct.
        public float massDelta = 0f; // assigned whenever part.mass is changed.
        
        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit)
        {
            return massDelta;
        }

        public ModifierChangeWhen GetModuleMassChangeWhen ()
        {
            return ModifierChangeWhen.FIXED;
        }

        private void UpdateTweakableMenu ()
        {
            if (!compatible) {
                return;
            }

            bool activeChanged = false;
            bool activeEditor = (UsedVolume != 0);
            BaseEvent evt = Events["Empty"];
            if (evt != null) {
                activeChanged |= evt.guiActiveEditor != activeEditor;
                evt.guiActiveEditor = activeEditor;
            }

            activeEditor = (AvailableVolume >= 0.001);

            for (int i = 0; i < Events.Count; ++i) {
                evt = Events.GetByIndex (i);
                if (!evt.name.StartsWith ("MFT")) {
                    continue;
                }
                activeChanged |= evt.guiActiveEditor != activeEditor;
                evt.guiActiveEditor = activeEditor;
            }
        }

        public float GetModuleCost (float defaultCost, ModifierStagingSituation sit)
        {
            double cst = Mathf.Max(0f, baseCostConst);
            if (baseCostPV >= 0f && baseCostConst >= 0f ) {
                cst += volume * Mathf.Max(baseCostPV, 0f);
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
            GetModuleCostRF(ref cst);
            return (float)cst;
        }

        public ModifierChangeWhen GetModuleCostChangeWhen ()
        {
            return ModifierChangeWhen.FIXED;
        }

        public void RaiseResourceInitialChanged(PartResource resource, double amount)
        {
            var data = new BaseEventDetails (BaseEventDetails.Sender.USER);
            data.Set<PartResource> ("resource", resource);
            data.Set<double> ("amount", amount);
            part.SendEvent ("OnResourceInitialChanged", data, 0);
        }

        public void RaiseResourceMaxChanged (PartResource resource, double amount)
        {
            var data = new BaseEventDetails (BaseEventDetails.Sender.USER);
            data.Set<PartResource> ("resource", resource);
            data.Set<double> ("amount", amount);
            part.SendEvent ("OnResourceMaxChanged", data, 0);
        }

        public void RaiseResourceListChanged ()
        {
            GameEvents.onPartResourceListChange.Fire (part);
            part.ResetSimulationResources ();
            part.SendEvent ("OnResourceListChanged", null, 0);
            MarkWindowDirty();
        }

        public void PartResourcesChanged ()
        {
            // We'll need to update the volume display regardless
            massDirty = true;
        }

        [KSPField(guiActiveEditor = true, guiName = "Tank UI", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName)]
        [UI_Toggle(enabledText = "Hide", disabledText = "Show", suppressEditorShipModified = true)]
        [NonSerialized]
        public bool showUI;

        [KSPEvent (guiName = "Remove All Tanks", guiActive = false, guiActiveEditor = true, name = "Empty", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName)]
        public void Empty ()
        {
            for (int i = 0; i < tankList.Count; i++) {
                tankList[i].maxAmount = 0;
            }
            MarkWindowDirty();
            GameEvents.onEditorShipModified.Fire (EditorLogic.fetch.ship);
        }
        internal void MarkWindowDirty ()
        {
            if (!started) {
                return;
            }
            if (UIPartActionController.Instance?.GetItem(part) is UIPartActionWindow paw)
                paw.displayDirty = true;
            else
                windowDirty = true; // The PAW isn't open, so request refresh later
            //MonoUtilities.RefreshPartContextWindow(part);
        }


        // looks to see if we should ignore this fuel when creating an autofill for an engine
        private static bool IgnoreFuel (string name)
        {
            return MFSSettings.ignoreFuelsForFill.Contains (name);
        }

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

            foreach(Part p in parts)
            {
                string title = p.partInfo.title;
                for(int j = 0; j < p.Modules.Count; ++j)
                {
                    FuelInfo f = null;
                    PartModule m = p.Modules[j];
                    if (m is ModuleEngines)
                        f = new FuelInfo((m as ModuleEngines).propellants, this, title);
                    else if (m is ModuleRCS)
                        f = new FuelInfo((m as ModuleRCS).propellants, this, title);
                    if (f?.ratioFactor > 0d)
                        UpdateFuelInfo(f, title);
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
                        guiName = info.Label,
                        groupName = guiGroupName,
                        groupDisplayName = guiGroupDisplayName
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
            foreach (PartModule engine_module in engine.Modules)
            {
                List<Propellant> propellants = GetEnginePropellants (engine_module);
                if ((object)propellants != null)
                {
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
            foreach (Propellant tfuel in fi.propellants)
            {
                if (PartResourceLibrary.Instance.GetDefinition (tfuel.name).resourceTransferMode != ResourceTransferMode.NONE)
                {
                    FuelTank tank;
                    if (tankList.TryGet (tfuel.name, out tank))
                    {
                        double amt = availableVolume * tfuel.ratio / fi.efficiency;
                        tank.maxAmount += amt;
                        tank.amount += amt;
                    }
                }
            }
            GameEvents.onEditorShipModified.Fire (EditorLogic.fetch.ship);
        }

        List<Propellant> GetEnginePropellants(PartModule engine)
        {
            if (engine is ModuleEngines)
                return (engine as ModuleEngines).propellants;
            else if (engine is ModuleRCS)
                return (engine as ModuleRCS).propellants;
            return null;
        }

        #region Partial Methods

        partial void OnStartRF(StartState state);
        partial void UpdateTestFlight();
        partial void ParseInsulationFactor(ConfigNode node);
        partial void UpdateTankTypeRF(TankDefinition def);
        partial void GetModuleCostRF(ref double cost);
        partial void CalculateMassRF(ref double mass);
        partial void OnLoadRF(ConfigNode node);
        partial void UpdateRF();

        #endregion
    }
}
