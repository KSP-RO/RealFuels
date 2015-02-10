using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using System.Collections.ObjectModel;
using KSPAPIExtensions;
using KSPAPIExtensions.PartMessage;

// ReSharper disable InconsistentNaming, CompareOfFloatsByEqualityOperator

namespace RealFuels
{
    public class ModuleFuelTanks : ModularFuelPartModule, IPartCostModifier
    {
        // looks to see if we should ignore this fuel when creating an autofill for an engine
        private static bool IgnoreFuel(string name)
        {
            return MFSSettings.Instance.ignoreFuelsForFill.Contains(name);
        }

        [PartMessageListener(typeof(PartChildAttached), relations: PartRelationship.AnyPart, scenes: GameSceneFilter.AnyEditor)]
        [PartMessageListener(typeof(PartChildDetached), relations: PartRelationship.AnyPart, scenes: GameSceneFilter.AnyEditor)]
        public void VesselAttachmentsChanged(Part childPart)
        {
            UpdateUsedBy();
        }

        [PartMessageListener(typeof (PartEngineConfigChanged), relations: PartRelationship.AnyPart, scenes: GameSceneFilter.AnyEditor)]
        public void EngineConfigsChanged()
        {
            UpdateUsedBy();
        }

        private readonly Dictionary<string, FuelInfo> usedBy = new Dictionary<string, FuelInfo>();
        private int engineCount;

        List<Propellant> GetEnginePropellants(PartModule engine)
        {
            string typename = engine.GetType().ToString ();
            if (typename.Equals("ModuleEnginesFX"))
            {
                ModuleEnginesFX e = (ModuleEnginesFX)engine;
                return e.propellants;
            }
            else if (typename.Equals("ModuleEngines"))
            {
                ModuleEngines e = (ModuleEngines)engine;
                return e.propellants;
            }
            else if (typename.Equals("ModuleRCSFX"))
            {
                ModuleRCS e = (ModuleRCS)engine;
                return e.propellants;
            }
            else if (typename.Equals("ModuleRCS"))
            {
                ModuleRCS e = (ModuleRCS)engine;
                return e.propellants;
            }
            return null;
        }

        private void UpdateUsedBy()
        {
            //print("*RK* Updating UsedBy");
            if (dedicated)
            {
                Empty();
                UsedVolume = 0;
                ConfigureFor(part);
                MarkWindowDirty();
                return;
            }


            usedBy.Clear();

            List<Part> enginesList = GetEnginesFedBy(part);
            engineCount = enginesList.Count;

            foreach (Part engine in enginesList)
            {
                foreach (PartModule engine_module in engine.Modules)
                {
                    List<Propellant> propellants = GetEnginePropellants (engine_module);
                    if ((object)propellants != null)
                    {
                        FuelInfo f = new FuelInfo(propellants, this, engine.partInfo.title);
                        if (f.ratioFactor > 0.0)
                        {
                            FuelInfo found;
                            if (!usedBy.TryGetValue(f.Label, out found))
                            {
                                usedBy.Add(f.Label, f);
                            }
                            else if (!found.names.Contains(engine.partInfo.title))
                            {
                                found.names += ", " + engine.partInfo.title;
                            }
                        }
                    }
                }
            }

            // Need to update the tweakable menu too
            if (HighLogic.LoadedSceneIsEditor)
            {
                Events.RemoveAll(button => button.name.StartsWith("MFT"));

                bool activeEditor = (AvailableVolume >= 0.001);

                int idx = 0;
                foreach (FuelInfo info in usedBy.Values)
                {
                    KSPEvent kspEvent = new KSPEvent
                    {
                        name = "MFT" + idx++,
                        guiActive = false,
                        guiActiveEditor = activeEditor,
                        guiName = info.Label
                    };
                    FuelInfo info1 = info;
                    BaseEvent button = new BaseEvent(Events, kspEvent.name, () => ConfigureFor(info1), kspEvent)
                    {
                        guiActiveEditor = activeEditor
                    };
                    Events.Add(button);
                }
                MarkWindowDirty();
            }
        }

        public void ConfigureFor(Part engine)
        {
            foreach (PartModule engine_module in engine.Modules)
            {
                List<Propellant> propellants = GetEnginePropellants(engine_module);
                if ((object)propellants != null)
                {
                    ConfigureFor(new FuelInfo(propellants, this, engine.partInfo.title));
                    break;
                }
            }
        }

        private void ConfigureFor(FuelInfo fi)
        {
            if (fi.ratioFactor == 0.0 || fi.efficiency == 0) // can't configure for this engine
                return;

            double availableVolume = AvailableVolume;
            foreach (Propellant tfuel in fi.propellants)
            {
                if (dedicated || PartResourceLibrary.Instance.GetDefinition(tfuel.name).resourceTransferMode != ResourceTransferMode.NONE)
                {
                    FuelTank tank;
                    if (tankList.TryGet(tfuel.name, out tank))
                    {
                        double amt = availableVolume * tfuel.ratio / fi.efficiency;
                        tank.maxAmount += amt;
                        tank.amount += amt;
                    }
                }
            }
        }

        public static List<Part> GetEnginesFedBy(Part part)
        {
            Part ppart = part;
            while (ppart.parent != null && ppart.parent != ppart)
                ppart = ppart.parent;

            return new List<Part>(ppart.FindChildParts<Part>(true)).FindAll(p => (p.Modules.Contains("ModuleEngines") || p.Modules.Contains("ModuleEnginesFX") || p.Modules.Contains("ModuleRCSFX") || p.Modules.Contains("ModuleRCS")));
        }
    }
}
