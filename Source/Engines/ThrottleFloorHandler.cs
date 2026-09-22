using UnityEngine;

namespace RealFuels
{
    /// <summary>
    /// Keeps the throttle-down key from taking the throttle all the way to zero while an engine is
    /// burning. ModuleEnginesRF drops out of its ignited state as soon as the main throttle hits
    /// exactly zero, so with limited ignitions the slightest overshoot while feeling for minimum
    /// throttle costs an ignition. While the key is held the throttle bottoms out at
    /// <see cref="ThrottleFloor"/> instead; the throttle cutoff key still cuts to zero.
    /// Opt-in through <see cref="RFGameParameters.throttleDownKeepsEnginesLit"/>.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class ThrottleFloorHandler : MonoBehaviour
    {
        /// <summary>Lowest throttle the throttle-down key is allowed to reach: 0.1%.</summary>
        public const float ThrottleFloor = 0.001f;

        private Vessel _hookedVessel;
        private float _lastThrottle;
        private bool _holdingFloor;
        private bool _floorEnabled;

        private void Start()
        {
            GameEvents.OnGameSettingsApplied.Add(LoadSettings);
            LoadSettings();
        }

        private void Update()
        {
            // Polled rather than driven off onVesselChange: the active vessel may not be set yet
            // when a flight scene addon starts up, and this also picks up docking and undocking.
            if (_hookedVessel != FlightGlobals.ActiveVessel)
                HookVessel(FlightGlobals.ActiveVessel);
        }

        private void OnDestroy()
        {
            GameEvents.OnGameSettingsApplied.Remove(LoadSettings);
            HookVessel(null);
        }

        private void LoadSettings()
        {
            _floorEnabled = HighLogic.CurrentGame?.Parameters.CustomParams<RFGameParameters>()?.throttleDownKeepsEnginesLit ?? false;
        }

        private void HookVessel(Vessel v)
        {
            if (_hookedVessel != null)
                _hookedVessel.OnPreAutopilotUpdate -= ApplyThrottleFloor;

            _hookedVessel = v;
            _holdingFloor = false;

            if (_hookedVessel != null)
            {
                // Runs from FlightInputHandler.FixedUpdate() right after the keyboard input has been
                // pushed into ctrlState, and before any autopilot gets to touch it.
                _hookedVessel.OnPreAutopilotUpdate += ApplyThrottleFloor;
                _lastThrottle = _hookedVessel.ctrlState.mainThrottle;
            }
        }

        private void ApplyThrottleFloor(FlightCtrlState st)
        {
            if (!GameSettings.THROTTLE_DOWN.GetKey() || GameSettings.THROTTLE_CUTOFF.GetKey())
            {
                _holdingFloor = false;
            }
            // Requiring the previous throttle to be above the floor means a throttle that is already
            // at zero stays there, and that a cutoff from any other source is not undone.
            else if (_floorEnabled && st.mainThrottle < ThrottleFloor && _lastThrottle >= ThrottleFloor)
            {
                // The engines can't shut themselves off while we hold the floor, so the vessel only
                // needs to be scanned on the frame the key first bottoms the throttle out.
                if (!_holdingFloor)
                    _holdingFloor = AnyEngineBurning(_hookedVessel);

                if (_holdingFloor)
                {
                    st.mainThrottle = ThrottleFloor;
                    // FlightInputHandler keeps the master copy and re-derives the throttle from it
                    // every physics frame, so it has to be corrected too or the floor lasts one frame.
                    FlightInputHandler.state.mainThrottle = ThrottleFloor;
                }
            }

            _lastThrottle = st.mainThrottle;
        }

        private static bool AnyEngineBurning(Vessel v)
        {
            for (int i = v.parts.Count - 1; i >= 0; --i)
            {
                PartModuleList modules = v.parts[i].Modules;
                for (int j = modules.Count - 1; j >= 0; --j)
                {
                    if (modules[j] is ModuleEngines engine && engine.EngineIgnited && !engine.flameout && engine.currentThrottle > 0f)
                        return true;
                }
            }
            return false;
        }
    }
}
