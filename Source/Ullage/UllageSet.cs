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
        double fuelRatio;

        bool pressureFed = false;
        bool tanksHighlyPressurized = false;
        bool ullageEnabled = true;
        Quaternion rotationFromPart = Quaternion.identity;
        #endregion

        #region Setup
        public UllageSet(ModuleEnginesRF eng)
        {
            MonoBehaviour.print("*U* Ullage constructor called on " + eng.part.name);
            engine = eng;
            ullageSim = new UllageSimulator(engine.part.name);
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
            fuelRatio = 1d;

            // iterate through all propellants.
            for (int i = engine.propellants.Count - 1; i >= 0; --i)
            {
                Propellant p = engine.propellants[i];
                List<PartResource> resources = Utilities.FindResources(engine.part, p);
                double propAmt = 0d, propMax = 0d;
                bool presTank = false;
                for (int j = resources.Count - 1; j >= 0; --j)
                {
                    PartResource r = resources[j];
                    propAmt += r.amount;
                    propMax += r.maxAmount;

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

                // set ratio
                fuelRatio = Math.Min(fuelRatio, propAmt / propMax);
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
            if (!HighLogic.LoadedSceneIsEditor && node.HasNode("UllageSim"))
            {
#if DEBUG
                MonoBehaviour.print("*U* Ullage load called on " + engine.part.name);
#endif
                ullageSim.Load(node.GetNode("UllageSim"));
            }
        }
        public void Save(ConfigNode node)
        {
            if (ullageSim != null && !HighLogic.LoadedSceneIsEditor)
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
            
            fuelRatio = 1d;
            if(HighLogic.LoadedSceneIsFlight && engine.EngineIgnited)
            {
                int pCount = engine.propellants.Count;
                for(int i = pCount - 1; i >= 0; --i)
                {
                    Propellant p = engine.propellants[i];
                    double tmp = p.totalResourceAvailable / p.totalResourceCapacity;
                    if(!double.IsNaN(tmp)) // Ordinarily we'd set to 0 if capacity = 0, but if so engine will flame out, so we just toss the result.
                        fuelRatio = Math.Min(fuelRatio, tmp);
                }
                // do pressure-fed tests?
            }
            if(ullageEnabled && RFSettings.Instance.simulateUllage)
                ullageSim.Update(acceleration, angularVelocity, timeDelta, ventingAcc, fuelRatio);
        }
        public string Engine()
        {
            if (engine != null)
                return engine.part.partInfo.title;
            return "No valid engine";
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
