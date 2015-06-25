using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/* Sim code largely based on that of EngineIgnitor by HoneyFox (MIT license), just tweaked for
 * speed and persistence and the new use case. */
/*The MIT License (MIT)

Copyright (c) 2013 HoneyFox

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.*/

namespace RealFuels.Ullage
{
    public class UllageSimulator : IConfigNode
    {
        double ullageHeightMin, ullageHeightMax;
        double ullageRadialMin, ullageRadialMax;

        double propellantStability = 1d;
        string propellantStatus = "";
        double UT = double.MinValue;

        static double veryStable = 0.996d; // will be clamped above this.
        static double stable = 0.95d;
        static double risky = 0.75d;
        static double veryRisky = 0.3d;
        static double unstable = 0.15d;

        public UllageSimulator()
        {
            Reset();
        }

        public void Load(ConfigNode node)
        {
            node.TryGetValue("ullageHeightMin", ref ullageHeightMin);
            node.TryGetValue("ullageHeightMax", ref ullageHeightMax);
            node.TryGetValue("ullageRadialMin", ref ullageRadialMin);
            node.TryGetValue("ullageRadialMax", ref ullageRadialMax);
            node.TryGetValue("UT", ref UT);
        }
        public void Save(ConfigNode node)
        {
            node.AddValue("ullageHeightMin", ullageHeightMin.ToString("G17"));
            node.AddValue("ullageHeightMax", ullageHeightMax.ToString("G17"));
            node.AddValue("ullageRadialMin", ullageRadialMin.ToString("G17"));
            node.AddValue("ullageRadialMax", ullageRadialMax.ToString("G17"));
            node.AddValue("UT", UT.ToString("G17"));
        }

        public void Reset()
        {
            ullageHeightMin = 0.05d; ullageHeightMax = 0.95d;
            ullageRadialMin = 0.0d; ullageRadialMax = 0.95d;
        }

