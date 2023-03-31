using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Linq;
using UnityEngine;

namespace RealFuels
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class EntryCostInitializer : MonoBehaviour
    {
        public void Start()
        {
            Object.DontDestroyOnLoad(this);

            EntryCostManager.FillUpgrades();

            EntryCostDatabase.Initialize();

            EntryCostDatabase.UpdateEntryCosts();

            // KSP's Funding binds to the same event and is used for deducting funds for entry purchases.
            // RF will need to bind to the event before KSP itself does to process ECMs right after KSP has updated the funding.
            // Note that KSP fires events in a reverse while loop.
            GameEvents.OnPartPurchased.Add(OnPartPurchased);
            GameEvents.OnPartUpgradePurchased.Add(OnPartUpgradePurchased);
        }

        private void OnPartPurchased(AvailablePart ap)
        {
            EntryCostManager.Instance?.OnPartPurchased(ap);
        }

        private void OnPartUpgradePurchased(PartUpgradeHandler.Upgrade up)
        {
            EntryCostManager.Instance?.OnPartUpgradePurchased(up);
        }
    }
}
