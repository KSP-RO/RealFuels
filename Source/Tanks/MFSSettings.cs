using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;

namespace RealFuels
{
    public class MFSSettings
	{
        public static bool useRealisticMass = true;
        public static float tankMassMultiplier = 1;
        public static float baseCostPV = 0.0f;
        public static float partUtilizationDefault = 86;
        public static bool partUtilizationTweakable = false;
        public static string unitLabel = "u";
        public static bool basemassUseTotalVolume = false;
        public static double radiatorMinTempMult = 0.99d;

        // Move all possible tank upgrades into the preview list in OnStart
        // It requires an external mod to be responsible for calling the Validate() method.
        public static bool previewAllLockedTypes = false;
        // "Pill tank fit" at the start of a game session: Off, Selected (the part whose PAW is open) or All
        public static Tanks.PillTankView pillTankPreview = Tanks.PillTankView.Off;
        // Parts whose tank is the largest pill that fits in them: ProceduralParts shapes (module names; a hollow shell
        // - a shape with shellMode on - never), and parts with one of these modules.
        private static readonly string[] pillTankShapesDefault =
            { "ProceduralShapeCylinder", "ProceduralShapePill", "ProceduralShapeCone", "ProceduralShapeBezierCone",
              "ProceduralShapePolygon", "ProceduralShapeSpine" };
        // The steepest a pill may taper (its cone's half-angle, degrees): a conical part's tank narrows with it. 0: a
        // plain pill, one radius end to end.
        public static double pillTankMaxTaper = 15;
        // A pill's end domes: their height over the radius. 1: hemispheres (highly pressurized types); less, flatter
        // (ellipsoidal) domes. A tank type can set its own (TankDefinition.pillTankDome).
        public static double pillTankDome = 0.70710678, pillTankDomeHP = 1;
        // Low pressure (not highly pressurized) types' pills may taper two ways, kinked once along the axis (a cylinder
        // under a nose cone, say).
        public static bool pillTankTwoTapers = true;
        // A tapered pill's smaller end: its radius at least this (m), unless the whole pill is no wider.
        public static double pillTankMinEndRadius = 0.1;
        private static readonly string[] pillTankModulesDefault = { "WingProcedural" };
        public static readonly HashSet<string> pillTankShapes = new HashSet<string>(pillTankShapesDefault);
        public static readonly HashSet<string> pillTankModules = new HashSet<string>(pillTankModulesDefault);

        public static readonly HashSet<string> ignoreFuelsForFill = new HashSet<string>();
        public static readonly Dictionary<string, Tanks.TankDefinition> tankDefinitions = new Dictionary<string, Tanks.TankDefinition>();

        public static readonly Dictionary<string, double> resourceVsps = new Dictionary<string, double>();
        public static readonly Dictionary<string, double> resourceConductivities = new Dictionary<string, double>();

        private static readonly Dictionary<string, ConfigNode[]> overrides = new Dictionary<string, ConfigNode[]>();

        static string version;
        // A comma-separated list, when given, replaces the set; else it's the defaults (again, on a database reload).
        private static void ReadSet(ConfigNode node, string key, HashSet<string> set, string[] defaults)
        {
            set.Clear();
            string s = node?.GetValue(key);
            if (s == null) { set.UnionWith(defaults); return; }
            foreach (string n in s.Split(new[] { ',', ' ', ';' }, System.StringSplitOptions.RemoveEmptyEntries)) set.Add(n);
        }

        public static string GetVersion ()
        {
            if (version != null) {
                return version;
            }

            var asm = Assembly.GetCallingAssembly ();
            var title = MFSVersionReport.GetAssemblyTitle (asm);
            version = title + " " + MFSVersionReport.GetAssemblyVersionString (asm);

            return version;
        }

