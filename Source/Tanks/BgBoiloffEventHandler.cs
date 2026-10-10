using ROUtils;
using UnityEngine.Profiling;

namespace RealFuels.Tanks
{
    /// <summary>
    /// Used for persisting boiloff data from background updates.
    /// This is done to avoid writing those back to the module values on every fixed frame.
    /// </summary>
    public class BgBoiloffEventHandler : HostedSingleton
    {
        public BgBoiloffEventHandler(SingletonHost host) : base(host) { }

        public override void Awake()
        {
            GameEvents.onProtoPartModuleSnapshotLoad.Add(OnProtoPartModuleSnapshotLoad);
            GameEvents.onGameStateSave.Add(OnGameStateSave);
        }

        /// <summary>
        /// onProtoPartModuleSnapshotLoad fires before ProtoPartModuleSnapshot.LoadModule(moduleValues, ...)
        /// </summary>
        /// <param name="action"></param>
        private void OnProtoPartModuleSnapshotLoad(GameEvents.FromToAction<ProtoPartModuleSnapshot, ConfigNode> action)
        {
            if (ModuleFuelTanks.bgCache.TryGetValue(action.from, out BgBoiloffCache cache))
                FlushToModuleValues(action.from, cache);
        }

        /// <summary>
        /// onGameStateSave fires before Game.Save() serializes to disk
        /// </summary>
        /// <param name="_"></param>
        private void OnGameStateSave(ConfigNode _)
        {
            Profiler.BeginSample("BgBoiloffEventHandler.OnGameStateSave");
            double ut = Planetarium.GetUniversalTime();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                if (vessel.loaded) continue;

                foreach (ProtoPartSnapshot part in vessel.protoVessel.protoPartSnapshots)
                {
                    foreach (ProtoPartModuleSnapshot module in part.modules)
                    {
                        if (ModuleFuelTanks.bgCache.TryGetValue(module, out BgBoiloffCache cache))
                            FlushToModuleValues(module, cache, ut);
                    }
                }
            }
            Profiler.EndSample();
        }

        private static void FlushToModuleValues(ProtoPartModuleSnapshot module, BgBoiloffCache cache, double ut = -1d)
        {
            // Under very high timewarp it is theoretically possible that using current UT could be a large leap from actual last background update.
            if (ut < 0d) ut = Planetarium.GetUniversalTime();
            module.moduleValues.SetValue(nameof(ModuleFuelTanks.bgBoiloffLastUpdate), ut);
            ModuleFuelTanks.PersistBackgroundTankTemps(module, cache);
        }
    }
}
