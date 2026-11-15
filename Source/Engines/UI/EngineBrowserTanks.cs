using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using RealFuels.Tanks;

namespace RealFuels
{
    internal enum TankFamily { Conventional, Isogrid, Balloon }

    /// <summary>
    /// Structural factor of an engine's propellant mix in a tank family:
    /// tank dry mass / (tank dry mass + propellant mass), per litre of tank, using the same
    /// mass model as ModuleFuelTanks (basemass per volume + per-resource tank mass).
    /// </summary>
    internal static class EngineBrowserTanks
    {
        public static readonly string[] FamilyLabels = { "Conventional", "Isogrid", "Balloon" };
        public const int FamilyCount = 3;

        private class Candidate
        {
            public TankDefinition Def;
            public string Material;      // definition name without the "-HP" suffix
            public string Tech;
            public float BasePerVolume;  // t per litre
            public float Utilization;    // usable fraction of the tank volume
        }

        /// <summary>A material within a family: its base (non-HP) definition plus any HP variant.</summary>
        public struct Material
        {
            public string Id;     // base definition name, e.g. "Tank-Iso-AlLi"
            public string Title;  // base definition title, e.g. "Al-Li Gridded Tank"
            public string Tech;
        }

        private static List<Candidate>[] _families;
        private static List<Material>[] _materials;

        /// <summary>One propellant that needs tank volume (massless and engine-internal ones are skipped).</summary>
        public struct TankProp
        {
            public string Name;
            public float Ratio;
            public float Density; // t per unit
        }

        public struct Result
        {
            public float SF;          // NaN when no compatible tank
            public float MixDensity;  // kg/L of usable volume
            public string TankTitle;
            public string Note;       // why there is no value
        }

        internal static void Invalidate()
        {
            _families = null;
            _materials = null;
        }

        /// <summary>Materials of a family, heaviest (earliest) first, as offered in the material picker.</summary>
        public static List<Material> Materials(TankFamily f)
        {
            if (_materials == null)
            {
                _materials = new List<Material>[FamilyCount];
                for (int i = 0; i < FamilyCount; i++)
                {
                    var byId = new Dictionary<string, Candidate>();
                    foreach (var c in Families[i])
                    {
                        // Prefer the non-HP definition to represent the material.
                        if (!byId.TryGetValue(c.Material, out Candidate cur) || (cur.Def.name != c.Material && c.Def.name == c.Material))
                            byId[c.Material] = c;
                    }
                    var list = new List<Candidate>(byId.Values);
                    list.Sort((a, b) => b.BasePerVolume.CompareTo(a.BasePerVolume));
                    _materials[i] = list.ConvertAll(c => new Material { Id = c.Material, Title = c.Def.Title, Tech = c.Tech });
                }
            }
            return _materials[(int)f];
        }

        private static string MaterialOf(string defName)
            => defName.EndsWith("-HP") ? defName.Substring(0, defName.Length - 3) : defName;

        public static bool HasFamily(TankFamily f) => Families[(int)f].Count > 0;

        /// <summary>The preferred family if installed, else the first one that is (e.g. no Isogrid without RP-1).</summary>
        public static TankFamily Effective(TankFamily preferred)
        {
            if (HasFamily(preferred))
                return preferred;
            for (int i = 0; i < FamilyCount; i++)
                if (HasFamily((TankFamily)i))
                    return (TankFamily)i;
            return preferred;
        }

        private static List<Candidate>[] Families
        {
            get
            {
                if (_families == null)
                    _families = Build();
                return _families;
            }
        }

        private static List<Candidate>[] Build()
        {
            var fams = new List<Candidate>[FamilyCount];
            for (int i = 0; i < FamilyCount; i++)
                fams[i] = new List<Candidate>();

            // Tank types are unlocked by the PARTUPGRADE "RFTech-<type>" (see ModuleFuelTanks.GetUpgradeForType);
            // the definition's own techRequired is the fallback when no such upgrade exists.
            var techs = new Dictionary<string, string>();
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("TANK_DEFINITION"))
            {
                string name = n.GetValue("name");
                if (name != null && !techs.ContainsKey(name))
                    techs[name] = n.GetValue("techRequired") ?? string.Empty;
            }
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("PARTUPGRADE"))
            {
                string name = n.GetValue("name");
                string tech = n.GetValue("techRequired");
                if (name != null && tech != null && name.StartsWith("RFTech-"))
                    techs[name.Substring("RFTech-".Length)] = tech;
            }

            // RP-1 tank families, grouped the same way as its tooling UI. Without them, fall back
            // to RF's legacy types so the column still means something.
            bool rp1 = false;
            foreach (string name in MFSSettings.tankDefinitions.Keys)
                if (name.StartsWith("Tank-Sep-") || name.StartsWith("Tank-Iso-") || name.StartsWith("Tank-Balloon-"))
                    rp1 = true;

