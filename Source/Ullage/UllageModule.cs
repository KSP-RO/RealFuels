using System.Collections.Generic;
using UnityEngine;

namespace RealFuels.Ullage
{
    public class UllageModule : VesselModule
    {
        private readonly List<UllageSet> ullageSets = new List<UllageSet>();
        private readonly List<Tanks.ModuleFuelTanks> tanks = new List<Tanks.ModuleFuelTanks>();

        bool packed = true;
        int partCount = -1;

        public void FixedUpdate()
        {
            if (vessel == null || !vessel.loaded || !FlightGlobals.ready)
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
                accel = vessel.perturbation;
                angVel = vessel.angularVelocity;
            }
            // are we stopped but the fuel is under gravity?
            if (vessel.LandedOrSplashed || vessel.situation == Vessel.Situations.PRELAUNCH) // catch the launch clamp case
                accel = -(Vector3)FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D());

            // get boiloff accel
            double massRate = 0d;
            double ventingAcceleration = 0d;
            foreach (var tank in tanks)
                massRate += tank.BoiloffMassRate;

            // technically we should vent in the correct direction per engine's tanks
            // Instead, this will just give magical "correct direction" acceleration from total
            // boiloff mass, for every engine (i.e. for every orientation)
            if (massRate > 0d)
            {
                ventingAcceleration = RFSettings.Instance.ventingVelocity * massRate / vessel.totalMass;
            }

            // Update ullage sims
            foreach (var set in ullageSets)
            {
                set.Update(accel, angVel, TimeWarp.fixedDeltaTime, ventingAcceleration);
            }
        }

        public void Reset()
        {
            ullageSets.Clear();
            tanks.Clear();

            foreach (Part p in vessel.Parts)
            {
                for (int j = p.Modules.Count - 1; j >= 0; --j)
                {
                    if (p.Modules[j] is Tanks.ModuleFuelTanks tank)
                    {
                        if (!tanks.Contains(tank))
                            tanks.Add(tank);
                    }
                    else if (p.Modules[j] is ModuleEnginesRF engine)
                    {
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
