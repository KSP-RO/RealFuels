using System;
using System.Collections.Generic;
using System.Globalization;

namespace RealFuels.Tanks
{
    internal sealed class BgBoiloffCache
    {
        internal readonly int MliLayers;
        internal readonly BgTankEntry[] Tanks;
        internal readonly double[] InternalTemps; // mutable, parallel to Tanks
        internal readonly double[] FluxScratch;   // mutable per-tick scratch for Q_kW pre-pass

        internal readonly double CoolerInputKW;     // (0 if no cooler installed)
        internal readonly double CoolerFrac;        // fraction-of-Carnot at CoolerLowestTempK
        internal readonly double CoolerLowestTempK; // cold-side T of the cooler

        internal BgBoiloffCache(int mliLayers, BgTankEntry[] tanks,
            double coolerInputKW, double coolerFrac, double coolerLowestTempK)
        {
            MliLayers = mliLayers;
            Tanks = tanks;
            InternalTemps = new double[tanks.Length];
            FluxScratch = new double[tanks.Length];
            CoolerInputKW = coolerInputKW;
            CoolerFrac = coolerFrac;
            CoolerLowestTempK = coolerLowestTempK;
        }

        internal static BgBoiloffCache Build(string data, string coolerData, int mliLayers)
        {
            var tanks = new List<BgTankEntry>();

            foreach (string entry in data.Split(';'))
            {
                if (string.IsNullOrEmpty(entry)) continue;
                string[] split = entry.Split(',');
                if (split.Length != 7) continue;

                string resourceName = split[0];
                if (!double.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double boilingPointK)) continue;
                if (!double.TryParse(split[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double tankAreaM2)) continue;
                if (!double.TryParse(split[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double conductWPerK)) continue;
                if (!int.TryParse(split[4], out int isDewarInt)) continue;
                if (!double.TryParse(split[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double hsp)) continue;
                if (!double.TryParse(split[6], NumberStyles.Float, CultureInfo.InvariantCulture, out double structThermalMassKJ)) continue;

                PartResourceDefinition resDef = PartResourceLibrary.Instance.GetDefinition(resourceName);
                if (resDef == null || resDef.density <= 0d) continue;
                if (!MFSSettings.resourceVsps.TryGetValue(resourceName, out double vsp) || vsp <= 0) continue;

                tanks.Add(new BgTankEntry
                {
                    Name = resourceName,
                    Vsp = vsp,
                    Density = resDef.density,
                    BoilingPointK = boilingPointK,
                    TankAreaM2 = tankAreaM2,
                    ConductWPerK = conductWPerK,
                    IsDewar = isDewarInt != 0,
                    Hsp = hsp,
                    StructThermalMassKJ = structThermalMassKJ,
                });
            }

            double coolerInputKW = 0d, coolerFrac = 0d, coolerLowestTempK = 0d;
            if (!string.IsNullOrEmpty(coolerData))
            {
                string[] cSplit = coolerData.Split(',');
                if (cSplit.Length == 3
                    && double.TryParse(cSplit[0], NumberStyles.Float, CultureInfo.InvariantCulture, out coolerInputKW)
                    && double.TryParse(cSplit[1], NumberStyles.Float, CultureInfo.InvariantCulture, out coolerFrac)
                    && double.TryParse(cSplit[2], NumberStyles.Float, CultureInfo.InvariantCulture, out coolerLowestTempK))
                {
                    // ok
                }
                else
                {
                    coolerInputKW = 0d; coolerFrac = 0d; coolerLowestTempK = 0d;
                }
            }

            return new BgBoiloffCache(mliLayers, tanks.ToArray(), coolerInputKW, coolerFrac, coolerLowestTempK);
        }

        internal void InitTemps(ProtoPartModuleSnapshot proto_module)
        {
            // Seed InternalTemps from the persisted TANK nodes
            var tempLookup = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (ConfigNode tankNode in proto_module.moduleValues.GetNodes("TANK"))
            {
                string tName = tankNode.GetValue("name");
                string sVal = tankNode.GetValue("internalTemp");
                if (tName != null && double.TryParse(sVal, NumberStyles.Float, CultureInfo.InvariantCulture, out double t))
                    tempLookup[tName] = t;
            }

            for (int i = 0; i < Tanks.Length; i++)
            {
                InternalTemps[i] = tempLookup.TryGetValue(Tanks[i].Name, out double v) && v > 0
                    ? v : Tanks[i].BoilingPointK;
            }
        }
    }

    internal struct BgTankEntry
    {
        internal string Name;
        internal double Vsp;                 // kJ/t
        internal double Density;             // t/unit
        internal double BoilingPointK;
        internal double TankAreaM2;          // per-tank surface area; used by MLI and Dewar formulas
        internal double ConductWPerK;        // wall conductance for non-MLI tanks; 0 for MLI/Dewar
        internal bool IsDewar;
        internal double Hsp;                 // specific heat capacity, kJ/(t·K)
        internal double StructThermalMassKJ; // structural thermal mass contribution for this tank, kJ/K
    }
}
