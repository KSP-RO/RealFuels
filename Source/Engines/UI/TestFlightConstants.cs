namespace RealFuels
{
    /// <summary>
    /// Central store for TestFlight values that the engine config UI displays.
    /// These originate in TestFlight's engine failure and data system — if TestFlight
    /// changes failure mode weights, DU rewards, or the running data rate formula,
    /// update the constants here and the info panel will reflect the change automatically.
    ///
    /// Future: if TestFlight exposes these via a public API or reflection surface,
    /// this class is the right place to add that dynamic binding (see EngineConfigRP1Integration
    /// for the established pattern).
    /// </summary>
    internal static class TestFlightConstants
    {
        // --- Failure modes ---
        // Four possible engine failure modes, their display names, data units awarded,
        // and percentage likelihood of each (must sum to 100%).
        // Source: TestFlight core failure system.
        public static readonly string[] FailureTypeNames  = { "Shutdown", "Perf. Loss", "Reduced Thrust", "Explode" };
        public static readonly int[]    FailureTypeDU     = { 1000, 800, 700, 1000 };
        public static readonly float[]  FailureTypePercent = { 55.2f, 27.6f, 13.8f, 3.4f };

        // --- Data unit awards ---
        // DU awarded for an ignition failure.
        public const int IgnitionFailDU = 1000;

        // Maximum DU that can be earned in a single flight, regardless of events.
        public const int MaxDUPerFlight = 1000;

        // --- Running data rate ---
        // DU earned per second while the engine runs = RunningDataNumerator / ratedBurnTime.
        // i.e. one full rated burn always yields RunningDataNumerator DU of running data.
        public const float RunningDataNumerator = 640f;
    }
}
