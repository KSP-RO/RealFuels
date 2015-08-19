using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using KSP;
using SolverEngines;

namespace RealFuels
{
    public class SolverDEV : EngineSolver
    {
        // engine params
        private double minFlow, maxFlow, thrustRatio = 1d;
        private bool combusting = true;
        private double varyThrust = 0d;
        private bool pressure = true, ullage = true, ignited = false;
        private double scale = 1d; // scale for tweakscale


        // temperature
        private double Tcns, chamberNominalTemp, chamberMaxTemp, partTemperature = 288d;
        private double Pcns, chamberNominalPressure, chamberMaxPressure;
        private double Pe, nozzleThroatArea, nozzleNominalExitPressure, nozzleExitArea, nozzleExpansionRatio;
        private double R;
        private double pR;
        private double fuelFraction;
        // fx
        private float fxPower;
        private float fxThrottle;


        // FIXME hack values
        private const double tempMin = 0.8d;
        private const double tempLerpRate = 1.0d;

        public float overTempRatio=1, overPressureRatio=1;
        public double Cstar { get; private set; } = -1;
        public double Ct { get; private set; } = -1;
        public string chamberType { get; private set; }="";
        public string nozzleType { get; private set; } = "";
        private float stability = 1;
        public float Stability {
            get {
                if (useStablity) return stability; else return 1;
            }
            private set { stability = value; }
        }
        public bool useStablity = true;
        protected float heatMult = 1;
        public enum isFailed : short
        {
            PART_TEMP = 1 << 8,
            CHAMBER_TEMP = 1 << 7,
            GAMMA = 1 << 6,
            FUELFLOW = 1 << 5,
            OVPR = 1 << 4,
            OVHT = 1 << 3,
            THRUST = 1 << 2,
            IGNITION = 1,
            NONE = 0
        }
        public isFailed failed = isFailed.NONE;
        public void InitializeOverallEngineData(
            double mTcns,
            double mPcns,
            double mmaxTc,
            double mmaxPc,
            double mPe,
            double mAt,
            double mFF,
            double mR,
            double mmaxFuelFlow,
            double mminFuelFlow,
            double nVaryThrust,
            string mnozzleType,
            string mchamberType)//For Liquid+Bipropellant and deLaval now
        {
            double gamma_t = CalculateGamma(mTcns, mFF);
            double inv_gamma_t = 1 / gamma_t;
            double inv_gamma_tm1 = 1 / (gamma_t - 1);
            chamberNominalTemp = mTcns;
            chamberNominalPressure = mPcns;
            chamberMaxTemp = mmaxTc;
            chamberMaxPressure = mmaxPc;
            fuelFraction = mFF;
            nozzleThroatArea = mAt * scale;
            nozzleNominalExitPressure = mPe;
            R = mR;
            pR = chamberNominalPressure / nozzleNominalExitPressure;
            nozzleExpansionRatio =
                (Math.Pow(2d / (gamma_t + 1d), (inv_gamma_tm1)) * Math.Pow(pR, inv_gamma_t))
                    /
                Math.Sqrt(
                    (gamma_t + 1) * inv_gamma_tm1 * (1 - Math.Pow(1 / pR, gamma_t / inv_gamma_t))
                );
            nozzleExitArea = nozzleThroatArea * nozzleExpansionRatio;
            maxFlow = mmaxFuelFlow;
            minFlow = mminFuelFlow;

            varyThrust = nVaryThrust;
            chamberType = mchamberType;
            nozzleType = mnozzleType;
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

        private void UpdateTc()
        {
            // Calculate chamber temperature as ratio
            double desiredTempRatio = Math.Max(tempMin, 1);/*MAGIC*/

            // set temp based on desired
            double desiredTemp = desiredTempRatio * chamberNominalTemp;
            if (!HighLogic.LoadedSceneIsFlight) {
                Tcns = desiredTemp;
                return;
            }
            if (!combusting) desiredTemp = t0; else if (varyThrust > 0d && fuelFlow > 0d && HighLogic.LoadedSceneIsFlight) {
                desiredTemp *= (1d + (Mathf.PerlinNoise(Time.time, 196883f) * 2d - 1d) * (varyThrust+ (overPressureRatio-1) * 10));/*MAGIC*/
            }
            if (Math.Abs(desiredTemp - Tcns) < 1d)
                Tcns = desiredTemp;
            else {
                double lerpVal = UtilMath.Clamp01(tempLerpRate * TimeWarp.fixedDeltaTime);
                Tcns = UtilMath.LerpUnclamped(Tcns, desiredTemp, lerpVal);
            }
            if ((failed & isFailed.CHAMBER_TEMP) != isFailed.NONE) {
                Tcns /= Stability;
            }
            //if (GetRunning()) {
            //    designTemp = designChamberTemp;
            //}
            //if (!HighLogic.LoadedSceneIsFlight) {
            //    Tcns = designChamberTemp;
            //    return;
            //}
            //double d = designTemp - Tcns;
            //double thisTick = 10 * d * TimeWarp.fixedDeltaTime;/*MAGIC*/
            //if (thisTick > 0) thisTick *= Math.Min(0.2, fuelFlow / cycleMaxMassFlow);
            //if (Math.Abs(d) > 1000 * TimeWarp.fixedDeltaTime) {
            //    Tcns += thisTick;
            //} else {
            //    Tcns = designTemp;
            //}

        }
        public override void CalculatePerformance(double airRatio, double commandedThrottle, double flowMult, double ispMult)
        {
            if (!HighLogic.LoadedSceneIsFlight) {
                Tcns = chamberNominalTemp;
            }
            // set base bits
            base.CalculatePerformance(airRatio, commandedThrottle, flowMult, ispMult);
            M0 = mach;
            gamma_c = CalculateGamma(Tcns, fuelFraction);
            if ((failed & isFailed.GAMMA) != isFailed.NONE) {
                gamma_c *= Stability;
                gamma_c = gamma_c < 1 ? 1 : gamma_c;
            }
            inv_gamma_c = 1 / gamma_c;
            inv_gamma_cm1 = 1 / (gamma_c - 1);
            double sqrtT = Math.Sqrt(Tcns);

            // if we're not combusting, don't combust and start cooling off
            combusting = running && ignited;
            statusString = "Nominal";

            // ullage check first, overwrite if bad pressure or no propellants
            if (!ullage) {
                combusting = false;
                statusString = "Vapor in feed line";
            }

            // check fuel flow fraction
            if (ffFraction <= 0d) {
                combusting = false;
                statusString = "No propellants";
            }
            // check pressure
            if (!pressure) {
                combusting = false;
                statusString = "Lack of pressure";
            }

            // check flow mult
            //double fuelFlowMult = FlowMult();
            //if (fuelFlowMult < flowMultMin)
            //{
            //    combusting = false;
            //    statusString = "Airflow outside specs";
            //}

            if (!combusting || commandedThrottle <= 0d) {
                combusting = false; // for throttle FX
                fxPower = 0f;
            } else {
                // get current flow, and thus thrust.
                fuelFlow = scale * flowMult * UtilMath.LerpUnclamped(minFlow, maxFlow, commandedThrottle) * thrustRatio;
                if ((overTempRatio>1||varyThrust > 0d) && fuelFlow > 0d && HighLogic.LoadedSceneIsFlight)
                    fuelFlow *= (1d + (Mathf.PerlinNoise(Time.time, 0f) * 2d - 1d) * (varyThrust + (overTempRatio - 1) * 10));

                Cstar = (Math.Sqrt(gamma_c * R) * sqrtT)
                            /
                        (gamma_c * Math.Sqrt(Math.Pow(2 / (gamma_c + 1), (gamma_c + 1) * inv_gamma_cm1)));
                Pcns = fuelFlow * Cstar / (nozzleThroatArea * 9806.65d);
                Pe = Pcns / pR;
                double pR_Frac = Math.Sqrt(1 - Math.Pow(1 / pR, ((gamma_c - 1) * inv_gamma_c)));
                double Ct1_per_pR_Frac =
                    Math.Sqrt(
                         (2 * gamma_c * gamma_c) *
                         (Math.Pow(2 / (gamma_c + 1), (gamma_c + 1) * inv_gamma_cm1))
                         * inv_gamma_cm1
                    );
                double Ct2 = nozzleExpansionRatio * (Pe - p0 / 1000d) / Pcns;
                Ct = pR_Frac * Ct1_per_pR_Frac + Ct2;
                double pR_e = Pe * 1000d / p0;
                statusString = "";
                if (Pcns > chamberMaxPressure) {
                    overPressureRatio = (float)(Pcns / chamberMaxPressure - 1);
                    if ((failed & isFailed.OVPR) != isFailed.NONE) {
                        overPressureRatio /= Stability;
                    }
                    Stability /= (1 + 0.0001f * overPressureRatio);/*MAGIC*/
                    statusString = "OVPR ";//Over pressure
                } else overPressureRatio = 1;
                if (Tcns > chamberMaxTemp) {//chamberMaxTemp should be maxEngineTemp * 0.8 ,for the overheat box in SolverEngine
                    overTempRatio = (float)(Tcns / (chamberMaxTemp));
                    if ((failed & isFailed.OVHT) != isFailed.NONE) {
                        overTempRatio /= Stability;
                    }
                    Stability /= (1 + 0.0001f * overTempRatio);/*MAGIC*/
                    statusString += "OVHT ";//Over Temprature
                } else overTempRatio = 1;
                if (pR_e < 0.3 && pR_e >= 0.1) { Ct = Ct * Math.Sqrt(pR_e * 5 - 0.50f); statusString += "Shockwave in Nozzle"; }//TODO:Actually jet sepration wouldn't cost so much
                    else if (pR_e < 0.10001) { Ct = 0; statusString += "Jet Sepration"; }
                if (statusString == "") { statusString = "Nominal"; }
                Isp = Cstar * Ct / 9.80665d;
                Isp *= ispMult;

                SFC = 3600d / Isp;

                fxThrottle = (float)commandedThrottle;
                fxPower = (float)((Ct > 1.5f ? 1 : Ct / 1.5f) * fuelFlow / maxFlow * ispMult); // FX is proportional to fuel flow and Isp mult.
                if (float.IsNaN(fxPower))
                    fxPower = 0f;
                thrust = ispMult * Cstar * Ct * fuelFlow;
                if ((failed & isFailed.THRUST) != isFailed.NONE) {
                    thrust *= 1 - (1 - Stability) * UnityEngine.Random.value;
                }

                if ((failed & isFailed.FUELFLOW) != isFailed.NONE) {
                    fuelFlow /= UnityEngine.Mathf.Pow(Stability, 3);
                }
#if DEBUG
                Debug.Log($"CalculatePerformance:Tcns:{Tcns},Pcns:{Pcns},chamberMaxTemp:{chamberMaxTemp},Pe:{Pe},nozzleThroatArea:{nozzleThroatArea},R:{R}");
                Debug.Log($"CalculatePerformance:gamma_c:{gamma_c},Cstar:{Cstar},fuelFlow:{fuelFlow},pR_Frac:{pR_Frac},Ct2:{Ct2},Ct:{Ct}");

#endif
            }
            UpdateTc();
        }
        // engine status
        public override double GetEngineTemp()     => Tcns;
        public double          GetEnginePressure() => Pcns;
        public override double GetArea()           => 0d;
        // FX etc
        public override double GetEmissive()       => UtilMath.Clamp01(Tcns / chamberNominalTemp);
        public override float  GetFXPower()        => fxPower;
        public override float  GetFXRunning()      => fxPower;
        public override float  GetFXThrottle()     => fxThrottle;
        public override float  GetFXSpool()        => (float)(UtilMath.Clamp01(Tcns / chamberNominalTemp));
        public override bool   GetRunning()        => combusting;
        public double          GetHeat() {
            double heat = (Tcns - t0) * 0.05 * heatMult;/*MAGIC*/
            if ((failed & isFailed.PART_TEMP)!=isFailed.NONE) {
                heat /= UnityEngine.Mathf.Pow(Stability, 2);
            }
            return heat;
        }
        public void             SetDamageFrac(float frac) 
                                                    => Stability /= frac;
        // new methods
        public void UpdateThrustRatio(double r) { thrustRatio = r; }

    }
}
