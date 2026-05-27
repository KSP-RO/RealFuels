using System;
using UnityEngine;
using RealFuels.TechLevels;
using KSP.Localization;

namespace RealFuels
{
    /// <summary>
    /// Handles tech level calculations, validation, and UI for ModuleEngineConfigs.
    /// Extracted to separate concerns and improve maintainability.
    /// </summary>
    public class EngineConfigTechLevels
    {
        private readonly ModuleEngineConfigsBase _module;

        public EngineConfigTechLevels(ModuleEngineConfigsBase module)
        {
            _module = module;
        }

        #region Tech Level Validation

        /// <summary>
        /// Checks if a configuration is unlocked (entry cost paid).
        /// </summary>
        public static bool UnlockedConfig(ConfigNode config, Part p)
        {
            if (config == null)
                return false;
            if (!config.HasValue("name"))
                return false;
            if (EntryCostManager.Instance != null && HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode != Game.Modes.SANDBOX)
                return EntryCostManager.Instance.ConfigUnlocked((RFSettings.Instance.usePartNameInConfigUnlock ? Utilities.GetPartName(p) : string.Empty) + config.GetValue("name"));
            return true;
        }

        /// <summary>
        /// Checks if a configuration can be used (tech requirement met).
        /// </summary>
        public static bool CanConfig(ConfigNode config)
        {
            if (config == null)
                return false;
            if (!config.HasValue("techRequired") || HighLogic.CurrentGame == null)
                return true;
            if (HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX || ResearchAndDevelopment.GetTechnologyState(config.GetValue("techRequired")) == RDTech.State.Available)
                return true;
            return false;
        }

        /// <summary>
        /// Checks if a tech level is unlocked (entry cost paid).
        /// </summary>
        public static bool UnlockedTL(string tlName, int newTL)
        {
            if (EntryCostManager.Instance != null && HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode != Game.Modes.SANDBOX)
                return EntryCostManager.Instance.TLUnlocked(tlName) >= newTL;
            return true;
        }

        #endregion

        #region Tech Level Calculations

        /// <summary>
        /// Calculates thrust multiplier based on tech level difference.
        /// </summary>
        public double ThrustTL(ConfigNode cfg = null)
        {
            if (_module.techLevel != -1 && !_module.engineType.Contains("S"))
            {
                TechLevel oldTL = new TechLevel(), newTL = new TechLevel();
                if (oldTL.Load(cfg ?? _module.config, _module.techNodes, _module.engineType, _module.origTechLevel) &&
                    newTL.Load(cfg ?? _module.config, _module.techNodes, _module.engineType, _module.techLevel))
                    return newTL.Thrust(oldTL);
            }
            return 1;
        }

        /// <summary>
        /// Applies tech level thrust multiplier to a float value.
        /// </summary>
        public float ThrustTL(float thrust, ConfigNode cfg = null)
        {
            return (float)Math.Round(thrust * ThrustTL(cfg), 6);
        }

        /// <summary>
        /// Applies tech level thrust multiplier to a string value.
        /// </summary>
        public float ThrustTL(string thrust, ConfigNode cfg = null)
        {
            float.TryParse(thrust, out float tmp);
            return ThrustTL(tmp, cfg);
        }

        /// <summary>
        /// Calculates mass multiplier based on tech level difference.
        /// </summary>
        public double MassTL(ConfigNode cfg = null)
        {
            if (_module.techLevel != -1)
            {
                TechLevel oldTL = new TechLevel(), newTL = new TechLevel();
                if (oldTL.Load(cfg ?? _module.config, _module.techNodes, _module.engineType, _module.origTechLevel) &&
                    newTL.Load(cfg ?? _module.config, _module.techNodes, _module.engineType, _module.techLevel))
                    return newTL.Mass(oldTL, _module.engineType.Contains("S"));
            }
            return 1;
        }

        /// <summary>
        /// Applies tech level mass multiplier to a float value.
        /// </summary>
        public float MassTL(float mass)
        {
            return (float)Math.Round(mass * MassTL(), 6);
        }

