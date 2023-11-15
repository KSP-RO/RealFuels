using System;
using System.Collections.Generic;
using UnityEngine;

namespace RealFuels.Ullage
{
    public class UllageSet : IConfigNode
    {
        #region Fields
        private readonly ModuleEnginesRF engine;
        private readonly List<Part> tanks = new List<Part>();
        private readonly Dictionary<Part, Tanks.ModuleFuelTanks> rfTanks = new Dictionary<Part, Tanks.ModuleFuelTanks>();
        private readonly UllageSimulator ullageSim;
        private UllageModule module;

        Vector3d acceleration, angularVelocity;
        double fuelRatio;

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
            module = engine.vessel?.GetComponent<UllageModule>();

            // set engine fields
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
            foreach (Propellant p in engine.propellants)
            {
                List<PartResource> resources = Utilities.FindResources(engine.part, p);
                double propAmt = 0d, propMax = 0d;
                bool presTank = false;
                foreach (PartResource r in resources)
                {
                    propAmt += r.amount;
                    propMax += r.maxAmount;

                    Part part = r.part;
                    if (!rfTanks.TryGetValue(part, out Tanks.ModuleFuelTanks tank))
                    {
                        tanks.Add(part);
                        for (int k = part.Modules.Count - 1; k >= 0; --k)
                        {
                            if (part.Modules[k] is Tanks.ModuleFuelTanks)
                            {
                                tank = part.Modules[k] as Tanks.ModuleFuelTanks;
                                rfTanks[part] = tank;
                            }
                        }
                    }
                    if (tank != null)
                    {
                        // noPresTank will stay true only if no pressurized tank found.
                        tank.pressurizedFuels.TryGetValue(r.resourceName, out bool resourcePres);
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
                ullageSim.Load(node.GetNode("UllageSim"));
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
                foreach (var p in engine.propellants)
                {
                    double tmp = p.totalResourceAvailable / p.totalResourceCapacity;
                    if(!double.IsNaN(tmp)) // Ordinarily we'd set to 0 if capacity = 0, but if so engine will flame out, so we just toss the result.
                        fuelRatio = Math.Min(fuelRatio, tmp);
                }
                // do pressure-fed tests?
            }
            if(ullageEnabled && RFSettings.Instance.simulateUllage)
                ullageSim.Update(acceleration, angularVelocity, timeDelta, ventingAcc, fuelRatio);
        }
        public string Engine() => engine?.part?.partInfo?.title ?? "No valid engine";
        public void SetUllageEnabled(bool enabled) => ullageEnabled = enabled;
        public void SetUllageStability(double newStability) => ullageSim.SetPropellantStability(newStability);
        public string GetUllageState(out Color col) => ullageSim.GetPropellantStatus(out col);
        public double GetUllageStability() => ullageSim.GetPropellantStability();
        public double GetUllageProbability() => ullageSim.GetPropellantProbability();
        public bool PressureOK() => !engine.pressureFed || tanksHighlyPressurized;
        public bool EditorPressurized()
        {
            SetTanks();
            return tanksHighlyPressurized;
        }
        public void SetModule(UllageModule newModule) => module = newModule;
        #endregion
    }
}
