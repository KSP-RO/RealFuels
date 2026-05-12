using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RealFuels.Tanks
{
    internal sealed class BgBoiloffCache
    {
        internal readonly string DataVersion;
        internal readonly int MliLayers;
        internal readonly double TankAreaM2;
        // Arrays are paired up: VspParams[i] and VspInfo[i]; DewarParams[i] and DewarInfo[i]
        internal readonly (double conductW, double coldK)[] VspParams;
        internal readonly (string name, double vsp, double density)[] VspInfo;
        internal readonly (double dewarArea, double coldK)[] DewarParams;
        internal readonly (string name, double vsp, double density)[] DewarInfo;

        internal BgBoiloffCache(string dataVersion, int mliLayers, double tankAreaM2,
            (double conductW, double coldK)[] vspParams, (string name, double vsp, double density)[] vspInfo,
            (double dewarArea, double coldK)[] dewarParams, (string name, double vsp, double density)[] dewarInfo)
        {
            DataVersion = dataVersion;
            MliLayers = mliLayers;
            TankAreaM2 = tankAreaM2;
            VspParams = vspParams;
            VspInfo = vspInfo;
            DewarParams = dewarParams;
            DewarInfo = dewarInfo;
        }
    }
}
