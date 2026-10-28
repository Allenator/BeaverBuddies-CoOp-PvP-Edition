using HarmonyLib;
using System;

namespace BeaverBuddies.DesyncDetecter
{
    /**
     * The tick's hash line, "Tick N IO done; Order hash …; Move hash …; Random s0 …": the first tick two machines' lines
     * differ at is where a desync began. Every tick, it was 13 MB of a night's 20 MB log (October 25, 2026). It's logged
     * every 100th tick, and every tick with tracing on and in test copies, whose scenarios and helpers read it; the
     * last 1000 ticks' are kept, nothing allocated, and written out in the same lines when they're wanted: at a desync,
     * at a resync, as a crash's report is made.
     */
    public static class TickLog
    {
        private const int Every = 100;
        private const int Kept = 1000;

        private static readonly int[] _ticks = new int[Kept];
        private static readonly uint[] _order = new uint[Kept];
        private static readonly uint[] _move = new uint[Kept];
        private static readonly uint[] _random = new uint[Kept];
        private static int _next, _count;

        // A test copy logs as a player's game does with BB_TICKLOG_LIVE
        private static readonly bool LiveInTests = Environment.GetEnvironmentVariable("BB_TICKLOG_LIVE") == "1";
        private static bool EveryTick => Settings.Debug || DevTools.TestHarness.Active && !LiveInTests;

        public static void Record(int tick, uint order, uint move, uint random)
        {
            _ticks[_next] = tick;
            _order[_next] = order;
            _move[_next] = move;
            _random[_next] = random;
            _next = (_next + 1) % Kept;
            if (_count < Kept) _count++;
            if (EveryTick || tick % Every == 0) Plugin.Log(Line(tick, order, move, random));
        }

        /** The kept ticks not logged yet, and then none: those of the game a load replaces mean nothing in the next. */
        public static void Dump(string why)
        {
            if (_count == 0) return;
            if (!EveryTick)
            {
                Plugin.Log($"The last {_count} ticks' hashes, {why}:");
                for (int i = 0; i < _count; i++)
                {
                    int at = (_next - _count + i + Kept) % Kept;
                    if (_ticks[at] % Every != 0) Plugin.Log(Line(_ticks[at], _order[at], _move[at], _random[at]));
                }
            }
            _count = 0;
        }

        /** A game loaded: the ticks kept were another's. */
        public static void Forget() => _count = 0;

        // As it's always been, for the scripts that set machines' logs side by side
        private static string Line(int tick, uint order, uint move, uint random) =>
            $"Tick {tick:D5} IO done; Order hash: {order:X8}; Move hash: {move:X8}; Random s0: {random:X8}";

        // The report a crash makes takes Player.log as it is: the ticks before it go in first
        [HarmonyPatch(typeof(Timberborn.ErrorReporting.ErrorReporter), nameof(Timberborn.ErrorReporting.ErrorReporter.CreateErrorReport))]
        class ErrorReportPatcher
        {
            static void Prefix()
            {
                try { Dump("before the crash's report"); }
                catch (Exception) { }
            }
        }
    }
}