        /// <summary>
        /// Calculates cost adjusted for tech level.
        /// </summary>
        public float CostTL(float cost, ConfigNode cfg = null)
        {
            TechLevel cTL = new TechLevel();
            TechLevel oTL = new TechLevel();
            if (cTL.Load(cfg, _module.techNodes, _module.engineType, _module.techLevel) &&
                oTL.Load(cfg, _module.techNodes, _module.engineType, _module.origTechLevel) &&
                _module.part.partInfo != null)
            {
                // Bit of a dance: we have to figure out the total cost of the part, but doing so
                // also depends on us. So we zero out our contribution first
                // and then restore configCost.
                float oldCC = _module.configCost;
                _module.configCost = 0f;
                float totalCost = _module.part.partInfo.cost + _module.part.GetModuleCosts(_module.part.partInfo.cost);
                _module.configCost = oldCC;
                cost = (totalCost + cost) * (cTL.CostMult / oTL.CostMult) - totalCost;
            }

            return cost;
        }

        /// <summary>
        /// Resolves ignition count based on tech level (supports negative values like -1 for TL-based).
        /// </summary>
        public int ConfigIgnitions(int ignitions)
        {
            if (ignitions < 0)
            {
                ignitions = _module.techLevel + ignitions;
                if (ignitions < 1)
                    ignitions = 1;
            }
            else if (ignitions == 0 && !_module.literalZeroIgnitions)
                ignitions = -1;
            return ignitions;
        }

        #endregion

        #region Tech Level UI

        private enum TLBadgeState { Past, Current, FreeUpgrade, Purchasable, Locked }

