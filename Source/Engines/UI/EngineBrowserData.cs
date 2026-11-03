using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace RealFuels
{
    internal enum EngineKind { Liquid, Solid, Generic, Nuclear, Electric, Plane, RCS }

    /// <summary>
    /// One row of the engine browser: a single config (or SUBCONFIG variant) of one part.
    /// Static stats are resolved once from the part prefab; career state (researched,
    /// unlocked, entry cost) is refreshed periodically by the browser.
    /// </summary>
    internal class EngineBrowserEntry
    {
        public const int IgnNone = -2;
        public const int IgnUnlimited = -1;
        public const int IgnGround = 0;

        public AvailablePart Part;
        public ModuleEngineConfigsBase Module;
        public int ModuleIndex;
        public ConfigNode Node;
        public string ConfigName;
        public string PatchName;

        public string Family;
        public string Config;
        public EngineKind Kind;
        public int EngineCount = 1;      // engineTypeMult: chambers/engines in one part
        public float Thrust = -1f;       // kN, vacuum
        public float ThrustSL = -1f;     // kN, sea level
        public float TwrVac = -1f;
        public float TwrSL = -1f;
        public float MinThrottle = -1f;  // fraction of max
        public float IspVac = -1f;
        public float IspSL = -1f;
        public float Mass = -1f;         // t
        public float Gimbal = -1f;       // deg, -1 = no gimbal
        public string GimbalText;
        public int Ignitions = IgnNone;
        public bool Ullage;
        public bool PressureFed;
        public bool GroundLit;
        public bool Storable;
        public string[] Propellants;
        public string PropellantText;
        public float Rated = -1f;
        public float RatedContinuous = -1f;
        public float Tested = -1f;
        public float IgnStart = -1f, IgnEnd = -1f;
        public float CycleStart = -1f, CycleEnd = -1f;
        public string Tech;
        public string TechTitle;
        public string Spec;
        public string Description;
        public float Cost;
        public string SearchText;

        // Career state, refreshed by EngineBrowser.RefreshState
        public bool Researched;
        public bool Unlocked;
        public bool PartAvailable;
        public double EntryCost;
        public float StructFactor = float.NaN;   // for the selected tank family
        public string StructNote;               // tank used, or why there is no value

        // Propellants that need tank volume, and a key for caching structural factors
        public EngineBrowserTanks.TankProp[] TankProps;
        public string TankKey;

        // Display text per column, see EngineBrowser.Col
        public string[] Cells;
        public string Tooltip;
    }

    /// <summary>
    /// Invalidates the browser caches on GameDatabase reloads, which usually happen outside the
    /// editor. Lives for the whole session; KSP's EventVoid can't take a static method.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    internal class EngineBrowserReloadHook : MonoBehaviour
    {
        private void Awake()
        {
            DontDestroyOnLoad(this);
            GameEvents.OnGameDatabaseLoaded.Add(OnDatabaseLoaded);
        }

        private void OnDestroy() => GameEvents.OnGameDatabaseLoaded.Remove(OnDatabaseLoaded);

        private void OnDatabaseLoaded() => EngineBrowserDatabase.Invalidate();
    }

    internal static class EngineBrowserDatabase
    {
        private static List<EngineBrowserEntry> _entries;
        private static HashSet<string> _nonStorable;
        private static string _builtForSave;

        /// <summary>Bumped on every rebuild so the browser knows to redo cells and widths.</summary>
        public static int Version { get; private set; }

        /// <summary>
        /// All entries. Rebuilt when a different save is loaded (config display filters and the
        /// tech tree can differ per save) or after a GameDatabase reload.
        /// </summary>
        public static List<EngineBrowserEntry> Entries
        {
            get
            {
                string save = HighLogic.SaveFolder ?? string.Empty;
                // Destroyed prefabs mean the parts were reloaded without the reload event reaching us.
                bool stale = _entries != null && _entries.Count > 0 && _entries[0].Part.partPrefab == null;
                if (_entries == null || stale || _builtForSave != save)
                {
                    // Whatever triggered the rebuild, derived caches may be from the old database too.
                    _nonStorable = null;
                    EngineBrowserTanks.Invalidate();
                    _builtForSave = save;
                    _entries = Build();
                    Version++;
                }
                return _entries;
            }
        }

        /// <summary>Drops the cached entries and tank data; see EngineBrowserReloadHook.</summary>
        internal static void Invalidate()
        {
            _entries = null;
            _nonStorable = null;
            EngineBrowserTanks.Invalidate();
        }

        public static bool IsStorable(string resource) => !NonStorable.Contains(resource);

        /// <summary>
        /// Resources that boil off: they have a boil-off model in RF (vsp or a tank loss_rate)
        /// and boil below room temperature in at least one tank type that can hold them.
        /// MM-patchable via RF_ENGINE_BROWSER { storableTemperature, storable, nonStorable }.
        /// </summary>
        private static HashSet<string> NonStorable
        {
            get
            {
                if (_nonStorable != null)
                    return _nonStorable;

                float threshold = 293.15f;
                var forceStorable = new HashSet<string>();
                var forceNonStorable = new HashSet<string>();
                foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("RF_ENGINE_BROWSER"))
                {
                    n.TryGetValue("storableTemperature", ref threshold);
                    foreach (string s in n.GetValues("storable"))
                        forceStorable.Add(s);
                    foreach (string s in n.GetValues("nonStorable"))
                        forceNonStorable.Add(s);
                }

                var minTemp = new Dictionary<string, float>();
                var hasLossRate = new HashSet<string>();
                foreach (var def in MFSSettings.tankDefinitions.Values)
                {
                    foreach (var kv in def.tankList)
                    {
                        float t = kv.Value.temperature;
                        minTemp[kv.Key] = minTemp.TryGetValue(kv.Key, out float cur) ? Mathf.Min(cur, t) : t;
                        if (kv.Value.loss_rate > 0)
                            hasLossRate.Add(kv.Key);
                    }
                }

                var set = new HashSet<string>();
                var boiling = new HashSet<string>(hasLossRate);
                foreach (var kv in MFSSettings.resourceVsps)
                    if (kv.Value > 0)
                        boiling.Add(kv.Key);
                foreach (string res in boiling)
                {
                    // No tank holds it, so no known boiling point: assume it boils off.
                    if (!minTemp.TryGetValue(res, out float t) || t < threshold)
                        set.Add(res);
                }

                set.UnionWith(forceNonStorable);
                set.ExceptWith(forceStorable);
                _nonStorable = set;
                return set;
            }
        }

        private static List<EngineBrowserEntry> Build()
        {
            var list = new List<EngineBrowserEntry>(1024);
            try { ModuleEngineConfigsBase.BuildTechNodeMap(); }
            catch (Exception ex) { Debug.LogWarning($"[RFEngineBrowser] Could not build tech title map: {ex.Message}"); }

            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
            {
                if (ap?.partPrefab == null || ap.TechHidden || ap.category == PartCategories.none)
                    continue;

                var modules = ap.partPrefab.Modules;
                for (int i = 0; i < modules.Count; i++)
                {
                    if (!(modules[i] is ModuleEngineConfigsBase mec) || !mec.isMaster || !mec.compatible)
                        continue;
                    try
                    {
                        mec.CheckConfigs();
                        var ctx = new ModuleContext
                        {
                            Part = ap,
                            Module = mec,
                            ModuleIndex = i,
                            TechLevels = new EngineConfigTechLevels(mec),
                            Gimbals = ap.partPrefab.Modules.OfType<ModuleGimbal>().ToList(),
                            Target = ModuleEngineConfigsBase.GetSpecifiedModules(ap.partPrefab, mec.engineID, mec.moduleIndex, mec.type, mec.useWeakType).FirstOrDefault(),
                            EngineCount = GetEngineCount(ap)
                        };
                        foreach (var v in mec.BrowserVariants())
                            list.Add(BuildEntry(ctx, v));
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[RFEngineBrowser] Failed to read configs of {ap.name}: {ex}");
                    }
                }
            }

            Debug.Log($"[RFEngineBrowser] Indexed {list.Count} engine configs");
            return list;
        }

        private static float GetFloat(ConfigNode node, string key, float fallback = -1f)
        {
            string s = node.GetValue(key);
            return s != null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : fallback;
        }

        /// <summary>Per-module data shared by all of that module's variants.</summary>
        private class ModuleContext
        {
            public AvailablePart Part;
            public ModuleEngineConfigsBase Module;
            public int ModuleIndex;
            public EngineConfigTechLevels TechLevels;
            public List<ModuleGimbal> Gimbals;
            public PartModule Target;
            public int EngineCount;
        }

        /// <summary>RO's engineTypeMult: how many engines/chambers one part represents.</summary>
        private static int GetEngineCount(AvailablePart ap)
        {
            string s = ap.partConfig?.GetValue("engineTypeMult");
            if (s != null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                return Mathf.Max(1, Mathf.RoundToInt(f));
            return 1;
        }

        private static EngineBrowserEntry BuildEntry(ModuleContext ctx, ModuleEngineConfigsBase.BrowserVariant v)
        {
            ConfigNode node = v.Node;
            AvailablePart ap = ctx.Part;
            ModuleEngineConfigsBase m = ctx.Module;
            EngineConfigTechLevels tl = ctx.TechLevels;
            var e = new EngineBrowserEntry
            {
                Part = ap,
                Module = m,
                ModuleIndex = ctx.ModuleIndex,
                Node = node,
                ConfigName = v.ConfigName,
                PatchName = v.PatchName,
                Family = ctx.EngineCount > 1 ? $"{ap.title} x{ctx.EngineCount}" : ap.title,
                EngineCount = ctx.EngineCount,
                Config = v.DisplayName,
                Tech = node.GetValue("techRequired") ?? string.Empty,
                Spec = node.GetValue("specLevel") ?? string.Empty,
                Description = node.GetValue("description") ?? string.Empty,
            };

            e.TechTitle = e.Tech.Length > 0 && ModuleEngineConfigsBase.techNameToTitle.TryGetValue(e.Tech, out string title) ? title : e.Tech;

            if (node.HasValue(m.thrustRating))
                e.Thrust = m.scale * tl.ThrustTL(node.GetValue(m.thrustRating), node);

            float maxT = GetFloat(node, m.thrustRating);
            float minT = GetFloat(node, "minThrust");
            if (minT >= 0f && maxT > 0f)
                e.MinThrottle = minT / maxT;
            else if (node.HasValue("throttle"))
                e.MinThrottle = GetFloat(node, "throttle");
            if (e.MinThrottle > 1f)
                e.MinThrottle = -1f; // TL-relative throttle values aren't fractions

            if (tl.TryGetIspAtTL(node, m.techLevel, out float vac, out float sl))
            {
                e.IspVac = vac;
                e.IspSL = sl;
            }

            if (m.origMass > 0f)
            {
                e.Mass = m.scale * m.origMass * RFSettings.Instance.EngineMassMultiplier * GetFloat(node, "massMult", 1f);
                // ModuleEngineConfigs.DoConfig also scales TL engines' mass by the TL mass ratio.
                if (m.techLevel != -1 && node.HasValue(m.thrustRating))
                    e.Mass *= (float)Math.Round(tl.MassTL(node), 6);
            }
            else
                e.Mass = ap.partPrefab.mass;

            // Same delta as the engine config window: TL engines scale config cost with the TL.
            float configCost = m.scale * GetFloat(node, "cost", 0f);
            if (m.techLevel != -1)
                configCost = tl.CostTL(configCost, node) - tl.CostTL(0f, node);
            e.Cost = ap.cost + configCost;

            BuildGimbal(e, m, ctx.Gimbals);
            BuildIgnitions(e, m, tl);

            e.Ullage = node.GetValue("ullage")?.ToLower() == "true";
            e.PressureFed = node.GetValue("pressureFed")?.ToLower() == "true";

            e.Propellants = node.GetNodes("PROPELLANT")
                .Select(p => p.GetValue("name"))
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            e.PropellantText = e.Propellants.Length > 0 ? string.Join(" / ", e.Propellants) : "-";
            e.Storable = e.Propellants.Length > 0 && e.Propellants.All(IsStorable);
            e.TankProps = EngineBrowserTanks.TankProps(node);
            e.TankKey = (e.PressureFed ? "HP|" : "|") + string.Join(";", e.TankProps.Select(p => $"{p.Name}:{p.Ratio.ToString("R", CultureInfo.InvariantCulture)}"));

            e.Rated = GetFloat(node, "ratedBurnTime");
            e.RatedContinuous = GetFloat(node, "ratedContinuousBurnTime");
            e.Tested = GetFloat(node, "testedBurnTime");
            e.IgnStart = GetFloat(node, "ignitionReliabilityStart");
            e.IgnEnd = GetFloat(node, "ignitionReliabilityEnd");
            e.CycleStart = GetFloat(node, "cycleReliabilityStart");
            e.CycleEnd = GetFloat(node, "cycleReliabilityEnd");

            e.Kind = Classify(m, ctx.Target, e);

            // Rocket rated thrust is vacuum thrust, and at constant mass flow SL thrust scales with
            // Isp. Air-breathing engines don't work that way, so they get no SL figure.
            if (e.Kind != EngineKind.Plane && e.Thrust >= 0f && e.IspVac > 0f && e.IspSL > 0f)
                e.ThrustSL = e.Thrust * e.IspSL / e.IspVac;
            const float g0 = 9.80665f;
            if (e.Mass > 0f)
            {
                if (e.Thrust >= 0f) e.TwrVac = e.Thrust / (e.Mass * g0);
                if (e.ThrustSL >= 0f) e.TwrSL = e.ThrustSL / (e.Mass * g0);
            }

            e.SearchText = string.Join("\n", e.Family, e.Config, ap.name, e.PropellantText, e.Tech, e.TechTitle, e.Spec, e.Kind.ToString())
                .ToLowerInvariant();
            return e;
        }

        private static void BuildGimbal(EngineBrowserEntry e, ModuleEngineConfigsBase m, List<ModuleGimbal> prefabGimbals)
        {
            if (prefabGimbals.Count == 0)
                return;

            // Prefab gimbals still hold the part defaults: RF only overrides them after OnStart.
            Dictionary<string, Gimbal> gimbals = m.ExtractGimbals(e.Node);
            if (gimbals.Count == 0)
            {
                foreach (var g in prefabGimbals)
                    gimbals[g.gimbalTransformName] = new Gimbal(g.gimbalRange, g.gimbalRangeXP, g.gimbalRangeXN, g.gimbalRangeYP, g.gimbalRangeYN);
            }
            if (gimbals.Count == 0)
                return;

            e.Gimbal = gimbals.Values.Max(g => Mathf.Max(g.gimbalRange, g.gimbalRangeXP, g.gimbalRangeXN, g.gimbalRangeYP, g.gimbalRangeYN));
            e.GimbalText = string.Join(", ", gimbals.Values.Select(g => g.Info()).Distinct());
        }

        private static void BuildIgnitions(EngineBrowserEntry e, ModuleEngineConfigsBase m, EngineConfigTechLevels tl)
        {
            string s = e.Node.GetValue("ignitions");
            if (s == null)
                return;
            if (!int.TryParse(s, out int ign))
            {
                e.Ignitions = EngineBrowserEntry.IgnUnlimited;
                return;
            }
            int resolved = tl.ConfigIgnitions(ign);
            if (resolved == 0 && m.literalZeroIgnitions)
            {
                e.Ignitions = EngineBrowserEntry.IgnGround;
                e.GroundLit = true;
            }
            else
                e.Ignitions = resolved < 0 ? EngineBrowserEntry.IgnUnlimited : resolved;
        }

        private static readonly string[] ElectricPropellantKeys = { "Xenon", "Argon", "Krypton", "Neon", "Lithium", "Iodine", "Bismuth", "Teflon", "PTFE" };

        private static bool IsNuclearFuel(string res) => res.Contains("Uranium") || res.Contains("Plutonium");

        private static bool IsSolidPropellant(string res)
            => !IsNuclearFuel(res) && PartResourceLibrary.Instance.GetDefinition(res)?.resourceFlowMode == ResourceFlowMode.NO_FLOW;

        private static EngineKind Classify(ModuleEngineConfigsBase m, PartModule target, EngineBrowserEntry e)
        {
            if (m.type.Contains("ModuleRCS"))
                return EngineKind.RCS;

            EngineType et = (target as ModuleEngines)?.engineType ?? EngineType.Generic;
            string targetType = target != null ? target.GetType().Name : m.type;
            string[] props = e.Propellants;

            if (targetType.Contains("AJE") || et == EngineType.Turbine || et == EngineType.Piston || et == EngineType.ScramJet
                || props.Any(p => p.StartsWith("IntakeA")))
                return EngineKind.Plane;
            if (props.Any(IsNuclearFuel) || et == EngineType.Nuclear)
                return EngineKind.Nuclear;
            if (m.techLevel != -1)
                return EngineKind.Generic;
            if (et == EngineType.Electric
                || (props.Contains("ElectricCharge") && (props.Any(p => ElectricPropellantKeys.Any(p.Contains)) || e.IspVac > 600f)))
                return EngineKind.Electric;
            if (et == EngineType.SolidBooster || props.Any(IsSolidPropellant))
                return EngineKind.Solid;
            return EngineKind.Liquid;
        }
    }
}
