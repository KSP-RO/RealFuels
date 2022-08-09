using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RealFuels
{
    public class EntryCostDatabase
    {
        #region Fields
        protected static Dictionary<string, PartEntryCostHolder> holders = null;
        protected static Dictionary<string, AvailablePart> nameToPart = null;
        protected static Dictionary<string, PartUpgradeHandler.Upgrade> nameToUpgrade = null;
        protected static HashSet<string> unlocks = null;

        protected static HashSet<string> unlockPathTracker = new HashSet<string>();

        public static Dictionary<string, AvailablePart>.ValueCollection PartsRegistered => nameToPart.Values;
        public static Dictionary<string, PartUpgradeHandler.Upgrade>.ValueCollection UpgradesRegistered => nameToUpgrade.Values;

        public delegate bool CanAffordDelegate(string techID, string ecmName, double cost);
        public delegate double GetSubsidyDelegate(string techID, string ecmName, double cost);

        /// <summary>
        /// This method should take the tech node (might be null if PurchaseConfig
        /// is called without passing a tech node), the ECM name, and the ECM cost.
        /// It returns whether the ECM can afford to be purchased.
        /// </summary>
        public static CanAffordDelegate CanAfford = null;

        /// <summary>
        /// This method should take the tech node (might be null if PurchaseConfig
        /// is called without passing a tech node), the ECM name, and the ECM cost.
        /// It returns a subsidy to apply to the ECM cost, prior to the
        /// fund transaction.
        /// </summary>
        public static GetSubsidyDelegate GetSubsidy = null;
        #endregion

        #region Setup
        public EntryCostDatabase()
        {
            Initialize();
        }
        public static void Initialize()
        {
            if (nameToPart == null)
                FillPartList();

            if (nameToUpgrade == null)
                FillUpgradeList();

            if (holders == null)
                FillHolders();

            if (unlocks == null)
                unlocks = new HashSet<string>();
        }
        protected static void FillPartList()
        {
            nameToPart = new Dictionary<string, AvailablePart>();

            // now fill our dictionary of parts
            if (PartLoader.Instance == null || PartLoader.LoadedPartsList == null)
            {
                Debug.LogError("*RP-0 EC: ERROR: Partloader instance null or list null!");
                return;
            }
            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
            {
                if (ap?.partPrefab is Part)
                    nameToPart[GetPartName(ap)] = ap;
            }
        }

        protected static void FillUpgradeList()
        {
            nameToUpgrade = new Dictionary<string, PartUpgradeHandler.Upgrade>();

            foreach (PartUpgradeHandler.Upgrade upgrade in PartUpgradeManager.Handler)
            {
                nameToUpgrade[Utilities.SanitizeName(upgrade.name)] = upgrade;
            }
        }

        protected static void FillHolders()
        {
            holders = new Dictionary<string, PartEntryCostHolder>();

            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("ENTRYCOSTMODS"))
            {
                foreach (ConfigNode.Value v in node.values)
                {
                    PartEntryCostHolder p = new PartEntryCostHolder(v.name, v.value);
                    holders[p.name] = p;
                }
            }
        }
        #endregion

        #region Helpers
        // from RF
        protected static string GetPartName(Part part)
        {
            return part.partInfo != null ? GetPartName(part.partInfo) : Utilities.SanitizeName(part.name);
        }

        protected static string GetPartName(AvailablePart ap)
        {
            return GetPartName(ap.name);
        }

        protected static string GetPartName(string partName)
        {
            return Utilities.SanitizeName(partName);
        }
        #endregion

        #region Interface
        public static bool IsUnlocked(string name)
        {
            return unlocks.Contains(Utilities.SanitizeName(name));
        }

        public static void SetUnlocked(AvailablePart ap)
        {
            SetUnlocked(GetPartName(ap));
        }

        public static void SetUnlocked(PartUpgradeHandler.Upgrade up)
        {
            SetUnlocked(Utilities.SanitizeName(up.name));
        }

        public static void SetUnlocked(string name)
        {
            name = Utilities.SanitizeName(name);
            unlocks.Add(name);

            if (GetHolder(name) is PartEntryCostHolder h)
                foreach (string s in h.children)
                    SetUnlocked(s);
        }

        public static int GetCost(string name)
        {
            TryGetCost(name, out int cost);
            return cost;
        }

        public static bool TryGetCost(string name, out int cost)
        {
            cost = 0;
            name = Utilities.SanitizeName(name);
            if (unlockPathTracker.Contains(name))
            {
                /*string msg = "[EntryCostDatabase]: Circular reference on " + name;
                foreach (string s in unlockPathTracker)
                    msg += "\n" + s;

                Debug.LogError(msg);*/
                return true;
            }

            unlockPathTracker.Add(name);

            if (GetHolder(name) is PartEntryCostHolder h)
            {
                cost = h.GetCost();
                return true;
            }

            return false;
        }

        public static PartEntryCostHolder GetHolder(string s)
        {
            holders.TryGetValue(Utilities.SanitizeName(s), out var h);
            return h;
        }

        public static void UpdateEntryCost(AvailablePart ap)
        {
            ClearTracker();
            if (GetHolder(GetPartName(ap)) is PartEntryCostHolder h)
                ap.SetEntryCost(h.GetCost());
        }

        public static void UpdateEntryCost(PartUpgradeHandler.Upgrade upgrade)
        {
            ClearTracker();
            if (GetHolder(Utilities.SanitizeName(upgrade.name)) is PartEntryCostHolder h)
                upgrade.entryCost = h.GetCost();

            // Work around a stock bug
            if (upgrade.entryCost == 0)
                upgrade.entryCost = 0.00001f;
        }

        public static void Save(ConfigNode node)
        {
            foreach (string s in unlocks)
            {
                node.AddValue(s, true);
            }
        }

        public static void Load(ConfigNode node)
        {
            unlocks.Clear();

            if (node == null)
                return;

            foreach (ConfigNode.Value v in node.values)
            {
                unlocks.Add(v.name);
            }
        }

        public static void ClearTracker()
        {
            unlockPathTracker.Clear();
        }

        public static void UpdateEntryCosts()
        {
            foreach (var ap in PartLoader.LoadedPartsList)
            {
                if (ap?.partPrefab is Part)
                    UpdateEntryCost(ap);
            }

            foreach (PartUpgradeHandler.Upgrade upgrade in PartUpgradeManager.Handler)
            {
                UpdateEntryCost(upgrade);
            }
        }
        #endregion
    }
}