            foreach (var def in MFSSettings.tankDefinitions.Values)
            {
                int fam = rp1 ? Rp1Family(def.name) : LegacyFamily(def.name);
                if (fam < 0)
                    continue;
                float maxUtil = def.maxUtilization > 0f ? def.maxUtilization : 100f;
                fams[fam].Add(new Candidate
                {
                    Def = def,
                    Material = MaterialOf(def.name),
                    Tech = techs.TryGetValue(def.name, out string t) ? t : string.Empty,
                    BasePerVolume = ParseBasePerVolume(def),
                    Utilization = Mathf.Clamp(maxUtil, Mathf.Max(def.minUtilization, 1f), 100f) / 100f
                });
            }
            return fams;
        }

        private static int Rp1Family(string name)
        {
            if (name.StartsWith("Tank-Sep-")) return (int)TankFamily.Conventional;
            if (name.StartsWith("Tank-Iso-")) return (int)TankFamily.Isogrid;
            if (name.StartsWith("Tank-Balloon-")) return (int)TankFamily.Balloon;
            return -1;
        }

        private static int LegacyFamily(string name)
        {
            switch (name)
            {
                case "Default":
                case "Cryogenic":
                    return (int)TankFamily.Conventional;
                case "Balloon":
                case "BalloonCryo":
                    return (int)TankFamily.Balloon;
                default:
                    return -1;
            }
        }

        /// <summary>
        /// Per-volume part of the definition's basemass, parsed the same way ModuleFuelTanks does.
        /// A constant basemass is per tank, so it has no per-litre share and is left out.
        /// </summary>
        private static float ParseBasePerVolume(TankDefinition def)
        {
            if (string.IsNullOrEmpty(def.basemass))
                return 0f;
            if (Tanks.ModuleFuelTanks.TryParseBaseMass(def.basemass, out float perVolume, out _))
                return perVolume;
            Debug.LogWarning($"[RFEngineBrowser] Unable to parse basemass \"{def.basemass}\" of tank type {def.name}");
            return 0f;
        }

        /// <summary>Propellants that need tank volume: drops massless (ElectricCharge) and NO_FLOW (solid grain, fission fuel).</summary>
        public static TankProp[] TankProps(ConfigNode config)
        {
            var list = new List<TankProp>();
            foreach (ConfigNode p in config.GetNodes("PROPELLANT"))
            {
                string name = p.GetValue("name");
                var res = name != null ? PartResourceLibrary.Instance.GetDefinition(name) : null;
                if (res == null || res.density <= 0f || res.resourceFlowMode == ResourceFlowMode.NO_FLOW)
                    continue;
                float ratio = 0f;
                if (!p.TryGetValue("ratio", ref ratio) || ratio <= 0f)
                    continue;
                list.Add(new TankProp { Name = name, Ratio = ratio, Density = res.density });
            }
            return list.ToArray();
        }

        /// <summary>
        /// Lowest structural factor over the family's tank types that can hold every propellant.
        /// With no material given, only researched types count ("best researched"); with one, only
        /// that material's definitions count, researched or not. Pressure-fed engines only consider
        /// highly pressurized types.
        /// </summary>
        public static Result Evaluate(TankProp[] props, bool pressureFed, TankFamily family, string material, Func<string, bool> techAvailable)
        {
            var result = new Result { SF = float.NaN };
            bool picked = !string.IsNullOrEmpty(material);
            List<Candidate> cands = picked ? Families[(int)family].FindAll(c => c.Material == material) : Families[(int)family];
            string fam = picked && cands.Count > 0 ? cands.Find(c => c.Def.name == material)?.Def.Title ?? material : FamilyLabels[(int)family] + " tank";
            if (props.Length == 0)
            {
                result.Note = "No propellants stored in tanks";
                return result;
            }
            if (cands.Count == 0)
            {
                result.Note = picked ? $"Tank type {material} not installed" : $"No {FamilyLabels[(int)family]} tank types installed";
                return result;
            }

            double massMult = Tanks.ModuleFuelTanks.MassMult;
            bool anyPressure = false, anyHolds = false, anyResearched = false;
            foreach (var c in cands)
            {
                if (pressureFed && !c.Def.highlyPressurized)
                    continue;
                anyPressure = true;

                // Volume share of each propellant: engine ratios are in units, and a tank stores
                // `utilization` units per litre of a resource.
                double wSum = 0;
                bool holds = true;
                foreach (var p in props)
                {
                    if (!c.Def.tankList.TryGetValue(p.Name, out FuelTank t) || t.utilization <= 0f)
                    {
                        holds = false;
                        break;
                    }
                    wSum += p.Ratio / t.utilization;
                }
                if (!holds || wSum <= 0)
                    continue;
                anyHolds = true;
                if (!picked)
                {
                    if (!techAvailable(c.Tech))
                        continue;
                    // Individual resources of a tank type can have their own tech gate.
                    bool slotsUnlocked = true;
                    foreach (var p in props)
                        slotsUnlocked &= c.Def.tankList[p.Name].canHave;
                    if (!slotsUnlocked)
                        continue;
                }
                anyResearched = true;

                // Per litre of total tank volume.
                double usable = c.Utilization;
                double tankMass = c.BasePerVolume * (MFSSettings.basemassUseTotalVolume ? 1.0 : usable);
                double propMass = 0;
                foreach (var p in props)
                {
                    FuelTank t = c.Def.tankList[p.Name];
                    double share = p.Ratio / t.utilization / wSum;
                    tankMass += usable * share * t.mass;
                    propMass += usable * share * t.utilization * p.Density;
                }
                tankMass *= massMult;
                if (propMass <= 0)
                    continue;

                float sf = (float)(tankMass / (tankMass + propMass));
                if (float.IsNaN(result.SF) || sf < result.SF)
                {
                    result.SF = sf;
                    result.TankTitle = c.Def.Title;
                    result.MixDensity = (float)(propMass / usable * 1000.0);
                }
            }

            if (float.IsNaN(result.SF))
            {
                if (!anyPressure) result.Note = $"No highly pressurized {fam} for a pressure-fed engine";
                else if (!anyHolds) result.Note = $"{fam} can't hold all of these propellants";
                else if (!anyResearched) result.Note = $"No compatible {fam} researched yet";
            }
            return result;
        }
    }
}
