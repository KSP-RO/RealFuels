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
        internal Dictionary<string, FuelTank> tanksDict = new Dictionary<string, FuelTank>();
        internal FuelTankList tankList = new FuelTankList();
        public List<string> typesAvailable = new List<string>();
        internal List<string> lockedTypes = new List<string>();
        internal List<string> allPossibleTypes = new List<string>();    // typesAvailable if all upgrades were applied
        public ConfigNode config;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Tank Type", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string type = "Default";

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

        [KSPField]
        public bool massIsAdditive = false;

        [KSPField(guiActiveEditor = true, guiName = "Mass", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName)]
        public string massDisplay;

        [KSPField(guiActiveEditor = true, guiName = "Tank UI", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName)]
        [UI_Toggle(enabledText = "Hide", disabledText = "Show", suppressEditorShipModified = true)]
        [NonSerialized]
        public bool showUI;

        bool started;
        internal bool massDirty = true;
        private bool windowDirty = false;

        internal HashSet<string> managedResources = new HashSet<string>(32);
        private bool IsManaged(string n) => managedResources.Contains(n) && !unmanagedResources.ContainsKey(n);

        public bool fueledByLaunchClamp = false;

        private const string guiGroupName = "RealFuels";
        private const string guiGroupDisplayName = "Real Fuels";

        public TankDefinition TankDefinition => MFSSettings.tankDefinitions[type];
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
                typesAvailable = new List<string>(mft.typesAvailable);  // Copy so any changes don't impact the prefab
                allPossibleTypes = new List<string>(mft.allPossibleTypes);
                config = mft.config;
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

        private void RecordManagedResources(List<string> types)
        {
            managedResources.Clear();
            foreach (string t in types)
                if (MFSSettings.tankDefinitions.TryGetValue(t, out var def))
                    foreach (var kvp in def.tankList)
                        managedResources.Add(kvp.Key);
        }

        // Return list of resources that are not declared unmanaged and not storable by the given tank definition
        private List<PartResource> UnsupportedResources(string type)
        {
            if (!MFSSettings.tankDefinitions.TryGetValue(type, out TankDefinition def))
                return part.Resources.ToList();
            return part.Resources.Where(r => !unmanagedResources.ContainsKey(r.resourceName) && !def.tankList.Values.Any(t => t.name.Equals(r.resourceName) && t.canHave)).ToList();
        }

        // Remove all resources not valid for this type.
        private void CleanResources(bool leaveValid = false)
        {
            // Remove only MFT-managed resources
            // Exclude resources allowed in the new tank type if leaveValid is true
            List<PartResource> removeList = part.Resources.Where(x => IsManaged(x.resourceName) && (!leaveValid || !tanksDict.ContainsKey(x.resourceName))).ToList();
            if (removeList.Count > 0)
            {
                if (!leaveValid)
                    removeList = new List<PartResource>(part.Resources);
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
                typesAvailable.AddUnique(type);
                GatherUnmanagedResources(node);
                config = node;
                InitUtilization();
                InitVolume(node);
                UpdateTypesAvailable(node);
                ValidateTankType();
                BuildTanks(node, true);    // Starting from the definition, apply the node on top and set resources
                ParseBaseMass(node);
                ParseBaseCost(node);
                GatherAllPossibleTypes(node);
                UpdateTankTypeRF(MFSSettings.tankDefinitions[type]);
            }
            else if (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)
            {
                // Load the persistent data (from .craft or .sfs)
                // Always re-generate this list from the current set of available types
                RecordManagedResources(typesAvailable);   // Also called via UpdateTypesAvailable()
                config = node;
                ValidateTankType();

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
            info.AppendLine ("Modular Fuel Tank:");
            info.Append ("  Max Volume: ").AppendLine (KSPUtil.PrintSI (volume, MFSSettings.unitLabel));
            info.AppendLine ("  Tank can hold:");
            foreach (FuelTank tank in tanksDict.Values)
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
                if (MFSSettings.previewAllLockedTypes)
                    GatherLockedTypesFromAllPossible();
                InitializeTankType();
                InitUtilization();
                Fields[nameof(utilization)].uiControlEditor.onFieldChanged += OnUtilizationChanged;
                Fields[nameof(utilization)].uiControlEditor.onSymmetryFieldChanged += OnUtilizationChanged;
                Fields[nameof(type)].uiControlEditor.onFieldChanged += OnTankTypeChanged;
                Fields[nameof(type)].uiControlEditor.onSymmetryFieldChanged += OnTankTypeChanged;
            }
            ValidateTankType();
            // If we never passed an OnLoad() then config will be from the prefab
            BuildTanks(config, false);    // Starting from definition, apply the node on top but do not adjust amounts
            UpdateTankTypeRF(MFSSettings.tankDefinitions[type]);
            OnStartRF(state);

            massDirty = true;
            CalculateMass();

            UpdateUsedBy();
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
            OnDestroyRF();
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
            InitializeTankType();
        }

        private readonly Dictionary<string, PartUpgradeHandler.Upgrade> upgradeLookup = new Dictionary<string, PartUpgradeHandler.Upgrade>();

        public static PartUpgradeHandler.Upgrade GetUpgradeForType(ModuleFuelTanks mft)
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
                                if (v.value == mft.type)
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
            bool defFound = true;
            if (!MFSSettings.tankDefinitions.ContainsKey(type))
            {
                defFound = false;
                validationError = $"definition {type} has no global definition";
            }
            else if (!typesAvailable.Contains(type))
            {
                validationError = $"definition {type} is not available";
            }
            else if (lockedTypes.Contains(type))
            {
                validationError = $"definition {type}: is currently locked";
            }

            if (defFound)
            {
                if (!upgradeLookup.TryGetValue(type, out var upgrade))
                {
                    upgrade = GetUpgradeForType(this);
                }
                if (upgrade != null)
                {
                    canBeResolved = ResearchAndDevelopment.GetTechnologyState(upgrade.techRequired) == RDTech.State.Available;
                    costToResolve = upgrade.entryCost;
                    techToResolve = upgrade.techRequired;
                    validationError = $"definition {type}: {(canBeResolved ? string.Empty : $"research {techToResolve} and")}purchase the upgrade";
                }
            }

            return validationError == null;
        }

        public virtual bool ResolveValidationError()
        {
            PartUpgradeHandler.Upgrade upgrade = GetUpgradeForType(this);
            if (upgrade == null)
                return false;

            CurrencyModifierQuery cmq = CurrencyModifierQuery.RunQuery(TransactionReasons.RnDPartPurchase, -upgrade.entryCost, 0, 0);
            if (!cmq.CanAfford())
                return false;

            PartUpgradeManager.Handler.SetUnlocked(upgrade.name, true);
            GameEvents.OnPartUpgradePurchased.Fire(upgrade);
            ApplyUpgrades(StartState.Editor);
            return true;
        }

        private void ValidateTankType()
        {
            if (!MFSSettings.tankDefinitions.TryGetValue(type, out TankDefinition def))
            {
                string replacementType = typesAvailable.FirstOrDefault();
                Debug.LogError($"[ModuleFuelTanks] Found tank type {type} on {part} but it has no definition.  Reset to {replacementType}");
                type = replacementType;
            }
        }

        // Starting from the definition, apply the node on top
        private void BuildTanks(ConfigNode node, bool initializeAmounts = false)
        {
            tanksDict.Clear();
            tankList.Clear();
            if (MFSSettings.tankDefinitions.TryGetValue(type, out TankDefinition def))
            {
                foreach (var res in UnsupportedResources(type))
                {
                    part.Resources.Remove(res);
                    part.SimulationResources?.Remove(res);
                }
                foreach (FuelTank tank in def.tankList.Values.Where(t => !unmanagedResources.ContainsKey(t.name) && t.canHave))
                {
                    ConfigNode tankNode = node?.GetNode("TANK", "name", tank.name);
                    FuelTank newTank = tank.CreateCopy(this, tankNode, initializeAmounts);
                    tanksDict.Add(newTank.name, newTank);
                    tankList.Add(newTank);
                }
            }
            BuildTanksRF();
        }

        private void OnTankTypeChanged(BaseField field, object obj) => UpdateTankType();
        private void UpdateTankType()
        {
            ValidateTankType();
            if (!MFSSettings.tankDefinitions.TryGetValue(type, out TankDefinition def))
                return;

            // If there are any unsupported resources for the new type, remove *all* resources.
            CleanResources(true);
            BuildTanks(config, false);
            if (!basemassOverride)
                ParseBaseMass(def.basemass);
            if (!baseCostOverride)
                ParseBaseCost(def.baseCost);

            UpdateUsedBy();
            UpdateTankTypeRF(def);
            UpdateTestFlight();
            massDirty = true;
            CalculateMass();
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
                volumeDisplay = "Avail: " + availVolStr + " / Tot: " + volStr;

                double resourceMass = part.Resources.Cast<PartResource> ().Sum (partResource => partResource.maxAmount* partResource.info.density);

                double wetMass = mass + resourceMass;
                massDisplay = "Dry: " + FormatMass (mass) + " / Wet: " + FormatMass ((float)wetMass);

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

        public void PartResourcesChanged()
        {
            // We'll need to update the volume display regardless
            massDirty = true;
        }

        [KSPEvent(guiName = "Remove All Tanks", guiActiveEditor = true, name = "Empty", groupName = guiGroupName, groupDisplayName = guiGroupDisplayName)]
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
            allPossibleTypes.Add(type);
            allPossibleTypes.AddUniqueRange(node.GetValuesList("typeAvailable"));
            if (node.GetNode("UPGRADES") is ConfigNode upgradeNodeContainer)
                foreach (var upgradeNode in upgradeNodeContainer.GetNodes("UPGRADE"))
                    allPossibleTypes.AddUniqueRange(upgradeNode.GetValuesList("typeAvailable"));
        }

        private void GatherLockedTypesFromAllPossible()
        {
            var validLockedTypes = allPossibleTypes.Where(x => MFSSettings.tankDefinitions.ContainsKey(x) && !typesAvailable.Contains(x));
            lockedTypes.AddUniqueRange(validLockedTypes);
            typesAvailable.AddUniqueRange(lockedTypes);
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
                            guiName = $"Fill: {info.title}",
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
        partial void OnDestroyRF();
        partial void BuildTanksRF();

        #endregion
    }
}
