using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSP.Localization;
using ClickThroughFix;

namespace RealFuels.Tanks
{
    public class TankDefinitionSelectionGUI : MonoBehaviour
    {
        public ModuleFuelTanks parentModule;
        public Part part => parentModule.part;

        private Rect guiWindowRect = new Rect(300, 300, 0, 0);
        private Rect guiTooltipRect = new Rect(0, 0, 0, 0);
        private static readonly int _tooltipWindowId = "MFTTooltipID".GetHashCode();

        // Editor input lock — prevent the editor from picking/deselecting parts while
        // the mouse is over this window.  Uses the same mechanism as EngineConfigGUI.
        private bool _editorLocked = false;
        private const string LockID = "TankDefinitionSelectionGUI_Lock";
        private GUILayoutOption expandWidth, expandHeight;
        private GUIStyle windowStyle, tooltipStyle;

        private readonly List<Filter> filterList = new List<Filter>();

        private string tooltip = string.Empty;

        public void Awake()
        {
            Styles.InitStyles();
            windowStyle = new GUIStyle(Styles.styleEditorPanel);
            windowStyle.alignment = TextAnchor.UpperCenter;

            tooltipStyle = new GUIStyle(Styles.styleEditorTooltip);
            tooltipStyle.wordWrap = false;
            tooltipStyle.stretchWidth = true;
            tooltipStyle.stretchHeight = false;
            tooltipStyle.clipping = TextClipping.Overflow;

            expandWidth = GUILayout.ExpandWidth(true);
            expandHeight = GUILayout.ExpandHeight(true);

            filterList.Add(new Filter(Localizer.GetStringByTag("#RF_TankDefineSelection_HighlyPressurized"), false, (x) => x.highlyPressurized)); // "Highly Pressurized"
            filterList.Add(new Filter(Localizer.GetStringByTag("#RF_TankDefineSelection_NotHighlyPressurized"), false, (x) => !x.highlyPressurized)); // "Not Highly Pressurized"
            if (MFSSettings.previewAllLockedTypes)
                filterList.Add(new Filter(Localizer.GetStringByTag("#RF_TankDefineSelection_UnlockedTypes"), true, (x) => !string.IsNullOrEmpty(part.partInfo.TechRequired) && // "Unlocked types"
                                                                         ResearchAndDevelopment.GetTechnologyState(part.partInfo.TechRequired) == RDTech.State.Available &&
                                                                         !parentModule.lockedTypes.Contains(x)));
        }
        public void OnGUI()
        {
            // Only render in the Parts tab.  If the player switches to Action Groups
            // (or any other tab) while this window is open, close it immediately.
            if (!HighLogic.LoadedSceneIsEditor || EditorLogic.fetch == null
                || EditorLogic.fetch.editorScreen != EditorScreen.Parts)
            {
                EditorUnlock();
                Destroy(this);
                return;
            }

            if (!string.IsNullOrWhiteSpace(tooltip))
            {
                // ClickThruBlocker ensures the uGUI overlay panel is present over the tooltip
                // so that EventSystem.current.IsPointerOverGameObject() returns true, which
                // prevents EditorActionPartSelector from deselecting the part on click-through.
                guiTooltipRect = ClickThruBlocker.GUILayoutWindow(_tooltipWindowId, guiTooltipRect, GUITooltipWindow, "", tooltipStyle, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(false));
                GUI.BringWindowToFront(_tooltipWindowId);
            }
            guiWindowRect = ClickThruBlocker.GUILayoutWindow(GetInstanceID(), guiWindowRect, GUIWindow, Localizer.GetStringByTag("#RF_TankDefineSelection_TankDefinitions"), windowStyle, expandWidth, expandHeight); // "Tank Definitions"

            // Lock the editor while the mouse is over our window so part-picking and
            // part-deselection raycasts are suppressed.
            Vector3 mp = Input.mousePosition;
            mp.y = Screen.height - mp.y;
            bool over = guiWindowRect.Contains(mp) || guiTooltipRect.Contains(mp);
            if (over)
                EditorLock();
            else
                EditorUnlock();
        }

        private void OnDestroy()
        {
            EditorUnlock();
        }

        private void EditorLock()
        {
            if (!_editorLocked && HighLogic.LoadedSceneIsEditor && EditorLogic.fetch != null)
            {
                EditorLogic.fetch.Lock(false, false, false, LockID);
                _editorLocked = true;
            }
        }

        private void EditorUnlock()
        {
            if (_editorLocked && EditorLogic.fetch != null)
            {
                EditorLogic.fetch.Unlock(LockID);
                _editorLocked = false;
            }
        }

        private class Filter
        {
            public string name;
            public bool enabled;
            public Func<TankDefinition, bool> filter;

            public Filter(string name, bool enabled, Func<TankDefinition, bool> filter)
            {
                this.name = name;
                this.enabled = enabled;
                this.filter = filter;
            }
        }

        public void GUITooltipWindow(int windowID)
        {
            GUILayout.BeginVertical(expandWidth);
            GUILayout.Label(tooltip, Styles.styleEditorTooltip, expandWidth);
            GUILayout.EndVertical();
        }

        private List<TankDefinition> available = new List<TankDefinition>();
        public void GUIWindow(int windowID)
        {
            GUILayout.BeginVertical(expandWidth, expandHeight);
            GUILayout.Space(15);
            GUILayout.Label($"{part.partInfo.title}");
            GUILayout.Label($"{Localizer.GetStringByTag("#RF_TankDefineSelection_CurrentType")}: {parentModule.typeDisp}", expandWidth); // Current type

            available.Clear();
            available.AddRange(parentModule.typesAvailable);

            GUILayout.BeginVertical(Localizer.GetStringByTag("#RF_TankDefineSelection_Filters"), Styles.styleEditorBox, expandWidth); // "Filters"
            GUILayout.Space(15);
            foreach (var filter in filterList)
            {
                GUILayout.BeginHorizontal();
                filter.enabled = GUILayout.Toggle(filter.enabled, filter.name, expandWidth);
                if (filter.enabled)
                    available = available.Where(x => filter.filter(x)).ToList();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical(Styles.styleEditorBox, expandWidth, expandHeight);
            foreach (TankDefinition def in available)
            {
                // Tooltip Demonstrator.  Goal: Show dry mass of a new tank with current resources if this def was chosen
                GUIContent content = new GUIContent(def.Title, $"{def.Title}: {def.description} {Localizer.GetStringByTag("#RF_TankDefineSelection_MaxUtilization")}: {def.maxUtilization}"); // Max Utilization
                if (GUILayout.Button(content, expandWidth) && parentModule.type != def.name)
                {
                    parentModule.Fields[nameof(parentModule.type)].SetValue(def.name, parentModule);
                    MonoUtilities.RefreshPartContextWindow(parentModule.part);
                }
            }
            GUILayout.EndVertical();
            bool destroy = GUILayout.Button(Localizer.GetStringByTag("#RF_TankDefineSelection_Close")); // "Close"
            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint && !tooltip.Equals(GUI.tooltip.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                tooltip = GUI.tooltip.Trim();
                Vector3 mousePos = Input.mousePosition; //Mouse location; based on Kerbal Engineer Redux code
                mousePos.y = Screen.height - mousePos.y;
                guiTooltipRect = new Rect(guiWindowRect.xMin + 45, mousePos.y + 20, 250, 15);
            }
            GUI.DragWindow();
            if (destroy)
                Destroy(this);
        }
    }
}
