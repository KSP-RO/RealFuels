using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RealFuels.Ullage
{
    public class UllageManager
    {
        Dictionary<ModuleEnginesRF, UllageSet> engineLookup;
        List<UllageSet> ullageSets;
        List<Tanks.ModuleFuelTanks> tanks;

        Vessel vessel;

        public UllageSet AddEngine(ModuleEnginesRF engine)
        {
            UllageSet set;
            if (engineLookup.TryGetValue(engine, out set))
                return set;

            // try to place engine in existing set
            for(int i = ullageSets.Count - 1; i >= 0; --i)
            {
                set = ullageSets[i];
                if (set.AddEngine(engine))
                {
                    engineLookup[engine] = set;
                    return set;
                }
            }
            set = new UllageSet(this, engine);
            ullageSets.Add(set);
            return set;
        }

        public UllageSet GetUllage(ModuleEnginesRF engine)
        {
            UllageSet retVal;
            if (engineLookup.TryGetValue(engine, out retVal))
                return retVal;

            return AddEngine(engine); // or should we return null, or throw?
        }

        public void UpdateUllage()
        {
            if (vessel == null)
                return;

            // get boiloff accel
            double massRate = 0d;
            double ventingAcceleration = 0d;
            for (int i = tanks.Count - 1; i >= 0; --i)
                massRate += tanks[i].BoiloffMass;

            if (massRate > 0d)
            {
                double vesselMass = 0d;
                for (int i = vessel.Parts.Count - 1; i >= 0; --i)
                {
                    Part p = vessel.Parts[i];
                    if (p.rb != null)
                        vesselMass += p.rb.mass;
                }
                ventingAcceleration = massRate / vesselMass * UllageSimulator.s_VentingVelocity;
            }
            for (int i = ullageSets.Count - 1; i >= 0; --i)
            {
                ullageSets[i].Update(vessel.transform.position, vessel.srf_velocity, 
            }
        }

        public UllageManager(Vessel v)
        {
            vessel = v;
            engineLookup = new Dictionary<ModuleEnginesRF, UllageSet>();
            ullageSets = new List<UllageSet>();

            for (int i = vessel.Parts.Count - 1; i >= 0; --i)
            {
                Part part = vessel.Parts[i];
                for (int j = part.Modules.Count - 1; j >= 0; --j)
                {
                    PartModule m = part.Modules[j];
                    if (m is Tanks.ModuleFuelTanks)
                    {
                        Tanks.ModuleFuelTanks tank = m as Tanks.ModuleFuelTanks;
                        if(!tanks.Contains(tank))
                            tanks.Add(tank);
                    }
                    else if (m is ModuleEnginesRF)
                    {
                        AddEngine(m as ModuleEnginesRF);
                    }
                }
            }
        }
    }
}
