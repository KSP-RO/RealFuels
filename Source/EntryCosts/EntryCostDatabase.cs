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
                nameToUpgrade[GetPartName(upgrade.name)] = upgrade;
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
            return part.partInfo != null ? GetPartName(part.partInfo) : GetPartName(part.name);
        }

        protected static string GetPartName(AvailablePart ap)
        {
            return GetPartName(ap.name);
        }

        protected static string GetPartName(string partName)
        {
            partName = partName.Replace(".", "-");
            return partName.Replace("_", "-");
        }
        #endregion

        #region Interface
        public static bool IsUnlocked(string name)
        {
            return unlocks.Contains(name);
        }

        public static void SetUnlocked(AvailablePart ap)
        {
            SetUnlocked(GetPartName(ap));
        }

        public static void SetUnlocked(string name)
        {
            unlocks.Add(name);

            if (holders.TryGetValue(name, out PartEntryCostHolder h))
                foreach (string s in h.children)
                    SetUnlocked(s);
        }

        public static int GetCost(string name)
        {
            if (unlockPathTracker.Contains(name))
            {
                /*string msg = "[EntryCostDatabase]: Circular reference on " + name;
                foreach (string s in unlockPathTracker)
                    msg += "\n" + s;

                Debug.LogError(msg);*/
                return 0;
            }

            unlockPathTracker.Add(name);

            if (holders.TryGetValue(name, out PartEntryCostHolder h))
                return h.GetCost();

            return 0;
        }

        public static void UpdateEntryCost(AvailablePart ap)
        {
            ClearTracker();
            if (holders.TryGetValue(GetPartName(ap), out PartEntryCostHolder h))
                ap.SetEntryCost(h.GetCost());
        }

        public static void UpdateEntryCost(PartUpgradeHandler.Upgrade upgrade)
        {
            ClearTracker();
            if (holders.TryGetValue(GetPartName(upgrade.name), out PartEntryCostHolder h))
                upgrade.entryCost = h.GetCost();
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

        public static void UpdatePartEntryCosts()
        {
            foreach (var ap in PartLoader.LoadedPartsList)
            {
                if (ap?.partPrefab is Part)
                    UpdateEntryCost(ap);
            }
        }

        public static void UpdateUpgradeEntryCosts()
        {
            foreach (PartUpgradeHandler.Upgrade upgrade in PartUpgradeManager.Handler)
            {
                UpdateEntryCost(upgrade);
            }
        }
        #endregion
    }
}
