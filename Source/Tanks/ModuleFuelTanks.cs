using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using UnityEngine;

using KSP.UI.Screens;
using System.Reflection;
using KSP.Localization;
using ROUtils;

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
        internal Dictionary<string, FuelTank> tanksDict = new Dictionary<string, FuelTank>();
        internal FuelTankList tankList = new FuelTankList();
        public List<TankDefinition> typesAvailable = new List<TankDefinition>();
        internal List<TankDefinition> lockedTypes = new List<TankDefinition>();
        internal List<TankDefinition> allPossibleTypes = new List<TankDefinition>();    // typesAvailable if all upgrades were applied

        [KSPField(isPersistant = true)]
        public string type = Localizer.GetStringByTag("#RF_FuelTank_Default"); // "Default"
        private string oldType;

        [KSPField(guiActiveEditor = true, guiActive = true, guiName = "#RF_FuelTank_TankType", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName), UI_ChooseOption(scene = UI_Scene.Editor)] //Tank Type
        public string typeDisp = Localizer.GetStringByTag("#RF_FuelTank_Default"); // "Default"

        [KSPEvent(active = true, guiActiveEditor = true, guiName = "#RF_FuelTank_ChooseTankType", groupName = guiGroupName)] // Choose Tank Type
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

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "#RF_FuelTank_Utilization", guiUnits = "%", guiFormat = "F0", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName), // Utilization
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

        [KSPField(guiActiveEditor = true, guiName = "#RF_FuelTank_Volume", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName)] // Volume
        public string volumeDisplay;

        // Conversion between tank volume in kL, and whatever units this tank uses.
        // Default to 1000 for RF. Varies for MFT. Needed to interface with PP.
        [KSPField]
        public float tankVolumeConversion = 1000;

        [KSPField(isPersistant = true)]
        public float mass;

        [KSPField]
        public bool massIsAdditive = false;

        [KSPField(guiActiveEditor = true, guiName = "#RF_FuelTank_Mass", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName)] // Mass
        public string massDisplay;

        [KSPField(guiActiveEditor = true, guiName = "#RF_FuelTank_TankUI", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName)] // Tank UI 
        [UI_Toggle(enabledText = "#RF_FuelTank_Hide", disabledText = "#RF_FuelTank_Show", suppressEditorShipModified = true)] // HideShow
        [NonSerialized]
        public bool showUI;

        bool started;
        internal bool massDirty = true;
        private bool windowDirty = false;

        internal HashSet<string> managedResources = new HashSet<string>(32);
        private bool IsManaged(string n) => managedResources.Contains(n) && !unmanagedResources.ContainsKey(n);

        public bool fueledByLaunchClamp = false;

        private const string guiGroupName = "RealFuels";
        private const string guiGroupDisplayName = "#RF_FuelTank_GroupDisplayName"; // "Real Fuels"

        public double UsedVolume { get; private set; }

        public double AvailableVolume => volume - UsedVolume;

        private static double MassMult => MFSSettings.useRealisticMass ? 1.0 : MFSSettings.tankMassMultiplier;

        private static float DefaultBaseCostPV => MFSSettings.baseCostPV;

        public delegate void UpdateTweakableButtonsDelegateType();
        public UpdateTweakableButtonsDelegateType UpdateTweakableButtonsDelegate;
        public override void OnAwake()
        {
            UpdateTweakableButtonsDelegate = (UpdateTweakableButtonsDelegateType)Delegate.CreateDelegate(typeof(UpdateTweakableButtonsDelegateType), this, "UpdateTweakableButtons", true);

            if (utilization == -1)
                utilization = Mathf.Clamp(MFSSettings.partUtilizationDefault, minUtilization, maxUtilization);

            if (HighLogic.LoadedScene == GameScenes.LOADING)
                unmanagedResources = new Dictionary<string, UnmanagedResource>();
            else if (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor)
            {
                int index = part.Modules.IndexOf(this);
                if (index < 0)
                    index = part.Modules.Count;
                Part prefab = part.partInfo.partPrefab;
                ModuleFuelTanks mft;
                if (prefab.Modules.Count > index && prefab.Modules[index] is ModuleFuelTanks m)
                    mft = m;
                else
                    mft = prefab.FindModuleImplementing<ModuleFuelTanks>();
                unmanagedResources = mft.unmanagedResources;
                typesAvailable = new List<TankDefinition>(mft.typesAvailable);  // Copy so any changes don't impact the prefab
                allPossibleTypes = mft.allPossibleTypes;
                managedResources = mft.managedResources;
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

        private void RecordManagedResources(List<TankDefinition> defs)
        {
            managedResources.Clear();
            foreach (TankDefinition def in defs)
                foreach (var kvp in def.tankList)
                    managedResources.Add(kvp.Key);
        }

        private void CleanResources(bool leaveValid = false)
        {
            // Remove only MFT-managed resources
            // Exclude resources allowed in the new tank type if leaveValid is true
            List<PartResource> removeList = part.Resources.Where(x => IsManaged(x.resourceName) && (!leaveValid || !tanksDict.ContainsKey(x.resourceName))).ToList();
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
            tanksDict.Clear ();
            tankList.Clear();
            foreach (var kvp in prefab.tanksDict)
            {
                FuelTank src = kvp.Value;
                var tank = src.CreateCopy(this, null, false);
                tank.maxAmount = src.maxAmount;
                tank.amount = src.amount;
                tanksDict.Add(kvp.Key, tank);
                tankList.Add(tank);
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            // Make sure this isn't an upgrade node because if we got here during an upgrade application
            // then RaiseResourceListChanged will throw an error when it hits SendEvent()
            if (node.name == "CURRENTUPGRADE")
            {
                if (part != part.partInfo.partPrefab)   // Don't update the prefab, which is active on Toolbox mouseover
                    UpdateTypesAvailable(node);
            }
            else if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                typesAvailable.ResolveAndAddUnique(type);
                GatherUnmanagedResources(node);
                InitUtilization();
                InitVolume(node);

                MFSSettings.SaveOverrideList(part, node.GetNodes("TANK"));
                ParseBaseMass(node);
                ParseBaseCost(node);
                UpdateTypesAvailable(node);
                GatherAllPossibleTypes(node);
                RecordManagedResources(allPossibleTypes);
                UpdateTankType(initializeAmounts: true);
            }
            else if (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)
            {
                // The amounts initialized flag is there so that the tank type loading doesn't
                // try to set up any resources. They'll get loaded directly from the save.
                UpdateTankType(false);

                InitUtilization();
                InitVolume(node);

                CleanResources();

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

        public override string GetInfo()
        {
            var info = StringBuilderCache.Acquire();
            info.AppendLine ($"{Localizer.GetStringByTag("#RF_FuelTank_ModularFuelTank")}:"); // Modular Fuel Tank
            info.Append ($"  {Localizer.GetStringByTag("#RF_FuelTank_MaxVolume")}: ").AppendLine (KSPUtil.PrintSI (volume, MFSSettings.unitLabel)); // Max Volume
            info.AppendLine ($"  {Localizer.GetStringByTag("#RF_FuelTank_TankCanHold")}:"); // Tank can hold
            foreach (FuelTank tank in tanksDict.Values)
                //info.Append("      ").Append(tank).Append(" ").AppendLine(tank.note);
                info.Append("      ").Append(PartResourceLibrary.Instance.GetDefinition(tank.name).displayName).Append(" ").AppendLine(tank.note);
            return info.ToStringAndRelease();
        }

        public string GetPrimaryField () => $"{Localizer.GetStringByTag("#RF_FuelTank_MaxVolume")}: {KSPUtil.PrintSI(volume, MFSSettings.unitLabel)}, {type}{(typesAvailable.Count() > 1 ? "*" : "")}"; // Max Volume

        public Callback<Rect> GetDrawModulePanelCallback() => null;

        public string GetModuleTitle() => "Modular Fuel Tank";
        public override string GetModuleDisplayName() => Localizer.GetStringByTag("#RF_FuelTank_ModularFuelTank");

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onPartAttach.Add(OnPartAttach);
                GameEvents.onPartRemove.Add(OnPartRemove);
                GameEvents.onEditorShipModified.Add(OnEditorShipModified);
                GameEvents.onPartActionUIDismiss.Add(OnPartActionGuiDismiss);
                GameEvents.onPartActionUIShown.Add(OnPartActionUIShown);
                if (MFSSettings.previewAllLockedTypes)
                    GatherLockedTypesFromAllPossible();
                InitializeTankType();
                UpdateTankType(false);
                InitUtilization();
                Fields[nameof(utilization)].uiControlEditor.onFieldChanged += OnUtilizationChanged;
                Fields[nameof(utilization)].uiControlEditor.onSymmetryFieldChanged += OnUtilizationChanged;
                Fields[nameof(typeDisp)].uiControlEditor.onFieldChanged += OnTypeDispChanged;
                Fields[nameof(typeDisp)].uiControlEditor.onSymmetryFieldChanged += OnTypeDispChanged;
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
            // Don't spam save files with empty tank nodes, only save the relevant stuff
            foreach (FuelTank tank in tanksDict.Values.Where(t => t.amount > 0 || t.maxAmount > 0))
            {
                ConfigNode tankNode = new ConfigNode("TANK");
                tank.Save(tankNode);
                node.AddNode(tankNode);
            }
            OnSaveRF(node);
        }

        private void OnUtilizationChanged(BaseField f, object obj) => ChangeTotalVolume(totalVolume);

        private void OnTypeDispChanged(BaseField f, object obj)
        {
            TankDefinition def = typesAvailable.First(t => t.Title == typeDisp);
            type = def.name;
        }

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
                UpdateTankType(true);
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
            Fields[nameof(typeDisp)].guiActiveEditor = typesAvailable.Count > 1;
            var c = (Fields[nameof(typeDisp)].uiControlEditor as UI_ChooseOption);
            c.options = typesAvailable.Select(t => t.Title).ToArray();
            c.display = typesAvailable.Select(t => ConstructColoredTypeTitle(t)).ToArray();
        }

        private string ConstructColoredTypeTitle(TankDefinition def)
        {
            if (!MFSSettings.previewAllLockedTypes || HighLogic.LoadedScene == GameScenes.LOADING)
                return def.Title;

            string partTech = part.partInfo.TechRequired;
            if (string.IsNullOrEmpty(partTech) || ResearchAndDevelopment.GetTechnologyState(partTech) != RDTech.State.Available)
                return $"<color=orange>{def.Title}</color>";

            if (!upgradeLookup.TryGetValue(def.name, out PartUpgradeHandler.Upgrade upgrade))
            {
                upgrade = GetUpgradeForType(this, def.name);
            }
            bool isTechAvailable = upgrade == null || ResearchAndDevelopment.GetTechnologyState(upgrade.techRequired) == RDTech.State.Available;
            return isTechAvailable ? def.Title : $"<color=orange>{def.Title}</color>";
        }

        public void AllowLockedTypes(List<string> lockedList)
        {
            IEnumerable<string> actuallyLockedTypes = lockedList.Where(x => !typesAvailable.Any(t => t.name == x));
            typesAvailable.ResolveAndAddUnique(actuallyLockedTypes);
            lockedTypes.ResolveAndAddUnique(actuallyLockedTypes);
        }

        private void UpdateTypesAvailable(ConfigNode node) => UpdateTypesAvailable(node.GetValuesList("typeAvailable"));
        private void UpdateTypesAvailable(List<string> types)
        {
            typesAvailable.ResolveAndAddUnique(types);
            InitializeTankType();
        }

        private readonly Dictionary<string, PartUpgradeHandler.Upgrade> upgradeLookup = new Dictionary<string, PartUpgradeHandler.Upgrade>();

        public static PartUpgradeHandler.Upgrade GetUpgradeForType(ModuleFuelTanks mft) => GetUpgradeForType(mft, mft.type);

        public static PartUpgradeHandler.Upgrade GetUpgradeForType(ModuleFuelTanks mft, string typeName)
        {
            int index = 0;
            for (int i = 0; i < mft.part.Modules.Count; ++i)
            {
                if (mft.part.Modules[i] == mft)
                    break;
                else if (mft.part.Modules[i].name == nameof(ModuleFuelTanks))
                    ++index;
            }

            int mftIndex = 0;
            foreach (ConfigNode mftNode in mft.part.partInfo.partConfig.GetNodes("MODULE"))
            {
                if (mftNode.GetValue("name") == nameof(ModuleFuelTanks))
                {
                    if (mftIndex++ != index)
                        continue;

                    var node = mftNode.GetNode("UPGRADES");
                    if (node != null)
                    {
                        foreach (var upNode in node.GetNodes("UPGRADE"))
                        {
                            foreach (ConfigNode.Value v in upNode.values)
                            {
                                if (v.value == typeName)
                                {
                                    string upgradeName = upNode.GetValue("name__");
                                    return PartUpgradeManager.Handler.GetUpgrade(upgradeName);
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        public virtual bool Validate(out string validationError, out bool canBeResolved, out float costToResolve, out string techToResolve)
        {
            validationError = null;
            canBeResolved = false;
            costToResolve = 0;
            techToResolve = null;
            if (!MFSSettings.tankDefinitions.TryGetValue(type, out TankDefinition def))
            {
                validationError = $"definition {type} has no global definition";
            }
            else if (!typesAvailable.Contains(def))
            {
                validationError = $"definition {def.Title} is not available";
            }
            else if (lockedTypes.Contains(def))
            {
                validationError = $"definition {def.Title}: is currently locked";
            }

            if (def != null)
            {
                if (!upgradeLookup.TryGetValue(type, out var upgrade))
                {
                    upgrade = GetUpgradeForType(this, type);
                }
                if (upgrade != null)
                {
                    canBeResolved = ResearchAndDevelopment.GetTechnologyState(upgrade.techRequired) == RDTech.State.Available;
                    costToResolve = upgrade.entryCost;
                    techToResolve = upgrade.techRequired;
                    validationError = $"definition {def.Title}: {(canBeResolved ? string.Empty : $"research {techToResolve} and ")}purchase the upgrade";
                }
            }

            return validationError == null;
        }

        public virtual bool ResolveValidationError()
        {
            PartUpgradeHandler.Upgrade upgrade = GetUpgradeForType(this, type);
            if (upgrade == null)
                return false;

            CurrencyModifierQuery cmq = CurrencyModifierQuery.RunQuery(TransactionReasons.RnDPartPurchase, -upgrade.entryCost, 0, 0);
            if (!cmq.CanAfford())
                return false;

            PartUpgradeManager.Handler.SetUnlocked(upgrade.name, true);
            GameEvents.OnPartUpgradePurchased.Fire(upgrade);    // This deducts the funds
            ApplyUpgrades(StartState.Editor);
            return true;
        }

        // This is strictly a change handler!
        private void UpdateTankType (bool initializeAmounts = false)
        {
            if (oldType == type || type == null) {
                return;
            }

            // Copy the tank list from the tank definitiion
            if (!MFSSettings.tankDefinitions.TryGetValue(type, out TankDefinition def))
            {
                string msg = $"[ModuleFuelTanks] Tried to set tank type to {type} but it has no definition.";
                if (!MFSSettings.tankDefinitions.TryGetValue(oldType, out def))
                {
                    def = typesAvailable.First();
                }
                type = def.name;
                Debug.LogError($"{msg} Reset to {type}");
            }

            string oldTypeForEvent = oldType;
            oldType = type;
            typeDisp = def.Title;

            // Build the new tank list.
            tanksDict.Clear();
            tankList.Clear();
            foreach (FuelTank tank in def.tankList.Values) {
                // Pull the override from the list of overrides
                ConfigNode overNode = MFSSettings.GetOverrideList(part).FirstOrDefault(n => n.GetValue("name") == tank.name);
                var newTank = tank.CreateCopy(this, overNode, initializeAmounts);
                if (!newTank.canHave)
                    newTank.maxAmount = 0;
                tanksDict.Add(newTank.name, newTank);
                tankList.Add(newTank);
            }

            // Destroy any managed resources that are not in the new type.
            var removeList = part.Resources.Where(x => managedResources.Contains(x.resourceName) && !tanksDict.ContainsKey(x.resourceName) && !unmanagedResources.ContainsKey(x.resourceName)).ToList();
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
            RaiseTankDefinitionChanged(oldTypeForEvent, def);
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

        [KSPEvent]
        void LoadMFTModuleFromConfigNode(BaseEventDetails data)
        {
            // Things that are supported:
            // setting type/Tank_Definition
            // Adding typeAvailable
            // setting volume
            // setting basemass
            // setting basecost
            // setting TANKs (Clears the current tanks)

            ConfigNode MFTConfigNode = data.Get<ConfigNode>("MFTNode");

            // 'typeAvailable = x' lines provided
            List<string> types = MFTConfigNode.GetValuesList("typeAvailable");

            // 'type = x' provided
            if (MFTConfigNode.TryGetValue("type", ref type))
            {
                types.Add(type);
            }

            if (types.Count > 0)
            {
                typesAvailable.Clear();
            }
            UpdateTypesAvailable(types);

            // 'volume = x' provided
            MFTConfigNode.TryGetValue("volume", ref volume);

            // 'basemass = x' provided
            ParseBaseMass(MFTConfigNode);

            // 'basecost = x' provided
            ParseBaseCost(MFTConfigNode);

            // 'TANK {}' provided
            ConfigNode[] tankNodes = MFTConfigNode.GetNodes("TANK");
            if (tankNodes.Length > 0)
            {
                // Clear the current tank before adding the new ones
                Empty();
                foreach (var tankNode in tankNodes)
                {
                    var tankName = tankNode.GetValue("name");
                    var maxAmount = double.Parse(tankNode.GetValue("maxAmount"));
                    var amount = double.Parse(tankNode.GetValue("amount"));
                    if (tanksDict.TryGetValue(tankName, out FuelTank internalTank))
                    {
                        internalTank.amount = amount;
                        internalTank.maxAmount = maxAmount;
                    }
                }
            }
        }

        // ChangeVolume() called by StretchyTanks has been converted to use OnPartVolumeChanged

        protected void ChangeResources (double volumeRatio, bool propagate = false)
        {
            // The used volume will rescale automatically when setting maxAmount
            foreach (FuelTank tank in tanksDict.Values)
            {
                bool save_propagate = tank.propagate;
                tank.propagate = propagate;
                tank.maxAmount *= volumeRatio;
                tank.propagate = save_propagate;
            }
        }

        public void ChangeTotalVolume (double newTotalVolume, bool propagate = false)
        {
            double oldVolume = volume;
            double newVolume = Math.Round (newTotalVolume * utilization * 0.01d, 4);
            totalVolume = newTotalVolume;
            volume = newVolume;

            if (oldVolume > 0)    // Can't rescale resource amounts if previously the tank had 0 volume and thus also no resources
            {
                double volumeRatio = newVolume / oldVolume;
                bool doResources = false;
                if (oldVolume > newVolume)
                {
                    ChangeResources (volumeRatio, propagate);
                }
                else
                {
                    doResources = true;
                }

                if (propagate)
                {
                    foreach (Part p in part.symmetryCounterparts)
                    {
                        // FIXME: Not safe, assumes only 1 MFT on the part.
                        ModuleFuelTanks m = p.FindModuleImplementing<ModuleFuelTanks>();
                        m.totalVolume = newTotalVolume;
                        m.volume = newVolume;
                    }
                }

                if (doResources)
                {
                    ChangeResources (volumeRatio, propagate);
                }
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
                double tankDryMass = tanksDict.Values.Sum(t => t.Volume * t.mass);
                mass = (float) ((basemass + tankDryMass) * MassMult);
            }
            else
            {
                mass = 0;
                massIsAdditive = true;
            }

            if (HighLogic.LoadedSceneIsEditor) {
                UsedVolume = tanksDict.Values.Sum(t => t.Volume);

                double availRounded = AvailableVolume;
                if (Math.Abs(availRounded) < 0.001d)
                    availRounded = 0d;
                string availVolStr = KSPUtil.PrintSI (availRounded, MFSSettings.unitLabel);
                string volStr = KSPUtil.PrintSI (volume, MFSSettings.unitLabel);
                volumeDisplay = Localizer.Format("#RF_FuelTank_volumeDisplayinfo1", availVolStr, volStr); // "Avail: " + availVolStr + " / Tot: " + volStr

                double resourceMass = part.Resources.Cast<PartResource> ().Sum (partResource => partResource.maxAmount* partResource.info.density);

                double wetMass = mass + resourceMass;
                massDisplay = Localizer.Format("#RF_FuelTank_volumeDisplayinfo2", ResourceUnits.PrintMass(mass), ResourceUnits.PrintMass(wetMass)); // "Dry: " + FormatMass (mass) + " / Wet: " + FormatMass ((float)wetMass)

                UpdateTweakableMenu ();
            }
        }

        // mass-change interface, so Engineer's Report / Pad limit checking is correct.
        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) => massIsAdditive ? mass : mass - defaultMass;

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

        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit)
        {
            double cst = Mathf.Max(0f, baseCostConst);
            if (baseCostPV >= 0f && baseCostConst >= 0f)
            {
                cst += volume * Mathf.Max(baseCostPV, 0f);
                cst += tanksDict.Values.Sum(t => t.Volume * t.cost);
            }
            GetModuleCostRF(ref cst);
            return (float)cst;
        }

        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;

        public void RaiseResourceInitialChanged(PartResource resource, double amount)
        {
            var data = new BaseEventDetails(BaseEventDetails.Sender.USER);
            data.Set<PartResource>("resource", resource);
            data.Set<double>("amount", amount);
            part.SendEvent("OnResourceInitialChanged", data, 0);
        }

        public void RaiseResourceMaxChanged(PartResource resource, double amount)
        {
            var data = new BaseEventDetails(BaseEventDetails.Sender.USER);
            data.Set<PartResource>("resource", resource);
            data.Set<double>("amount", amount);
            part.SendEvent("OnResourceMaxChanged", data, 0);
        }

        public void RaiseResourceListChanged()
        {
            GameEvents.onPartResourceListChange.Fire(part);
            part.ResetSimulationResources();
            part.SendEvent("OnResourceListChanged", null, 0);
            MarkWindowDirty();
        }

        public void RaiseTankDefinitionChanged(string oldType, TankDefinition newDef)
        {
            var data = new BaseEventDetails(BaseEventDetails.Sender.USER);
            data.Set<string>("oldTypeName", oldType);
            data.Set<string>("newTypeName", newDef.name);
            data.Set("newDef", newDef);
            part.SendEvent("OnTankDefinitionChanged", data, 0);
        }

        public void PartResourcesChanged()
        {
            // We'll need to update the volume display regardless
            massDirty = true;
        }

        [KSPEvent(guiName = "#RF_TankWindow_RemoveAllTanks", guiActiveEditor = true, name = "Empty", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName)] // 
        public void Empty()
        {
            foreach (FuelTank tank in tanksDict.Values)
                tank.maxAmount = 0;
            MarkWindowDirty();
            GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }

        internal void MarkWindowDirty()
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

        private void GatherAllPossibleTypes(ConfigNode node)
        {
            allPossibleTypes.Clear();
            allPossibleTypes.ResolveAndAddUnique(type);
            allPossibleTypes.ResolveAndAddUnique(node.GetValuesList("typeAvailable"));
            if (node.GetNode("UPGRADES") is ConfigNode upgradeNodeContainer)
                foreach (var upgradeNode in upgradeNodeContainer.GetNodes("UPGRADE"))
                    allPossibleTypes.ResolveAndAddUnique(upgradeNode.GetValuesList("typeAvailable"));
        }

        private void GatherLockedTypesFromAllPossible()
        {
            var lockedTypes = allPossibleTypes.Where(x => !typesAvailable.Contains(x));
            this.lockedTypes.AddUniqueRange(lockedTypes);
            typesAvailable.AddUniqueRange(this.lockedTypes);
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

        internal readonly Dictionary<PartModule, FuelInfo> usedBy = new Dictionary<PartModule, FuelInfo>();
        internal readonly HashSet<FuelTank> usedByTanks = new HashSet<FuelTank>();

        private void UpdateFuelInfo(FuelInfo f, PartModule source)
        {
            usedBy[source] = f;
            foreach (Propellant tfuel in f.propellantVolumeMults.Keys)
                if (tanksDict.TryGetValue(tfuel.name, out FuelTank tank) && tank.canHave)
                    usedByTanks.Add(tank);
        }

        public void UpdateUsedBy()
        {
            if (!HighLogic.LoadedSceneIsEditor) return;

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
                    if (f?.valid == true)
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
                            guiName = Localizer.Format("#RF_FuelTank_FillTank", info.title), // $"Fill: {info.title}"
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
                MarkWindowDirty();
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
            if (!fi.valid) // can't configure for this engine
                return;

            double availableVolume = AvailableVolume;
            foreach (Propellant tfuel in fi.propellantVolumeMults.Keys)
            {
                if (tanksDict.TryGetValue(tfuel.name, out FuelTank tank))
                {
                    double amt = availableVolume * tfuel.ratio / fi.efficiency;
                    tank.maxAmount += amt;
                    tank.amount += amt;
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
        partial void OnSaveRF(ConfigNode node);
        partial void UpdateRF();

        #endregion
    }
}
