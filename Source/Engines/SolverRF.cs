using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using KSP;
using SolverEngines;

namespace RealFuels
{
    public class SolverRF : EngineSolver
    {
        // engine params
        private FloatCurve atmosphereCurve = null, atmCurve = null, velCurve = null, atmCurveIsp = null, velCurveIsp = null;
        private double minFlow, maxFlow, maxFlowRecip, thrustRatio = 1d, throttleResponseRate, machLimit, machMult;
        private double flowMultMin, flowMultCap, flowMultCapSharpness;
        private bool combusting = true;
        private double varyThrust = 0d;
        private bool pressure = true, ullage = true, ignited = false, disableUnderwater;
        private double scale = 1d; // scale for tweakscale

        private float seed = 0f;

        // temperature
        private double chamberTemp, chamberNominalTemp, chamberNominalTemp_recip, partTemperature = 288d;

        // fx
        private float fxPower;
        private float fxThrottle;

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
            FloatCurve nAtmCurveIsp,
            FloatCurve nVelCurveIsp,
            bool nDisableUnderwater,
            double nThrottleResponseRate,
            double nChamberNominalTemp,
            double nMachLimit,
            double nMachMult,
            double nFlowMultMin,
            double nFlowMultCap,
            double nFlowMultSharp,
            double nVaryThrust,
            float nSeed)
        {
            minFlow = nMinFlow * 1000d; // to kg
            maxFlow = nMaxFlow * 1000d;
            maxFlowRecip = 1d / maxFlow;
            atmosphereCurve = nAtmosphereCurve;
            atmCurve = nAtmCurve;
            velCurve = nVelCurve;
            atmCurveIsp = nAtmCurveIsp;
            velCurveIsp = nVelCurveIsp;
            disableUnderwater = nDisableUnderwater;
            throttleResponseRate = nThrottleResponseRate;
            chamberTemp = 288d;
            chamberNominalTemp = nChamberNominalTemp;
            chamberNominalTemp_recip = 1d / chamberNominalTemp;
            machLimit = nMachLimit;
            machMult = nMachMult;
            flowMultMin = nFlowMultMin;
            flowMultCap = nFlowMultCap;
            flowMultCapSharpness = nFlowMultSharp;
            varyThrust = nVaryThrust;
            seed = nSeed;

            // falloff at > sea level pressure.
            if (atmosphereCurve.Curve.keys.Length == 2 && atmosphereCurve.Curve.keys[0].value != atmosphereCurve.Curve.keys[1].value)
            {
                Keyframe k0 = atmosphereCurve.Curve.keys[0];
                Keyframe k1 = atmosphereCurve.Curve.keys[1];
                if(k0.time > k1.time)
                {
                    Keyframe t = k0;
                    k0 = k1;
                    k1 = t;
                }
                float minIsp = 0.0001f;
                float invSlope = (k1.time - k0.time) / (k0.value - k1.value);
                float maxP = k1.time + (k1.value - minIsp) * invSlope;

                atmosphereCurve = new FloatCurve();
                atmosphereCurve.Add(k0.time, k0.value, k0.inTangent, k0.outTangent);
                atmosphereCurve.Add(k1.time, k1.value, k1.inTangent, k1.outTangent);
                atmosphereCurve.Add(maxP, minIsp);
            }
        }

        public void SetPartTemp(double tmp)
        {
            partTemperature = tmp;
        }

        public void SetEngineStatus(bool pressureOK, bool ullageOK, bool nIgnited)
        {
            pressure = pressureOK;
            ullage = ullageOK;
            ignited = nIgnited;
        }
        public void SetScale(double newScale)
        {
            scale = newScale;
        }

