using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RealFuels.Ullage
{
    public class UllageSimulator : IConfigNode
    {
        double ullageHeightMin, ullageHeightMax;
        double ullageRadialMin, ullageRadialMax;

        double propellantStability = 1d;
        string propellantStatus = "";

        public void Load(ConfigNode node)
        {
            node.TryGetValue("ullageHeightMin", ref ullageHeightMin);
            node.TryGetValue("ullageHeightMax", ref ullageHeightMax);
            node.TryGetValue("ullageRadialMin", ref ullageRadialMin);
            node.TryGetValue("ullageRadialMax", ref ullageRadialMax);
        }
        public void Save(ConfigNode node)
        {
            node.AddValue("ullageHeightMin", ullageHeightMin.ToString("G17"));
            node.AddValue("ullageHeightMax", ullageHeightMax.ToString("G17"));
            node.AddValue("ullageRadialMin", ullageRadialMin.ToString("G17"));
            node.AddValue("ullageRadialMax", ullageRadialMax.ToString("G17"));
        }

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
            if (ventingAcc <= RFSettings.Instance.ventingAccThreshold)
            {
                double ventingConst = (1d - ventingAcc / RFSettings.Instance.ventingAccThreshold) * invFuelRatioFactor * deltaTime;
                ullageHeightMin = UtilMath.Lerp(ullageHeightMin, 0.05d, RFSettings.Instance.naturalDiffusionRateY * ventingConst);
                ullageHeightMax = UtilMath.Lerp(ullageHeightMax, 0.95d, RFSettings.Instance.naturalDiffusionRateY * ventingConst);
                ullageRadialMin = UtilMath.Lerp(ullageRadialMin, 0.00d, RFSettings.Instance.naturalDiffusionRateX * ventingConst);
                ullageRadialMax = UtilMath.Lerp(ullageRadialMax, 0.95d, RFSettings.Instance.naturalDiffusionRateX * ventingConst);
            }

            // Translate forward/backward.
            ullageHeightMin = UtilMath.Clamp(ullageHeightMin + localAccelerationAmount.y * RFSettings.Instance.translateAxialCoefficientY * fuelRatioFactor, 0.0d, 0.9d);
            ullageHeightMax = UtilMath.Clamp(ullageHeightMax + localAccelerationAmount.y * RFSettings.Instance.translateAxialCoefficientY * fuelRatioFactor, 0.1d, 1.0d);
            ullageRadialMin = UtilMath.Clamp(ullageRadialMin - Math.Abs(localAccelerationAmount.y) * RFSettings.Instance.translateAxialCoefficientX * fuelRatioFactor, 0.0d, 0.9d);
            ullageRadialMax = UtilMath.Clamp(ullageRadialMax + Math.Abs(localAccelerationAmount.y) * RFSettings.Instance.translateAxialCoefficientX * fuelRatioFactor, 0.1d, 1.0d);

            // Translate up/down/left/right.
            Vector3d sideAcc = new Vector3d(localAccelerationAmount.x, 0.0d, localAccelerationAmount.z);
            ullageHeightMin = UtilMath.Clamp(ullageHeightMin - sideAcc.magnitude * RFSettings.Instance.translateSidewayCoefficientY * fuelRatioFactor, 0.0d, 0.9d);
            ullageHeightMax = UtilMath.Clamp(ullageHeightMax + sideAcc.magnitude * RFSettings.Instance.translateSidewayCoefficientY * fuelRatioFactor, 0.1d, 1.0d);
            ullageRadialMin = UtilMath.Clamp(ullageRadialMin + sideAcc.magnitude * RFSettings.Instance.translateSidewayCoefficientX * fuelRatioFactor, 0.0d, 0.9d);
            ullageRadialMax = UtilMath.Clamp(ullageRadialMax + sideAcc.magnitude * RFSettings.Instance.translateSidewayCoefficientX * fuelRatioFactor, 0.1d, 1.0d);

            // Rotate yaw/pitch.
            Vector3d rotateYawPitch = new Vector3d(rotation.x, 0.0d, rotation.z);
            if (ullageHeightMin < 0.45d)
                ullageHeightMin = UtilMath.Clamp(ullageHeightMin + rotateYawPitch.magnitude * RFSettings.Instance.rotateYawPitchCoefficientY, 0.0d, 0.45d);
            else
                ullageHeightMin = UtilMath.Clamp(ullageHeightMin - rotateYawPitch.magnitude * RFSettings.Instance.rotateYawPitchCoefficientY, 0.45d, 0.9d);

            if (ullageHeightMax < 0.55d)
                ullageHeightMax = UtilMath.Clamp(ullageHeightMax + rotateYawPitch.magnitude * RFSettings.Instance.rotateYawPitchCoefficientY, 0.1d, 0.55d);
            else
                ullageHeightMax = UtilMath.Clamp(ullageHeightMax - rotateYawPitch.magnitude * RFSettings.Instance.rotateYawPitchCoefficientY, 0.55d, 1.0d);

            ullageRadialMin = UtilMath.Clamp(ullageRadialMin - rotateYawPitch.magnitude * RFSettings.Instance.rotateYawPitchCoefficientX, 0.0d, 0.9d);
            ullageRadialMax = UtilMath.Clamp(ullageRadialMax + rotateYawPitch.magnitude * RFSettings.Instance.rotateYawPitchCoefficientX, 0.1d, 1.0d);

            // Rotate roll.
            ullageHeightMin = UtilMath.Clamp(ullageHeightMin - Math.Abs(rotation.y) * RFSettings.Instance.rotateRollCoefficientY * fuelRatioFactor, 0.0d, 0.9d);
            ullageHeightMax = UtilMath.Clamp(ullageHeightMax + Math.Abs(rotation.y) * RFSettings.Instance.rotateRollCoefficientY * fuelRatioFactor, 0.1d, 1.0d);
            ullageRadialMin = UtilMath.Clamp(ullageRadialMin - Math.Abs(rotation.y) * RFSettings.Instance.rotateRollCoefficientX * fuelRatioFactor, 0.0d, 0.9d);
            ullageRadialMax = UtilMath.Clamp(ullageRadialMax - Math.Abs(rotation.y) * RFSettings.Instance.rotateRollCoefficientX * fuelRatioFactor, 0.1d, 1.0d);

            //Debug.Log("Ullage: Height: (" + ullageHeightMin.ToString("F2") + " - " + ullageHeightMax.ToString("F2") + ") Radius: (" + ullageRadialMin.ToString("F2") + " - " + ullageRadialMax.ToString("F2") + ")");

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

            propellantStability = Math.Max(0.0d, 1.0d - (pVertical * pHorizontal * (0.75d + Math.Sqrt(bLevel))));

            if (propellantStability >= 0.996d)
                propellantStatus = "Very Stable";
            else if (propellantStability >= 0.95d)
                propellantStatus = "Stable";
            else if (propellantStability >= 0.75d)
                propellantStatus = "Risky";
            else if (propellantStability >= 0.50d)
                propellantStatus = "Very Risky";
            else if (propellantStability >= 0.30d)
                propellantStatus = "Unstable";
            else
                propellantStatus = "Very Unstable";
        }

        public double GetPropellantStability()
        {
            return propellantStability;
        }

        public string GetPropellantStatus()
        {
            return propellantStatus;
        }

    }
}