        public static void SaveOverrideList(Part p, ConfigNode[] nodes)
        {
            string id = GetPartIdentifier(p);
            if (overrides.ContainsKey(id))
                Debug.Log("*MFT* ERROR: overrides already stored for " + id);
            else
                overrides[id] = nodes;
        }

        public static ConfigNode[] GetOverrideList(Part p)
        {
            string id = GetPartIdentifier(p);
            if (overrides.TryGetValue(id, out var result))
                return result;

            Debug.Log("*MFT* WARNING: no entry in overrides for " + id);
            return new ConfigNode[0];
        }

        private static string GetPartIdentifier(Part part)
        {
            string partName = part.partInfo != null ? part.partInfo.name : part.name;
            partName = Utilities.SanitizeName(partName);
            return partName;
        }

        public static void ModuleManagerPostLoad()
        {
            resourceVsps.Clear();
            resourceConductivities.Clear();

            // fill vsps & conductivities
            foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("RESOURCE_DEFINITION"))
            {
                string nm = n.GetValue("name");
                double dtmp = 0;
                if (n.TryGetValue("vsp", ref dtmp))
                    resourceVsps[nm] = dtmp;
                if (n.TryGetValue("conductivity", ref dtmp))
                    resourceConductivities[nm] = dtmp;
            }

            ConfigNode node = GameDatabase.Instance.GetConfigNodes("MFSSETTINGS").LastOrDefault();
            Debug.Log ("[MFS] Loading global settings");

            if (node != null) {
                node.TryGetValue("useRealisticMass", ref useRealisticMass);
                node.TryGetValue("tankMassMultiplier", ref tankMassMultiplier);
                node.TryGetValue("baseCostPV", ref baseCostPV);
                node.TryGetValue("partUtilizationDefault", ref partUtilizationDefault);
                node.TryGetValue("partUtilizationTweakable", ref partUtilizationTweakable);
                node.TryGetValue("unitLabel", ref unitLabel);
                node.TryGetValue("basemassUseTotalVolume", ref basemassUseTotalVolume);
                node.TryGetValue("radiatorMinTempMult", ref radiatorMinTempMult);
                node.TryGetValue("previewAllLockedTypes", ref previewAllLockedTypes);
                string view = null;
                if (node.TryGetValue("pillTankPreview", ref view))
                {
                    // also True / False (Selected / Off)
                    if (bool.TryParse(view, out bool on)) pillTankPreview = on ? Tanks.PillTankView.Selected : Tanks.PillTankView.Off;
                    else if (System.Enum.TryParse(view, true, out Tanks.PillTankView v)) pillTankPreview = v;
                }
                node.TryGetValue("pillTankMaxTaper", ref pillTankMaxTaper);
                node.TryGetValue("pillTankDome", ref pillTankDome);
                node.TryGetValue("pillTankDomeHP", ref pillTankDomeHP);
                node.TryGetValue("pillTankTwoTapers", ref pillTankTwoTapers);
                node.TryGetValue("pillTankMinEndRadius", ref pillTankMinEndRadius);

                ignoreFuelsForFill.Clear();
                if (node.GetNode("IgnoreFuelsForFill") is ConfigNode ignoreNode)
                    foreach (ConfigNode.Value v in ignoreNode.values)
                        ignoreFuelsForFill.Add(v.name);
            }
            ReadSet(node, "pillTankShapes", pillTankShapes, pillTankShapesDefault);
            ReadSet(node, "pillTankModules", pillTankModules, pillTankModulesDefault);

            tankDefinitions.Clear();
            foreach (ConfigNode defNode in GameDatabase.Instance.GetConfigNodes("TANK_DEFINITION")) {
                if (tankDefinitions.ContainsKey(defNode.GetValue("name"))) {
                    Debug.LogWarning ("[MFS] Ignored duplicate definition of tank type " + defNode.GetValue ("name"));
                } else {
                    var def = new Tanks.TankDefinition(defNode);
                    tankDefinitions.Add(def.name, def);
                }
            }
        }
    }
}
