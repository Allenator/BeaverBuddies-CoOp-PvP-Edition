namespace BeaverBuddies.Performance;

// The settings the ported optimizations read. They follow PerformanceSettings.Enabled;
// T3MP's per-feature command line switches are not kept.
internal static class LoadSettings
{
    public static readonly bool EnableEventBusFastDelegates = true;
    public static readonly bool EnableLoadEventRouting = true;
    public static readonly bool EnablePreparedStatusIcons = true;
    public static readonly bool EnableTickFrontier = true;
    public static readonly bool EnableWalkerSpeedDelegates = true;
    public static readonly bool EnableTerrainNeighborVisits = true;
    public static readonly bool EnableAllowedGoodRows = true;
    public static readonly bool EnableYielderReachabilitySkip = true;
    public static readonly bool EnableMovementSubsteps = true;
    public static readonly bool CoarseMovementSubsteps = true;
    public const float LoadSlowCallThresholdMilliseconds = 250f;
}
