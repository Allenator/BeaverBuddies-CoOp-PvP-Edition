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
        // Tests profiling stutters set it lower, with BB_HITCH_MS, which the test harness reads
        internal static bool Profiling => DevTools.HarnessFlags.HitchMs != null;
        private static double HitchMs => DevTools.HarnessFlags.HitchMs ?? 250;

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
            if (inGame && _wasInGame && frame == _frame + 1)
            {
                double ms = Ms(now - _frameStart);
                LivePace(ms, now);
                if (Profiling || UnityEngine.Application.isFocused)
                {
                    if (ms > HitchMs) Report(ms, gc - _gcAtStart, now);
                    if (Profiling) Pace(ms, now);
                }
            }
            else if (!inGame) _liveStart = 0;
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

        // Every Co-op game, focused or not: how its frames and ticks went over the last ten minutes, and its memory,
        // so a session's logs say what each player's game did (a guest's frame rate, collections, the speed it got)
        // Tests take it shorter, with BB_PACE_SECONDS (read by the test harness, so in its copies only: a stray 0 would
        // log every frame)
        private static double LivePaceSeconds => DevTools.HarnessFlags.PaceSeconds ?? 600;
        private static long _liveStart;
        private static int _liveFrames, _liveUnfocused, _liveGcs, _liveTicks;
        private static double _liveSum, _liveMax;
        // Frame times by the millisecond, the last for 255 ms and over: the 95th percentile with nothing allocated
        private static readonly int[] _liveFrameTimes = new int[256];

        private static void LivePace(double ms, long now)
        {
            var replay = SingletonManager.GetSingleton<ReplayService>();
            if (replay == null) return;
            // A load starts the ticks again, and the window with them
            if (_liveStart == 0 || replay.TicksSinceLoad < _liveTicks)
            {
                _liveStart = now;
                _liveTicks = replay.TicksSinceLoad;
                _liveGcs = GC.CollectionCount(0);
                _liveFrames = _liveUnfocused = 0;
                _liveSum = _liveMax = 0;
                Array.Clear(_liveFrameTimes, 0, _liveFrameTimes.Length);
            }
            _liveFrames++;
            _liveSum += ms;
            if (ms > _liveMax) _liveMax = ms;
            _liveFrameTimes[Math.Min(_liveFrameTimes.Length - 1, (int)ms)]++;
            if (!UnityEngine.Application.isFocused) _liveUnfocused++;
            double seconds = Ms(now - _liveStart) / 1000;
            if (seconds < LivePaceSeconds) return;
            int under = _liveFrameTimes.Length, seen = 0;
            for (int i = 0; i < _liveFrameTimes.Length; i++)
            {
                seen += _liveFrameTimes[i];
                if (seen >= _liveFrames * 0.95) { under = i + 1; break; }
            }
            Plugin.Log($"[Pace] {seconds:F0} s to tick {replay.TicksSinceLoad}: {(replay.TicksSinceLoad - _liveTicks) / seconds:F1} ticks a second " +
                $"at speed {replay.TargetSpeed} (time scale {UnityEngine.Time.timeScale:F2}), {_liveFrames} frames, avg {_liveSum / _liveFrames:F1} ms, " +
                $"95% under {under} ms, max {_liveMax:F0} ms, {_liveUnfocused} unfocused, " +
                $"{GC.CollectionCount(0) - _liveGcs} collections, heap {GC.GetTotalMemory(false) >> 20} MB");
            _liveStart = 0;
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
