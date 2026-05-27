using System;
using UnityEngine;

namespace RealFuels
{
    /// <summary>
    /// Cached GUIStyle objects to prevent allocation every frame.
    /// Styles are initialized once and reused across all GUI rendering.
    /// Set FontScale before calling Initialize() (or after calling Reset()).
    /// </summary>
    public static class EngineConfigStyles
    {
        private static bool _initialized = false;

        /// <summary>
        /// Global font scale multiplier. Change this, then call Reset() + Initialize()
        /// (or just Reset() — Initialize() is called lazily on next use).
        /// Valid range: 0.7 – 1.5. Default 1.0.
        /// </summary>
        public static float FontScale = 1.0f;

        /// <summary>Scale a base font size by FontScale, clamping to a minimum of 8.</summary>
        private static int SF(int size) => Mathf.Max(8, Mathf.RoundToInt(size * FontScale));

        // Header styles
        public static GUIStyle HeaderCell { get; private set; }
        public static GUIStyle HeaderCellHover { get; private set; }

        // Row styles
        public static GUIStyle RowPrimary { get; private set; }
        public static GUIStyle RowPrimaryHover { get; private set; }
        public static GUIStyle RowPrimaryLocked { get; private set; }
        public static GUIStyle RowSecondary { get; private set; }
        // Centered variant for boolean/symbol columns (Igns, Ullage, PFed).
        public static GUIStyle RowSecondaryCenter { get; private set; }
        // Tech column variant: smaller font so long tech names fit; always hard-clips.
        public static GUIStyle TechCell { get; private set; }

        // Button styles
        public static GUIStyle SmallButton { get; private set; }
        public static GUIStyle CompactButton { get; private set; }
        // KSP-native action-cell buttons with colour coding
        public static GUIStyle ActionButton { get; private set; }
        public static GUIStyle ActionButtonPurchase { get; private set; }
        public static GUIStyle ActionButtonOwned { get; private set; }

        // Label styles
        public static GUIStyle TimeLabel { get; private set; }
        public static GUIStyle GridLabel { get; private set; }
        public static GUIStyle ChartTitle { get; private set; }
        public static GUIStyle Legend { get; private set; }

        // Info panel styles
        public static GUIStyle InfoText { get; private set; }
        public static GUIStyle InfoHeader { get; private set; }
        public static GUIStyle InfoSection { get; private set; }
        public static GUIStyle Bullet { get; private set; }
        public static GUIStyle IndentedBullet { get; private set; }
        public static GUIStyle Footer { get; private set; }
        public static GUIStyle Control { get; private set; }
        public static GUIStyle FailureRate { get; private set; }

        // Tooltip style
        public static GUIStyle ChartTooltip { get; private set; }

        // Menu styles
        public static GUIStyle MenuHeader { get; private set; }
        public static GUIStyle MenuLabel { get; private set; }

        // Main window styles
        public static GUIStyle DescriptionLabel { get; private set; }
        public static GUIStyle CloseButton { get; private set; }

        // Table layout styles
        public static GUIStyle TableScrollView { get; private set; }
        public static GUIStyle TableRowLayout { get; private set; }
        public static GUIStyle CellMeasure { get; private set; }

        // Column settings menu styles
        public static GUIStyle ColumnMenuHeader { get; private set; }
        public static GUIStyle ColumnMenuLabel { get; private set; }

        // No-chart placeholder label (shown when bottom section is open but config has no reliability data)
        public static GUIStyle NoChartLabel { get; private set; }

        // Tech Level panel styles (expanded badge track shown in place of chart for non-reliability engines)
        public static GUIStyle TLAlertBanner { get; private set; }
        public static GUIStyle TLBadgeLabel  { get; private set; }
        public static GUIStyle TLSubLabel    { get; private set; }
        public static GUIStyle TLStatHeader  { get; private set; }
        public static GUIStyle TLStatValue   { get; private set; }

        /// <summary>
        /// Initialize all cached styles. Called once on first use (or after Reset()).
        /// FontScale must be set before this is called.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            // Header styles
            HeaderCell = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(14),
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
                alignment = TextAnchor.LowerLeft,
                richText = true
            };

            HeaderCellHover = new GUIStyle(HeaderCell)
            {
                fontSize = SF(15)
            };