        /// <summary>
        /// Compact +/− selector strip. Used when the reliability chart is also visible.
        /// </summary>
        public void DrawTechLevelSelector()
        {
            if (_module.techLevel == -1) return;
            string tlName = Utilities.GetPartName(_module.part) + _module.configuration;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{Localizer.GetStringByTag("#RF_Engine_TechLevel")}: ");
            DrawTechLevelButtons(tlName);
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Expanded badge-track panel. Replaces the chart area for engines with no
        /// reliability data (e.g. RCS). Shows the full TL range, an alert when the
        /// player is not at the best available level, a stat comparison, and the +/−
        /// navigation buttons.
        /// </summary>
        public void DrawTechLevelPanel(float panelWidth)
        {
            if (_module.techLevel == -1) return;

            int minTL = _module.minTechLevel;
            int maxTL = _module.maxTechLevel;
            int currentTL = _module.techLevel;
            int count = maxTL - minTL + 1;
            if (count <= 0) { DrawTechLevelSelector(); return; }

            string tlName = Utilities.GetPartName(_module.part) + _module.configuration;

            // ── Gather per-level state ─────────────────────────────────────────────
            var states = new TLBadgeState[count];
            var costLabels = new string[count];
            var techLabels = new string[count]; // tooltip text for locked levels
            int bestAvailTL = currentTL;

            for (int i = 0; i < count; i++)
            {
                int tl = minTL + i;
                costLabels[i] = string.Empty;
                techLabels[i] = string.Empty;

                if (tl < currentTL)
                {
                    states[i] = TLBadgeState.Past;
                }
                else if (tl == currentTL)
                {
                    states[i] = TLBadgeState.Current;
                }
                else if (!TechLevel.CanTL(_module.config, _module.techNodes, _module.engineType, tl))
                {
                    states[i] = TLBadgeState.Locked;
                    // Retrieve the tech requirement for the tooltip
                    var tlObj = new TechLevel();
                    if (tlObj.Load(_module.config, _module.techNodes, _module.engineType, tl)
                        && !string.IsNullOrEmpty(tlObj.TechRequired))
                    {
                        string techId = tlObj.TechRequired;
                        if (!ModuleEngineConfigsBase.techNameToTitle.TryGetValue(techId, out string techTitle))
                            techTitle = techId;
                        techLabels[i] = $"Requires: {techTitle}";
                    }
                }
                else
                {
                    double tlIncrMult = (double)(tl - _module.origTechLevel);
                    if (UnlockedTL(tlName, tl))
                    {
                        states[i] = TLBadgeState.FreeUpgrade;
                        bestAvailTL = tl;
                    }
                    else
                    {
                        double cost = (EntryCostManager.Instance?.TLEntryCost(tlName) ?? 0d) * tlIncrMult;
                        double sciCost = (EntryCostManager.Instance?.TLSciEntryCost(tlName) ?? 0d) * tlIncrMult;

                        if (cost <= 0d && sciCost <= 0d)
                        {
                            states[i] = TLBadgeState.FreeUpgrade;
                            bestAvailTL = tl;
                        }
                        else
                        {
                            states[i] = TLBadgeState.Purchasable;
                            string cs = string.Empty;
                            if (cost > 0d) cs += cost.ToString("N0") + "√";
                            if (sciCost > 0d) { if (cs.Length > 0) cs += "/"; cs += sciCost.ToString("N1") + "s"; }
                            costLabels[i] = cs;
                            bestAvailTL = tl;
                        }
                    }
                }
            }

            bool upgradeAvail = bestAvailTL > currentTL;

            // ── Alert / confirmation banner ────────────────────────────────────────
            GUILayout.Space(8);
            Rect alertRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.Width(panelWidth), GUILayout.Height(30f));

            if (Event.current.type == EventType.Repaint)
            {
                if (upgradeAvail)
                {
                    int bi = bestAvailTL - minTL;
                    string alertText = states[bi] == TLBadgeState.FreeUpgrade
                        ? $"<color=#FFD700>▲</color>  Tech Level {bestAvailTL} upgrade is available — <b>free!</b>"
                        : $"<color=#FFD700>▲</color>  Tech Level {bestAvailTL} upgrade is available — <b>{costLabels[bi]}</b>";
                    GUI.color = new Color(1f, 0.6f, 0.05f, 0.18f);
                    GUI.DrawTexture(alertRect, Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    GUI.Label(alertRect, alertText, EngineConfigStyles.TLAlertBanner);
                }
                else
                {
                    GUI.color = new Color(0.2f, 0.8f, 0.4f, 0.12f);
                    GUI.DrawTexture(alertRect, Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    GUI.Label(alertRect,
                        $"<color=#66FF99>✓</color>  Tech Level {currentTL} — at maximum available level",
                        EngineConfigStyles.TLAlertBanner);
                }
            }

            GUILayout.Space(10);

            // ── Badge track ────────────────────────────────────────────────────────
            const float maxBadgeW = 78f;
            const float minConnW = 10f;
            const float maxConnW = 42f;
            const float badgeH = 52f;
            const float subLabelH = 18f;

            float badgeW = Mathf.Min(maxBadgeW, (panelWidth - 32f - (count - 1) * minConnW) / count);
            float connW = count > 1
                ? Mathf.Min(maxConnW, (panelWidth - 32f - count * badgeW) / (count - 1))
                : 0f;
            float trackW = count * badgeW + (count - 1) * connW;
            float trackAreaH = badgeH + subLabelH + 4f;

            Rect trackArea = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.Width(panelWidth), GUILayout.Height(trackAreaH));

            if (Event.current.type == EventType.Repaint || Event.current.type == EventType.MouseMove
                || Event.current.type == EventType.Layout)
            {
                float startX = trackArea.x + (trackArea.width - trackW) * 0.5f;
                float badgeY = trackArea.y;

                for (int i = 0; i < count; i++)
                {
                    float bx = startX + i * (badgeW + connW);
                    Rect br = new Rect(bx, badgeY, badgeW, badgeH);

                    // Connector to next badge
                    if (i < count - 1 && Event.current.type == EventType.Repaint)
                    {
                        GUI.color = ConnectorColor(states[i], states[i + 1]);
                        GUI.DrawTexture(
                            new Rect(bx + badgeW, badgeY + badgeH * 0.5f - 1f, connW, 2f),
                            Texture2D.whiteTexture);
                        GUI.color = Color.white;
                    }

                    // Badge + tooltip for locked levels
                    string badgeTooltip = states[i] == TLBadgeState.Locked ? techLabels[i] : string.Empty;
                    DrawBadge(br, minTL + i, states[i], badgeTooltip);

                    // Sub-label (Active / Free / cost / Locked)
                    if (Event.current.type == EventType.Repaint)
                    {
                        string subText; Color subColor;
                        switch (states[i])
                        {
                            case TLBadgeState.Current:
                                subText = "Active"; subColor = new Color(1f, 0.84f, 0f); break;
                            case TLBadgeState.FreeUpgrade:
                                subText = "Free"; subColor = new Color(0.4f, 1f, 0.78f); break;
                            case TLBadgeState.Purchasable:
                                subText = costLabels[i]; subColor = new Color(1f, 0.72f, 0.25f); break;
                            case TLBadgeState.Locked:
                                subText = "Locked"; subColor = new Color(0.4f, 0.4f, 0.45f); break;
                            default:
                                subText = string.Empty; subColor = Color.clear; break;
                        }
                        if (!string.IsNullOrEmpty(subText))
                        {
                            GUI.contentColor = subColor;
                            GUI.Label(new Rect(bx, badgeY + badgeH + 2f, badgeW, subLabelH),
                                subText, EngineConfigStyles.TLSubLabel);
                            GUI.contentColor = Color.white;
                        }
                    }
                }
                GUI.color = Color.white;
            }

            GUILayout.Space(8);

            // ── Stat comparison ────────────────────────────────────────────────────
            DrawTLStatComparison(currentTL, bestAvailTL, panelWidth);

            GUILayout.Space(8);

            // ── +/− navigation buttons (styled for expanded panel) ─────────────────
            DrawStyledTechLevelButtons(tlName);

            GUILayout.Space(6);
        }

        // ── Private helpers ────────────────────────────────────────────────────────

        private void DrawBadge(Rect rect, int level, TLBadgeState state, string tooltip)
        {
            const float borderW = 2.5f;
            Color borderColor, fillColor, textColor;

            switch (state)
            {
                case TLBadgeState.Current:
                    borderColor = new Color(1f, 0.84f, 0f);
                    fillColor = new Color(0.08f, 0.08f, 0.12f);
                    textColor = Color.white;
                    break;
                case TLBadgeState.Past:
                    borderColor = new Color(0.28f, 0.32f, 0.42f);
                    fillColor = new Color(0.12f, 0.14f, 0.18f);
                    textColor = new Color(0.45f, 0.50f, 0.60f);
                    break;
                case TLBadgeState.FreeUpgrade:
                    borderColor = new Color(0.25f, 0.85f, 0.62f);
                    fillColor = new Color(0.04f, 0.16f, 0.12f);
                    textColor = new Color(0.4f, 1f, 0.78f);
                    break;
                case TLBadgeState.Purchasable:
                    borderColor = new Color(1f, 0.62f, 0.08f);
                    fillColor = new Color(0.18f, 0.10f, 0.02f);
                    textColor = new Color(1f, 0.75f, 0.28f);
                    break;
                case TLBadgeState.Locked:
                default:
                    borderColor = new Color(0.22f, 0.22f, 0.26f);
                    fillColor = new Color(0.09f, 0.09f, 0.11f);
                    textColor = new Color(0.32f, 0.32f, 0.36f);
                    break;
            }

            if (Event.current.type == EventType.Repaint)
            {
                GUI.color = borderColor;
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = fillColor;
                GUI.DrawTexture(new Rect(rect.x + borderW, rect.y + borderW,
                                         rect.width - 2f * borderW,
                                         rect.height - 2f * borderW), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.contentColor = textColor;
                GUI.Label(rect, new GUIContent(level.ToString(), tooltip), EngineConfigStyles.TLBadgeLabel);
                GUI.contentColor = Color.white;
            }
            else
            {
                // Non-repaint passes: still register the label so GUI.tooltip is set on hover
                GUI.Label(rect, new GUIContent(level.ToString(), tooltip), EngineConfigStyles.TLBadgeLabel);
            }
        }

        private static Color ConnectorColor(TLBadgeState left, TLBadgeState right)
        {
            // Gold tint on the approach to the current badge
            if (left == TLBadgeState.Past && right == TLBadgeState.Current)
                return new Color(1f, 0.84f, 0f, 0.45f);
            // Dim for past–past or anything leading into locked
            if (left == TLBadgeState.Past || right == TLBadgeState.Locked)
                return new Color(0.26f, 0.30f, 0.38f);
            // Brighter for the segment leaving the current badge
            return new Color(0.42f, 0.44f, 0.50f);
        }

        /// <summary>
        /// Computes the actual displayed Vac and SL ISP for <paramref name="config"/> at
        /// <paramref name="tl"/>, mirroring the logic in <c>EngineConfigGUI.GetIspString</c>
        /// so that the table column and the TL stat panel always agree.
        ///
        /// <para>Two config layouts are supported:</para>
        /// <list type="bullet">
        ///   <item><c>atmosphereCurve</c> node — ISP is stored as absolute values directly
        ///     in the config (e.g. HTP at 137 s vacuum).  A TechLevel multiplier is applied
        ///     on top if one exists for this engine type / TL combination.</item>
        ///   <item><c>IspSL</c> + <c>IspV</c> values — ISP is expressed as a fraction of the
        ///     TechLevel's base ISP curve, so the TL multiplier is mandatory.</item>
        /// </list>
        /// </summary>
        public bool TryGetIspAtTL(ConfigNode config, int tl, out float vacIsp, out float slIsp)
        {
            vacIsp = 0f;
            slIsp  = 0f;
            if (config == null) return false;

            // ── Branch A: direct atmosphereCurve (e.g. RCS propellants like HTP) ───
            if (config.HasNode("atmosphereCurve"))
            {
                var curve = new FloatCurve();
                curve.Load(config.GetNode("atmosphereCurve"));
                // Use explicit pressures: 0 = vacuum, 1 = 1 atm
                // (avoids isp.maxTime trap when a third high-pressure key is present)
                vacIsp = curve.Evaluate(0f);
                slIsp  = curve.Evaluate(1f);

                // No TL ISP scaling here.  When a config stores ISP via atmosphereCurve the
                // values are absolute (propellant-defined), not fractions of a TL base.
                // TechLevel.AtmosphereCurve also contains absolute ISP (used for thrust/mass
                // ratio calculations between TLs), NOT a ratio multiplier — multiplying the two
                // would give nonsense values like 46 000 s.  TL scaling for these engines
                // affects thrust and mass only, not ISP.
                return vacIsp > 0f;
            }

            // ── Branch B: IspV / IspSL fractions scaled by TL atmosphere curve ─────
            if (config.HasValue("IspSL") && config.HasValue("IspV"))
            {
                float.TryParse(config.GetValue("IspSL"), out slIsp);
                float.TryParse(config.GetValue("IspV"),  out vacIsp);

                if (_module.techLevel != -1)
                {
                    var tlObj = new TechLevel();
                    if (tlObj.Load(config, _module.techNodes, _module.engineType, tl) && tlObj.AtmosphereCurve != null)
                    {
                        vacIsp *= ModuleEngineConfigsBase.ispVMult  * tlObj.AtmosphereCurve.Evaluate(0);
                        slIsp  *= ModuleEngineConfigsBase.ispSLMult * tlObj.AtmosphereCurve.Evaluate(1);
                    }
                }
                return vacIsp > 0f;
            }

            return false;
        }

        private void DrawTLStatComparison(int currentTL, int bestAvailTL, float panelWidth)
        {
            // Use TryGetIspAtTL so we get the actual config ISP (not the raw TL curve),
            // matching what GetIspString shows in the table column.
            if (!TryGetIspAtTL(_module.config, currentTL, out float cVac, out float cAsl))
                return;

            if (bestAvailTL == currentTL)
            {
                // Already at best — show current stats on one line.
                string line = string.Empty;
                if (cVac > 0f) line += $"<color=#AAAAAA>Vac Isp:</color>  {cVac:F0} s";
                if (cAsl > 0f) line += $"   <color=#AAAAAA>ASL Isp:</color>  {cAsl:F0} s";
                if (line.Length > 0)
                    GUILayout.Label(line, EngineConfigStyles.TLStatValue);
                return;
            }

            if (!TryGetIspAtTL(_module.config, bestAvailTL, out float bVac, out float bAsl))
            {
                // Best TL data unavailable — fall back to showing current stats only.
                string line = string.Empty;
                if (cVac > 0f) line += $"<color=#AAAAAA>Vac Isp:</color>  {cVac:F0} s";
                if (cAsl > 0f) line += $"   <color=#AAAAAA>ASL Isp:</color>  {cAsl:F0} s";
                if (line.Length > 0)
                    GUILayout.Label(line, EngineConfigStyles.TLStatValue);
                return;
            }

            GUILayout.BeginHorizontal();

            // Current TL column
            GUILayout.BeginVertical(GUILayout.Width(panelWidth * 0.42f));
            GUILayout.Label($"<b>TL {currentTL}</b>  —  Active", EngineConfigStyles.TLStatHeader);
            if (cVac > 0f) GUILayout.Label($"<color=#AAAAAA>Vac Isp:</color>  {cVac:F0} s", EngineConfigStyles.TLStatValue);
            if (cAsl > 0f) GUILayout.Label($"<color=#AAAAAA>ASL Isp:</color>  {cAsl:F0} s", EngineConfigStyles.TLStatValue);
            GUILayout.EndVertical();

            GUILayout.Label("→", EngineConfigStyles.TLStatHeader, GUILayout.Width(22f));

            // Best available TL column
            GUILayout.BeginVertical();
            GUILayout.Label($"<b>TL {bestAvailTL}</b>  —  Best Available", EngineConfigStyles.TLStatHeader);
            if (bVac > 0f)
            {
                string d = cVac > 0f ? $"<color=#66FF99> (+{(bVac / cVac - 1f) * 100f:F1}%)</color>" : string.Empty;
                GUILayout.Label($"<color=#AAAAAA>Vac Isp:</color>  {bVac:F0} s{d}", EngineConfigStyles.TLStatValue);
            }
            if (bAsl > 0f)
            {
                string d = cAsl > 0f ? $"<color=#66FF99> (+{(bAsl / cAsl - 1f) * 100f:F1}%)</color>" : string.Empty;
                GUILayout.Label($"<color=#AAAAAA>ASL Isp:</color>  {bAsl:F0} s{d}", EngineConfigStyles.TLStatValue);
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Styled +/− navigation row for the expanded Tech Level panel.
        /// Draws a thin separator, then a centred row with coloured − / TL label / + controls
        /// that match the badge-track visual language.
        /// </summary>
        private void DrawStyledTechLevelButtons(string tlName)
        {
            // ── Thin separator ────────────────────────────────────────────────────
            Rect sepRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.Height(1f), GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                GUI.color = new Color(0.35f, 0.38f, 0.48f, 0.8f);
                GUI.DrawTexture(sepRect, Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            GUILayout.Space(6f);

            // ── Determine button states ───────────────────────────────────────────
            bool canMinus = TechLevel.CanTL(_module.config, _module.techNodes, _module.engineType, _module.techLevel - 1)
                         && _module.techLevel > _module.minTechLevel;

            bool canPlus = false, canBuy = false;
            string plusLabel = "✕";
            Color plusBg = new Color(0.12f, 0.12f, 0.15f); // disabled
            double tlIncrMult = (double)(_module.techLevel + 1 - _module.origTechLevel);

            if (TechLevel.CanTL(_module.config, _module.techNodes, _module.engineType, _module.techLevel + 1)
                && _module.techLevel < _module.maxTechLevel)
            {
                if (UnlockedTL(tlName, _module.techLevel + 1))
                {
                    plusLabel = "+";
                    canPlus = true;
                    plusBg = new Color(0.04f, 0.20f, 0.15f); // teal – free upgrade
                }
                else
                {
                    double cost = EntryCostManager.Instance.TLEntryCost(tlName) * tlIncrMult;
                    double sciCost = EntryCostManager.Instance.TLSciEntryCost(tlName) * tlIncrMult;
                    bool autobuy = true;
                    plusLabel = string.Empty;
                    if (cost > 0d) { plusLabel += cost.ToString("N0") + "√"; autobuy = false; canBuy = true; }
                    if (sciCost > 0d) { if (cost > 0d) plusLabel += "/"; plusLabel += sciCost.ToString("N1") + "s"; autobuy = false; canBuy = true; }
                    if (autobuy)
                    {
                        EntryCostManager.Instance.SetTLUnlocked(tlName, _module.techLevel + 1);
                        plusLabel = "+"; canPlus = true; canBuy = false;
                        plusBg = new Color(0.04f, 0.20f, 0.15f); // teal
                    }
                    else
                    {
                        plusBg = new Color(0.22f, 0.10f, 0.02f); // amber – purchasable
                    }
                }
            }

            // ── Centred button row ────────────────────────────────────────────────
            Color origBg = GUI.backgroundColor;

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            // "−" button
            GUI.backgroundColor = canMinus
                ? new Color(0.18f, 0.22f, 0.35f)   // dim navy – available
                : new Color(0.12f, 0.12f, 0.15f);   // near-black – disabled
            if (GUILayout.Button("−", EngineConfigStyles.CompactButton,
                    GUILayout.Width(30f), GUILayout.Height(28f)) && canMinus)
            {
                _module.techLevel--;
                _module.SetConfiguration();
                _module.UpdateSymmetryCounterparts();
                _module.MarkWindowDirty();
            }
            GUI.backgroundColor = origBg;

            // Gold "Tech Level N" label
            GUILayout.Space(8f);
            GUILayout.Label(
                $"<color=#FFD700><b>Tech Level  {_module.techLevel}</b></color>",
                EngineConfigStyles.TLStatHeader,
                GUILayout.MinWidth(110f));
            GUILayout.Space(8f);

            // "+" / cost button
            GUI.backgroundColor = plusBg;
            if (GUILayout.Button(plusLabel, EngineConfigStyles.CompactButton,
                    GUILayout.MinWidth(30f), GUILayout.Height(28f)) && (canPlus || canBuy))
            {
                if (!canBuy || EntryCostManager.Instance.PurchaseTL(tlName, _module.techLevel + 1, tlIncrMult))
                {
                    _module.techLevel++;
                    _module.SetConfiguration();
                    _module.UpdateSymmetryCounterparts();
                    _module.MarkWindowDirty();
                }
            }
            GUI.backgroundColor = origBg;

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Shared +/− button row used by both the compact selector and the expanded panel.
        /// Caller is responsible for wrapping in a BeginHorizontal/EndHorizontal if needed.
        /// </summary>
        private void DrawTechLevelButtons(string tlName)
        {
            string minusStr = "X";
            bool canMinus = false;
            if (TechLevel.CanTL(_module.config, _module.techNodes, _module.engineType, _module.techLevel - 1)
                && _module.techLevel > _module.minTechLevel)
            {
                minusStr = "-";
                canMinus = true;
            }
            if (GUILayout.Button(minusStr) && canMinus)
            {
                _module.techLevel--;
                _module.SetConfiguration();
                _module.UpdateSymmetryCounterparts();
                _module.MarkWindowDirty();
            }

            GUILayout.Label(_module.techLevel.ToString());

            string plusStr = "X";
            bool canPlus = false, canBuy = false;
            double tlIncrMult = (double)(_module.techLevel + 1 - _module.origTechLevel);
            if (TechLevel.CanTL(_module.config, _module.techNodes, _module.engineType, _module.techLevel + 1)
                && _module.techLevel < _module.maxTechLevel)
            {
                if (UnlockedTL(tlName, _module.techLevel + 1))
                {
                    plusStr = "+";
                    canPlus = true;
                }
                else
                {
                    double cost = EntryCostManager.Instance.TLEntryCost(tlName) * tlIncrMult;
                    double sciCost = EntryCostManager.Instance.TLSciEntryCost(tlName) * tlIncrMult;
                    bool autobuy = true;
                    plusStr = string.Empty;
                    if (cost > 0d) { plusStr += cost.ToString("N0") + "√"; autobuy = false; canBuy = true; }
                    if (sciCost > 0d) { if (cost > 0d) plusStr += "/"; plusStr += sciCost.ToString("N1") + "s"; autobuy = false; canBuy = true; }
                    if (autobuy)
                    {
                        EntryCostManager.Instance.SetTLUnlocked(tlName, _module.techLevel + 1);
                        plusStr = "+"; canPlus = true; canBuy = false;
                    }
                }
            }
            if (GUILayout.Button(plusStr) && (canPlus || canBuy))
            {
                if (!canBuy || EntryCostManager.Instance.PurchaseTL(tlName, _module.techLevel + 1, tlIncrMult))
                {
                    _module.techLevel++;
                    _module.SetConfiguration();
                    _module.UpdateSymmetryCounterparts();
                    _module.MarkWindowDirty();
                }
            }
        }

        #endregion
    }
}
