using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;

namespace RealFuels
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, new GameScenes[] { GameScenes.EDITOR, GameScenes.SPACECENTER })]
    public class RFUpgradeManager : ScenarioModule
    {
        #region Fields
        protected Dictionary<string, EngineConfigUpgrade> configUpgrades;
        protected Dictionary<string, TLUpgrade> techLevelUpgrades;
        #region Instance
        private static RFUpgradeManager _instance = null;
        public static RFUpgradeManager Instance
        {
            get
            {
                return _instance;
            }
        }
        #endregion
        #endregion

        #region Overrides and Monobehaviour methods
        public override void OnAwake()
        {
            base.OnAwake();

            if (_instance != null)
            {
                Object.Destroy(this);
                return;
            }
            _instance = this;

            configUpgrades = new Dictionary<string, EngineConfigUpgrade>();
            techLevelUpgrades = new Dictionary<string, TLUpgrade>();
            FillUpgrades();
            GameEvents.OnPartPurchased.Add(new EventData<AvailablePart>.OnEvent(onPartPurchased));
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
            {
                foreach (ConfigNode n in node.GetNodes("EngineConfigUpgrade"))
                {
                    EngineConfigUpgrade eCfg = null;
                    if (n.HasValue("name"))
                    {
                        string cfgName = n.GetValue("name");
                        if (configUpgrades.TryGetValue(cfgName, out eCfg))
                            eCfg.Load(n);
                        else
                        {
                            eCfg = new EngineConfigUpgrade(n);
                            configUpgrades[cfgName] = eCfg;
                        }
                    }
                }
                foreach (ConfigNode n in node.GetNodes("TLUpgrade"))
                {
                    TLUpgrade tU = null;
                    if (n.HasValue("name"))
                    {
                        string tlName = n.GetValue("name");
                        if (techLevelUpgrades.TryGetValue(tlName, out tU))
                            tU.Load(n);
                        else
                        {
                            tU = new TLUpgrade(n);
                            techLevelUpgrades[tlName] = tU;
                        }
                    }
                }
            }
        }
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
            {
                foreach (EngineConfigUpgrade eCfg in configUpgrades.Values)
                {
                    ConfigNode n = new ConfigNode("EngineConfigUpgrade");
                    eCfg.Save(n);
                    node.AddNode(n);
                }
                foreach (TLUpgrade tU in techLevelUpgrades.Values)
                {
                    ConfigNode n = new ConfigNode("TLUpgrade");
                    tU.Save(n);
                    node.AddNode(n);    
                }
            }
        }
        public void OnDestroy()
        {
            GameEvents.OnPartPurchased.Remove(new EventData<AvailablePart>.OnEvent(onPartPurchased));
        }
        #endregion

        #region Methods
        public void FillUpgrades()
        {
            if (PartLoader.Instance == null || PartLoader.LoadedPartsList == null)
            {
                Debug.LogError("*RFUM: ERROR: Partloader instance null or list null!");
                return;
            }
            for(int a = PartLoader.LoadedPartsList.Count - 1; a >= 0; --a)
            {
                AvailablePart ap = PartLoader.LoadedPartsList[a];
                if (ap == null)
                {
                    continue;
                }
                Part part = ap.partPrefab;
                if(part != null)
                {
                    if (part.Modules == null)
                        continue;

                    for(int i = part.Modules.Count - 1; i >= 0; --i)
                    {
                        PartModule m = part.Modules[i];
                        if(m is ModuleEngineConfigs)
                        {
                            ModuleEngineConfigs mec = m as ModuleEngineConfigs;
                            mec.CheckConfigs();
                            for(int j = mec.configs.Count - 1; j >= 0; --j)
                            {
                                ConfigNode cfg = mec.configs[j];
                                if(cfg.HasValue("name"))
                                {
                                    string cfgName = cfg.GetValue("name");
                                    if (RFSettings.Instance.usePartNameInConfigUnlock)
                                        cfgName = Utilities.GetPartName(ap) + cfgName;
                                    // config upgrades
                                    EngineConfigUpgrade eConfig = new EngineConfigUpgrade(cfg, cfgName);
                                    configUpgrades[cfgName] = eConfig;
                                    if (ResearchAndDevelopment.Instance != null && ap.TechRequired != null)
                                    {
                                        if(ResearchAndDevelopment.PartModelPurchased(ap))
                                        {
                                            if (cfg.HasValue("techRequired"))
                                            {
                                                string tech = cfg.GetValue("techRequired");
                                                if (tech != "" && tech != ap.TechRequired)
                                                    continue;
                                            }

                                            bool unlocked = false;
                                            if (cfg.HasValue("unlocked"))
                                                bool.TryParse(cfg.GetValue("unlocked"), out unlocked);

                                            if (mec.autoUnlock || unlocked)
                                                eConfig.unlocked = true;
                                        }
                                        else
                                            eConfig.unlocked = false;

                                        // TL upgrades
                                        if (mec.techLevel >= 0)
                                        {
                                            TLUpgrade tU = new TLUpgrade(cfg, mec);
                                            techLevelUpgrades[tU.name] = tU;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        public void onPartPurchased(AvailablePart ap)
        {
            Part part = ap.partPrefab;
            if(part != null)
            {
                for(int i = part.Modules.Count - 1; i >= 0; --i)
                {
                    PartModule m = part.Modules[i];
                    if(m is ModuleEngineConfigs)
                    {
                        ModuleEngineConfigs mec = m as ModuleEngineConfigs;
                        mec.CheckConfigs();
                        for(int j = mec.configs.Count - 1; j >= 0; --j)
                        {
                            ConfigNode cfg = mec.configs[j];
                            if(cfg.HasValue("name"))
                            {
                                string cfgName = cfg.GetValue("name");
                                if(cfg.HasValue("techRequired"))
                                {
                                    string tech = cfg.GetValue("techRequired");
                                    if(tech != "" && tech != ap.TechRequired)
                                        continue;
                                }
                                // TL upgrades
                                if (mec.techLevel >= 0)
                                {
                                    string tUName = Utilities.GetPartName(ap) + cfgName;
                                    SetTLUnlocked(tUName, mec.techLevel);
                                }
                                // unlock the config if it defaults to unlocked, or if autoUnlock is on.
                                bool unlocked = false;
                                if (cfg.HasValue("unlocked"))
                                    bool.TryParse(cfg.GetValue("unlocked"), out unlocked);
                                if (mec.autoUnlock || unlocked)
                                    SetConfigUnlock(cfgName, true);
                            }
                        }
                    }
                }
            }
        }
        public bool ConfigUnlocked(string cfgName)
        {
            EngineConfigUpgrade cfg = null;
            if(configUpgrades.TryGetValue(cfgName, out cfg))
                return cfg.unlocked;
            Debug.LogError("*RFUM: ERROR: upgrade " + cfgName + " does not exist!");
            return false;
        }

        public void SetConfigUnlock(string cfgName, bool newVal)
        {
            EngineConfigUpgrade cfg = null;
            if(configUpgrades.TryGetValue(cfgName, out cfg))
                cfg.unlocked = newVal;
            else
                Debug.LogError("*RFUM: ERROR: upgrade " + cfgName + " does not exist!");
        }
        public double ConfigEntryCost(string cfgName)
        {
            EngineConfigUpgrade cfg = null;
            if(configUpgrades.TryGetValue(cfgName, out cfg))
                return cfg.EntryCost();

            Debug.LogError("*RFUM: ERROR: upgrade " + cfgName + " does not exist!");
            return 0d;
        }
        public double ConfigSciEntryCost(string cfgName)
        {
            EngineConfigUpgrade cfg = null;
            if (configUpgrades.TryGetValue(cfgName, out cfg))
                return cfg.SciEntryCost();

            Debug.LogError("*RFUM: ERROR: upgrade " + cfgName + " does not exist!");
            return 0d;
        }
        public bool PurchaseConfig(string cfgName)
        {
            if (ConfigUnlocked(cfgName))
                return false;

            double cfgCost = ConfigEntryCost(cfgName);
            if (!HighLogic.CurrentGame.Parameters.Difficulty.BypassEntryPurchaseAfterResearch)
            {
                if (Funding.Instance.Funds < cfgCost)
                    return false;

                Funding.Instance.AddFunds(-cfgCost, TransactionReasons.RnDPartPurchase);
            }
            float sciCost = (float)ConfigSciEntryCost(cfgName);
            if (sciCost > 0f && ResearchAndDevelopment.Instance != null)
            {
                if (!ResearchAndDevelopment.CanAfford(sciCost))
                    return false;
                ResearchAndDevelopment.Instance.AddScience(-sciCost, TransactionReasons.RnDPartPurchase);
            }

            SetConfigUnlock(cfgName, true);
            return true;
        }

        public int TLUnlocked(string tUName)
        {
            TLUpgrade tU = null;
            if (techLevelUpgrades.TryGetValue(tUName, out tU))
                return tU.currentTL;
            Debug.LogError("*RFUM: ERROR: TL " + tUName + " does not exist!");
            return -1;
        }

        public void SetTLUnlocked(string tUName, int newVal)
        {
            TLUpgrade tU = null;
            if (techLevelUpgrades.TryGetValue(tUName, out tU))
            {
                if (newVal > tU.currentTL)
                    tU.currentTL = newVal;
            }
            else
                Debug.LogError("*RFUM: ERROR: TL " + tUName + " does not exist!");
        }
        public double TLEntryCost(string tUName)
        {
            TLUpgrade tU = null;
            if (techLevelUpgrades.TryGetValue(tUName, out tU))
                return tU.techLevelEntryCost;

            Debug.LogError("*RFUM: ERROR: TL " + tUName + " does not exist!");
            return 0d;
        }
        public double TLSciEntryCost(string tUName)
        {
            TLUpgrade tU = null;
            if (techLevelUpgrades.TryGetValue(tUName, out tU))
                return tU.techLevelSciEntryCost;
            Debug.LogError("*RFUM: ERROR: TL " + tUName + " does not exist!");
            return 0d;
        }
        public bool PurchaseTL(string tUName, int tl, double multiplier)
        {
            if (TLUnlocked(tUName) >= tl)
                return false;

            double tuCost = TLEntryCost(tUName) * multiplier;
            if (!HighLogic.CurrentGame.Parameters.Difficulty.BypassEntryPurchaseAfterResearch)
            {
                if (Funding.Instance.Funds < tuCost)
                    return false;

                Funding.Instance.AddFunds(-tuCost, TransactionReasons.RnDPartPurchase);
            }
            float sciCost = (float)(TLSciEntryCost(tUName) * multiplier);
            if (sciCost > 0f && ResearchAndDevelopment.Instance != null)
            {
                if (!ResearchAndDevelopment.CanAfford(sciCost))
                    return false;
                ResearchAndDevelopment.Instance.AddScience(-sciCost, TransactionReasons.RnDPartPurchase);
            }
            SetTLUnlocked(tUName, tl);
            return true;
        }

        #endregion
    }
}
