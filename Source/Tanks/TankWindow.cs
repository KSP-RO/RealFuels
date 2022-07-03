using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using KSP.UI.Screens;
using System.Linq;

// ReSharper disable InconsistentNaming, CompareOfFloatsByEqualityOperator

namespace RealFuels.Tanks
{
    [KSPAddon (KSPAddon.Startup.EditorAny, false)]
    public class TankWindow : MonoBehaviour
    {
        internal static EventVoid OnActionGroupEditorOpened = new EventVoid ("OnActionGroupEditorOpened");
        internal static EventVoid OnActionGroupEditorClosed = new EventVoid ("OnActionGroupEditorClosed");
        [KSPField]
        public int offsetGUIPos = -1;

        [KSPField (isPersistant = false, guiActiveEditor = true, guiActive = false, guiName = "Show Tank"),
         UI_Toggle (enabledText = "Tank GUI", disabledText = "GUI")]
        [NonSerialized]
        public bool showRFGUI;

        private GUIStyle unchanged;
        private GUIStyle changed;
        private GUIStyle greyed;
        private GUIStyle overfull;
        public string myToolTip = "";

        private static TankWindow instance;

        private int counterTT;
        private Vector2 scrollPos;

        private bool ActionGroupMode;
        private ModuleFuelTanks tank_module;

        private readonly Dictionary<string, string> addLabelCache = new Dictionary<string, string>();
        private double oldAvailableVolume = 0;
        private string oldTankType = "newnewnew"; //force refresh on first call to EnsureFreshAddLabelCache()

        public static void HideGUI()
        {
            instance?.ShowHideGUI(null);
            if (instance != null && EditorLogic.fetch is EditorLogic editor && editor != null)
                editor.Unlock("MFTGUILock");
        }

        public static void HideGUIForModule(ModuleFuelTanks tank_module)
        {
            if (instance?.tank_module == tank_module)
                HideGUI();
        }

        public static void ShowGUI(ModuleFuelTanks tank_module) => instance?.ShowHideGUI(tank_module);

        private void ShowHideGUI(ModuleFuelTanks tank_module)
        {
            if (instance != null)
            {
                instance.tank_module = tank_module;
                instance.enabled = tank_module != null;
                if (!instance.enabled && EditorLogic.fetch is EditorLogic editor && editor != null)
                    editor.Unlock("MFTGUILock");
            }
        }

        private void EnsureFreshAddLabelCache()
        {
            if (tank_module.AvailableVolume != oldAvailableVolume || tank_module.type != oldTankType){
                foreach (FuelTank tank in tank_module.tankList) {
                    double maxVol = tank_module.AvailableVolume * tank.utilization;
                    string maxVolStr = KSPUtil.PrintSI(maxVol, "L");
                    string label = "Max: " + maxVolStr + " (+" + ModuleFuelTanks.FormatMass((float)(tank_module.AvailableVolume * tank.mass)) + " )";
                    addLabelCache[tank.name] = label;
                }
                oldAvailableVolume = tank_module.AvailableVolume;
                oldTankType = tank_module.type;
            }
        }

        private IEnumerator CheckActionGroupEditor ()
        {
            while (EditorLogic.fetch == null) {
                yield return null;
            }
            EditorLogic editor = EditorLogic.fetch;
            while (EditorLogic.fetch != null)
            {
                if (editor.editorScreen == EditorScreen.Actions)
                {
                    if (!ActionGroupMode)
                    {
                        Debug.Log("TankWindow.CheckActionGroupEditor() hiding tank window (!AGM)");
                        HideGUI ();
                        OnActionGroupEditorOpened.Fire ();
                    }
                    var age = EditorActionGroups.Instance;
                    if (tank_module && !age.GetSelectedParts ().Contains (tank_module.part))
                    {
                        Debug.Log("TankWindow.CheckActionGroupEditor() hiding tank window (selected part does not contain this module)");
                        HideGUI ();
                    }
                    ActionGroupMode = true;
                }
                else
                {
                    if (ActionGroupMode)
                    {
                        Debug.Log("TankWindow.CheckActionGroupEditor() hiding tank window (editorScreen == Actions && AGM)");
                        HideGUI ();
                        OnActionGroupEditorClosed.Fire ();
                    }
                    ActionGroupMode = false;
                }
                yield return null;
            }
        }

        private void Awake()
        {
            enabled = false;
            instance = this;
            StartCoroutine (CheckActionGroupEditor ());
        }

        private void OnDestroy()
        {
            instance = null;
        }

