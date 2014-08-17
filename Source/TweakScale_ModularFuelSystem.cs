using System;
using System.Linq;
using TweakScale;

namespace RealFuels
{
    class TweakScaleModularFuelTanksUpdater : IRescalable<RealFuels.ModuleFuelTanks>
    {
        private RealFuels.ModuleFuelTanks _module;

        private RealFuels.ModuleFuelTanks Module
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

        public TweakScaleModularFuelTanksUpdater(RealFuels.ModuleFuelTanks pm)
        {
            _module = pm;
        }

        public void OnRescale(ScalingFactor factor)
        {
            Module.ChangeVolume(Module.volume * factor.relative.cubic);
            foreach (PartResource f in Part.Resources)
            {
                f.amount /= factor.relative.cubic;
                f.maxAmount /= factor.relative.cubic;
            }
        }
    }
}
