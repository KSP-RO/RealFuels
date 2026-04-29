using System;
using UnityEngine;

namespace RealFuels
{
    /// <summary>
    /// Cached GUIStyle objects to prevent allocation every frame.
    /// Styles are initialized once and reused across all GUI rendering.
    /// </summary>
    public static class EngineConfigStyles
    {
        private static bool _initialized = false;

        // Header styles
        public static GUIStyle HeaderCell { get; private set; }
        public static GUIStyle HeaderCellHover { get; private set; }

        // Row styles
        public static GUIStyle RowPrimary { get; private set; }
        public static GUIStyle RowPrimaryHover { get; private set; }
        public static GUIStyle RowPrimaryLocked { get; private set; }
        public static GUIStyle RowSecondary { get; private set; }

        // Button styles
        public static GUIStyle SmallButton { get; private set; }
        public static GUIStyle CompactButton { get; private set; }

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

        /// <summary>
        /// Initialize all cached styles. Called once on first use.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            // Header styles
            HeaderCell = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
                alignment = TextAnchor.LowerLeft,
                richText = true
            };

            HeaderCellHover = new GUIStyle(HeaderCell)
            {
                fontSize = 15
            };

            // Row styles
            RowPrimary = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) },
                alignment = TextAnchor.MiddleLeft,
                richText = true,
                padding = new RectOffset(5, 0, 0, 0)
            };

            RowPrimaryHover = new GUIStyle(RowPrimary)
            {
                fontSize = 15
            };

            RowPrimaryLocked = new GUIStyle(RowPrimary)
            {
                normal = { textColor = new Color(1f, 0.65f, 0.3f) }
            };

            RowSecondary = new GUIStyle(RowPrimary)
            {
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };

            // Button styles
            SmallButton = new GUIStyle(HighLogic.Skin.button)
            {
                fontSize = 11,
                padding = new RectOffset(2, 2, 2, 2)
            };

            CompactButton = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };

            // Label styles
            TimeLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = Color.grey },
                alignment = TextAnchor.UpperCenter
            };

            GridLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = Color.grey }
            };

            ChartTitle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter
            };

            Legend = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = Color.white },
                alignment = TextAnchor.UpperLeft
            };

            // Info panel styles
            InfoText = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                normal = { textColor = Color.white },
                wordWrap = true,
                richText = true,
                padding = new RectOffset(8, 8, 2, 2)
            };

            InfoHeader = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
                wordWrap = true,
                richText = true,
                padding = new RectOffset(8, 8, 0, 4)
            };

            InfoSection = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                wordWrap = true,
                richText = true,
                padding = new RectOffset(8, 8, 4, 2)
            };

            Bullet = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
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
                fontSize = 11,
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) },
                padding = new RectOffset(8, 8, 1, 1)
            };

            Control = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.8f, 0.8f, 0.8f) },
                padding = new RectOffset(8, 8, 2, 2)
            };

            FailureRate = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                richText = true
            };

            // Tooltip style
            ChartTooltip = new GUIStyle(GUI.skin.box)
            {
                fontSize = 15,
                normal = { textColor = Color.white },
                padding = new RectOffset(8, 8, 6, 6),
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                richText = true
            };

            // Menu styles
            MenuHeader = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            MenuLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = Color.white }
            };

            // Main window styles
            DescriptionLabel = new GUIStyle(GUI.skin.label)
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                normal = { textColor = Color.white }
            };

            CloseButton = new GUIStyle(GUI.skin.button)
            {
                normal = { textColor = new Color(1f, 0.4f, 0.4f) },
                hover = { textColor = new Color(1f, 0.2f, 0.2f) },
                fontStyle = FontStyle.Bold,
                fontSize = 14
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
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(5, 0, 0, 0)
            };

            // Column settings menu styles
            ColumnMenuHeader = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
            };

            ColumnMenuLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
            };

            NoChartLabel = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.55f, 0.55f, 0.55f) },
                wordWrap = true,
                fontSize = 13
            };

            _initialized = true;
        }

        /// <summary>
        /// Reset all styles. Call this if you need to reinitialize (e.g., after skin change).
        /// </summary>
        public static void Reset()
        {
            _initialized = false;
        }
    }
}
