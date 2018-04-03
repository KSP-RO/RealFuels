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
            EntryCostManager.FillUpgrades();

            EntryCostDatabase.Initialize();

            EntryCostDatabase.UpdatePartEntryCosts();
        }
    }
}
