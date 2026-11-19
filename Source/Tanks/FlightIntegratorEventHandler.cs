using RealFuels.Harmony;
using ROUtils;

namespace RealFuels.Tanks
{
    /// <summary>
    /// Invalidates the PatchFlightIntegrator tank cache on vessel changes.
    /// </summary>
    public class FlightIntegratorEventHandler : HostedSingleton
    {
        public FlightIntegratorEventHandler(SingletonHost host) : base(host) { }

        public override void Awake()
        {
            GameEvents.onVesselWasModified.Add(OnVesselWasModified);
        }

        private void OnVesselWasModified(Vessel v) => PatchFlightIntegrator.Invalidate(v);
    }
}
