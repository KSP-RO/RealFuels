using System.Collections.Generic;
using System.Collections;
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

        private static EntryCostManager _instance = null;
        public static EntryCostManager Instance { get => _instance; }

        #endregion

        #region Overrides and Monobehaviour methods

        public override void OnAwake()
        {
            if (_instance != null)
            {
                Object.Destroy(this);
                return;
            }
            _instance = this;

            if (configUpgrades == null)
                FillUpgrades();

            EntryCostDatabase.Initialize(); // should not be needed though.
        }

        protected IEnumerator UpdateEntryCosts_Coroutine()
        {
            yield return null;
            yield return null;

            EntryCostDatabase.UpdatePartEntryCosts();
            EntryCostDatabase.UpdateUpgradeEntryCosts();
        }

        public override void OnLoad(ConfigNode node)
        {

            EntryCostDatabase.Load(node.GetNode("Unlocks"));

            string tlName = string.Empty;
            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
            {
                foreach (ConfigNode n in node.GetNodes("TLUpgrade"))
                {
                    if (n.TryGetValue("name", ref tlName))
                    {
                        if (techLevelUpgrades.TryGetValue(tlName, out TLUpgrade tU))
                            tU.Load(n);
                        else
                            techLevelUpgrades[tlName] = new TLUpgrade(n);
                    }
                }
            }

            // Do this in a coroutine so we run after the PartUpgradeManager loads.
            StartCoroutine(UpdateEntryCosts_Coroutine());
        }
        public override void OnSave(ConfigNode node)
        {
            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
            {
                foreach (TLUpgrade tU in techLevelUpgrades.Values)
                {
                    tU.Save(node.AddNode("TLUpgrade"));
                }

                EntryCostDatabase.Save(node.AddNode("Unlocks"));
            }
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

            foreach (AvailablePart ap in PartLoader.LoadedPartsList.Where(x => x.partPrefab is Part p && p.Modules != null))
            {
                for (int i = ap.partPrefab.Modules.Count; i-- > 0;)
                {
                    if (ap.partPrefab.Modules[i] is ModuleEngineConfigs mec)
                    {
                        mec.CheckConfigs();
                        foreach (var cfg in mec.configs)
                        {
                            string cfgName = cfg.GetValue("name");
                            if (!string.IsNullOrEmpty(cfgName))
                            {
                                if (RFSettings.Instance.usePartNameInConfigUnlock)
                                    cfgName = Utilities.GetPartName(ap) + cfgName;

                                // config upgrades
                                if (!configUpgrades.ContainsKey(cfgName))
                                    configUpgrades[cfgName] = new EngineConfigUpgrade(cfg, cfgName);

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

        public void OnPartPurchased(AvailablePart ap)
        {
            EntryCostDatabase.SetUnlocked(ap);

            if (ap.partPrefab is Part part)
            {
                for(int i = part.Modules.Count - 1; i >= 0; --i)
                {
                    if (part.Modules[i] is ModuleEngineConfigs mec)
                    {
                        mec.CheckConfigs();
                        foreach (var cfg in mec.configs)
                        {
                            if (cfg.HasValue("name"))
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

            EntryCostDatabase.UpdatePartEntryCosts();
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

        public double ConfigEntryCost(IEnumerable<string> cfgNames)
        {
            EntryCostDatabase.ClearTracker();
            double sum = 0;
            foreach (string cfgName in cfgNames)
            {
                sum += EntryCostDatabase.GetCost(cfgName);
            }

            return sum;
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
            if (techLevelUpgrades.TryGetValue(tUName, out TLUpgrade tU))
                return tU.currentTL;
            Debug.LogError("*RFUM: ERROR: TL " + tUName + " does not exist!");
            return -1;
        }

        public void SetTLUnlocked(string tUName, int newVal)
        {
            if (techLevelUpgrades.TryGetValue(tUName, out TLUpgrade tU))
            {
                if (newVal > tU.currentTL)
                    tU.currentTL = newVal;
            }
            else
                Debug.LogError("*RFUM: ERROR: TL " + tUName + " does not exist!");
        }
        public double TLEntryCost(string tUName)
        {
            if (techLevelUpgrades.TryGetValue(tUName, out TLUpgrade tU))
                return tU.techLevelEntryCost;

            Debug.LogError("*RFUM: ERROR: TL " + tUName + " does not exist!");
            return 0d;
        }
        public double TLSciEntryCost(string tUName)
        {
            if (techLevelUpgrades.TryGetValue(tUName, out TLUpgrade tU))
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
