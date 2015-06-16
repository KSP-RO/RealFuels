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

        bool pressureFed = false;
        bool tankHighlyPressurized = false;
        bool ullageEnabled = true;

        public UllageSet(UllageManager manager, ModuleEnginesRF engine)
        {
            ullageSim = new UllageSimulator();
            tanks = new List<Part>();
            rfTanks = new Dictionary<Part, Tanks.ModuleFuelTanks>();

            pressureFed = engine.pressureFed;
            ullageEnabled = engine.ullage;

            // set tanks
            tankHighlyPressurized = true; // will be set false if any propellant has no highly-pres tank feeding it
            bool foundPTank = false;
            for(int i = engine.propellants.Count - 1; i >= 0; --i)
            {
                Propellant p = engine.propellants[i];
                bool noPresTank = true;
                for(int j = p.connectedResources.Count - 1; j >= 0; --j)
                {
                    PartResource r = p.connectedResources[j];
                    Part part = r.part;
                    Tanks.ModuleFuelTanks tank = null;
                    if (!tanks.Contains(part))
                    {
                        tanks.Add(part);
                        for(int k = part.Modules.Count - 1; k >= 0; --k)
                        {
                            PartModule m = part.Modules[k];
                            if (m is Tanks.ModuleFuelTanks)
                            {
                                tank = m as Tanks.ModuleFuelTanks;
                                rfTanks[part] = tank;
                            }
                        }
                    }
                    else
                    {
                        rfTanks.TryGetValue(part, out tank);
                    }
                    if (tank != null)
                    {
                        bool pressurized = tank.pressurizedFuels[r.resourceName];
                        noPresTank &= !pressurized; // stay true only if pressurized always false
                    }
                }
                tankHighlyPressurized &= !noPresTank; // i.e. if no tank, set false.
            }
        }
        public bool AddEngine(ModuleEnginesRF engine)
        {
            if (engines.Count == 0)
                return false;
            if (engine.pressureFed != pressureFed)
                return false;

            List<Part> tankList = new List<Part>();
            for (int i = engine.propellants.Count - 1; i >= 0; --i)
            {
                Propellant p = engine.propellants[i];
                for (int j = p.connectedResources.Count - 1; j >= 0; --j)
                {
                    PartResource r = p.connectedResources[j];
                    Part part = r.part;
                    if (!tankList.Contains(part))
                        tankList.Add(part);
                    if (!tanks.Contains(part))
                        return false;
                }
            }
            if (tankList.Count != tanks.Count)
                return false;

            engines.Add(engine);
            return true;
        }
        public void Update(Vector3d pos, Vector3d acc, Vector3d vel, Vector3d angVel, double timeDelta, double ventingAcc)
        {
            position = pos;
            velocity = vel;
            acceleration = acc;
            angularVelocity = angVel;
            double fuelRatio = 1d;
            if(HighLogic.LoadedSceneIsFlight)
            {
                int pCount = engines[0].propellants.Count;
                double sumRatio = 0d;
                for(int i = pCount - 1; i >= 0; --i)
                {
                    Propellant p = engines[0].propellants[i];
                    sumRatio += p.totalResourceAvailable / p.totalResourceCapacity;
                }
                sumRatio /= (double)pCount;
                // do pressure-fed tests?
            }
            if(ullageEnabled)
                ullageSim.Update(acceleration, angularVelocity, timeDelta, ventingAcc, fuelRatio);
        }
        public string GetUllageState()
        {
            return ullageSim.GetFuelFlowState();
        }
        public double GetUllageStability()
        {
            return ullageSim.GetFuelFlowStability();
        }
        public bool PressureOK()
        {
            return pressureFed ? tankHighlyPressurized : true;
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
