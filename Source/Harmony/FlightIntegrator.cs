using HarmonyLib;
using RealFuels.Tanks;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace RealFuels.Harmony
{
    [HarmonyPatch(typeof(FlightIntegrator))]
    internal class PatchFlightIntegrator
    {
        private class BoiloffPartCache
        {
            public readonly List<ModuleFuelTanks> modules = new List<ModuleFuelTanks>();
            public int partCount = -1;
            public Part rootPart;
        }

        // Per vessel: tank modules that run the RF tank thermal model (SupportsBoiloff)
        private static readonly ConditionalWeakTable<Vessel, BoiloffPartCache> partCache
            = new ConditionalWeakTable<Vessel, BoiloffPartCache>();

        /// <summary>
        /// Marks the cached tank list of the vessel as stale. It gets rebuilt on the next UpdateMassStats.
        /// </summary>
        internal static void Invalidate(Vessel v)
        {
            if (v != null && partCache.TryGetValue(v, out BoiloffPartCache cache))
                cache.partCount = -1;
        }

        /// <summary>
        /// RF tracks the heat capacity of boiloff propellants per tank.
        /// Remove those resources from part.thermalMass so their heat capacity is not counted twice.
        /// Other resources in the same part (ballast, life support, etc.) stay in part.thermalMass.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("UpdateMassStats")]
        internal static void Postfix_UpdateMassStats(FlightIntegrator __instance)
        {
            Vessel v = __instance.Vessel;
            if (v == null) return;

            BoiloffPartCache cache = partCache.GetOrCreateValue(v);
            // Root part changes when an unloaded vessel gets loaded again with new Part instances
            if (cache.partCount != v.parts.Count || !ReferenceEquals(cache.rootPart, v.rootPart))
                Rebuild(v, cache);

            double sshc = PhysicsGlobals.StandardSpecificHeatCapacity;
            List<ModuleFuelTanks> modules = cache.modules;
            for (int i = modules.Count; i-- > 0;)
            {
                ModuleFuelTanks mft = modules[i];
                double boiloffThermalMass = mft.GetBoiloffResourceThermalMass();
                if (boiloffThermalMass <= 0d) continue;

                // Same formula as stock UpdateMassStats. skinThermalMass does not depend on resources.
                Part part = mft.part;
                part.resourceThermalMass = Math.Max(0d, part.resourceThermalMass - boiloffThermalMass);
                part.thermalMass = Math.Max(part.mass * sshc * part.thermalMassModifier + part.resourceThermalMass - part.skinThermalMass, 0.1);
                part.thermalMassReciprocal = 1d / part.thermalMass;
            }
        }

        private static void Rebuild(Vessel v, BoiloffPartCache cache)
        {
            cache.modules.Clear();
            List<Part> parts = v.parts;
            for (int i = parts.Count; i-- > 0;)
            {
                ModuleFuelTanks mft = parts[i].FindModuleImplementing<ModuleFuelTanks>();
                if (mft != null && mft.SupportsBoiloff)
                    cache.modules.Add(mft);
            }
            cache.partCount = parts.Count;
            cache.rootPart = v.rootPart;
        }
    }
}
