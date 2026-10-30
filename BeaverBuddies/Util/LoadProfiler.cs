using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Timberborn.SingletonSystem;

namespace BeaverBuddies.Util
{
    /**
     * In a Co-op game and in the test harness, times each singleton's Load and
     * PostLoad as a game loads and logs the slow ones, and the total of each
     * step: where a load spends its time, the mods' share included. A player's
     * load that takes half a minute says why in their log. Only while the
     * game's load is the one copied here (LoadBodies.LoadProfile), and while
     * nothing but the load's GC budget is patched onto the four steps it runs
     * itself: that one's hooks it calls where the steps would have, so a
     * Windows or Wine load keeps its budget, which the copy alone would lose.
     */
    [ManualMethodOverwrite]
    /*
        10/27/2026 (1.1.2.8), each step a private method looping over its singletons, as Step does
        public void LoadAll()
        {
            _loadableSingletons = _singletonRepository.GetSingletons<ILoadableSingleton>().ToImmutableArray();
            _nonSingletonLoaders = _singletonRepository.GetSingletons<INonSingletonLoader>().ToImmutableArray();
            _postLoadableSingletons = _singletonRepository.GetSingletons<IPostLoadableSingleton>().ToImmutableArray();
            _nonSingletonPostLoaders = _singletonRepository.GetSingletons<INonSingletonPostLoader>().ToImmutableArray();
            _unloadableSingletons = _singletonRepository.GetSingletons<IUnloadableSingleton>().ToImmutableArray();
            _updatableSingletons = _singletonRepository.GetSingletons<IUpdatableSingleton>().ToImmutableArray();
            _lateUpdatableSingletons = _singletonRepository.GetSingletons<ILateUpdatableSingleton>().ToImmutableArray();
            LoadSingletons();
            LoadNonSingletons();
            PostLoadSingletons();
            PostLoadNonSingletons();
            _initializedSuccessfully = true;
        }
     */
    [HarmonyPatch(typeof(SingletonLifecycleService), nameof(SingletonLifecycleService.LoadAll))]
    class LoadProfiler
    {
        const long SlowMs = 100;

        // The four steps of LoadAll, which the copy runs itself
        static readonly MethodBase[] Steps = new[] { "LoadSingletons", "LoadNonSingletons", "PostLoadSingletons", "PostLoadNonSingletons" }
            .Select(name => AccessTools.Method(typeof(SingletonLifecycleService), name)).ToArray();

        // Last, so the performance patches' own LoadAll prefixes open their scopes first: this one runs the whole load
        [HarmonyPriority(Priority.Last)]
        static bool Prefix(SingletonLifecycleService __instance)
        {
            if (!DevTools.HarnessFlags.Active && IO.EventIO.IsNull) return true;
            // Only while the game loads as the copy below does: a game update that changes it has the game's own
            // load run, and the log say so, not this copy of the old one
            if (!Performance.LoadCompatibility.Reviewed(Performance.LoadBodies.LoadProfile)) return true;
            // Anything patched onto the steps but the GC budget would be passed by: the game's own load runs instead
            if (!Performance.LoadCompatibility.Unmodified(Steps, Performance.LoadGcBudget.Owner)) return true;
            var s = __instance;
            s._loadableSingletons = s._singletonRepository.GetSingletons<ILoadableSingleton>().ToImmutableArray();
            s._nonSingletonLoaders = s._singletonRepository.GetSingletons<INonSingletonLoader>().ToImmutableArray();
            s._postLoadableSingletons = s._singletonRepository.GetSingletons<IPostLoadableSingleton>().ToImmutableArray();
            s._nonSingletonPostLoaders = s._singletonRepository.GetSingletons<INonSingletonPostLoader>().ToImmutableArray();
            s._unloadableSingletons = s._singletonRepository.GetSingletons<IUnloadableSingleton>().ToImmutableArray();
            s._updatableSingletons = s._singletonRepository.GetSingletons<IUpdatableSingleton>().ToImmutableArray();
            s._lateUpdatableSingletons = s._singletonRepository.GetSingletons<ILateUpdatableSingleton>().ToImmutableArray();
            Performance.LoadGcBudget.BeforeLoadSteps(s);
            Step("Load", s._loadableSingletons, x => x.Load());
            Performance.LoadGcBudget.AfterLoadStep();
            Step("LoadNonSingletons", s._nonSingletonLoaders, x => x.LoadNonSingletons());
            Performance.LoadGcBudget.AfterLoadStep();
            Step("PostLoad", s._postLoadableSingletons, x => x.PostLoad());
            Performance.LoadGcBudget.AfterLoadStep();
            Step("PostLoadNonSingletons", s._nonSingletonPostLoaders, x => x.PostLoadNonSingletons());
            Performance.LoadGcBudget.AfterLoadStep();
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