        public override void CalculatePerformance(double airRatio, double commandedThrottle, double flowMult, double ispMult)
        {
            // set base bits
            base.CalculatePerformance(airRatio, commandedThrottle, flowMult, ispMult);
            M0 = mach;

            // Calculate Isp (before the shutdown check, so it displays even then)
            Isp = atmosphereCurve.Evaluate((float)(p0 * 0.001d * PhysicsGlobals.KpaToAtmospheres)) * ispMult;

            // if we're not combusting, don't combust and start cooling off
            combusting = running && ignited;
            statusString = "Nominal";

            // ullage check first, overwrite if bad pressure or no propellants
            if (!ullage)
            {
                combusting = false;
                statusString = "Vapor in feed line";
            }
            
            // check fuel flow fraction
            if (ffFraction <= 0d)
            {
                combusting = false;
                statusString = "No propellants";
            }
            // check pressure
            if (!pressure)
            {
                combusting = false;
                statusString = "Lack of pressure";
            }

            if (disableUnderwater && underwater)
            {
                combusting = false;
                statusString = "Underwater";
            }

            // check flow mult
            double fuelFlowMult = FlowMult();
            if (fuelFlowMult < flowMultMin)
            {
                combusting = false;
                statusString = "Airflow outside specs";
            }

            if (!combusting || commandedThrottle <= 0d)
            {
                combusting = false; // for throttle FX
                double declinePow = Math.Pow(tempDeclineRate, TimeWarp.fixedDeltaTime);
                chamberTemp = Math.Max(Math.Max(t0, partTemperature), chamberTemp * declinePow);
                fxPower = 0f;
            }
            else
            {

                // get current flow, and thus thrust.
                fuelFlow = scale * flowMult * maxFlow * commandedThrottle * thrustRatio;
                
                if (varyThrust > 0d && fuelFlow > 0d && HighLogic.LoadedSceneIsFlight)
                    fuelFlow *= (1d + (Mathf.PerlinNoise(Time.time, 0f) * 2d - 1d) * varyThrust);

                fxPower = (float)(fuelFlow * maxFlowRecip * ispMult); // FX is proportional to fuel flow and Isp mult.
                
                // apply fuel flow multiplier
                double ffMult = fuelFlow * fuelFlowMult;
                fuelFlow = ffMult;

                if(atmCurveIsp != null)
                    Isp *= atmCurveIsp.Evaluate((float)(rho * (1d / 1.225d)));
                if (velCurveIsp != null)
                    Isp *= velCurveIsp.Evaluate((float)mach);

                double exhaustVelocity = Isp * 9.80665d;
                SFC = 3600d / Isp;
                
                thrust = ffMult * exhaustVelocity; // either way, thrust is base * mult * EV

                // Calculate chamber temperature as ratio
                double desiredTempRatio = Math.Max(tempMin, fxPower);
                double machTemp = MachTemp() * 0.05d;
                desiredTempRatio = desiredTempRatio * (1d + machTemp) + machTemp;

                // set temp based on desired
                double desiredTemp = desiredTempRatio * chamberNominalTemp;
                if (Math.Abs(desiredTemp - chamberTemp) < 1d)
                    chamberTemp = desiredTemp;
                else
                {
                    double lerpVal = UtilMath.Clamp01(tempLerpRate * TimeWarp.fixedDeltaTime);
                    chamberTemp = UtilMath.LerpUnclamped(chamberTemp, desiredTemp, lerpVal);
                }
            }
            fxThrottle = combusting ? (float)throttle : 0f;
        }
        // engine status
        public override double GetEngineTemp() { return chamberTemp; }
        public override double GetArea() { return 0d; }
        // FX etc
        public override double GetEmissive() { return UtilMath.Clamp01(chamberTemp * chamberNominalTemp_recip); }
        public override float GetFXPower() { return fxPower; }
        public override float GetFXRunning() { return fxPower; }
        public override float GetFXThrottle() { return fxThrottle; }
        public override float GetFXSpool() { return (float)(UtilMath.Clamp01(chamberTemp * chamberNominalTemp_recip)); }
        public override bool GetRunning() { return combusting; }
        
        // new methods
        public void UpdateThrustRatio(double r) { thrustRatio = r; }

        // helpers
        protected double FlowMult()
        {
            double flowMult = 1d;
            if (atmCurve != null)
                flowMult *= atmCurve.Evaluate((float)(rho * (1d / 1.225d)));

            if (velCurve != null)
                flowMult *= velCurve.Evaluate((float)mach);

            if (flowMult > flowMultCap)
            {
                double extra = flowMult - flowMultCap;
                flowMult = flowMultCap + extra / (flowMultCapSharpness + extra / flowMultCap);
            }

            return Math.Max(flowMult, 1e-5);
        }

        protected double MachTemp()
        {
            if (mach < machLimit)
                return 0d;
            return (mach - machLimit) * machMult;
        }
    }
}