        public void Update(Vector3d localAcceleration, Vector3d rotation, double deltaTime, double ventingAcc, double fuelRatio)
        {
            double utTimeDelta = deltaTime;
            if (Planetarium.fetch)
            {
                double newUT = Planetarium.GetUniversalTime();
                utTimeDelta = newUT - UT;
                UT = newUT;
            }

            double fuelRatioFactor = (0.5d + fuelRatio) * (1d / 1.4d);
            double fuelRatioFactorRecip = 1.0d / fuelRatioFactor;

            //if (ventingAcc != 0.0f) Debug.Log("BoilOffAcc: " + ventingAcc.ToString("F8"));
            //else Debug.Log("BoilOffAcc: No boiloff.");
            
            Vector3d localAccelerationAmount = localAcceleration * deltaTime;
            Vector3d rotationAmount = rotation * deltaTime;

            //Debug.Log("Ullage: dt: " + deltaTime.ToString("F2") + " localAcc: " + localAcceleration.ToString() + " rotateRate: " + rotation.ToString());

            // Natural diffusion.
            //Debug.Log("Ullage: LocalAcc: " + localAcceleration.ToString());
            if (ventingAcc <= RFSettings.Instance.ventingAccThreshold)
            {
                double ventingConst = Math.Min(1d, (1d - ventingAcc / RFSettings.Instance.ventingAccThreshold) * fuelRatioFactorRecip * utTimeDelta);
                ullageHeightMin = UtilMath.LerpUnclamped(ullageHeightMin, 0.05d, RFSettings.Instance.naturalDiffusionRateY * ventingConst);
                ullageHeightMax = UtilMath.LerpUnclamped(ullageHeightMax, 0.95d, RFSettings.Instance.naturalDiffusionRateY * ventingConst);
                ullageRadialMin = UtilMath.LerpUnclamped(ullageRadialMin, 0.00d, RFSettings.Instance.naturalDiffusionRateX * ventingConst);
                ullageRadialMax = UtilMath.LerpUnclamped(ullageRadialMax, 0.95d, RFSettings.Instance.naturalDiffusionRateX * ventingConst);
            }

            // Translate forward/backward.
            double radialFac = Math.Abs(localAccelerationAmount.y) * RFSettings.Instance.translateAxialCoefficientX * fuelRatioFactor;
            double heightFac = localAccelerationAmount.y * RFSettings.Instance.translateAxialCoefficientY * fuelRatioFactor;
            ullageHeightMin = UtilMath.Clamp(ullageHeightMin + heightFac, 0.0d, 0.9d);
            ullageHeightMax = UtilMath.Clamp(ullageHeightMax + heightFac, 0.1d, 1.0d);
            ullageRadialMin = UtilMath.Clamp(ullageRadialMin - radialFac, 0.0d, 0.9d);
            ullageRadialMax = UtilMath.Clamp(ullageRadialMax + radialFac, 0.1d, 1.0d);

            // Translate up/down/left/right.
            Vector3d sideAcc = new Vector3d(localAccelerationAmount.x, 0.0d, localAccelerationAmount.z);
            double sideFactor = sideAcc.magnitude * fuelRatioFactor;
            double sideY = sideFactor * RFSettings.Instance.translateSidewayCoefficientY;
            double sideX = sideFactor * RFSettings.Instance.translateSidewayCoefficientX;
            ullageHeightMin = UtilMath.Clamp(ullageHeightMin - sideY, 0.0d, 0.9d);
            ullageHeightMax = UtilMath.Clamp(ullageHeightMax + sideY, 0.1d, 1.0d);
            ullageRadialMin = UtilMath.Clamp(ullageRadialMin + sideX, 0.0d, 0.9d);
            ullageRadialMax = UtilMath.Clamp(ullageRadialMax + sideX, 0.1d, 1.0d);

            // Rotate yaw/pitch.
            Vector3d rotateYawPitch = new Vector3d(rotation.x, 0.0d, rotation.z);
            double mag = rotateYawPitch.magnitude;
            double ypY = mag * RFSettings.Instance.rotateYawPitchCoefficientY;
            double ypX = mag * RFSettings.Instance.rotateYawPitchCoefficientX;
            if (ullageHeightMin < 0.45d)
                ullageHeightMin = UtilMath.Clamp(ullageHeightMin + ypY, 0.0d, 0.45d);
            else
                ullageHeightMin = UtilMath.Clamp(ullageHeightMin - ypY, 0.45d, 0.9d);

            if (ullageHeightMax < 0.55d)
                ullageHeightMax = UtilMath.Clamp(ullageHeightMax + ypY, 0.1d, 0.55d);
            else
                ullageHeightMax = UtilMath.Clamp(ullageHeightMax - ypY, 0.55d, 1.0d);

            ullageRadialMin = UtilMath.Clamp(ullageRadialMin - ypX, 0.0d, 0.9d);
            ullageRadialMax = UtilMath.Clamp(ullageRadialMax + ypX, 0.1d, 1.0d);

            // Rotate roll.
            double absRot = Math.Abs(rotation.y) * fuelRatioFactor;
            double absRotX = absRot * RFSettings.Instance.rotateRollCoefficientX;
            double absRotY = absRot * RFSettings.Instance.rotateRollCoefficientY;
            ullageHeightMin = UtilMath.Clamp(ullageHeightMin - absRotY, 0.0d, 0.9d);
            ullageHeightMax = UtilMath.Clamp(ullageHeightMax + absRotY, 0.1d, 1.0d);
            ullageRadialMin = UtilMath.Clamp(ullageRadialMin - absRotX, 0.0d, 0.9d);
            ullageRadialMax = UtilMath.Clamp(ullageRadialMax - absRotX, 0.1d, 1.0d);

            //Debug.Log("Ullage: Height: (" + ullageHeightMin.ToString("F2") + " - " + ullageHeightMax.ToString("F2") + ") Radius: (" + ullageRadialMin.ToString("F2") + " - " + ullageRadialMax.ToString("F2") + ")");

            double bLevel = UtilMath.Clamp((ullageHeightMax - ullageHeightMin) * (ullageRadialMax - ullageRadialMin) * 10d * UtilMath.Clamp(8.2d - 8d * fuelRatio, 0.0d, 8.2d) - 1.0d, 0.0d, 15.0d);
            //Debug.Log("Ullage: bLevel: " + bLevel.ToString("F3"));

            double pVertical = UtilMath.Clamp01(1.0d - (ullageHeightMin - 0.1d) * 5d);
            //Debug.Log("Ullage: pVertical: " + pVertical.ToString("F3"));

            double pHorizontal = UtilMath.Clamp01(1.0d - (ullageRadialMin - 0.1d) * 5d);
            //Debug.Log("Ullage: pHorizontal: " + pHorizontal.ToString("F3"));

            propellantStability = Math.Max(0.0d, 1.0d - (pVertical * pHorizontal * (0.75d + Math.Sqrt(bLevel))));
                

            SetStateString();
        }
        private void SetStateString()
        {
            if (propellantStability >= veryStable)
            {
                propellantStability = 1d;
                propellantStatus = "Very Stable";
            }
            else if (propellantStability >= stable)
                propellantStatus = "Stable";
            else if (propellantStability >= risky)
                propellantStatus = "Risky";
            else if (propellantStability >= veryRisky)
                propellantStatus = "Very Risky";
            else if (propellantStability >= unstable)
                propellantStatus = "Unstable";
            else
                propellantStatus = "Very Unstable";
        }
        public double GetPropellantStability()
        {
            return propellantStability;
        }
        public void SetPropellantStability(double newStab)
        {
            propellantStability = newStab;
            SetStateString();
        }
        public string GetPropellantStatus()
        {
            return propellantStatus;
        }

    }
}
