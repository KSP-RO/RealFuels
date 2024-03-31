using UnityEngine;

namespace RealFuels.B9PSPatch
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class HarmonyPatcher : MonoBehaviour
    {
        internal void Start()
        {
            var harmony = new HarmonyLib.Harmony("RealFuels.Harmony.HarmonyPatcher");
            harmony.PatchAll();
        }
    }
}