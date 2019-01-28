using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using ModularFI;

namespace RealFuels
{
    [KSPAddon(KSPAddon.Startup.SpaceCentre, true)]
    public class MFIRegistration : MonoBehaviour
    {
        void Start()
        {
            ModularFlightIntegrator.RegisterUpdateMassStatsOverride(UpdateMassStats);
            GameObject.Destroy(this);
        }

        void UpdateMassStats(ModularFlightIntegrator fi)
        {
            if (fi != null)
            {
                if (fi.Vessel != null && fi.Vessel.parts != null)
                {
                    int partCount = fi.Vessel.parts.Count;
                    int index = partCount;
                    while (index-- > 0)
                    {
                        Part part = fi.Vessel.parts[index];
                        if (part != null)
                        {
                            part.resourceMass = part.GetResourceMassNoCryo(out part.resourceThermalMass);
                            part.thermalMass = (double)part.mass * PhysicsGlobals.StandardSpecificHeatCapacity * part.thermalMassModifier + part.resourceThermalMass;
                            fi.SetSkinThermalMass(part);
                            part.thermalMass = Math.Max(part.thermalMass - part.skinThermalMass, 0.1);
                            part.thermalMassReciprocal = 1.0 / part.thermalMass;
                        }
                    }
                    int index2 = partCount;
                    while (index2-- > 0)
                    {
                        Part part = fi.Vessel.parts[index2];
                        if (part != null)
                        {
                            if (part.rb != null)
                            {
                                part.physicsMass = (double)(part.mass + part.resourceMass + fi.BaseFIGetPhysicslessChildMass(part));
                                if (!part.packed)
                                {
                                    part.rb.mass = (float)part.physicsMass;
                                    part.rb.centerOfMass = part.CoMOffset;
                                }
                            }
                            else
                            {
                                part.physicsMass = 0.0;
                            }
                        }
                    }
                }
                else
                    Debug.Log("[RealFuels.Utilities.MFIRegistration] CATASTROPHIC FAILURE IN UpdateStats(); null vessel");
            }
        }
    }
}
