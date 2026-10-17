using BeaverBuddies.IO;
using HarmonyLib;
using System;
using System.Diagnostics;
using Timberborn.SingletonSystem;
using Timberborn.TickSystem;

namespace BeaverBuddies.Performance
{
    /**
     * Logs a "[Hitch]" line in Player.log for every frame of a multiplayer
     * game that takes over HitchMs, with what the frame spent its time on:
     * garbage collections, the game's ticking (and its slowest bucket),
     * the host's events, the wait for the parallel tick, and the slowest
     * singleton of each kind. A hitch on the host stalls every client
     * waiting for its next tick, so these lines on the host say what the
     * clients waited on.
     *
     * It times what the game already runs, a pair of clock reads per
     * singleton and bucket, and allocates nothing unless it logs. Nothing
     * here touches the game.
     */
    public static class HitchWatch
    {
        // Tests profiling stutters set it lower, with BB_HITCH_MS
        internal static readonly bool Profiling =
            double.TryParse(Environment.GetEnvironmentVariable("BB_HITCH_MS"), out _profilingMs);
        private static double _profilingMs;
        private static double HitchMs => Profiling ? _profilingMs : 250;

        // Profiling only: frame pacing over the last stretch
        private static long _statsStart;
        private static bool _stalled;
        private static int _framesStalled;
        private static int _frames, _framesNoTick, _framesSlow, _gcsAtStart;
        private static long _heapAtStart;
        private static double _maxFrame, _sumFrame, _sumTicking;

        private static readonly double MsPerTimestamp = 1000.0 / Stopwatch.Frequency;

        private static int _frame;
        private static bool _wasInGame;
        private static long _frameStart;
        private static int _gcAtStart;
        private static long _lastGc;
        private static bool _toldMode;

        private static long _ticking, _io, _parallelWait;
        private static int _buckets;
        private static long _slowestBucket;
        private static int _slowestBucketIndex;
        private static Slowest _tick, _update, _lateUpdate;
        private static string _slowestPart;
        private static long _slowestPartTime;

        private struct Slowest
        {
            public long Time;
            public object Who;

            public void Note(long time, object who)
            {
                if (time <= Time) return;
                Time = time;
                Who = who;
            }

            public string Describe(string kind)
            {
                return Who == null ? "" : $", slowest {kind} {Who.GetType().Name} {Ms(Time):F0} ms";
            }
        }

        public static long Now => Stopwatch.GetTimestamp();

        private static double Ms(long timestamps) => timestamps * MsPerTimestamp;

        // As the game updates its singletons. Two lifecycle services (the
        // scene's and a lasting one) update every frame, so a frame is
        // Unity's, counted from the first of them
        internal static void FrameStarted()
        {
            int frame = UnityEngine.Time.frameCount;
            if (frame == _frame) return;
            long now = Now;
            int gc = GC.CollectionCount(0);
            // The frames that loaded a game don't count. Nor do the ones of
            // a game in the background, which may be throttled
            bool inGame = !EventIO.IsNull;
            if (inGame && _wasInGame && frame == _frame + 1 && (Profiling || UnityEngine.Application.isFocused))
            {
                double ms = Ms(now - _frameStart);
                if (ms > HitchMs) Report(ms, gc - _gcAtStart, now);
                if (Profiling) Pace(ms, now);
            }
            if (gc != _gcAtStart) _lastGc = now;
            _frame = frame;
            _wasInGame = inGame;
            _frameStart = now;
            _gcAtStart = gc;
            _ticking = _io = _parallelWait = _slowestBucket = 0;
            _buckets = 0;
            _stalled = false;
            _tick = _update = _lateUpdate = default;
            _slowestPart = null;
            _slowestPartTime = 0;
        }

        // A named part of BeaverBuddies' own work, for the report
        internal static void Part(string name, long start)
        {
            long took = Now - start;
            if (took <= _slowestPartTime) return;
            _slowestPartTime = took;
            _slowestPart = name;
        }

        private static void Report(double ms, int collections, long now)
        {
            if (!_toldMode)
            {
                _toldMode = true;
                Plugin.LogWarning($"[Hitch] Incremental GC: {UnityEngine.Scripting.GarbageCollector.isIncremental}");
            }
            var replay = SingletonManager.GetSingleton<ReplayService>();
            string gc = collections == 0
                ? $"no GC (last {Ms(now - _lastGc) / 1000:F0} s ago)"
                : $"{collections} GC";
            Plugin.LogWarning($"[Hitch] {ms:F0} ms frame at tick {replay?.TicksSinceLoad}: {gc}, " +
                $"heap {GC.GetTotalMemory(false) >> 20} MB, " +
                $"ticking {Ms(_ticking):F0} ms in {_buckets} buckets (slowest #{_slowestBucketIndex} {Ms(_slowestBucket):F0} ms), " +
                $"tick IO {Ms(_io):F0} ms, parallel wait {Ms(_parallelWait):F0} ms" +
                _tick.Describe("tick") + _update.Describe("update") + _lateUpdate.Describe("late update") +
                (_slowestPart == null ? "" : $", slowest part {_slowestPart} {Ms(_slowestPartTime):F0} ms") +
                $", outside ticking {ms - Ms(_ticking):F0} ms");
        }

