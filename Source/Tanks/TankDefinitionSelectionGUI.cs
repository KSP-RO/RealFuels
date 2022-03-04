using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RealFuels.Tanks
{
    public class TankDefinitionSelectionGUI : MonoBehaviour
    {
        public ModuleFuelTanks parentModule;
        public Part part => parentModule.part;

        private Rect guiWindowRect = new Rect(0, 0, 0, 0);
        private Rect guiTooltipRect = new Rect(0, 0, 0, 0);
        private static readonly int _tooltipWindowId = "MFTTooltipID".GetHashCode();
        private GUILayoutOption expandWidth, expandHeight;
        private GUIStyle windowStyle, tooltipStyle;

        private readonly List<Filter> filterList = new List<Filter>();

        private string tooltip = string.Empty;

        public void Awake()
        {
            windowStyle = new GUIStyle(Styles.styleEditorPanel);
            windowStyle.alignment = TextAnchor.UpperCenter;

            tooltipStyle = new GUIStyle(Styles.styleEditorTooltip);
            tooltipStyle.wordWrap = false;
            tooltipStyle.stretchWidth = true;
            tooltipStyle.stretchHeight = false;
            tooltipStyle.clipping = TextClipping.Overflow;

            Debug.Log($"[MFT/RF Styles] {HighLogic.Skin.window.normal.background} / {HighLogic.Skin.box.normal.background}");

            expandWidth = GUILayout.ExpandWidth(true);
            expandHeight = GUILayout.ExpandHeight(true);

            filterList.Add(new Filter("Highly Pressurized", false, (x) => x.highlyPressurized));
            filterList.Add(new Filter("Not Highly Pressurized", false, (x) => !x.highlyPressurized));
        }
        public void OnGUI()
        {
            if (!string.IsNullOrWhiteSpace(tooltip))
            {
                guiTooltipRect = GUILayout.Window(_tooltipWindowId, guiTooltipRect, GUITooltipWindow, "", tooltipStyle, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(false));
                GUI.BringWindowToFront(_tooltipWindowId);
            }
            guiWindowRect = GUILayout.Window(GetInstanceID(), guiWindowRect, GUIWindow, $"{part.partInfo.title} Tank Definition Selection", windowStyle, expandWidth, expandHeight);
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
            GUILayout.Label(tooltip, Styles.styleEditorTooltip, expandWidth);
        }

        private List<string> available = new List<string>();
        public void GUIWindow(int windowID)
        {
            GUILayout.BeginVertical(expandWidth, expandHeight);
            GUILayout.Space(15);
            GUILayout.Label($"Current type: {parentModule.type}", expandWidth);

            available.Clear();
            available.AddRange(parentModule.typesAvailable);

            GUILayout.BeginVertical("Filters", Styles.styleEditorBox, expandWidth);
            GUILayout.Space(15);
            foreach (var filter in filterList)
            {
                GUILayout.BeginHorizontal();
                filter.enabled = GUILayout.Toggle(filter.enabled, filter.name, expandWidth);
                if (filter.enabled)
                    available = available.Where(x => filter.filter(MFSSettings.tankDefinitions[x])).ToList();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical(Styles.styleEditorBox, expandWidth, expandHeight);
            foreach (string s in available)
            {
                // Tooltip Demonstrator.  Goal: Show dry mass of a new tank with current resources if this def was chosen
                var def = MFSSettings.tankDefinitions[s];
                GUIContent content = new GUIContent(s, $"Tooltip = {def.name}: {def.basemass}");
                if (GUILayout.Button(content, expandWidth) && parentModule.type != s)
                {
                    parentModule.Fields[nameof(parentModule.type)].SetValue(s, parentModule);
                    MonoUtilities.RefreshPartContextWindow(parentModule.part);
                }
            }
            GUILayout.EndVertical();
            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint && !tooltip.Equals(GUI.tooltip.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                tooltip = GUI.tooltip.Trim();
                Vector3 mousePos = Input.mousePosition; //Mouse location; based on Kerbal Engineer Redux code
                mousePos.y = Screen.height - mousePos.y;
                guiTooltipRect = new Rect(guiWindowRect.xMin + 30, mousePos.y + 10, 250, 15);
            }
            GUI.DragWindow();
        }
    }
}
