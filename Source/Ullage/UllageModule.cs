using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RealFuels.Ullage
{
    public class UllageModule : VesselModule
    {
        List<UllageSet> ullageSets;
        List<Tanks.ModuleFuelTanks> tanks;

        Vessel vessel;
        bool packed = true;
        int partCount = -1;

        public void Start()
        {
            vessel = GetComponent<Vessel>();
            
            ullageSets = new List<UllageSet>();
            tanks = new List<Tanks.ModuleFuelTanks>();
            // will reset on first update
        }

        public void FixedUpdate()
        {
            if (vessel == null || !FlightGlobals.ready)
            {
                partCount = -1;
                return;
            }

            int newPartCount = vessel.Parts.Count;
            if (packed != vessel.packed || newPartCount != partCount)
            {
                partCount = newPartCount;
                Reset();
                packed = vessel.packed;
            }

            Vector3 accel;
            Vector3 angVel;
            if (TimeWarp.WarpMode == TimeWarp.Modes.HIGH && TimeWarp.CurrentRate > TimeWarp.MaxPhysicsRate)
            {
                // Time warping... (5x -> 100000x)
                angVel = Vector3.zero; // FIXME support rotation in timewarp!
                accel = Vector3.zero;
            }
            else
            {
                accel = (Vector3)(vessel.perturbation);
                angVel = vessel.angularVelocity;
            }
            // are we stopped but the fuel is under gravity?
            if (vessel.LandedOrSplashed)
                accel = -(Vector3)FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D());


            // get boiloff accel
            double massRate = 0d;
            double ventingAcceleration = 0d;
            for (int i = tanks.Count - 1; i >= 0; --i)
                massRate += tanks[i].BoiloffMassRate;

            // technically we should vent in the correct direction per engine's tanks
            // Instead, this will just give magical "correct direction" acceleration from total
            // boiloff mass, for every engine (i.e. for every orientation)
            if (massRate > 0d)
            {
                double vesselMass = 0d;
                for (int i = vessel.Parts.Count - 1; i >= 0; --i)
                {
                    Part p = vessel.Parts[i];
                    if (p.rb != null)
                        vesselMass += p.rb.mass;
                }
                ventingAcceleration = RFSettings.Instance.ventingVelocity * massRate / vesselMass;
            }

            // Update ullage sims
            for (int i = ullageSets.Count - 1; i >= 0; --i)
            {
                ullageSets[i].Update(accel, angVel, TimeWarp.fixedDeltaTime, ventingAcceleration);
            }
        }

        public void Reset()
        {
            ullageSets.Clear();
            tanks.Clear();

            for (int i = partCount - 1; i >= 0; --i)
            {
                Part part = vessel.Parts[i];
                for (int j = part.Modules.Count - 1; j >= 0; --j)
                {
                    PartModule m = part.Modules[j];
                    if (m is Tanks.ModuleFuelTanks)
                    {
                        Tanks.ModuleFuelTanks tank = m as Tanks.ModuleFuelTanks;
                        if (!tanks.Contains(tank))
                            tanks.Add(tank);
                    }
                    else if (m is ModuleEnginesRF)
                    {
                        ModuleEnginesRF engine = m as ModuleEnginesRF;

                        if (engine.ullageSet == null) // just in case
                        {
                            engine.ullageSet = new UllageSet(engine);
                        }
                        else
                        {
                            engine.ullageSet.SetTanks();
                            engine.ullageSet.SetModule(this);
                        }

                        ullageSets.Add(engine.ullageSet);
                    }
                }
            }
        }
    }
}