        private static void Pace(double ms, long now)
        {
            _frames++;
            _sumFrame += ms;
            _sumTicking += Ms(_ticking);
            if (ms > _maxFrame) _maxFrame = ms;
            if (ms > 33) _framesSlow++;
            if (_buckets == 0) _framesNoTick++;
            if (_stalled) _framesStalled++;
            if (_statsStart == 0) { _statsStart = now; _heapAtStart = GC.GetTotalMemory(false); _gcsAtStart = GC.CollectionCount(0); }
            if (Ms(now - _statsStart) < 10000) return;
            var replay = SingletonManager.GetSingleton<ReplayService>();
            Plugin.LogWarning($"[Pace] tick {replay?.TicksSinceLoad} speed {replay?.TargetSpeed}: {_frames} frames, " +
                $"avg {_sumFrame / _frames:F1} ms, max {_maxFrame:F0} ms, {_framesSlow} over 33 ms, " +
                $"{_framesNoTick} with no bucket ticked, {_framesStalled} stalled for the host, margin {replay?.Margin * 1000:F0} ms, pace {replay?.BucketBoost:F2}, ticking avg {_sumTicking / _frames:F1} ms, " +
                (GC.CollectionCount(0) == _gcsAtStart ? $"allocated {(GC.GetTotalMemory(false) - _heapAtStart) >> 20} MB" : "a GC ran"));
            _statsStart = now;
            _heapAtStart = GC.GetTotalMemory(false);
            _gcsAtStart = GC.CollectionCount(0);
            _frames = _framesNoTick = _framesSlow = _framesStalled = 0;
            _maxFrame = _sumFrame = _sumTicking = 0;
        }

        internal static void Stalled() => _stalled = true;

        internal static void Ticked(long start) => _ticking += Now - start;

        internal static void TickedIO(long start) => _io += Now - start;

        internal static void TickedBucket(int index, long start)
        {
            long took = Now - start;
            _buckets++;
            if (took <= _slowestBucket) return;
            _slowestBucket = took;
            _slowestBucketIndex = index;
        }

        internal static void WaitedForParallelTick(long start) => _parallelWait += Now - start;

        internal static void TickedSingleton(object singleton, long start) => _tick.Note(Now - start, singleton);

        internal static void UpdatedSingleton(object singleton, long start) => _update.Note(Now - start, singleton);

        internal static void LateUpdatedSingleton(object singleton, long start) => _lateUpdate.Note(Now - start, singleton);
    }

    [ManualMethodOverwrite]
    /*
        for (int i = 0; i < _updatableSingletons.Length; i++)
        {
            _updatableSingletons[i].UpdateSingleton();
        }
     */
    [HarmonyPatch(typeof(SingletonLifecycleService), nameof(SingletonLifecycleService.UpdateSingletons))]
    static class HitchWatchUpdateSingletonsPatcher
    {
        static bool Prefix(SingletonLifecycleService __instance)
        {
            HitchWatch.FrameStarted();
            if (HitchWatch.Profiling) Util.LeakFinder.Updating(__instance);
            var singletons = __instance._updatableSingletons;
            for (int i = 0; i < singletons.Length; i++)
            {
                long start = HitchWatch.Now;
                singletons[i].UpdateSingleton();
                HitchWatch.UpdatedSingleton(singletons[i], start);
            }
            return false;
        }
    }

    [ManualMethodOverwrite]
    /*
        for (int i = 0; i < _lateUpdatableSingletons.Length; i++)
        {
            _lateUpdatableSingletons[i].LateUpdateSingleton();
        }
     */
    [HarmonyPatch(typeof(SingletonLifecycleService), nameof(SingletonLifecycleService.LateUpdateSingletons))]
    static class HitchWatchLateUpdateSingletonsPatcher
    {
        static bool Prefix(SingletonLifecycleService __instance)
        {
            var singletons = __instance._lateUpdatableSingletons;
            for (int i = 0; i < singletons.Length; i++)
            {
                long start = HitchWatch.Now;
                singletons[i].LateUpdateSingleton();
                HitchWatch.LateUpdatedSingleton(singletons[i], start);
            }
            return false;
        }
    }

    [ManualMethodOverwrite]
    /*
        for (int i = 0; i < _tickableSingletons.Length; i++)
        {
            _tickableSingletons[i].Tick();
        }
     */
    [HarmonyPatch(typeof(TickableSingletonService), nameof(TickableSingletonService.TickSingletons))]
    static class HitchWatchTickSingletonsPatcher
    {
        static bool Prefix(TickableSingletonService __instance)
        {
            var singletons = __instance._tickableSingletons;
            for (int i = 0; i < singletons.Length; i++)
            {
                long start = HitchWatch.Now;
                singletons[i].Tick();
                HitchWatch.TickedSingleton(singletons[i]._tickableSingleton, start);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(TickableSingletonService), nameof(TickableSingletonService.FinishParallelTick))]
    static class HitchWatchFinishParallelTickPatcher
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(out long __state)
        {
            __state = HitchWatch.Now;
        }

        [HarmonyPriority(Priority.First)]
        static void Postfix(long __state)
        {
            HitchWatch.WaitedForParallelTick(__state);
        }
    }
}
