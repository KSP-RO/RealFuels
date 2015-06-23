using System;
using System.Linq;
using TweakScale;

namespace RealFuels
{
    class TweakScaleModularEnginesUpdater : IRescalable<ModuleEngineConfigs>
    {
        private ModuleEngineConfigs _module;

        private ModuleEngineConfigs Module
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

        public TweakScaleModularEnginesUpdater(ModuleEngineConfigs pm)
        {
            _module = pm;
        }

        public void OnRescale(ScalingFactor factor)
        {
            Module.scale = factor.absolute.quadratic;
            Module.SetConfiguration();
        }
    }
}
