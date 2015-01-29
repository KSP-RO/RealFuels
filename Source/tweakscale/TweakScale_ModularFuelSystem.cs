using System;
using System.Linq;
using TweakScale;

namespace RealFuels
{
    class TweakScaleModularFuelTanksUpdater : IRescalable<RealFuels.Tanks.ModuleFuelTanks>
    {
        private RealFuels.Tanks.ModuleFuelTanks _module;

        private RealFuels.Tanks.ModuleFuelTanks Module
        {
            get
            {
                return _module;
            }
        }

        private Part Part
        {
            get
            {
                return _module.part;
            }
        }

        public TweakScaleModularFuelTanksUpdater(RealFuels.Tanks.ModuleFuelTanks pm)
        {
            _module = pm;
        }

        public void OnRescale(ScalingFactor factor)
        {
            Module.ChangeVolumeRatio(factor.relative.cubic, false); // do not propagate since TS itself will.
            // hacky; will fix.
            /*foreach (PartResource f in Part.Resources)
            {
                f.amount /= factor.relative.cubic;
                f.maxAmount /= factor.relative.cubic;
            }*/
        }
    }
}
