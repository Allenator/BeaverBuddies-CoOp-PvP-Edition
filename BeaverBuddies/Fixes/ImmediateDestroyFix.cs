using HarmonyLib;
using Timberborn.EntitySystem;
using UnityEngine;

namespace BeaverBuddies.Fixes
{
    /**
     * The game removes an entity from its registry and tickables at once but
     * leaves Unity to destroy its GameObject at the end of the frame, and
     * until then the doomed entity still looks alive to whatever holds it: a
     * builder ticking against a demolished site finishes it. Ticks run in
     * buckets spread over frames, so whether a builder's turn comes before
     * or after that frame ends depends on timing, and the machines differ
     * (one finished the site, the other saw it gone). Destroyed on the spot,
     * it's gone for everyone's next turn.
     *
     * Only for what a player's demolition deletes, asked for outright (see
     * BuildingsDeconstructedEvent). A deletion the game makes in the middle
     * of something else (a tree that was cut, a tree a new building stands
     * on) goes on using the entity afterwards, and fails on a GameObject
     * that is already gone: the placement then throws halfway.
     */
    [HarmonyPatch(typeof(EntityService), nameof(EntityService.Delete))]
    public class ImmediateDestroyFix
    {
        static void Prefix(Timberborn.BaseComponentSystem.BaseComponent entity, out GameObject __state)
        {
            __state = entity ? entity.GameObject : null;
        }

        // Set while a player's demolition deletes what it was asked to
        internal static bool Immediate;

        static void Postfix(GameObject __state)
        {
            if (!__state || !Immediate || IO.EventIO.IsNull) return;
            Object.DestroyImmediate(__state);
        }
    }
}
