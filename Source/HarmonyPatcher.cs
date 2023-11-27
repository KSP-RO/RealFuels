using UnityEngine;

namespace RealFuels
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class HarmonyPatcher : MonoBehaviour
    {
        internal void Start()
        {
            var harmony = new HarmonyLib.Harmony("RF.HarmonyPatcher");
            harmony.PatchAll();
        }
    }
}
