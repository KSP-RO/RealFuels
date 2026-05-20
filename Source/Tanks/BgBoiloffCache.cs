namespace RealFuels.Tanks
{
    internal sealed class BgBoiloffCache
    {
        internal readonly string DataVersion;
        internal readonly int MliLayers;
        internal readonly BgTankEntry[] Tanks;
        internal readonly double[] InternalTemps; // mutable, parallel to Tanks; -1 = uninitialized

        internal BgBoiloffCache(string dataVersion, int mliLayers, BgTankEntry[] tanks)
        {
            DataVersion = dataVersion;
            MliLayers = mliLayers;
            Tanks = tanks;
            InternalTemps = new double[tanks.Length];
            for (int i = 0; i < tanks.Length; i++) InternalTemps[i] = -1d;
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
