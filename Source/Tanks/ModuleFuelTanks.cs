using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using UnityEngine;

using KSP.UI.Screens;
using System.Reflection;

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

        // The active fuel tanks. This will be the list from the tank type, with any overrides from the part file.
        internal FuelTankList tankList = new FuelTankList();
        public List<string> typesAvailable = new List<string>();
        internal List<string> lockedTypes = new List<string>();

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Tank Type", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string type = "Default";
        private string oldType;

        [KSPEvent(active = true, guiActiveEditor = true, guiName = "Choose Tank Type", groupName = guiGroupName)]
        public void ChooseTankDefinition()
        {
            if (tankDefinitionSelectionGUI == null)
            {
                tankDefinitionSelectionGUI = gameObject.AddComponent<TankDefinitionSelectionGUI>();
                tankDefinitionSelectionGUI.parentModule = this;
            }
        }
        private TankDefinitionSelectionGUI tankDefinitionSelectionGUI = null;

        // The total tank volume. This is prior to utilization
        public double totalVolume;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Utilization", guiUnits = "%", guiFormat = "F0", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName),
         UI_FloatRange(minValue = 1, maxValue = 100, stepIncrement = 1, scene = UI_Scene.Editor)]
        public float utilization = -1;

        [KSPField]
        public bool utilizationTweakable = false;

        [KSPField]
        public float minUtilization = 1f;

        [KSPField]
        public float maxUtilization = 100f;

        [KSPField(isPersistant = true)]
        public double volume;

        [KSPField(guiActiveEditor = true, guiName = "Volume", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName)]
        public string volumeDisplay;

        // Conversion between tank volume in kL, and whatever units this tank uses.
        // Default to 1000 for RF. Varies for MFT. Needed to interface with PP.
        [KSPField]
        public float tankVolumeConversion = 1000;

        [KSPField(isPersistant = true)]
        public float mass;

        [KSPField(guiActiveEditor = true, guiName = "Mass", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName)]
        public string massDisplay;

        [KSPField(guiActiveEditor = true, guiName = "Tank UI", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName)]
        [UI_Toggle(enabledText = "Hide", disabledText = "Show", suppressEditorShipModified = true)]
        [NonSerialized]
        public bool showUI;

        bool started;
        internal bool massDirty = true;
        private bool windowDirty = false;

        internal readonly HashSet<string> managedResources = new HashSet<string>(32);

        public bool fueledByLaunchClamp = false;

        private const string guiGroupName = "RealFuels";
        private const string guiGroupDisplayName = "Real Fuels";

        public double UsedVolume { get; private set; }

        public double AvailableVolume => volume - UsedVolume;

        private static double MassMult => MFSSettings.useRealisticMass ? 1.0 : MFSSettings.tankMassMultiplier;

        private static float DefaultBaseCostPV => MFSSettings.baseCostPV;

        public delegate void UpdateTweakableButtonsDelegateType();
        public UpdateTweakableButtonsDelegateType UpdateTweakableButtonsDelegate;
        public override void OnAwake()
        {
            MFSSettings.TryInitialize();

            UpdateTweakableButtonsDelegate = (UpdateTweakableButtonsDelegateType)Delegate.CreateDelegate(typeof(UpdateTweakableButtonsDelegateType), this, "UpdateTweakableButtons", true);

            if (utilization == -1)
                utilization = Mathf.Clamp(MFSSettings.partUtilizationDefault, minUtilization, maxUtilization);

            if (HighLogic.LoadedScene == GameScenes.LOADING)
                unmanagedResources = new Dictionary<string, UnmanagedResource>();
            else
            {
                unmanagedResources = part.partInfo.partPrefab.FindModuleImplementing<ModuleFuelTanks>().unmanagedResources;
                typesAvailable = new List<string>(typesAvailable);  // Copy so any changes don't impact the prefab
            }
            OnAwakeRF();
        }

        protected void InitUtilization()
        {
            var field = Fields[nameof(utilization)];
            field.guiActiveEditor = MFSSettings.partUtilizationTweakable || utilizationTweakable;
            UI_FloatRange f = field.uiControlEditor as UI_FloatRange;
            f.minValue = minUtilization;
            f.maxValue = maxUtilization;
            SetUtilization(Mathf.Clamp(utilization, minUtilization, maxUtilization));
        }

        private void RecordManagedResources()
        {
            managedResources.Clear();
            foreach (string t in typesAvailable)
                if (MFSSettings.tankDefinitions.TryGetValue(t, out var def))
                    foreach (FuelTank tank in def.tankList)
                        managedResources.Add(tank.name);
        }

        private void CleanResources()
        {
            // Do not remove any resources not managed by MFT
            List<PartResource> removeList = part.Resources.Where(x => tankList.Contains(x.resourceName) && !unmanagedResources.ContainsKey(x.resourceName)).ToList();
            if (removeList.Count > 0)
            {
                foreach (var resource in removeList)
                {
                    part.Resources.Remove(resource.info.id);
                    part.SimulationResources.Remove(resource.info.id);
                }
                RaiseResourceListChanged();
                massDirty = true;
                CalculateMass();
            }
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
            foreach (FuelTank src in prefab.tankList)
            {
                var tank = src.CreateCopy(this, null, false);
                tank.maxAmount = src.maxAmount;
                tank.amount = src.amount;
                tankList.Add(tank);
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            // Make sure this isn't an upgrade node because if we got here during an upgrade application
            // then RaiseResourceListChanged will throw an error when it hits SendEvent()
            if (node.name == "CURRENTUPGRADE")
                UpdateTypesAvailable(node);
            else if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                typesAvailable.AddUnique(type);
                GatherUnmanagedResources(node);
                InitUtilization();
                InitVolume(node);

                MFSSettings.SaveOverrideList(part, node.GetNodes("TANK"));
                ParseBaseMass(node);
                ParseBaseCost(node);
                UpdateTypesAvailable(node);
                UpdateTankType(initializeAmounts: true);
            }
            else if (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)
            {
                // Always re-generate this list from the current set of available types
                // Also called via UpdateTypesAvailable()
                RecordManagedResources();

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

        private void InitVolume(ConfigNode node)
        {
            // If totalVolume is specified, use that, otherwise scale up the provided volume.
            if (node.TryGetValue("totalVolume", ref totalVolume))
                ChangeTotalVolume(totalVolume);
            else if (node.TryGetValue("volume", ref volume))
                totalVolume = volume * 100d / utilization;
        }

        public override string GetInfo ()
        {
            var info = StringBuilderCache.Acquire();
            info.AppendLine ("Modular Fuel Tank:");
            info.Append ("  Max Volume: ").AppendLine (KSPUtil.PrintSI (volume, MFSSettings.unitLabel));
            info.AppendLine ("  Tank can hold:");
            foreach (FuelTank tank in tankList)
                info.Append("      ").Append(tank).Append(" ").AppendLine(tank.note);
            return info.ToStringAndRelease();
        }

        public string GetPrimaryField () => $"Max Volume: {KSPUtil.PrintSI(volume, MFSSettings.unitLabel)}, {type}{(typesAvailable.Count() > 1 ? "*" : "")}";

        public Callback<Rect> GetDrawModulePanelCallback() => null;

        public string GetModuleTitle() => "Modular Fuel Tank";

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onPartAttach.Add(OnPartAttach);
                GameEvents.onPartRemove.Add(OnPartRemove);
                GameEvents.onEditorShipModified.Add(OnEditorShipModified);
                GameEvents.onPartActionUIDismiss.Add(OnPartActionGuiDismiss);
                GameEvents.onPartActionUIShown.Add(OnPartActionUIShown);

                InitializeTankType();
                UpdateTankType(false);
                InitUtilization();
                Fields[nameof(utilization)].uiControlEditor.onFieldChanged += OnUtilizationChanged;
                Fields[nameof(utilization)].uiControlEditor.onSymmetryFieldChanged += OnUtilizationChanged;
                UpdateUsedBy();
            }

            OnStartRF(state);

            massDirty = true;
            CalculateMass ();

            UpdateTestFlight();
            started = true;
        }

        void OnDestroy()
        {
            GameEvents.onPartAttach.Remove(OnPartAttach);
            GameEvents.onPartRemove.Remove(OnPartRemove);
            GameEvents.onEditorShipModified.Remove(OnEditorShipModified);
            GameEvents.onPartActionUIDismiss.Remove(OnPartActionGuiDismiss);
            GameEvents.onPartActionUIShown.Remove(OnPartActionUIShown);
            TankWindow.HideGUI();
        }

        public override void OnSave (ConfigNode node)
        {
            tankList.Save(node, false);
        }

        private void OnUtilizationChanged(BaseField f, object obj) => ChangeTotalVolume(totalVolume);

        private void OnEditorShipModified(ShipConstruct _) => PartResourcesChanged();

        private bool PartContainsEngineOrRCS(Part p, bool testChildren = false)
        {
            if (p == null) return false;
            bool result = p.FindModuleImplementing<ModuleEngines>() || p.FindModuleImplementing<ModuleRCS>();
            if (testChildren && !result)
                foreach (Part p2 in p.children)
                    result |= PartContainsEngineOrRCS(p2, testChildren);
            return result;
        }

        // Only trigger updates if a part in the tree that was added/removed is a fuel consumer
        private void OnPartAttach(GameEvents.HostTargetAction<Part, Part> hostTarget)
        {
            // Attaching: host is the incoming part
            if (PartContainsEngineOrRCS(hostTarget.host, true) || PartContainsEngineOrRCS(hostTarget.target, false))
                UpdateUsedBy();
        }

        private void OnPartRemove(GameEvents.HostTargetAction<Part, Part> hostTarget)
        {
            // Removing: target is the detaching part
            if (PartContainsEngineOrRCS(hostTarget.host, false) || PartContainsEngineOrRCS(hostTarget.target, true))
                UpdateUsedBy();
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
            {
                showUI = false;
                if (tankDefinitionSelectionGUI != null)
                    Destroy(tankDefinitionSelectionGUI);
            }
        }

        public void Update()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                UpdateTankType(false);
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


        private void InitializeTankType()
        {
            var invalidTypes = typesAvailable.Where(x => !MFSSettings.tankDefinitions.ContainsKey(x));
            var validTypes = typesAvailable.Where(x => MFSSettings.tankDefinitions.ContainsKey(x));
            if (invalidTypes.Any())
            {
                string res = string.Join(" ", invalidTypes);
                Debug.LogError($"{part} declared these available types that have no definition: {res}");
                typesAvailable = validTypes.ToList();
                if (typesAvailable.Count == 0)
                    typesAvailable = new List<string>() { MFSSettings.tankDefinitions.Keys.FirstOrDefault() };
            }
            Fields[nameof(type)].guiActiveEditor = typesAvailable.Count > 1;
            (Fields[nameof(type)].uiControlEditor as UI_ChooseOption).options = typesAvailable.ToArray();
        }

        public void AllowLockedTypes(List<string> lockedList)
        {
            var validLockedTypes = lockedList.Where(x => MFSSettings.tankDefinitions.ContainsKey(x) && !typesAvailable.Contains(x));
            typesAvailable.AddUniqueRange(validLockedTypes);
            lockedTypes.AddUniqueRange(validLockedTypes);
        }

        private void UpdateTypesAvailable(ConfigNode node) => UpdateTypesAvailable(node.GetValuesList("typeAvailable"));
        private void UpdateTypesAvailable(List<string> types)
        {
            typesAvailable.AddUniqueRange(types);
            RecordManagedResources();
            InitializeTankType();
        }
        public bool Validate() => MFSSettings.tankDefinitions.ContainsKey(type) && typesAvailable.Contains(type) && !lockedTypes.Contains(type);

        // This is strictly a change handler!
        private void UpdateTankType (bool initializeAmounts = false)
        {
            if (oldType == type || type == null) {
                return;
            }

            // Copy the tank list from the tank definitiion
            if (!MFSSettings.tankDefinitions.TryGetValue(type, out TankDefinition def)) {
                string msg = $"[ModuleFuelTanks] Somehow tried to set tank type to {type} but it has no definition.";
                type = MFSSettings.tankDefinitions.ContainsKey(oldType) ? oldType : typesAvailable.First();
                Debug.LogError($"{msg} Reset to {type}");
            }

            oldType = type;

            // Build the new tank list.
            tankList = new FuelTankList();
            foreach (FuelTank tank in def.tankList) {
                // Pull the override from the list of overrides
                ConfigNode overNode = MFSSettings.GetOverrideList(part).FirstOrDefault(n => n.GetValue("name") == tank.name);
                tankList.Add(tank.CreateCopy(this, overNode, initializeAmounts));
            }
            tankList.TechAmounts(); // update for current techs

            // Destroy any managed resources that are not in the new type.
            var removeList = part.Resources.Where(x => managedResources.Contains(x.resourceName) && !tankList.Contains(x.resourceName) && !unmanagedResources.ContainsKey(x.resourceName)).ToList();
            foreach (var partResource in removeList)
            {
                part.Resources.Remove(partResource.info.id);
                part.SimulationResources.Remove(partResource.info.id);
            }
            if (removeList.Count > 0)
                RaiseResourceListChanged();
            if (!basemassOverride)
                ParseBaseMass(def.basemass);
            if (!baseCostOverride)
                ParseBaseCost(def.baseCost);

            if (HighLogic.LoadedScene != GameScenes.LOADING) {
                // being called in the SpaceCenter scene is assumed to be a database reload
                //FIXME is this really needed?
                
                massDirty = true;
            }
            UpdateUsedBy();

            UpdateTankTypeRF(def);
            UpdateTestFlight();
        }


        [KSPEvent (guiActive=false, active = true)]
        void OnPartVolumeChanged (BaseEventDetails data)
        {
            if (data.Get<string>("volName").Equals("Tankage"))
            {
                double newTotalVolume = data.Get<double>("newTotalVolume") * tankVolumeConversion;
                ChangeTotalVolume(newTotalVolume);
            }
        }

        // ChangeVolume() called by StretchyTanks has been converted to use OnPartVolumeChanged

        protected void ChangeResources (double volumeRatio, bool propagate = false)
        {
            // The used volume will rescale automatically when setting maxAmount
            foreach (FuelTank tank in tankList)
            {
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
                    ModuleFuelTanks m = p.FindModuleImplementing<ModuleFuelTanks>();
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

        // public so they copy
        public bool basemassOverride;
        public bool baseCostOverride;
        public float basemassPV;
        public float baseCostPV;
        public float basemassConst;
        public float baseCostConst;

        public static string FormatMass(float mass) => mass < 1.0f ? KSPUtil.PrintSI(mass * 1e6, "g", 4) : KSPUtil.PrintSI(mass, "t", 4);

        private void ParseBaseMass (ConfigNode node)
        {
            string baseMass = "";
            if (basemassOverride = node.TryGetValue("basemass", ref baseMass))
                ParseBaseMass(baseMass);
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
            string baseCost = "";
            if (baseCostOverride = node.TryGetValue("baseCost", ref baseCost))
                ParseBaseCost(baseCost);
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
                baseCostPV = DefaultBaseCostPV;
                baseCostConst = 0f;
            }
        }

        public void CalculateMass ()
        {
            if (!massDirty)
            {
                return;
            }
            massDirty = false;

            double basemass = basemassConst + basemassPV * (MFSSettings.basemassUseTotalVolume ? totalVolume : volume);
            CalculateMassRF(ref basemass);

            if (basemass >= 0)
            {
                double tankDryMass = 0;
                foreach (FuelTank tank in tankList)
                    tankDryMass += tank.maxAmount * tank.mass / tank.utilization;
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

            if (HighLogic.LoadedSceneIsEditor) {
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
        
        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) => massDelta;

        public ModifierChangeWhen GetModuleMassChangeWhen () => ModifierChangeWhen.FIXED;

        private void UpdateTweakableMenu ()
        {
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
                if (PartResourceLibrary.Instance != null) {
                    foreach (FuelTank t in tankList) {
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

        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;

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

        [KSPEvent (guiName = "Remove All Tanks", guiActive = false, guiActiveEditor = true, name = "Empty", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName)]
        public void Empty ()
        {
            foreach (FuelTank tank in tankList)
                tank.maxAmount = 0;
            MarkWindowDirty();
            GameEvents.onEditorShipModified.Fire (EditorLogic.fetch.ship);
        }

        internal void MarkWindowDirty ()
        {
            if (!started) return;
            if (UIPartActionController.Instance?.GetItem(part) is UIPartActionWindow paw)
                paw.displayDirty = true;
            else
                windowDirty = true; // The PAW isn't open, so request refresh later
            //MonoUtilities.RefreshPartContextWindow(part);
        }

        private void GatherUnmanagedResources(ConfigNode node)
        {
            foreach (ConfigNode unmanagedResourceNode in node.GetNodes("UNMANAGED_RESOURCE"))
            {
                string name = "";
                double amount = 0;
                double maxAmount = 0;
                // we're going to be strict and demand all of these be present
                if (!unmanagedResourceNode.TryGetValue("name", ref name) || !unmanagedResourceNode.TryGetValue("amount", ref amount) || !unmanagedResourceNode.TryGetValue("maxAmount", ref maxAmount))
                {
                    Debug.Log($"[ModuleFuelTanks.OnLoad()] UNMANAGED_RESOURCE on {part} was missing either name, amount or maxAmount: {unmanagedResourceNode}");
                    continue;
                }
                if (PartResourceLibrary.Instance.GetDefinition(name) == null)
                {
                    Debug.Log($"[ModuleFuelTanks.OnLoad()] {part} could not find UNMANAGED_RESOURCE resource {name}");
                    continue;
                }
                amount = Math.Max(amount, 0d);
                maxAmount = Math.Max(amount, maxAmount);
                if (maxAmount <= 0)
                    Debug.Log($"[ModuleFuelTanks.OnLoad()] did not add UnmanagedResource {name}; maxAmount = 0");
                else
                {
                    if (!unmanagedResources.TryGetValue(name, out var unmanagedResource))
                    {
                        unmanagedResource = new UnmanagedResource(name, 0, 0);
                        unmanagedResources.Add(name, unmanagedResource);
                    }
                    unmanagedResource.amount += amount;
                    unmanagedResource.maxAmount += maxAmount;

                    Debug.Log($"[ModuleFuelTanks.OnLoad()] Adding UnmanagedResource {name}: {amount}/{maxAmount}");
                    if (!part.Resources.Contains(name))
                    {
                        ConfigNode resNode = new ConfigNode("RESOURCE");
                        resNode.AddValue("name", name);
                        resNode.AddValue("amount", unmanagedResource.amount);
                        resNode.AddValue("maxAmount", unmanagedResource.maxAmount);
                        part.AddResource(resNode);
                    }
                    else
                    {
                        part.Resources[name].amount = unmanagedResource.amount;
                        part.Resources[name].maxAmount = unmanagedResource.maxAmount;
                    }
                }
            }
        }

        private void SetUtilization(float value)
        {
            var f = Fields[nameof(utilization)].uiControlEditor as UI_FloatRange;
            // If the PAW is available, grab the item in order to trigger the change handlers
            // If it is not... we could force it, but let's not for now.
            // We don't actually need to here, really only during change handling and this is an initializer being slightly misused.
            //field.SetValue(Mathf.Clamp(utilization, minUtilization, maxUtilization), this);
            if (f.partActionItem is UIPartActionFieldItem item && item != null
                && item.GetType().GetMethod("SetFieldValue", BindingFlags.Instance | BindingFlags.NonPublic) is MethodInfo mi)
            {
                mi.Invoke(f.partActionItem, new object[] { value });
            }
            else
                utilization = value;
        }

        // looks to see if we should ignore this fuel when creating an autofill for an engine
        private static bool IgnoreFuel(string name) => MFSSettings.ignoreFuelsForFill.Contains(name);

        internal readonly Dictionary<PartModule, FuelInfo> usedBy = new Dictionary<PartModule, FuelInfo>();
        internal readonly HashSet<FuelTank> usedByTanks = new HashSet<FuelTank>();

        private void UpdateFuelInfo(FuelInfo f, PartModule source)
        {
            usedBy[source] = f;
            foreach (Propellant tfuel in f.propellantVolumeMults.Keys)
                if (tankList.TryGet(tfuel.name, out FuelTank tank) && tank.canHave)
                    usedByTanks.Add(tank);
        }

        public void UpdateUsedBy()
        {
            usedBy.Clear();
            usedByTanks.Clear();

            // Get part list
            List<Part> parts;
            if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch.ship != null)
                parts = EditorLogic.fetch.ship.parts;
            else if (HighLogic.LoadedSceneIsFlight && vessel != null)
                parts = vessel.parts;
            else
                return;

            foreach(Part p in parts)
            {
                string title = p.partInfo.title;
                foreach(PartModule m in p.Modules)
                {
                    FuelInfo f = null;
                    if (m is ModuleEngines)
                        f = new FuelInfo((m as ModuleEngines).propellants, this, m);
                    else if (m is ModuleRCS)
                        f = new FuelInfo((m as ModuleRCS).propellants, this, m);
                    if (f?.ratioFactor > 0d)
                        UpdateFuelInfo(f, m);
                }
            }

            UpdateTweakableButtonsDelegate();
        }

        private readonly HashSet<string> displayedParts = new HashSet<string>();
        protected void UpdateTweakableButtons()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                displayedParts.Clear();
                Events.RemoveAll(button => button.name.StartsWith("MFT"));
                bool activeEditor = AvailableVolume >= 0.001;

                int idx = 0;
                foreach (FuelInfo info in usedBy.Values)
                {
                    if (!displayedParts.Contains(info.title))
                    {
                        KSPEvent kspEvent = new KSPEvent
                        {
                            name = "MFT" + idx++,
                            guiActive = false,
                            guiActiveEditor = activeEditor,
                            guiName = info.title,
                            groupName = guiGroupName,
                            groupDisplayName = guiGroupDisplayName
                        };
                        FuelInfo info1 = info;
                        BaseEvent button = new BaseEvent(Events, kspEvent.name, () => ConfigureFor(info1), kspEvent)
                        {
                            guiActiveEditor = activeEditor
                        };
                        Events.Add(button);
                        displayedParts.Add(info.title);
                    }
                }
                MonoUtilities.RefreshPartContextWindow(part);
            }
        }

        public void ConfigureFor(Part engine)
        {
            foreach (PartModule engine_module in engine.Modules)
            {
                List<Propellant> propellants = GetEnginePropellants(engine_module);
                if (propellants != null)
                {
                    ConfigureFor(new FuelInfo(propellants, this, engine_module));
                    break;
                }
            }
        }

        internal void ConfigureFor(FuelInfo fi)
        {
            if (fi.ratioFactor == 0.0 || fi.efficiency == 0) // can't configure for this engine
                return;

            double availableVolume = AvailableVolume;
            foreach (Propellant tfuel in fi.propellantVolumeMults.Keys)
            {
                // Extra sanity check, FuelInfo.propellantVolumeMults will have filtered this case out already:
                if (PartResourceLibrary.Instance.GetDefinition (tfuel.name).resourceTransferMode != ResourceTransferMode.NONE)
                {
                    if (tankList.TryGet(tfuel.name, out FuelTank tank))
                    {
                        double amt = availableVolume * tfuel.ratio / fi.efficiency;
                        tank.maxAmount += amt;
                        tank.amount += amt;
                    }
                }
            }
            GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }

        List<Propellant> GetEnginePropellants(PartModule engine)
        {
            if (engine is ModuleEngines me)
                return me.propellants;
            else if (engine is ModuleRCS mr)
                return mr.propellants;
            return null;
        }

        #region Partial Methods

        partial void OnAwakeRF();
        partial void OnStartRF(StartState state);
        partial void UpdateTestFlight();
        partial void UpdateTankTypeRF(TankDefinition def);
        partial void GetModuleCostRF(ref double cost);
        partial void CalculateMassRF(ref double mass);
        partial void OnLoadRF(ConfigNode node);
        partial void UpdateRF();

        #endregion
    }
}
