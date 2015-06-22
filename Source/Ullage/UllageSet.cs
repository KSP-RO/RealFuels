using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RealFuels.Ullage
{
    public class UllageSet : IConfigNode
    {
        #region Fields
        ModuleEnginesRF engine;
        List<Part> tanks;
        Dictionary<Part, Tanks.ModuleFuelTanks> rfTanks;

        UllageSimulator ullageSim;
        UllageModule module;

        Vector3d acceleration, angularVelocity;

        bool pressureFed = false;
        bool tanksHighlyPressurized = false;
        bool ullageEnabled = true;
        Quaternion rotationFromPart = Quaternion.identity;
        #endregion

        #region Setup
        public UllageSet(ModuleEnginesRF eng)
        {
            engine = eng;
            ullageSim = new UllageSimulator();
            if (engine.vessel != null)
                module = engine.vessel.GetComponent<UllageModule>();
            else
                module = null;

            // set engine fields
            pressureFed = engine.pressureFed;
            ullageEnabled = engine.ullage;
            
            // create orientaiton
            if (engine.thrustAxis != Vector3.zero)
                rotationFromPart = Quaternion.FromToRotation(engine.thrustAxis, Vector3.up);

            SetTanks(); // fill tank lists, find pressurization, etc.
        }

        public void SetTanks()
        {
            // set tanks
            tanks = new List<Part>();
            rfTanks = new Dictionary<Part, Tanks.ModuleFuelTanks>();
            tanksHighlyPressurized = true; // will be set false if any propellant has no highly-pres tank feeding it

            for (int i = engine.propellants.Count - 1; i >= 0; --i)
            {
                Propellant p = engine.propellants[i];
                bool noPresTank = true;
                for (int j = p.connectedResources.Count - 1; j >= 0; --j)
                {
                    PartResource r = p.connectedResources[j];
                    Part part = r.part;
                    Tanks.ModuleFuelTanks tank = null;
                    if (!tanks.Contains(part))
                    {
                        tanks.Add(part);
                        for (int k = part.Modules.Count - 1; k >= 0; --k)
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
                        // noPresTank will stay true only if no pressurized tank found.
                        noPresTank &= !tank.pressurizedFuels[r.resourceName];
                    }
                }
                tanksHighlyPressurized &= !noPresTank; // i.e. if no tank, set false.
            }
        }
        #endregion

        #region Load/Save
        public void Load(ConfigNode node)
        {
            if (node.HasNode("UllageSim"))
                ullageSim.Load(node.GetNode("UllageSim"));
        }
        public void Save(ConfigNode node)
        {
            if (ullageSim != null)
            {
                ConfigNode simNode = new ConfigNode("UllageSim");
                ullageSim.Save(simNode);
                node.AddNode(simNode);
            }
        }
        #endregion

        #region Interface
        public void Update(Vector3 acc, Vector3 angVel, double timeDelta, double ventingAcc)
        {
            acceleration = rotationFromPart * engine.transform.InverseTransformDirection(acc);
            acceleration.y += ventingAcc;

            angularVelocity = rotationFromPart * engine.transform.InverseTransformDirection(angVel);

            double fuelRatio = 1d;
            if(HighLogic.LoadedSceneIsFlight)
            {
                int pCount = engine.propellants.Count;
                fuelRatio = 0d;
                for(int i = pCount - 1; i >= 0; --i)
                {
                    Propellant p = engine.propellants[i];
                    fuelRatio += p.totalResourceAvailable / p.totalResourceCapacity;
                }
                fuelRatio /= (double)pCount;
                // do pressure-fed tests?
            }
            if(ullageEnabled)
                ullageSim.Update(acceleration, angularVelocity, timeDelta, ventingAcc, fuelRatio);
        }
        public string GetUllageState()
        {
            return ullageSim.GetPropellantStatus();
        }
        public double GetUllageStability()
        {
            return ullageSim.GetPropellantStability();
        }
        public bool PressureOK()
        {
            return pressureFed ? tanksHighlyPressurized : true;
        }
        #endregion
    }
}