            // Row styles
            RowPrimary = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(14),
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) },
                alignment = TextAnchor.MiddleLeft,
                richText = true,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(5, 0, 0, 0)
            };

            RowPrimaryHover = new GUIStyle(RowPrimary)
            {
                fontSize = SF(15)
            };

            RowPrimaryLocked = new GUIStyle(RowPrimary)
            {
                normal = { textColor = new Color(1f, 0.65f, 0.3f) }
            };

            RowSecondary = new GUIStyle(RowPrimary)
            {
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };

            RowSecondaryCenter = new GUIStyle(RowSecondary)
            {
                alignment = TextAnchor.MiddleCenter
            };

            // Tech column cell: slightly smaller text so long tech names fit within the
            // 110 px column cap without wrapping.  Always hard-clips horizontally.
            TechCell = new GUIStyle(RowSecondary)
            {
                fontSize = SF(11),
                wordWrap = false,
                clipping = TextClipping.Clip
            };

            // Button styles
            SmallButton = new GUIStyle(HighLogic.Skin.button)
            {
                fontSize = SF(11),
                padding = new RectOffset(2, 2, 2, 2)
            };

            // CompactButton uses the KSP skin so it matches the rest of the editor UI.
            CompactButton = new GUIStyle(HighLogic.Skin.button)
            {
                fontSize = SF(12),
                fontStyle = FontStyle.Bold
            };

            // Action-cell buttons — KSP-native look, colour-coded by state.
            // fontStyle is set explicitly so all three variants render identically
            // regardless of what HighLogic.Skin.button inherits.
            ActionButton = new GUIStyle(HighLogic.Skin.button)
            {
                fontSize  = SF(12),
                fontStyle = FontStyle.Normal,
                padding   = new RectOffset(3, 3, 2, 2)
            };
            // Purchase button: golden text to hint at a cost action.
            ActionButtonPurchase = new GUIStyle(ActionButton);
            ActionButtonPurchase.normal.textColor = new Color(1.00f, 0.85f, 0.30f);
            ActionButtonPurchase.hover.textColor  = new Color(1.00f, 0.95f, 0.50f);
            // Owned / free button: green text to indicate already unlocked.
            ActionButtonOwned = new GUIStyle(ActionButton);
            ActionButtonOwned.normal.textColor = new Color(0.50f, 0.85f, 0.50f);

            // Label styles
            TimeLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(13),
                normal = { textColor = Color.grey },
                alignment = TextAnchor.UpperCenter
            };

            GridLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(13),
                normal = { textColor = Color.grey }
            };

            ChartTitle = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(16),
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter
            };

            Legend = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(13),
                normal = { textColor = Color.white },
                alignment = TextAnchor.UpperLeft
            };

            // Info panel styles
            InfoText = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(15),
                normal = { textColor = Color.white },
                wordWrap = true,
                richText = true,
                padding = new RectOffset(8, 8, 2, 2)
            };

            InfoHeader = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(17),
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
                wordWrap = true,
                richText = true,
                padding = new RectOffset(8, 8, 0, 4)
            };

            InfoSection = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(16),
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                wordWrap = true,
                richText = true,
                padding = new RectOffset(8, 8, 4, 2)
            };

            Bullet = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(14),
                normal = { textColor = Color.white },
                wordWrap = false,
                richText = true,
                padding = new RectOffset(8, 8, 1, 1)
            };

            IndentedBullet = new GUIStyle(Bullet)
            {
                padding = new RectOffset(24, 8, 1, 1)
            };

            Footer = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(11),
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) },
                padding = new RectOffset(8, 8, 1, 1)
            };

            Control = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(12),
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.8f, 0.8f, 0.8f) },
                padding = new RectOffset(8, 8, 2, 2)
            };

            FailureRate = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(18),
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                richText = true
            };

            // Tooltip style
            ChartTooltip = new GUIStyle(GUI.skin.box)
            {
                fontSize = SF(15),
                normal = { textColor = Color.white },
                padding = new RectOffset(8, 8, 6, 6),
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                richText = true
            };

            // Menu styles
            MenuHeader = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(13),
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            MenuLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(12),
                normal = { textColor = Color.white }
            };

            // Main window styles — richText enabled so EngineManagerGUI can render
            // the composite "Configure [part]: [description]" label with inline markup.
            DescriptionLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(13),
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                normal = { textColor = Color.white },
                richText = true,
                wordWrap = false
            };

            CloseButton = new GUIStyle(GUI.skin.button)
            {
                normal = { textColor = new Color(1f, 0.4f, 0.4f) },
                hover = { textColor = new Color(1f, 0.2f, 0.2f) },
                fontStyle = FontStyle.Bold,
                fontSize = SF(14)
            };

            // Table layout styles
            TableScrollView = new GUIStyle(GUI.skin.scrollView)
            {
                padding = new RectOffset(0, 0, 0, 0)
            };

            TableRowLayout = new GUIStyle
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0)
            };

            CellMeasure = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(14),
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(5, 0, 0, 0)
            };

            // Column settings menu styles — sized to match the main window's secondary text.
            ColumnMenuHeader = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(13),
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
            };

            ColumnMenuLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(12),
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
            };

            NoChartLabel = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.55f, 0.55f, 0.55f) },
                wordWrap = true,
                fontSize = SF(13)
            };

            // Tech Level panel
            TLAlertBanner = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(13),
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.78f, 0.3f) },
                richText = true,
                padding = new RectOffset(10, 8, 0, 0),
                alignment = TextAnchor.MiddleLeft
            };

            TLBadgeLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(18),
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                richText = false
            };

            TLSubLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(11),
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                alignment = TextAnchor.UpperCenter,
                richText = false
            };

            TLStatHeader = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(13),
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                richText = true,
                padding = new RectOffset(4, 4, 2, 2)
            };

            TLStatValue = new GUIStyle(GUI.skin.label)
            {
                fontSize = SF(13),
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) },
                richText = true,
                padding = new RectOffset(4, 4, 1, 1)
            };

            _initialized = true;
        }

        /// <summary>
        /// Reset all styles. Set FontScale to the new value, then let Initialize() be called
        /// lazily (via EnsureTexturesAndStyles) to apply the new scale.
        /// </summary>
        public static void Reset()
        {
            _initialized = false;
        }
    }
}
