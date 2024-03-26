using HarmonyLib;
using System.Linq;
using System.Collections.Generic;
using B9PartSwitch.PartSwitch.PartModifiers;
using B9PartSwitch;

namespace RealFuels.Harmony
{
    [HarmonyPatch(typeof(ModuleModifierInfo))]
    internal class PatchModuleModifierInfo
    {
        internal static bool Prepare()
        {
            bool foundB9PS = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.name.Equals("B9PartSwitch", System.StringComparison.OrdinalIgnoreCase)) != null;
            return foundB9PS;
        }


        [HarmonyPostfix]
        [HarmonyPatch("CreatePartModifiers")]
        internal static IEnumerable<IPartModifier> Postfix_CreatePartModifiers(IEnumerable<IPartModifier> result, Part part, ModuleModifierInfo __instance, BaseEventDetails moduleDataChangedEventDetails)
        {
            foreach (var partModifier in result)
            {
                if (partModifier is ModuleDataHandlerBasic)
                {
                    ModuleMatcher moduleMatcher = new ModuleMatcher(__instance.identifierNode);
                    PartModule module = moduleMatcher.FindModule(part);
                    ConfigNode originalNode = moduleMatcher.FindPrefabNode(module);
                    if (module.moduleName == "ModuleFuelTanks")
                    {
                        yield return new ModuleFuelTanksHandler(module, originalNode, __instance.dataNode, moduleDataChangedEventDetails);
                        continue;
                    }
                }
                yield return partModifier;
            }

        }
    }
}
