using System;
using System.Linq;
using TweakScale;

namespace RealFuels
{
    class TweakScaleModuleEnginesRFUpdater : IRescalable<ModuleEnginesRF>
    {
        private ModuleEnginesRF _module;

        private ModuleEnginesRF Module
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

        public TweakScaleModuleEnginesRFUpdater(ModuleEnginesRF pm)
        {
            _module = pm;
        }

        public void OnRescale(ScalingFactor factor)
        {
            bool change = true;
            if (Part != null)
            {
                for (int i = Part.Modules.Count - 1; i >= 0; --i)
                {
                    PartModule m = Part.Modules[i];
                    if (m is ModuleEngineConfigs)
                    {
                        change = false;
                        break;
                    }
                }
            }
            if(change)
                Module.SetScale(factor.absolute.quadratic);
        }
    }
}