        private Rect guiWindowRect = new Rect (0, 0, 0, 0);
        private static Vector3 mousePos = Vector3.zero;
        public void OnGUI()
        {
            EditorLogic editor = EditorLogic.fetch;
            if (!HighLogic.LoadedSceneIsEditor || !editor)
                return;

            bool cursorInGUI = false; // nicked the locking code from Ferram
            mousePos = Input.mousePosition; //Mouse location; based on Kerbal Engineer Redux code
            mousePos.y = Screen.height - mousePos.y;

            Rect tooltipRect;
            int posMult = 0;
            if (offsetGUIPos != -1) {
                posMult = offsetGUIPos;
            }
            if (ActionGroupMode) {
                if (guiWindowRect.width == 0) {
                    guiWindowRect = new Rect (430 * posMult, 365, 460, (Screen.height - 365));
                }
                tooltipRect = new Rect (guiWindowRect.xMin + 440, mousePos.y-5, 300, 20);
            } else {
                if (guiWindowRect.width == 0) {
                    guiWindowRect = new Rect (Screen.width - 8 - 430 * (posMult+1), 365, 460, (Screen.height - 365));
                }
                tooltipRect = new Rect (guiWindowRect.xMin - (230-8), mousePos.y - 5, 220, 20);
            }
            cursorInGUI = guiWindowRect.Contains (mousePos);
            if (cursorInGUI) {
                editor.Lock (false, false, false, "MFTGUILock");
                if (KSP.UI.Screens.Editor.PartListTooltipMasterController.Instance != null)
                    KSP.UI.Screens.Editor.PartListTooltipMasterController.Instance.HideTooltip ();
            } else {
                editor.Unlock ("MFTGUILock");
            }
            if (!string.IsNullOrEmpty(myToolTip))
                GUI.Label(tooltipRect, myToolTip, Styles.styleEditorTooltip);
            guiWindowRect = GUILayout.Window (GetInstanceID (), guiWindowRect, GUIWindow, "Fuel Tanks for " + tank_module.part.partInfo.title, Styles.styleEditorPanel);
        }

        void DisplayMass()
        {
            float cost = tank_module.GetModuleCost(0, ModifierStagingSituation.CURRENT);
            GUILayout.Label($"Mass: {tank_module.massDisplay}, Cost: {cost:N1}");
        }

        public void GUIWindow (int windowID)
        {
            InitializeStyles();

            GUILayout.BeginVertical();
            GUILayout.Space(20);

            if (tank_module.tankList.Count == 0)
                GUILayout.Label("This fuel tank cannot hold resources.");
            else
            {
                string sVolume = $"Volume: {tank_module.volumeDisplay}";
                if (Math.Round (tank_module.AvailableVolume, 4) < 0)
                    GUILayout.Label(sVolume, overfull);
                else
                    GUILayout.Label (sVolume);

                DisplayMass();

                scrollPos = GUILayout.BeginScrollView(scrollPos);
                GUIEngines();
                GUITanks();
                GUILayout.EndScrollView ();

                GUILayout.Label(MFSSettings.GetVersion());
            }
            GUILayout.EndVertical();

            if (!string.IsNullOrEmpty(myToolTip) && string.IsNullOrEmpty(GUI.tooltip) && counterTT < 5)
                counterTT++;   // Delay 5 frames before syncing myToolTip to GUI.tooltip, only when clearing it.
            else
            {
                myToolTip = GUI.tooltip.Trim();
                counterTT = 0;
            }
            GUI.DragWindow();
        }

