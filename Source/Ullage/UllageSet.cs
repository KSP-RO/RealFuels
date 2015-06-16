using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RealFuels.Ullage
{
    public class UllageSet
    {
        List<ModuleEnginesRF> engines;
        List<Part> tanks;
        Dictionary<Part, Tanks.ModuleFuelTanks> rfTanks;

        UllageSimulator ullageSim;

        Vector3d centerOfMass, position, acceleration, velocity, angularVelocity;

        public UllageSet(ModuleEnginesRF engine)
        {
            ullageSim = new UllageSimulator();
            tanks = new List<Part>();
            rfTanks = new Dictionary<Part, Tanks.ModuleFuelTanks>();

            foreach (Propellant p in engine.propellants)
            {
                List<Part> feedParts = new List<Part>();
                List<PartResource> feedPartRes = new List<PartResource>();
                engine.part.GetConnectedResources(p.id, p.GetFlowMode(), feedPartRes);
                foreach (PartResource r in feedPartRes)
                {
                    Part part = r.part;
                    if (!tanks.Contains(part))
                    {
                        tanks.Add(part);
                        foreach (PartModule m in part.Modules)
                            if (m is Tanks.ModuleFuelTanks)
                                rfTanks[part] = (m as Tanks.ModuleFuelTanks);
                    }
                }
            }
            // Update must be run by creator.
        }
        public void Update(Vector3d pos, Vector3d acc, Vector3d vel, Vector3d angVel)
        {
            position = pos;
            velocity = vel;
            acceleration = acc;
            angularVelocity = angVel;
        }
        public string GetUllageState()
        {
            return ullageSim.GetFuelFlowState();
        }
        public double GetUllageStability()
        {
            return ullageSim.GetFuelFlowStability();
        }
        /*public void UpdateCoM()
        {
            if(tanks.Count == 0 || !HighLogic.LoadedSceneIsFlight)
                return;

            Vector3d com, ang, vel;
            com = vel = ang = Vector3d.zero;
            double mass = 0d;
            for (int i = tanks.Count - 1; i >= 0; --i)
            {
                Part part = tanks[i];
                if (part.rb != null)
                {
                    double pMass = part.rb.mass;
                    com += (Vector3d)part.rb.worldCenterOfMass * pMass;
                    
                    mass += pMass;
                }
            }
            if (mass > 0)
            {
                centerOfMass = com / mass;
            }
            else
                centerOfMass = tanks[0].rigidbody.worldCenterOfMass;
        }*/
    }
}
