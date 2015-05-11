using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using KSP;

namespace RealFuels
{
    public class SolverRF : EngineSolver
    {
        // engine params
        private FloatCurve atmosphereCurve = null, atmCurve = null, velCurve = null;
        private double minFlow, maxFlow, thrustRatio = 1d, throttleResponseRate, machLimit, machMult;

        // temperature
        private double chamberTemp, chamberNominalTemp, chamberNominalTemp_recip;

        // FIXME hack values
        private double tempDeclineRate = 0.95d;
        private double tempMin = 0.5d;
        private double tempLerpRate = 1.0d;

        public void InitializeOverallEngineData(
            double nMinFlow, 
            double nMaxFlow, 
            FloatCurve nAtmosphereCurve, 
            FloatCurve nAtmCurve, 
            FloatCurve nVelCurve,
            double nThrottleResponseRate,
            double nChamberNominalTemp,
            double nMachLimit,
            double nMachMult)
        {
            minFlow = nMinFlow * 1000d; // to kg
            maxFlow = nMaxFlow * 1000d;
            atmosphereCurve = nAtmosphereCurve;
            atmCurve = nAtmCurve;
            velCurve = nVelCurve;
            throttleResponseRate = nThrottleResponseRate;
            chamberTemp = 288d;
            chamberNominalTemp = nChamberNominalTemp;
            chamberNominalTemp_recip = 1d / chamberNominalTemp;
            machLimit = nMachLimit;
            machMult = nMachMult;
        }

        public override void CalculatePerformance(double airRatio, double commandedThrottle, double flowMult, double ispMult)
        {
            // set base bits
            base.CalculatePerformance(airRatio, commandedThrottle, flowMult, ispMult);

            // Calculate Isp (before the shutdown check, so it displays even then)
            Isp = atmosphereCurve.Evaluate((float)(p0 * 0.001d * PhysicsGlobals.KpaToAtmospheres)) * ispMult;

            // if we're not combusting, don't combust and start cooling off
            bool shutdown = !running;
            statusString = "Nominal";
            if (ffFraction <= 0d)
            {
                shutdown = true;
                statusString = "No propellants";
            }
            if (shutdown || commandedThrottle <= 0d)
            {
                double declinePow = Math.Pow(tempDeclineRate, TimeWarp.fixedDeltaTime);
                chamberTemp = Math.Max(t0, chamberTemp * declinePow);
                return;
            }

            // get current flow, and thus thrust.
            double fuelFlowMult = FlowMult();
            if (fuelFlowMult < 0.05d)
                fuelFlow = 0d;
            else
                fuelFlow = flowMult * UtilMath.LerpUnclamped(minFlow, maxFlow, commandedThrottle) * fuelFlowMult;

            double exhaustVelocity = Isp * 9.80665d;
            thrust = fuelFlow * exhaustVelocity;

            // Calculate chamber temperature as ratio
            double desiredTempRatio = Math.Max(tempMin, commandedThrottle);
            double machTemp = MachTemp() * 0.05d;
            desiredTempRatio = desiredTempRatio * (1d + machTemp) + machTemp;

            // set based on desired
            double desiredTemp = desiredTempRatio * chamberNominalTemp;
            if (Math.Abs(desiredTemp - chamberTemp) < 1d)
                chamberTemp = desiredTemp;
            else
            {
                double lerpVal = Math.Min(1d, tempLerpRate * TimeWarp.fixedDeltaTime);
                chamberTemp = UtilMath.LerpUnclamped(chamberTemp, desiredTemp, lerpVal);
            }
        }
        public override double GetEngineTemp() { return chamberTemp; }
        public override double GetArea() { return 0d; }
        public override double GetEmissive()
        {
            return chamberTemp * chamberNominalTemp_recip;
        }
        public void UpdateThrustRatio(double r) { thrustRatio = r; }

        // helpers
        protected double FlowMult()
        {
            double flowMult = 1d;
            if ((object)atmCurve != null)
                flowMult *= atmCurve.Evaluate((float)(rho / 1.225d));

            if ((object)velCurve != null)
                flowMult *= velCurve.Evaluate((float)mach);
            
            flowMult = Math.Max(flowMult, 1e-5);
            
            return flowMult;
        }

        protected double MachTemp()
        {
            if (mach < machLimit)
                return 0d;
            return (mach - machLimit) * machMult;
        }
    }
}
