using System;
using UnityEngine;

namespace RealFuels
{
    /// <summary>
    /// Manages texture lifecycle for engine config GUI.
    /// Ensures textures are created once and properly destroyed to prevent memory leaks.
    /// </summary>
    public class EngineConfigTextures : IDisposable
    {
        private static EngineConfigTextures _instance;
        public static EngineConfigTextures Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new EngineConfigTextures();
                return _instance;
            }
        }

        // Table textures
        public Texture2D RowHover { get; private set; }
        public Texture2D RowCurrent { get; private set; }
        public Texture2D RowLocked { get; private set; }
        public Texture2D ZebraStripe { get; private set; }
        public Texture2D ColumnSeparator { get; private set; }

        // Chart textures
        public Texture2D ChartBg { get; private set; }
        public Texture2D ChartGridMajor { get; private set; }
        public Texture2D ChartGridMinor { get; private set; }
        public Texture2D ChartGreenZone { get; private set; }
        public Texture2D ChartYellowZone { get; private set; }
        public Texture2D ChartRedZone { get; private set; }
        public Texture2D ChartDarkRedZone { get; private set; }
        public Texture2D ChartStartupZone { get; private set; }
        public Texture2D ChartLine { get; private set; }
        public Texture2D ChartMarkerBlue { get; private set; }
        public Texture2D ChartMarkerGreen { get; private set; }
        public Texture2D ChartMarkerYellow { get; private set; }
        public Texture2D ChartMarkerOrange { get; private set; }
        public Texture2D ChartMarkerDarkRed { get; private set; }
        public Texture2D ChartSeparator { get; private set; }
        public Texture2D ChartHoverLine { get; private set; }
        public Texture2D ChartOrangeLine { get; private set; }
        public Texture2D ChartGreenLine { get; private set; }
        public Texture2D ChartBlueLine { get; private set; }
        public Texture2D ChartTooltipBg { get; private set; }
        public Texture2D InfoPanelBg { get; private set; }

        private bool _initialized = false;
        private bool _disposed = false;

        private EngineConfigTextures()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized) return;

            // Table textures
            RowHover = CreateColorPixel(new Color(1f, 1f, 1f, 0.05f));
            RowCurrent = CreateColorPixel(new Color(0.3f, 0.6f, 1.0f, 0.20f));
            RowLocked = CreateColorPixel(new Color(1f, 0.5f, 0.3f, 0.15f));
            ZebraStripe = CreateColorPixel(new Color(0.05f, 0.05f, 0.05f, 0.3f));
            ColumnSeparator = CreateColorPixel(new Color(0.25f, 0.25f, 0.25f, 0.9f));

            // Chart textures
            ChartBg = CreateColorPixel(new Color(0.1f, 0.1f, 0.1f, 0.8f));
            ChartGridMajor = CreateColorPixel(new Color(0.3f, 0.3f, 0.3f, 0.4f));
            ChartGridMinor = CreateColorPixel(new Color(0.25f, 0.25f, 0.25f, 0.2f));
            ChartGreenZone = CreateColorPixel(new Color(0.2f, 0.5f, 0.2f, 0.15f));
            ChartYellowZone = CreateColorPixel(new Color(0.5f, 0.5f, 0.2f, 0.15f));
            ChartRedZone = CreateColorPixel(new Color(0.5f, 0.2f, 0.2f, 0.15f));
            ChartDarkRedZone = CreateColorPixel(new Color(0.4f, 0.1f, 0.1f, 0.25f));
            ChartStartupZone = CreateColorPixel(new Color(0.15f, 0.3f, 0.5f, 0.3f));
            ChartLine = CreateColorPixel(new Color(0.8f, 0.4f, 0.4f, 1f));
            ChartMarkerBlue = CreateColorPixel(new Color(0.4f, 0.6f, 0.9f, 0.5f));
            ChartMarkerGreen = CreateColorPixel(new Color(0.3f, 0.8f, 0.3f, 0.5f));
            ChartMarkerYellow = CreateColorPixel(new Color(0.9f, 0.9f, 0.3f, 0.5f));
            ChartMarkerOrange = CreateColorPixel(new Color(1f, 0.65f, 0f, 0.5f));
            ChartMarkerDarkRed = CreateColorPixel(new Color(0.8f, 0.1f, 0.1f, 0.5f));
            ChartSeparator = CreateColorPixel(new Color(0.6f, 0.6f, 0.6f, 0.5f));
            ChartHoverLine = CreateColorPixel(new Color(1f, 1f, 1f, 0.4f));
            ChartOrangeLine = CreateColorPixel(new Color(1f, 0.5f, 0.2f, 1f));
            ChartGreenLine = CreateColorPixel(new Color(0.3f, 0.9f, 0.3f, 1f));
            ChartBlueLine = CreateColorPixel(new Color(0.5f, 0.85f, 1.0f, 1f));
            ChartTooltipBg = CreateColorPixel(new Color(0.1f, 0.1f, 0.1f, 0.95f));
            InfoPanelBg = CreateColorPixel(new Color(0.12f, 0.12f, 0.12f, 0.9f));

            _initialized = true;
        }

        /// <summary>
        /// Ensures all textures are valid. Call this before rendering.
        /// Handles Unity texture destruction on scene changes.
        /// </summary>
        public void EnsureInitialized()
        {
            // Check if any texture was destroyed (Unity does this on scene changes)
            if (!RowHover || !ChartBg)
            {
                Dispose();
                _initialized = false;
                Initialize();
            }
        }

        private static Texture2D CreateColorPixel(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        public void Dispose()
        {
            if (_disposed) return;

            // Destroy all textures
            DestroyTexture(RowHover);
            DestroyTexture(RowCurrent);
            DestroyTexture(RowLocked);
            DestroyTexture(ZebraStripe);
            DestroyTexture(ColumnSeparator);

            DestroyTexture(ChartBg);
            DestroyTexture(ChartGridMajor);
            DestroyTexture(ChartGridMinor);
            DestroyTexture(ChartGreenZone);
            DestroyTexture(ChartYellowZone);
            DestroyTexture(ChartRedZone);
            DestroyTexture(ChartDarkRedZone);
            DestroyTexture(ChartStartupZone);
            DestroyTexture(ChartLine);
            DestroyTexture(ChartMarkerBlue);
            DestroyTexture(ChartMarkerGreen);
            DestroyTexture(ChartMarkerYellow);
            DestroyTexture(ChartMarkerOrange);
            DestroyTexture(ChartMarkerDarkRed);
            DestroyTexture(ChartSeparator);
            DestroyTexture(ChartHoverLine);
            DestroyTexture(ChartOrangeLine);
            DestroyTexture(ChartGreenLine);
            DestroyTexture(ChartBlueLine);
            DestroyTexture(ChartTooltipBg);
            DestroyTexture(InfoPanelBg);

            // Null them out
            RowHover = RowCurrent = RowLocked = ZebraStripe = ColumnSeparator = null;
            ChartBg = ChartGridMajor = ChartGridMinor = ChartGreenZone = ChartYellowZone = null;
            ChartRedZone = ChartDarkRedZone = ChartStartupZone = ChartLine = null;
            ChartMarkerBlue = ChartMarkerGreen = ChartMarkerYellow = ChartMarkerOrange = ChartMarkerDarkRed = null;
            ChartSeparator = ChartHoverLine = ChartOrangeLine = ChartGreenLine = ChartBlueLine = null;
            ChartTooltipBg = InfoPanelBg = null;

            _disposed = true;
        }

        private void DestroyTexture(Texture2D texture)
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        /// <summary>
        /// Call this when the game is shutting down or when you want to force cleanup.
        /// </summary>
        public static void Cleanup()
        {
            if (_instance != null)
            {
                _instance.Dispose();
                _instance = null;
            }
        }
    }
}
