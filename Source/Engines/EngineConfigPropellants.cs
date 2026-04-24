using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP.UI.Screens;

namespace RealFuels
{
    /// <summary>
    /// Static utility class for propellant and resource operations.
    /// Contains pure functions for clearing float curves, propellant gauges, and RCS propellants.
    /// </summary>
    public static class EngineConfigPropellants
    {
        private static readonly FieldInfo MRCSConsumedResources = typeof(ModuleRCS).GetField("consumedResources", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// Clears all FloatCurves that need to be cleared based on config node or tech level.
        /// </summary>
        public static void ClearFloatCurves(Type mType, PartModule pm, ConfigNode cfg, int techLevel)
        {
            // clear all FloatCurves we need to clear (i.e. if our config has one, or techlevels are enabled)
            bool delAtmo = cfg.HasNode("atmosphereCurve") || techLevel >= 0;
            bool delDens = cfg.HasNode("atmCurve") || techLevel >= 0;
            bool delVel = cfg.HasNode("velCurve") || techLevel >= 0;
            foreach (FieldInfo field in mType.GetFields())
            {
                if (field.FieldType == typeof(FloatCurve) &&
                    ((field.Name.Equals("atmosphereCurve") && delAtmo)
                    || (field.Name.Equals("atmCurve") && delDens)
                    || (field.Name.Equals("velCurve") && delVel)))
                {
                    field.SetValue(pm, new FloatCurve());
                }
            }
        }

        /// <summary>
        /// Clears propellant gauges from the staging icon.
        /// </summary>
        public static void ClearPropellantGauges(Type mType, PartModule pm)
        {
            foreach (FieldInfo field in mType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.FieldType == typeof(Dictionary<Propellant, ProtoStageIconInfo>) &&
                    field.GetValue(pm) is Dictionary<Propellant, ProtoStageIconInfo> boxes)
                {
                    foreach (ProtoStageIconInfo v in boxes.Values)
                    {
                        try
                        {
                            if (v is ProtoStageIconInfo)
                                pm.part.stackIcon.RemoveInfo(v);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError("*RFMEC* Trying to remove info box: " + e.Message);
                        }
                    }
                    boxes.Clear();
                }
            }
        }

        /// <summary>
        /// Clears RCS propellants and reloads them from config.
        /// </summary>
        /// <param name="part">The part containing RCS modules</param>
        /// <param name="cfg">The configuration node to load propellants from</param>
        /// <param name="doConfigAction">Action to execute DoConfig on the configuration</param>
        public static void ClearRCSPropellants(Part part, ConfigNode cfg, Action<ConfigNode> doConfigAction)
        {
            List<ModuleRCS> RCSModules = part.Modules.OfType<ModuleRCS>().ToList();
            if (RCSModules.Count > 0)
            {
                doConfigAction(cfg);
                foreach (var rcsModule in RCSModules)
                {
                    if (cfg.HasNode("PROPELLANT"))
                        rcsModule.propellants.Clear();
                    rcsModule.Load(cfg);
                    List<PartResourceDefinition> res = MRCSConsumedResources.GetValue(rcsModule) as List<PartResourceDefinition>;
                    res.Clear();
                    foreach (Propellant p in rcsModule.propellants)
                        res.Add(p.resourceDef);
                }
            }
        }
    }
}
