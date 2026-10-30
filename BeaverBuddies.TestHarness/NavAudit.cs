using HarmonyLib;
using System;
using Timberborn.BehaviorSystem;
using Timberborn.EntitySystem;
using Timberborn.WalkingSystem;
using UnityEngine;

namespace BeaverBuddies.DevTools
{
    /**
     * For tests (BB_NAV_AUDIT=1): each path a walker's found before the game's first tick, with where it stood, where
     * it was going and whether it found one. A walker's reachability isn't saved: the load finds every walker's path again
     * (NavMeshObserver.PostLoadEntity), and one it finds none for drops its destination, which its executor fails on
     * at the first tick. A walker with a path on one machine and none on another (a beaver walking into a Grill, with
     * no reachable walk on an x86-64 guest, say) fails at tick 1 on that one alone: it decides again there, draws
     * other random numbers, and the game splits, now and then at tick 3.
     */
    static class NavAudit
    {
        public static bool Active => TestHarness.Active && Environment.GetEnvironmentVariable("BB_NAV_AUDIT") == "1";

        private static bool _loading;
        private static int _found, _failed;

        [HarmonyPatch(typeof(ReplayService), nameof(ReplayService.PostLoad))]
        static class LoadStart
        {
            static bool Prepare() => Active;

            static void Postfix()
            {
                _loading = true;
                _found = _failed = 0;
            }
        }

        [HarmonyPatch(typeof(DesyncDetecter.TickLog), nameof(DesyncDetecter.TickLog.Record))]
        static class FirstTick
        {
            static bool Prepare() => Active;

            static void Prefix()
            {
                if (!_loading) return;
                _loading = false;
                Plugin.Log($"[NavAudit] before the first tick: {_found} paths found, {_failed} not");
            }
        }

        [HarmonyPatch(typeof(Walker), nameof(Walker.FindPath))]
        static class PathFound
        {
            static bool Prepare() => Active;

            static void Prefix(Walker __instance, out Vector3 __state) => __state = __instance.Transform.position;

            static void Postfix(Walker __instance, IDestination destination, ExecutorStatus __result, Vector3 __state)
            {
                if (!_loading) return;
                if (__result == ExecutorStatus.Failure) _failed++;
                else _found++;
                // Every path the load found, for two machines' lists to be set side by side
                string id = __instance.GetComponent<EntityComponent>().EntityId.ToString();
                Plugin.Log($"[NavAudit] {id} at ({__state.x:R}, {__state.y:R}, {__state.z:R}) {(__result == ExecutorStatus.Failure ? "found no path" : __result.ToString())} " +
                    $"to {DesyncDetecter.WalkerFindPathPatcher.GetDestinationString(destination)}");
            }
        }
    }
}
