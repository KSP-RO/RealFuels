using UnityEngine;

namespace RealFuels
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    internal class Styles : MonoBehaviour
    {
        // Base styles
        public static GUIStyle styleEditorTooltip;
        public static GUIStyle styleEditorPanel;
        public static GUIStyle styleEditorBox;
        public static GUIStyle labelGreen;
        public static GUIStyle labelYellow;
        public static GUIStyle labelOrange;

        private void Start()
        {
            InitStyles();
        }

        /// <summary>
        /// This one sets up the styles we use
        /// </summary>
        internal static void InitStyles()
        {
            styleEditorTooltip = new GUIStyle();
            styleEditorTooltip.name = "Tooltip";
            styleEditorTooltip.fontSize = 12;
            styleEditorTooltip.normal.textColor = new Color32(207,207,207,255);
            styleEditorTooltip.stretchHeight = true;
            styleEditorTooltip.wordWrap = true;
            styleEditorTooltip.normal.background = CreateColorPixel(new Color32(7,54,66,200));
            styleEditorTooltip.border = new RectOffset(3, 3, 3, 3);
            styleEditorTooltip.padding = new RectOffset(4, 4, 6, 4);
            styleEditorTooltip.alignment = TextAnchor.MiddleLeft;

            styleEditorPanel = new GUIStyle();
            styleEditorPanel.normal.background = CreateColorPixel(new Color32(7,54,66,200));
            styleEditorPanel.border = new RectOffset(27, 27, 27, 27);
            styleEditorPanel.padding = new RectOffset(10, 10, 10, 10);
            styleEditorPanel.normal.textColor = new Color32(147,161,161,255);
            styleEditorPanel.fontSize = 12;

            styleEditorBox = new GUIStyle(HighLogic.Skin.box);
            styleEditorBox.normal.textColor = new Color32(147, 161, 161, 255);
            styleEditorBox.wordWrap = false;
            styleEditorBox.fontSize = 12;
            styleEditorBox.alignment = TextAnchor.UpperCenter;

            labelGreen = new GUIStyle(HighLogic.Skin.label);
            labelGreen.normal.textColor = XKCDColors.Green;
            labelGreen.stretchWidth = true;

            labelYellow = new GUIStyle(HighLogic.Skin.label);
            labelYellow.normal.textColor = XKCDColors.Yellow;
            labelYellow.stretchWidth = true;

            labelOrange = new GUIStyle(HighLogic.Skin.label);
            labelOrange.normal.textColor = XKCDColors.Orange;
            labelOrange.stretchWidth = true;
        }


        /// <summary>
        /// Creates a 1x1 texture
        /// </summary>
        /// <param name="Background">Color of the texture</param>
        /// <returns></returns>
        internal static Texture2D CreateColorPixel(Color32 Background)
        {
            Texture2D retTex = new Texture2D(1, 1);
            retTex.SetPixel(0, 0, Background);
            retTex.Apply();
            return retTex;
        }
    }
}
