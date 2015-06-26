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

            tanks = new List<Part>();
            rfTanks = new Dictionary<Part, Tanks.ModuleFuelTanks>();

            // set engine fields
            pressureFed = engine.pressureFed;
            ullageEnabled = engine.ullage;
            
            // create orientaiton
            SetThrustAxis(engine.thrustAxis);
            if (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor)
                SetTanks(); // fill tank lists, find pressurization, etc.
        }

        public void SetTanks()
        {
            tanks.Clear();
            rfTanks.Clear();

            tanksHighlyPressurized = true; // will be set false if any propellant has no highly-pres tank feeding it

            // iterate through all propellants.
            for (int i = engine.propellants.Count - 1; i >= 0; --i)
            {
                Propellant p = engine.propellants[i];
                p.UpdateConnectedResources(engine.part);
                bool presTank = false;
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
                        bool resourcePres;
                        tank.pressurizedFuels.TryGetValue(r.resourceName, out resourcePres);
                        presTank |= resourcePres || tank.highlyPressurized;
                    }
                }
                tanksHighlyPressurized &= presTank; // i.e. if no tank, set false.
            }
        }
        public void SetThrustAxis(Vector3 thrustAxis)
        {
            if (thrustAxis != Vector3.zero)
                rotationFromPart = Quaternion.FromToRotation(thrustAxis, Vector3.up);
            else
                rotationFromPart = Quaternion.identity;
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

            if (engine.part.rb != null)
                angularVelocity = engine.transform.InverseTransformDirection(engine.part.rb.angularVelocity);
            else
                angularVelocity = engine.transform.InverseTransformDirection(angVel);

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
        public void SetUllageEnabled(bool enabled)
        {
            ullageEnabled = enabled;
        }
        public void SetUllageStability(double newStability)
        {
            ullageSim.SetPropellantStability(newStability);
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
        public bool EditorPressurized()
        {
            SetTanks();

            return tanksHighlyPressurized;
        }
        public void SetModule(UllageModule newModule)
        {
            module = newModule;
        }
        #endregion
    }
}
