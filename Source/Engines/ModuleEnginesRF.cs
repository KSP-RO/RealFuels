using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;
using KSP;

namespace RealFuels
{
    public class ModuleEnginesRF : ModuleEnginesSolver
    {
        #region Fields
        [KSPField]
        public double chamberNominalTemp;
        [KSPField]
        public double extHeatkW = 0d;

        [KSPField]
        public bool usesAir = false;

        #region Thrust Curve
        public bool useThrustCurve = false;
        [KSPField]
        public FloatCurve thrustCurve = null;
        [KSPField]
        public string curveResource = "";

        protected int curveProp = -1;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "% Rated Thrust", guiUnits = "%", guiFormat = "F3")]
        public float thrustCurveDisplay = 100f;
        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "Fuel Ratio", guiUnits = "%", guiFormat = "F3")]
        public float thrustCurveRatio = 1f;
        #endregion


        protected bool instantThrottle = false;
        protected double throttleResponseRate;
        #endregion

        #region Overrides
        public override void CreateEngine()
        {
            engineSolver = new SolverRF();
            if(!useAtmCurve)
                atmCurve = null;
            if(!useVelCurve)
                velCurve = null;
            if (!useThrustCurve)
                thrustCurve = null;
            
            // FIXME quick temp hax
            if (useAtmCurve)
            {
                if (maxEngineTemp == 0)
                    maxEngineTemp = 2000d;
                if (chamberNominalTemp == 0)
                    chamberNominalTemp = 950d;
            }
            else
            {
                if (maxEngineTemp == 0)
                    maxEngineTemp = 3600d;
                if (chamberNominalTemp == 0)
                    chamberNominalTemp = 3500d;
            } 

            (engineSolver as SolverRF).InitializeOverallEngineData(
                minFuelFlow,
                maxFuelFlow,
                atmosphereCurve,
                atmCurve,
                velCurve,
                throttleResponseRate,
                chamberNominalTemp,
                machLimit,
                machHeatMult);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            int pCount = propellants.Count;
            // thrust curve
            useThrustCurve = false;
            Fields["thrustCurveDisplay"].guiActive = false;
            if (node.HasNode("thrustCurve") && node.HasValue("curveResource"))
            {
                curveResource = node.GetValue("curveResource");
                if (curveResource != "")
                {
                    double ratio = 0.0;
                    for (int i = 0; i < pCount; ++i)
                    {
                        if (propellants[i].name.Equals(curveResource))
                        {
                            curveProp = i;
                            ratio = propellants[i].totalResourceAvailable / propellants[i].totalResourceCapacity;
                            break;
                        }
                    }
                    if (curveProp != -1)
                    {
                        useThrustCurve = true;
                        Fields["thrustCurveDisplay"].guiActive = true;
                    }
                }
            }

            // Set from propellants
            bool instantThrottle = true;
            for (int i = 0; i < pCount; ++i)
            {
                if (RFSettings.Instance.instantThrottleProps.Contains(propellants[i].name))
                {
                    instantThrottle = false;
                }
                // any other stuff
            }

            // FIXME calculating throttle change rate
            if(!instantThrottle)
                throttleResponseRate = 10d / Math.Sqrt(Math.Sqrt(part.mass * maxThrust));

            // set fields
            Fields["Inlet"].guiActive = usesAir;
        }
        public override void UpdateThrottle()
        {
            if (instantThrottle)
                currentThrottle = (float)(requestedThrottle * thrustPercentage * 0.01);
            else
            {
                
                double requiredThrottle = requestedThrottle * thrustPercentage * 0.01d;
                double deltaT = TimeWarp.fixedDeltaTime;

                double d = requiredThrottle - currentThrottle;
                if (Math.Abs(d) > throttleResponseRate * deltaT)
                {
                    double sign = 1d;
                    if (d < 0)
                        sign = -1d;
                    currentThrottle += (float)(sign * throttleResponseRate * deltaT);
                }
                else
                    currentThrottle = (float)requiredThrottle;
            }
            base.UpdateThrottle();
        }

        public override void UpdateFlightCondition(double altitude, double vel, double pressure, double temperature, double rho, double mach, bool oxygen)
        {
            // do thrust curve
            if (useThrustCurve && HighLogic.LoadedSceneIsFlight)
            {
                thrustCurveRatio = (float)((propellants[curveProp].totalResourceAvailable / propellants[curveProp].totalResourceCapacity));
                thrustCurveDisplay = thrustCurve.Evaluate(thrustCurveRatio);
                (engineSolver as SolverRF).UpdateThrustRatio(thrustCurveDisplay);
                thrustCurveDisplay *= 100f;
            }

            // do heat
            double tMass = part.mass * 800d;
            if (part.thermalMass > 0)
                tMass = part.thermalMass;
            heatProduction = (float)(extHeatkW / PhysicsGlobals.InternalHeatProductionFactor / tMass);

            // run base method
            base.UpdateFlightCondition(altitude, vel, pressure, temperature, rho, mach, oxygen);
        }
        #endregion

        #region Info
        protected string ThrottleString()
        {
            string output = "";
            double throttleP = 0d;
            if(minFuelFlow > 0d)
                throttleP = minFuelFlow / maxFuelFlow * 100d;
            if (minFuelFlow == maxFuelFlow)
                throttleP = 100d;
            if (!throttleLocked)
            {
                if (throttleP > 0d && throttleP < 100d)
                    output += ", " + throttleP.ToString("N0") + "% min throttle";
                else if(throttleP == 100d)
                    output += ", unthrottleable";
            }
            else
                output += ", throttle locked";

            return output;
        }
        protected string GetThrustInfo()
        {
            string output = "";
            //if (engineSolver == null || !(engineSolver is SolverRF))
                CreateEngine();

            // get stats
            double pressure = 101.325d, temperature = 288.15d;
            if (Planetarium.fetch != null)
            {
                CelestialBody home = Planetarium.fetch.Home;
                if (home != null)
                {
                    pressure = home.GetPressure(0d);
                    temperature = home.GetTemperature(0d);
                }
            }

            currentThrottle = 1f;
            OverallTPR = 1d;
            lastPropellantFraction = 1d;
            bool oldE = EngineIgnited;
            EngineIgnited = true;
            (engineSolver as SolverRF).UpdateThrustRatio(1d);
            UpdateFlightCondition(0d, 0d, pressure, temperature, 1.225d, 0d, true);
            double thrustASL = (engineSolver.GetThrust() * 0.001d);

            if (atmChangeFlow) // If it's a jet
            {
                output += "<b>Static Thrust: </b>" + (thrustASL).ToString("0.0##") + " kN" + ThrottleString();
                if (useVelCurve) // if thrust changes with mach
                {
                    float vMin, vMax, tMin, tMax;
                    velCurve.FindMinMaxValue(out vMin, out vMax, out tMin, out tMax); // get the max mult, and thus report maximum thrust possible.
                    output += "\n<b>Max. Thrust: </b>" + (thrustASL* vMax).ToString("0.0##") + " kN Mach " + tMax.ToString("0.#");
                }
            }
            else
            {
                if (Planetarium.fetch != null)
                {
                    CelestialBody home = Planetarium.fetch.Home;
                    if (home != null)
                    {
                        temperature = home.GetTemperature(home.atmosphereDepth + 1d);
                    }
                }
                UpdateFlightCondition(0d, 0d, 0d, temperature, 0d, 0d, true);
                double thrustVac = (engineSolver.GetThrust() * 0.001d);

                if (thrustASL != thrustVac)
                {
                    output += (throttleLocked ? "<b>" : "<b>Max. ") + "Thrust (Vac.): </b>" + (thrustVac).ToString("0.0##") + " kN" + ThrottleString()
                        + "\n" + (throttleLocked ? "<b>" : "<b>Max. ") + "Thrust (ASL): </b>" + (thrustASL).ToString("0.0##") + " kN";
                }
                else
                {
                    output += (throttleLocked ? "<b>" : "<b>Max. ") + "Thrust: </b>" + (thrustVac).ToString("0.0##") + " kN" + ThrottleString();
                }
            }
            output += "\n";
            EngineIgnited = oldE;
            return output;
        }

        public override string GetModuleTitle()
        {
            return "Engine (RealFuels)";
        }
        public override string GetPrimaryField()
        {
            return GetThrustInfo();
        }

        public override string GetInfo()
        {
            string output = GetThrustInfo();

            output += "<b>Engine Isp: </b>" + (atmosphereCurve.Evaluate(1f)).ToString("0.###") + " (ASL) - " + (atmosphereCurve.Evaluate(0f)).ToString("0.###") + " (Vac.)\n";

            output += "\n<b><color=#99ff00ff>Propellants:</color></b>\n";
            Propellant p;
            string pName;
            for (int i = 0; i < propellants.Count; ++i)
            {
                p = propellants[i];
                pName = KSPUtil.PrintModuleName(p.name);

                output += "- <b>" + pName + "</b>: " + getMaxFuelFlow(p).ToString("0.0##") + "/sec. Max.\n";
                output += p.GetFlowModeDescription();
            }
            output += "<b>Flameout under: </b>" + (ignitionThreshold * 100f).ToString("0.#") + "%\n";

            if (!allowShutdown) output += "\n" + "<b><color=orange>Engine cannot be shut down!</color></b>";
            if (!allowRestart) output += "\n" + "<b><color=orange>If shutdown, engine cannot restart.</color></b>";

            currentThrottle = 0f;

            return output;
        }
        #endregion
    }
}
