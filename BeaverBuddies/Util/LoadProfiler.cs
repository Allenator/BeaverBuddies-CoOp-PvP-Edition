using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using Timberborn.SingletonSystem;

namespace BeaverBuddies.Util
{
    /**
     * In the test harness, times each singleton's Load and PostLoad as a game
     * loads and logs the slow ones, and the total of each step: where a load
     * spends its time, the mods' share included.
     */
    [HarmonyPatch(typeof(SingletonLifecycleService), nameof(SingletonLifecycleService.LoadAll))]
    class LoadProfiler
    {
        const long SlowMs = 100;

        // Last, so the performance patches' own LoadAll prefixes open their scopes first: this one runs the whole load
        [HarmonyPriority(Priority.Last)]
        static bool Prefix(SingletonLifecycleService __instance)
        {
            if (!DevTools.TestHarness.Active) return true;
            var s = __instance;
            s._loadableSingletons = s._singletonRepository.GetSingletons<ILoadableSingleton>().ToImmutableArray();
            s._nonSingletonLoaders = s._singletonRepository.GetSingletons<INonSingletonLoader>().ToImmutableArray();
            s._postLoadableSingletons = s._singletonRepository.GetSingletons<IPostLoadableSingleton>().ToImmutableArray();
            s._nonSingletonPostLoaders = s._singletonRepository.GetSingletons<INonSingletonPostLoader>().ToImmutableArray();
            s._unloadableSingletons = s._singletonRepository.GetSingletons<IUnloadableSingleton>().ToImmutableArray();
            s._updatableSingletons = s._singletonRepository.GetSingletons<IUpdatableSingleton>().ToImmutableArray();
            s._lateUpdatableSingletons = s._singletonRepository.GetSingletons<ILateUpdatableSingleton>().ToImmutableArray();
            Step("Load", s._loadableSingletons, x => x.Load());
            Step("LoadNonSingletons", s._nonSingletonLoaders, x => x.LoadNonSingletons());
            Step("PostLoad", s._postLoadableSingletons, x => x.PostLoad());
            Step("PostLoadNonSingletons", s._nonSingletonPostLoaders, x => x.PostLoadNonSingletons());
            s._initializedSuccessfully = true;
            return false;
        }

        static void Step<T>(string name, IEnumerable<T> items, Action<T> run)
        {
            var total = Stopwatch.StartNew();
            var slow = new List<string>();
            foreach (T item in items)
            {
                var one = Stopwatch.StartNew();
                run(item);
                if (one.ElapsedMilliseconds >= SlowMs) slow.Add($"{item.GetType().Name} {one.ElapsedMilliseconds}ms");
            }
            Plugin.Log($"[Load] {name} took {total.ElapsedMilliseconds}ms{(slow.Count > 0 ? "; slow: " + string.Join(", ", slow) : "")}");
        }
    }
}
