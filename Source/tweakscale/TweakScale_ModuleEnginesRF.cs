using System;
using System.Linq;
using TweakScale;

namespace RealFuels
{
    class TweakScaleModuleEnginesRFUpdater : IRescalable<RealFuels.ModuleEnginesRF>
    {
        private RealFuels.ModuleEnginesRF _module;

        private RealFuels.ModuleEnginesRF Module
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

        public TweakScaleModuleEnginesRFUpdater(RealFuels.ModuleEnginesRF pm)
        {
            _module = pm;
        }

        public void OnRescale(ScalingFactor factor)
        {
            Module.SetScale(factor.absolute.quadratic);
        }
    }
}
