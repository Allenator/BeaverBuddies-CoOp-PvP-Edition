using System;

namespace BeaverBuddies.Performance;

// The settings the ported load optimizations read. They follow PerformanceSettings.Enabled;
// T3MP's per-feature command line switches are not kept.
internal static class LoadSettings
{
    public static readonly bool EnableEventBusFastDelegates = true;
    public static readonly bool EnableLoadEventRouting = true;
    public static readonly bool EnablePreparedStatusIcons = true;
    public const float LoadSlowCallThresholdMilliseconds = 250f;
}
