using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RealFuels
{
    public static class PartExtensions
    {
        public static float GetResourceMassNoCryo(this Part part, out double thermalMass)
        {
            float num = 0f;
            thermalMass = 0.0f;
            int count = part.Resources.Count;
            while (count-- > 0)
            {
                PartResource partResource = part.Resources[count];
                float num2 = (float)partResource.amount * partResource.info.density;
                num += num2;
                Tanks.ModuleFuelTanks m = part.Modules["ModuleFuelTanks"] as Tanks.ModuleFuelTanks;
                if (m != null && !m.SupportsBoiloff)
                    thermalMass += (num2 * partResource.info.specificHeatCapacity);
            }
            return num;
        }
    }
}
