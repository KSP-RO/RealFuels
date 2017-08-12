using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Linq;
using UnityEngine;

namespace RealFuels
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, new GameScenes[] { GameScenes.EDITOR, GameScenes.SPACECENTER })]
    public class EntryCostManager : ScenarioModule
    {
        #region Fields

        protected static Dictionary<string, EngineConfigUpgrade> configUpgrades;
        protected static Dictionary<string, TLUpgrade> techLevelUpgrades;

        #region Instance

        private static EntryCostManager _instance = null;
        public static EntryCostManager Instance
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

            if (configUpgrades == null) // just in case
                FillUpgrades();

            EntryCostDatabase.Initialize(); // should not be needed though.

            GameEvents.OnPartPurchased.Add(new EventData<AvailablePart>.OnEvent(onPartPurchased));
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            EntryCostDatabase.Load(node.GetNode("Unlocks"));

            EntryCostDatabase.UpdatePartEntryCosts();

            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
            {
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
                foreach (TLUpgrade tU in techLevelUpgrades.Values)
                {
                    tU.Save(node.AddNode("TLUpgrade"));
                }

                EntryCostDatabase.Save(node.AddNode("Unlocks"));
            }
        }
        public void OnDestroy()
        {
            GameEvents.OnPartPurchased.Remove(new EventData<AvailablePart>.OnEvent(onPartPurchased));
        }
        #endregion

        #region Methods

        public static void FillUpgrades()
        {
            if (PartLoader.Instance == null || PartLoader.LoadedPartsList == null)
            {
                Debug.LogError("*RFUM: ERROR: Partloader instance null or list null!");
                return;
            }

            configUpgrades = new Dictionary<string, EngineConfigUpgrade>();
            techLevelUpgrades = new Dictionary<string, TLUpgrade>();

            for (int a = PartLoader.LoadedPartsList.Count; a-- > 0;)
            {
                AvailablePart ap = PartLoader.LoadedPartsList[a];

                if (ap == null || ap.partPrefab == null)
                    continue;

                Part part = ap.partPrefab;
                if (part.Modules == null)
                    continue;

                for (int i = part.Modules.Count; i-- > 0;)
                {
                    PartModule m = part.Modules[i];
                    if (m is ModuleEngineConfigs)
                    {
                        ModuleEngineConfigs mec = m as ModuleEngineConfigs;
                        mec.CheckConfigs();
                        for (int j = mec.configs.Count; j-- > 0;)
                        {
                            ConfigNode cfg = mec.configs[j];
                            string cfgName = cfg.GetValue("name");
                            if (!string.IsNullOrEmpty(cfgName))
                            {
                                if (RFSettings.Instance.usePartNameInConfigUnlock)
                                    cfgName = Utilities.GetPartName(ap) + cfgName;

                                // config upgrades
                                if (!configUpgrades.ContainsKey(cfgName))
                                {
                                    EngineConfigUpgrade eConfig = new EngineConfigUpgrade(cfg, cfgName);
                                    configUpgrades[cfgName] = eConfig;
                                }

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
        public void onPartPurchased(AvailablePart ap)
        {
            EntryCostDatabase.SetUnlocked(ap);

            EntryCostDatabase.UpdatePartEntryCosts();

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
                                
                                // TL upgrades
                                if (mec.techLevel >= 0)
                                {
                                    string tUName = Utilities.GetPartName(ap) + cfgName;
                                    SetTLUnlocked(tUName, mec.techLevel);
                                }
                            }
                        }
                    }
                }
            }
        }
        
        public bool ConfigUnlocked(string cfgName)
        {
            return EntryCostDatabase.IsUnlocked(cfgName);
        }

        public double ConfigEntryCost(string cfgName)
        {
            EntryCostDatabase.ClearTracker();
            return EntryCostDatabase.GetCost(cfgName);
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

            EntryCostDatabase.SetUnlocked(cfgName);

            EntryCostDatabase.UpdatePartEntryCosts();

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