        private void InitializeStyles()
        {
            if (unchanged == null) {
                if (GUI.skin == null) {
                    unchanged = new GUIStyle ();
                    changed = new GUIStyle ();
                    greyed = new GUIStyle ();
                    overfull = new GUIStyle ();
                } else {
                    unchanged = new GUIStyle (GUI.skin.textField);
                    changed = new GUIStyle (GUI.skin.textField);
                    greyed = new GUIStyle (GUI.skin.textField);
                    overfull = new GUIStyle (GUI.skin.label);
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

        private void UpdateTank (FuelTank tank)
        {
            if (GUILayout.Button("Update", GUILayout.Width (53)))
            {
                string trimmed = tank.maxAmountExpression.Trim();

                if (string.IsNullOrEmpty(trimmed))
                {
                    tank.maxAmount = 0;
                    //Debug.LogWarning ("[MFT] Removing tank as empty input " + tank.name + " amount: " + tank.maxAmountExpression ?? "null");
                } else if (double.TryParse(trimmed, out double tmp))
                {
                    tank.maxAmount = tmp;

                    if (tmp != 0)
                    {
                        tank.amount = tank.fillable ? tank.maxAmount : 0;

                        // Need to round-trip the value
                        tank.maxAmountExpression = tank.maxAmount.ToString();
                        //Debug.LogWarning ("[MFT] Updating maxAmount " + tank.name + " amount: " + tank.maxAmountExpression ?? "null");
                    }
                }
                GameEvents.onEditorShipModified.Fire (EditorLogic.fetch.ship);
                tank_module.MarkWindowDirty();
            }
        }

        private void RemoveTank(FuelTank tank)
        {
            if (GUILayout.Button("Remove", GUILayout.Width (58)))
            {
                tank.maxAmount = 0;
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                //Debug.LogWarning ("[MFT] Removing tank from button " + tank.name + " amount: " + tank.maxAmountExpression ?? "null");
            }
        }

        private void EditTank(FuelTank tank)
        {
            GUILayout.Label(" ", GUILayout.Width (5));

            GUIStyle style = unchanged;
            if (tank.maxAmountExpression == null) {
                tank.maxAmountExpression = tank.maxAmount.ToString ();
                //Debug.LogWarning ("[MFT] Adding tank from API " + tank.name + " amount: " + tank.maxAmountExpression ?? "null");
            } else if (tank.maxAmountExpression.Length > 0 && tank.maxAmountExpression != tank.maxAmount.ToString ()) {
                style = changed;
            }

            tank.maxAmountExpression = GUILayout.TextField (tank.maxAmountExpression, style, GUILayout.Width (127));

            UpdateTank(tank);
            RemoveTank(tank);
        }

        private void AddTank(FuelTank tank)
        {
            GUILayout.Label (addLabelCache[tank.name], GUILayout.Width (150));

            if (GUILayout.Button ("Add", GUILayout.Width (120))) {
                tank.maxAmount = tank_module.AvailableVolume * tank.utilization;
                tank.amount = tank.fillable ? tank.maxAmount : 0;

                tank.maxAmountExpression = tank.maxAmount.ToString ();
                GameEvents.onEditorShipModified.Fire (EditorLogic.fetch.ship);
                tank_module.MarkWindowDirty();
                //Debug.LogWarning ("[MFT] Adding tank " + tank.name + " maxAmount: " + tank.maxAmountExpression ?? "null");
            }
        }

        private void TankLine(FuelTank tank)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(" " + tank, GUILayout.Width (137));

            // So our states here are:
            //   Not being edited currently (empty):   maxAmountExpression = null, maxAmount = 0
            //   Have updated the field, no user edit: maxAmountExpression == maxAmount.ToStringExt
            //   Other non UI updated maxAmount:       maxAmountExpression = null (set), maxAmount = non-zero
            //   User has updated the field:           maxAmountExpression != null, maxAmountExpression != maxAmount.ToStringExt

            // the unmanaged resource changes make this additional check for tank.maxAmount necessary but it may be that it should replace the other PartResource check outright - needs some thought
            if (tank_module.part.Resources.Contains(tank.name) && tank_module.part.Resources[tank.name].maxAmount > 0 && tank.maxAmount > 0) {
                EditTank(tank);
            } else if (tank_module.AvailableVolume >= 0.001) {
                AddTank(tank);
            } else {
                GUILayout.Label("  No room for tank.", GUILayout.Width(150));
            }
            GUILayout.EndHorizontal();
        }

        private void GUITanks()
        {
            EnsureFreshAddLabelCache();

            // "Sort" the tanks: give priority to ones in the usedBy list.
            GUILayout.BeginVertical(Styles.styleEditorBox);
            foreach (FuelTank tank in tank_module.usedByTanks)
                TankLine(tank);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(Styles.styleEditorBox, GUILayout.ExpandHeight(true));
            foreach (FuelTank tank in tank_module.tankList.Where(x => !tank_module.usedByTanks.Contains(x)))
            {
                if (tank.canHave)
                    TankLine(tank);
                else
                    GUILayout.Label("No tech for " + tank.name);
            }

            if (GUILayout.Button("Remove All Tanks"))
                tank_module.Empty();
            GUILayout.EndVertical();
        }

        private readonly HashSet<string> displayedParts = new HashSet<string>();
        private void GUIEngines()
        {
            GUILayout.BeginVertical(Styles.styleEditorBox);
            if (tank_module.usedBy.Count > 0 && tank_module.AvailableVolume >= 0.001)
            {
                displayedParts.Clear();
                GUILayout.Label("Configure remaining volume for detected engines:");

                foreach (FuelInfo info in tank_module.usedBy.Values)
                    if (!displayedParts.Contains(info.title))
                    {
                        if (GUILayout.Button(info.title))
                            tank_module.ConfigureFor(info);
                        displayedParts.Add(info.title);
                    }
            }
            GUILayout.EndVertical();
        }
    }
}
