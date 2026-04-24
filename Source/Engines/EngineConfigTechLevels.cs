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

        /// <summary>
        /// Draws the tech level selector UI with +/- buttons.
        /// </summary>
        public void DrawTechLevelSelector()
        {
            // NK Tech Level
            if (_module.techLevel != -1)
            {
                GUILayout.BeginHorizontal();

                GUILayout.Label($"{Localizer.GetStringByTag("#RF_Engine_TechLevel")}: "); // Tech Level
                string minusStr = "X";
                bool canMinus = false;
                if (TechLevel.CanTL(_module.config, _module.techNodes, _module.engineType, _module.techLevel - 1) && _module.techLevel > _module.minTechLevel)
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
                bool canPlus = false;
                bool canBuy = false;
                string tlName = Utilities.GetPartName(_module.part) + _module.configuration;
                double tlIncrMult = (double)(_module.techLevel + 1 - _module.origTechLevel);
                if (TechLevel.CanTL(_module.config, _module.techNodes, _module.engineType, _module.techLevel + 1) && _module.techLevel < _module.maxTechLevel)
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
                        if (cost > 0d)
                        {
                            plusStr += cost.ToString("N0") + "√";
                            autobuy = false;
                            canBuy = true;
                        }
                        if (sciCost > 0d)
                        {
                            if (cost > 0d)
                                plusStr += "/";
                            autobuy = false;
                            canBuy = true;
                            plusStr += sciCost.ToString("N1") + "s";
                        }
                        if (autobuy)
                        {
                            // auto-upgrade
                            EntryCostManager.Instance.SetTLUnlocked(tlName, _module.techLevel + 1);
                            plusStr = "+";
                            canPlus = true;
                            canBuy = false;
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
                GUILayout.EndHorizontal();
            }
        }

        #endregion
    }
}
