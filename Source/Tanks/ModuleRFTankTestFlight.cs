using UnityEngine;

namespace RealFuels.Tanks
{
    public class ModuleRFTankTestFlight : PartModule
    {
        private ModuleFuelTanks tankModule;

        public override void OnAwake()
        {
            base.OnAwake();
            
            tankModule = part.FindModuleImplementing<ModuleFuelTanks>();

            if (tankModule == null && (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor))
            {
                Debug.LogError($"[{part.name} {this.GetType().Name}] no {nameof(ModuleEnginesRF)} found on part.  This module will be removed.");
                part.Modules.Remove(this);
                Destroy(this);
                return;
            }

            tankModule?.onUpdateTankType.Add(UpdateTFInterops);
        }

        private void OnDestroy()
        {
            tankModule?.onUpdateTankType.Remove(UpdateTFInterops);
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            UpdateTFInterops();
        }

        private void UpdateTFInterops()
        {
            TestFlightWrapper.AddInteropValue(this.part, "tankType", tankModule.type, "realFuels");
        }
    }
}
