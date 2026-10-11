using HarmonyLib;
using System.Collections.Generic;
using Timberborn.EntitySystem;
using Timberborn.TickSystem;
using UnityEngine;

namespace BeaverBuddies.Fixes
{
    /**
     * The game takes a deleted entity out of its registry at once but has Unity
     * destroy its GameObject at the end of the frame, and the game's own
     * "is it still there" test is whether the GameObject is. A tick runs over
     * several frames, so whether an entity ticked after a deletion still sees
     * the entity depends on where a frame boundary fell: one machine's gatherer
     * went on harvesting a bush that another's found gone, a builder finished
     * a site the other saw demolished.
     *
     * A deletion no longer destroys the GameObject. It's queued, and the queue
     * is destroyed at points that are the same on every machine: after each
     * bucket of a tick and after each event that is replayed. Until then the
     * entity is out of the registry but not gone, as in the game, and the code
     * that deleted it can go on using it; after, it's gone for everyone's next
     * turn.
     */
    [HarmonyPatch(typeof(EntityService), nameof(EntityService.Delete))]
    public class DeferredDestroyFix
    {
        private static readonly List<GameObject> pending = new();

        // After the tracing prefix, which sees the entity before it's gone
        [HarmonyPriority(Priority.Last)]
        static bool Prefix(EntityService __instance, Timberborn.BaseComponentSystem.BaseComponent entity)
        {
            if (IO.EventIO.IsNull) return true;
            EntityComponent component = entity.GetComponent<EntityComponent>();
            component.Delete();
            __instance._entityRegistry.RemoveEntity(component);
            pending.Add(component.GameObject);
            return false;
        }

        /** Destroys what was deleted since the last time. */
        public static void Flush()
        {
            // What a destroyed object's OnDestroy deletes goes in the next round
            while (pending.Count > 0)
            {
                var doomed = pending.ToArray();
                pending.Clear();
                foreach (GameObject go in doomed)
                {
                    if (go) Object.DestroyImmediate(go);
                }
            }
        }

        public static void Reset() => pending.Clear();
    }

    [HarmonyPatch(typeof(TickableBucketService), "TickNextBucket")]
    public class DeferredDestroyBucketFlush
    {
        static void Postfix() => DeferredDestroyFix.Flush();
    }
}
