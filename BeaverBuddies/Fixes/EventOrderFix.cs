using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Timberborn.SingletonSystem;

namespace BeaverBuddies.Fixes
{
    /**
     * The game's event bus calls an event's handlers in the order of a Dictionary, where a handler that registers
     * takes the place the last one to go left. Some handlers are one player's alone, and come and go as that player
     * opens and closes things: the batch control window's tabs, an automation input picker. So a handler that
     * registered after one of those had come and gone took a different place on each machine, and the handlers of
     * an event that act on the game ran in a different order on each. A platform taken down with goods piles on it:
     * each pile falls, moves, or merges into another and is deleted, so different piles were left on each machine,
     * the haulers went to different ones, and the game desynced (October 26, 2026).
     *
     * In a Co-op game an event's handlers are called in the order they registered, so the game's own run in the same
     * order on every machine, whatever else comes and goes between them.
     */
    [HarmonyPatch]
    static class EventOrderFix
    {
        private class Order
        {
            internal readonly Dictionary<Type, LinkedList<object>> ByType = new Dictionary<Type, LinkedList<object>>();
            internal readonly Dictionary<object, List<(LinkedList<object> List, LinkedListNode<object> Node)>> BySubscriber =
                new Dictionary<object, List<(LinkedList<object>, LinkedListNode<object>)>>();
        }

        private static readonly ConditionalWeakTable<SubscriptionRegistry, Order> Orders = new ConditionalWeakTable<SubscriptionRegistry, Order>();

        // Only once the game's own Add has taken it: it throws for a handler there already
        [HarmonyPatch(typeof(SubscriptionRegistry), nameof(SubscriptionRegistry.Add))]
        [HarmonyPostfix]
        static void AfterAdd(SubscriptionRegistry __instance, Type eventType, object subscriber)
        {
            Order order = Orders.GetOrCreateValue(__instance);
            if (!order.ByType.TryGetValue(eventType, out LinkedList<object> list))
            {
                order.ByType[eventType] = list = new LinkedList<object>();
            }
            if (!order.BySubscriber.TryGetValue(subscriber, out var nodes))
            {
                order.BySubscriber[subscriber] = nodes = new List<(LinkedList<object>, LinkedListNode<object>)>();
            }
            nodes.Add((list, list.AddLast(subscriber)));
        }

        [HarmonyPatch(typeof(SubscriptionRegistry), nameof(SubscriptionRegistry.RemoveAll))]
        [HarmonyPostfix]
        static void AfterRemoveAll(SubscriptionRegistry __instance, object subscriber)
        {
            if (!Orders.TryGetValue(__instance, out Order order) || !order.BySubscriber.TryGetValue(subscriber, out var nodes)) return;
            foreach (var (list, node) in nodes) list.Remove(node);
            order.BySubscriber.Remove(subscriber);
        }

        [HarmonyPatch(typeof(SubscriptionRegistry), nameof(SubscriptionRegistry.GetSubscriptions))]
        [HarmonyPrefix]
        static bool GetSubscriptions(SubscriptionRegistry __instance, Type eventType, ref IEnumerable<Subscription> __result)
        {
            if (IO.EventIO.IsNull || !Orders.TryGetValue(__instance, out Order order)
                || !order.ByType.TryGetValue(eventType, out LinkedList<object> list)
                || !__instance._subscriptions.TryGetValue(eventType, out Dictionary<object, Action<object>> actions)) return true;
            __result = InOrder(list, actions);
            return false;
        }

        /**
         * An event's handlers in the order they registered, as GetSubscriptions gives them in a Co-op game, for the
         * load routers, which build their own lists of them (see Performance.LoadEventRouter). Null where the game's
         * own order stands.
         */
        internal static Subscription[] Ordered(SubscriptionRegistry registry, Type eventType, Dictionary<object, Action<object>> actions)
        {
            if (IO.EventIO.IsNull || !Orders.TryGetValue(registry, out Order order)
                || !order.ByType.TryGetValue(eventType, out LinkedList<object> list)) return null;
            return InOrder(list, actions).ToArray();
        }

        // Lazily, as the game's own: the bus holds back registrations while it's posting
        private static IEnumerable<Subscription> InOrder(LinkedList<object> list, Dictionary<object, Action<object>> actions)
        {
            foreach (object subscriber in list)
            {
                if (actions.TryGetValue(subscriber, out Action<object> action)) yield return new Subscription(subscriber, action);
            }
        }
    }
}
