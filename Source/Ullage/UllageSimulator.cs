using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RealFuels.Ullage
{
    public class UllageSimulator
    {
        public static bool s_SimulateUllage = true;
        public static bool s_ShutdownEngineWhenUnstable = true;
        public static bool s_ExplodeEngineWhenTooUnstable = false;

        public static double s_NaturalDiffusionRateX = 0.02d;
        public static double s_NaturalDiffusionRateY = 0.03d;

        public static double s_TranslateAxialCoefficientX = 0.06d;
        public static double s_TranslateAxialCoefficientY = 0.06d;

        public static double s_TranslateSidewayCoefficientX = 0.04d;
        public static double s_TranslateSidewayCoefficientY = 0.02d;

        public static double s_RotateYawPitchCoefficientX = 0.003d;
        public static double s_RotateYawPitchCoefficientY = 0.004d;

        public static double s_RotateRollCoefficientX = 0.005d;
        public static double s_RotateRollCoefficientY = 0.006d;

        public static double s_VentingVelocity = 100.0d;
        public static double s_VentingAccThreshold = 0.00000004d;


        double ullageHeightMin, ullageHeightMax;
        double ullageRadialMin, ullageRadialMax;

        string fuelFlowState = "";

        public void Reset()
        {
            ullageHeightMin = 0.05d; ullageHeightMax = 0.95d;
            ullageRadialMin = 0.0d; ullageRadialMax = 0.95d;
        }

        public void Update(Vector3d localAcceleration, Vector3d rotation, double deltaTime, double ventingAcc = 0d, double fuelRatio = 0.9d)
        {
            double fuelRatioFactor = (0.5d + fuelRatio) * (1d/ 1.4d);
            double invFuelRatioFactor = 1.0d / fuelRatioFactor;

            //if (ventingAcc != 0.0f) Debug.Log("BoilOffAcc: " + ventingAcc.ToString("F8"));
            //else Debug.Log("BoilOffAcc: No boiloff.");
            
            Vector3d localAccelerationAmount = localAcceleration * deltaTime;
            Vector3d rotationAmount = rotationAmount = rotation * deltaTime;

            //Debug.Log("Ullage: dt: " + deltaTime.ToString("F2") + " localAcc: " + localAcceleration.ToString() + " rotateRate: " + rotation.ToString());

            // Natural diffusion.
            //Debug.Log("Ullage: LocalAcc: " + localAcceleration.ToString());
            if (ventingAcc <= s_VentingAccThreshold)
            {
                double ventingConst = (1d - ventingAcc / s_VentingAccThreshold) * invFuelRatioFactor * deltaTime;
                ullageHeightMin = UtilMath.Lerp(ullageHeightMin, 0.05d, s_NaturalDiffusionRateY * ventingConst);
                ullageHeightMax = UtilMath.Lerp(ullageHeightMax, 0.95d, s_NaturalDiffusionRateY * ventingConst);
                ullageRadialMin = UtilMath.Lerp(ullageRadialMin, 0.00d, s_NaturalDiffusionRateX * ventingConst);
                ullageRadialMax = UtilMath.Lerp(ullageRadialMax, 0.95d, s_NaturalDiffusionRateX * ventingConst);
            }

            // Translate forward/backward.
            ullageHeightMin = UtilMath.Clamp(ullageHeightMin + localAccelerationAmount.y * s_TranslateAxialCoefficientY * fuelRatioFactor, 0.0d, 0.9d);
            ullageHeightMax = UtilMath.Clamp(ullageHeightMax + localAccelerationAmount.y * s_TranslateAxialCoefficientY * fuelRatioFactor, 0.1d, 1.0d);
            ullageRadialMin = UtilMath.Clamp(ullageRadialMin - Math.Abs(localAccelerationAmount.y) * s_TranslateAxialCoefficientX * fuelRatioFactor, 0.0d, 0.9d);
            ullageRadialMax = UtilMath.Clamp(ullageRadialMax + Math.Abs(localAccelerationAmount.y) * s_TranslateAxialCoefficientX * fuelRatioFactor, 0.1d, 1.0d);

            // Translate up/down/left/right.
            Vector3d sideAcc = new Vector3d(localAccelerationAmount.x, 0.0d, localAccelerationAmount.z);
            ullageHeightMin = UtilMath.Clamp(ullageHeightMin - sideAcc.magnitude * s_TranslateSidewayCoefficientY * fuelRatioFactor, 0.0d, 0.9d);
            ullageHeightMax = UtilMath.Clamp(ullageHeightMax + sideAcc.magnitude * s_TranslateSidewayCoefficientY * fuelRatioFactor, 0.1d, 1.0d);
            ullageRadialMin = UtilMath.Clamp(ullageRadialMin + sideAcc.magnitude * s_TranslateSidewayCoefficientX * fuelRatioFactor, 0.0d, 0.9d);
            ullageRadialMax = UtilMath.Clamp(ullageRadialMax + sideAcc.magnitude * s_TranslateSidewayCoefficientX * fuelRatioFactor, 0.1d, 1.0d);

            // Rotate yaw/pitch.
            Vector3d rotateYawPitch = new Vector3d(rotation.x, 0.0d, rotation.z);
            if (ullageHeightMin < 0.45d)
                ullageHeightMin = UtilMath.Clamp(ullageHeightMin + rotateYawPitch.magnitude * s_RotateYawPitchCoefficientY, 0.0d, 0.45d);
            else
                ullageHeightMin = UtilMath.Clamp(ullageHeightMin - rotateYawPitch.magnitude * s_RotateYawPitchCoefficientY, 0.45d, 0.9d);

            if (ullageHeightMax < 0.55d)
                ullageHeightMax = UtilMath.Clamp(ullageHeightMax + rotateYawPitch.magnitude * s_RotateYawPitchCoefficientY, 0.1d, 0.55d);
            else
                ullageHeightMax = UtilMath.Clamp(ullageHeightMax - rotateYawPitch.magnitude * s_RotateYawPitchCoefficientY, 0.55d, 1.0d);

            ullageRadialMin = UtilMath.Clamp(ullageRadialMin - rotateYawPitch.magnitude * s_RotateYawPitchCoefficientX, 0.0d, 0.9d);
            ullageRadialMax = UtilMath.Clamp(ullageRadialMax + rotateYawPitch.magnitude * s_RotateYawPitchCoefficientX, 0.1d, 1.0d);

            // Rotate roll.
            ullageHeightMin = UtilMath.Clamp(ullageHeightMin - Math.Abs(rotation.y) * s_RotateRollCoefficientY * fuelRatioFactor, 0.0d, 0.9d);
            ullageHeightMax = UtilMath.Clamp(ullageHeightMax + Math.Abs(rotation.y) * s_RotateRollCoefficientY * fuelRatioFactor, 0.1d, 1.0d);
            ullageRadialMin = UtilMath.Clamp(ullageRadialMin - Math.Abs(rotation.y) * s_RotateRollCoefficientX * fuelRatioFactor, 0.0d, 0.9d);
            ullageRadialMax = UtilMath.Clamp(ullageRadialMax - Math.Abs(rotation.y) * s_RotateRollCoefficientX * fuelRatioFactor, 0.1d, 1.0d);

            //Debug.Log("Ullage: Height: (" + ullageHeightMin.ToString("F2") + " - " + ullageHeightMax.ToString("F2") + ") Radius: (" + ullageRadialMin.ToString("F2") + " - " + ullageRadialMax.ToString("F2") + ")");
        }

        public double GetFuelFlowStability(double fuelRatio = 0.9d)
        {
            double bLevel = UtilMath.Clamp((ullageHeightMax - ullageHeightMin) * (ullageRadialMax - ullageRadialMin) * 10d * UtilMath.Clamp(8.2d - 8d * fuelRatio, 0.0d, 8.2d) - 1.0d, 0.0d, 15.0d);
            //Debug.Log("Ullage: bLevel: " + bLevel.ToString("F3"));

            double pVertical = 1.0d;
            pVertical = 1.0d - (ullageHeightMin - 0.1d) / 0.2d;
            pVertical = UtilMath.Clamp01(pVertical);
            //Debug.Log("Ullage: pVertical: " + pVertical.ToString("F3"));

            double pHorizontal = 1.0d;
            pHorizontal = 1.0d - (ullageRadialMin - 0.1d) / 0.2d;
            pHorizontal = UtilMath.Clamp01(pHorizontal);
            //Debug.Log("Ullage: pHorizontal: " + pHorizontal.ToString("F3"));

            double successProbability = Math.Max(0.0d, 1.0d - (pVertical * pHorizontal * (0.75d + Math.Sqrt(bLevel))));

            if (successProbability >= 0.996d)
                fuelFlowState = "Very Stable";
            else if (successProbability >= 0.95d)
                fuelFlowState = "Stable";
            else if (successProbability >= 0.75d)
                fuelFlowState = "Risky";
            else if (successProbability >= 0.50d)
                fuelFlowState = "Very Risky";
            else if (successProbability >= 0.30d)
                fuelFlowState = "Unstable";
            else
                fuelFlowState = "Very Unstable";

            return successProbability;
        }

        public string GetFuelFlowState()
        {
            return fuelFlowState;
        }

    }
}
