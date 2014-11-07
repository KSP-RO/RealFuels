using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using System.Collections.ObjectModel;
using KSPAPIExtensions;
using KSPAPIExtensions.PartMessage;

// ReSharper disable InconsistentNaming, CompareOfFloatsByEqualityOperator

namespace RealFuels.Tanks
{
    public class ModuleFuelTanks : PartModule, IPartCostModifier
    {
        #region loading stuff from config files

        bool compatible = true;
        
        [KSPField]
        public int offsetGUIPos = -1;

        private static float MassMult
        {
            get
            {
                return MFSSettings.Instance.useRealisticMass ? 1.0f : MFSSettings.Instance.tankMassMultiplier;
            }
        }

        private static MFSSettings Settings
        {
            get { return MFSSettings.Instance; }
        }

        private static float defaultBaseCostPV
        {
            get
            {
                return MFSSettings.Instance.baseCostPV;
            }
        }

        #endregion

        #region KSP Callbacks

        public override void OnAwake()
        {
            enabled = false;
            if (!CompatibilityChecker.IsAllCompatible())
            {
                compatible = false;
                return;
            }
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
            if (!compatible)
                return;
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
            if (!compatible)
                return;
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
                    // FIXME should detect if DB reload in progress; for now, check if in space center too...
                    if (GameSceneFilter.Loading.IsLoaded() || GameSceneFilter.SpaceCenter.IsLoaded())
                    {
                        LoadTankListOverridesInLoading(node);

                        ParseBasemass(node);
                        ParseBaseCost(node);

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
                            part.Resources.list.RemoveAt(i);
                            DestroyImmediate(partResource);
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
            if (!compatible)
                return "";
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
            if (!compatible)
                return;
            if (HighLogic.LoadedSceneIsEditor)
                enabled = true;
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
            if (!compatible)
                return;
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
            if (!compatible)
                return;
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

        public void FixedUpdate()
        {
            if (!compatible)
                return;
            try
            {
				CalculateTankLossFunction(TimeWarp.fixedDeltaTime);
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
            if ((object)typesAvailable == null || typesAvailable.Length <= 1)
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
            TankDefinition def;
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
                part.Resources.list.RemoveAt(i);
                Destroy(partResource);
                needsMesage = true;
            }
            if(needsMesage)
                RaiseResourceListChanged();

            // Update the basemass
            if (!basemassOverride)
                ParseBasemass(def.basemass);
            if (!baseCostOverride)
                ParseBaseCost(def.baseCost);

            // fixmne should detect DB reload. For now, just detecting space center...
            if (GameSceneFilter.Loading.IsLoaded() || GameSceneFilter.SpaceCenter.IsLoaded())
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
        
        [KSPField]
        public bool utilizationTweakable = false;


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

        protected void ChangeResources(double volumeRatio, bool propagate = false)
        {
            // The used volume will rescale automatically when setting maxAmount
            foreach (FuelTank tank in tankList)
            {
                bool btmp = tank.propagate;
                if(!propagate)
                    tank.propagate = false;
                tank.maxAmount *= volumeRatio;
                if (!propagate)
                    tank.propagate = btmp;
            }
        }

        public void ChangeTotalVolume(double newTotalVolume, bool propagate = false)
        {
            double newVolume = Math.Round(newTotalVolume * utilization / 100);
            double volumeRatio = newVolume / volume;
            bool doResources = false;
            if (totalVolume > newTotalVolume)
                ChangeResources(volumeRatio, propagate);
            else
                doResources = true;
            totalVolume = newTotalVolume;
            volume = newVolume;
            if (propagate)
            {
                foreach (Part p in part.symmetryCounterparts)
                {
                    // FIXME: Not safe, assumes only 1 MFT on the part.
                    ModuleFuelTanks m = (ModuleFuelTanks)p.Modules["ModuleFuelTanks"];
                    m.totalVolume = newTotalVolume;
                    m.volume = newVolume;
                }
            }
            if(doResources)
                ChangeResources(volumeRatio, propagate);
            massDirty = true;
        }

        public void ChangeVolumeRatio(double ratio, bool propagate = false)
        {
            ChangeTotalVolume(totalVolume * ratio, propagate);
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
            Fields["utilization"].guiActiveEditor = Settings.partUtilizationTweakable || utilizationTweakable;
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
        public bool baseCostOverride;
        public float basemassPV;
        public float baseCostPV;
        public float basemassConst;
        public float baseCostConst;

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

        private void ParseBaseCost(ConfigNode node)
        {
            if (!node.HasValue("baseCost"))
                return;

            string baseCost = node.GetValue("baseCost");
            ParseBaseCost(baseCost);
            baseCostOverride = true;
        }

        private void ParseBaseCost(string baseCost)
        {
            if (baseCost == null)
                baseCost = "";
            if (baseCost.Contains("*") && baseCost.Contains("volume"))
            {
                if (float.TryParse(baseCost.Replace("volume", "").Replace("*", "").Trim(), out baseCostPV))
                {
                    baseCostConst = 0;
                    return;
                }
            }
            else if (float.TryParse(baseCost.Trim(), out baseCostPV))
            {
                baseCostPV = (float)(baseCostPV / volume);
                baseCostConst = 0;
                return;
            }
            if (baseCost != "")
                Debug.LogWarning("[MFT] Unable to parse baseCost \"" + baseCost + "\"");
            else if ((object)MFSSettings.Instance != null)
                baseCostPV = MFSSettings.Instance.baseCostPV;
            else
                baseCostPV = 0.01f;
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

        public float GetModuleCost()
        {
            double cst = 0;
            if (baseCostPV >= 0)
            {
                cst = volume * baseCostPV;
                if ((object)(PartResourceLibrary.Instance) != null && (object)tankList != null)
                {
                    foreach (FuelTank t in tankList)
                    {
                        if ((object)t.resource != null)
                        {
                            PartResourceDefinition d = PartResourceLibrary.Instance.GetDefinition(t.resource.resourceName);
                            if ((object)d != null)
                                cst += t.maxAmount * (d.unitCost + t.cost / t.utilization);
                        }
                    }
                }
            }
            return (float)cst;
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
			GameEvents.onEditorShipModified.Fire (EditorLogic.fetch.ship);
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

        //[KSPField(isPersistant = true)]
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
        private Rect guiWindowRect = new Rect(0, 0, 0, 0);
        private static Vector3 mousePos = Vector3.zero;
        public void OnGUI()
        {
            if (!compatible)
                return;
            EditorLogic editor = EditorLogic.fetch;
            if (!HighLogic.LoadedSceneIsEditor || !editor || dedicated) {
                return;
            }

            //UpdateMixtures();
            bool cursorInGUI = false; // nicked the locking code from Ferram
            mousePos = Input.mousePosition; //Mouse location; based on Kerbal Engineer Redux code
            mousePos.y = Screen.height - mousePos.y;

            Rect tooltipRect;
            int posMult = 0;
            if (offsetGUIPos != -1)
                posMult = offsetGUIPos;
            if (editor.editorScreen == EditorLogic.EditorScreen.Actions && EditorActionGroups.Instance.GetSelectedParts ().Contains (part)) 
            {
                guiWindowRect = new Rect(430 * posMult, 365, 438, (Screen.height - 365));
                tooltipRect = new Rect(430 * (posMult+1)+10, mousePos.y-5, 300, 20);
                cursorInGUI = guiWindowRect.Contains(mousePos);
                if (cursorInGUI)
                {
                    editor.Lock(false, false, false, "MFTGUILock");
                    EditorTooltip.Instance.HideToolTip();
                }
                else if (!cursorInGUI)
                {
                    editor.Unlock("MFTGUILock");
                }
            }
            else if (showRFGUI && editor.editorScreen == EditorLogic.EditorScreen.Parts)
            {
                if(guiWindowRect.width == 0)
                    guiWindowRect = new Rect(Screen.width - 8 - 430 * (posMult+1), 365, 438, (Screen.height - 365));
                tooltipRect = new Rect(guiWindowRect.xMin - (230-8), mousePos.y - 5, 220, 20);
                if (cursorInGUI)
                {
                    editor.Lock(false, false, false, "MFTGUILock");
                    EditorTooltip.Instance.HideToolTip();
                }
                else if (!cursorInGUI)
                {
                    editor.Unlock("MFTGUILock");
                }
            }
            else 
            {
                showRFGUI = false;
                return;
            }

            GUI.Label(tooltipRect, myToolTip);
            guiWindowRect = GUILayout.Window(part.name.GetHashCode(), guiWindowRect, GUIWindow, "Fuel Tanks for " + part.partInfo.title);
        }

        public void GUIWindow(int windowID)
        {
            try
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

                    GUIEngines();

                    GUITanks();


                    GUILayout.EndScrollView();
                    GUILayout.Label(MFSSettings.GetVersion());
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
                if(showRFGUI)
                    GUI.DragWindow();
            }
            catch (Exception e)
            {
                print("*RF* Exception in GUI: " + e.Message);
            }
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
            try
            {
                foreach (FuelTank tank in tankList)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(" " + tank, GUILayout.Width(115));



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

                        tank.maxAmountExpression = GUILayout.TextField(tank.maxAmountExpression, style, GUILayout.Width(127));

                        if (GUILayout.Button("Update", GUILayout.Width(53)))
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
                        if (GUILayout.Button("Remove", GUILayout.Width(58)))
                        {
                            tank.maxAmount = 0;
							GameEvents.onEditorShipModified.Fire (EditorLogic.fetch.ship);
                            //Debug.LogWarning("[MFT] Removing tank from button " + tank.name + " amount: " + tank.maxAmountExpression ?? "null");
                        }
                        // FIXME: Need to add the tank logic before this can be reenabled.
                        /*if (tank.locked)
                        {
                            if (GUILayout.Button("=", GUILayout.Width(15)))
                                tank.locked = false;
                        }
                        else
                            if (GUILayout.Button("+", GUILayout.Width(15)))
                                tank.locked = true;*/
                    }
                    else if (AvailableVolume >= 0.001)
                    {
                        string extraData = "Max: " + (AvailableVolume * tank.utilization).ToStringExt("S3") + "L (+" + FormatMass((float)(AvailableVolume * tank.mass)) + " )";

                        GUILayout.Label(extraData, GUILayout.Width(150));

                        if (GUILayout.Button("Add", GUILayout.Width(120)))
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
            catch (Exception e)
            {
                print("RF GUITanks exception " + e);
            }
        }

        private void GUIEngines()
        {
            try
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
            catch (Exception e)
            {
                print("RF GUIEngines exception " + e);
            }
        }

        #endregion

        #region Engine bits

        private readonly Dictionary<string, FuelInfo> usedBy = new Dictionary<string, FuelInfo>();
        private int engineCount;

        List<Propellant> GetEnginePropellants(PartModule engine)
        {
            string typename = engine.GetType().ToString ();
            if (typename.Equals("ModuleEnginesFX"))
            {
                ModuleEnginesFX e = (ModuleEnginesFX)engine;
                return e.propellants;
            }
            else if (typename.Equals("ModuleEngines"))
            {
                ModuleEngines e = (ModuleEngines)engine;
                return e.propellants;
            }
            else if (typename.Equals("ModuleRCSFX"))
            {
                ModuleRCS e = (ModuleRCS)engine;
                return e.propellants;
            }
            else if (typename.Equals("ModuleRCS"))
            {
                ModuleRCS e = (ModuleRCS)engine;
                return e.propellants;
            }
            return null;
        }

        private void UpdateUsedBy()
        {
            //print("*RK* Updating UsedBy");
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
                foreach (PartModule engine_module in engine.Modules)
                {
                    List<Propellant> propellants = GetEnginePropellants (engine_module);
                    if ((object)propellants != null)
                    {
                        FuelInfo f = new FuelInfo(propellants, this, engine.partInfo.title);
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
            if (!compatible)
                return;
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

        public void ConfigureFor(Part engine)
        {
            foreach (PartModule engine_module in engine.Modules)
            {
                List<Propellant> propellants = GetEnginePropellants(engine_module);
                if ((object)propellants != null)
                {
                    ConfigureFor(new FuelInfo(propellants, this, engine.partInfo.title));
                    break;
                }
            }
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

            return new List<Part>(ppart.FindChildParts<Part>(true)).FindAll(p => (p.Modules.Contains("ModuleEngines") || p.Modules.Contains("ModuleEnginesFX") || p.Modules.Contains("ModuleRCSFX") || p.Modules.Contains("ModuleRCS")));
        }

        #endregion
    }
}
