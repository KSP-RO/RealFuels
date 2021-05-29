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
        // Variance tuning
        double VarianceBase = 0.6d;
        double VarianceRun = 0.3d;
        double VarianceDuring = 0.2d;

        // engine params
        private FloatCurve atmosphereCurve = null, atmCurve = null, velCurve = null, atmCurveIsp = null, velCurveIsp = null;
        private double minFlow, maxFlow, maxFlowRecip, thrustRatio = 1d, throttleResponseRate, machLimit, machMult;
        private double flowMultMin, flowMultCap, flowMultCapSharpness;
        private bool combusting = true;
        private double varyFlow = 0d;
        private double varyIsp = 0d;
        private double varyMR = 0d;
        private double baseVaryFlow = 0d;
        private double baseVaryIsp = 0d;
        private double baseVaryMR = 0d;
        private double runVaryFlow = 0d;
        private double runVaryIsp = 0d;
        private double runVaryMR = 0d;
        private System.Random seededRandom;
        private bool pressure = true, ullage = true, disableUnderwater;
        private double scale = 1d; // scale for tweakscale

        private bool wasCombusting = false;

        private float timeOffset = 0;

        // temperature
        private double chamberTemp, chamberNominalTemp, chamberNominalTemp_recip, partTemperature = 288d;

        // fx
        private float fxPower;
        private float fxThrottle;

        // FIXME hack values
        private double tempDeclineRate = 0.95d;
        private double tempMin = 0.5d;
        private double tempLerpRate = 1.0d;

        // Stored mixture ratio variance, multiplier to O/F
        private double mixtureRatioVariance = 0d;

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
            double nVaryFlow,
            double nVaryIsp,
            double nVaryMR,
            bool solid,
            int nSeed)
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
            varyFlow = nVaryFlow;
            varyIsp = nVaryIsp;
            varyMR = nVaryMR;
            timeOffset = (nSeed % 1024);

            if (solid)
            {
                VarianceBase = 0.3d;
                VarianceRun = 0.4d;
                VarianceDuring = 0.4d;
            }

            seededRandom = new System.Random(nSeed);
            double vFlow, vIsp, vMR;
            GetVariances(true, out vFlow, out vMR, out vIsp);
            baseVaryFlow = VarianceBase * varyFlow * vFlow;
            baseVaryIsp = VarianceBase * varyIsp * vIsp;
            baseVaryMR = VarianceBase * varyMR * vMR;


            // falloff at > sea level pressure.
            if (atmosphereCurve.Curve.keys.Length == 2 && atmosphereCurve.Curve.keys[0].value != atmosphereCurve.Curve.keys[1].value)
            {
                Keyframe k0 = atmosphereCurve.Curve.keys[0];
                Keyframe k1 = atmosphereCurve.Curve.keys[1];
                if (k0.time > k1.time)
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

        public void SetPropellantStatus(bool pressureOK, bool ullageOK)
        {
            pressure = pressureOK;
            ullage = ullageOK;
        }
        public void SetScale(double newScale)
        {
            scale = newScale;
        }

        public double MixtureRatioVariance()
        {
            return mixtureRatioVariance;
        }

        public override void CalculatePerformance(double airRatio, double commandedThrottle, double flowMult, double ispMult)
        {
            mixtureRatioVariance = 0d;

            // set base bits
            base.CalculatePerformance(airRatio, commandedThrottle, flowMult, ispMult);
            M0 = mach;

            // Calculate Isp (before the shutdown check, so it displays even then)
            Isp = atmosphereCurve.Evaluate((float)(p0 * 0.001d * PhysicsGlobals.KpaToAtmospheres)) * ispMult;

            // if we're not combusting, don't combust and start cooling off
            combusting = running;
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
                statusString = "Flameout";
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

            // FIXME handle engine spinning down, non-instant shutoff.
            if (commandedThrottle <= 0d)
                combusting = false;

            if (!wasCombusting && combusting)
            {
                // Reset run-to-run variances
                double vFlow, vIsp, vMR;
                GetVariances(false, out vFlow, out vMR, out vIsp);
                runVaryFlow = baseVaryFlow + VarianceRun * varyFlow * vFlow;
                runVaryIsp = baseVaryIsp + VarianceRun * varyIsp * vIsp;
                runVaryMR = baseVaryMR + VarianceRun * varyMR * vMR;
            }

            if (!combusting)
            {
                double declinePow = Math.Pow(tempDeclineRate, TimeWarp.fixedDeltaTime);
                // removed t0 from next calculation; under some circumstances t0 can spike during staging/decoupling resulting in engine part destruction even on an unfired engine.
                chamberTemp = Math.Max(partTemperature, chamberTemp * declinePow);
                fxPower = 0f;
            }
            else
            {

                // get current flow, and thus thrust.
                fuelFlow = scale * flowMult * maxFlow * commandedThrottle * thrustRatio;
                double perlin = 0d;
                if (HighLogic.LoadedSceneIsFlight && (varyFlow > 0 || varyIsp > 0))
                {
                    
                }

                if (HighLogic.LoadedSceneIsFlight && fuelFlow > 0d)
                {
                    mixtureRatioVariance = runVaryMR;

                    if (varyFlow > 0d || varyIsp > 0d || varyMR > 0d)
                    {
                        perlin = Mathf.PerlinNoise(Time.time * 0.5f, timeOffset) * 2d - 1d;
                    }
                    if (varyMR > 0d)
                    {
                        mixtureRatioVariance += (0.5d + Math.Abs(perlin) * 0.5d) * (Mathf.PerlinNoise(Time.time * 0.5f, -timeOffset) * 2d - 1d) * varyMR * VarianceDuring;
                    }
                    if (varyFlow > 0d)
                    {
                        fuelFlow *= (1d + runVaryFlow) * (1d + perlin * varyFlow * VarianceDuring);
                    }
                }

                // FIXME fuel flow is actually wrong, since mixture ratio varies now. Either need to fix MR for constant flow,
                // or fix fuel flow here in light of MR. But it's mostly just a visual bug, since the variation will be fine in most cases.

                // apply fuel flow multiplier
                double ffMult = fuelFlow * fuelFlowMult;
                fuelFlow = ffMult;

                double ispOtherMult = 1d;
                if (atmCurveIsp != null)
                    ispOtherMult *= atmCurveIsp.Evaluate((float)(rho * (1d / 1.225d)));
                if (velCurveIsp != null)
                    ispOtherMult *= velCurveIsp.Evaluate((float)mach);

                if (HighLogic.LoadedSceneIsFlight && varyIsp > 0d && fuelFlow > 0d)
                {
                    ispOtherMult *= (1d + runVaryIsp) * (1d + perlin * varyIsp * VarianceDuring);
                }

                Isp *= ispOtherMult;

                fxPower = (float)(fuelFlow * maxFlowRecip * ispMult * ispOtherMult); // FX is proportional to fuel flow and Isp mult.

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

        protected double GetRandom(bool useSeed)
        {
            if (useSeed)
                return seededRandom.NextDouble() * 2d - 1d;
            else
                return UnityEngine.Random.Range(-1f, 1f);
        }

        protected void GetVariances(bool useSeed, out double varianceFlow, out double varianceMR, out double varianceIsp)
        {
            varianceFlow = GetNormal(useSeed, 3d);
            varianceMR = GetNormal(useSeed, 3d) * (0.5d + Math.Abs(varianceFlow) * 0.5d);
            // MR probably has an effect on Isp but it's hard to say what. When running fuel-rich, increasing
            // oxidizer might raise Isp? And vice versa for ox-rich. So for now ignore MR.
            varianceIsp = (varianceFlow * 0.8d + GetNormal(useSeed, 3d) * 0.2d);
        }

        protected double GetNormal(bool useSeed, double stdDevClamp)
        {
            double u, v, S;

            do
            {
                u = GetRandom(useSeed);
                v = GetRandom(useSeed);
                S = u * u + v * v;
            }
            while (S >= 1d);

            double fac = Math.Sqrt(-2.0 * Math.Log(S) / S);
            double retVal = u * fac;
            if (stdDevClamp > 0)
                retVal = Math.Min(stdDevClamp, Math.Abs(retVal)) * Math.Sign(retVal);
            return retVal;
        }
    }
}
