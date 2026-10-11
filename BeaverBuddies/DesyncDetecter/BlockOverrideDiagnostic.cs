using HarmonyLib;
using System;
using Timberborn.BlockSystem;
using Timberborn.EntitySystem;

namespace BeaverBuddies.DesyncDetecter
{
    /**
     * A building placed over another block object asks the one it overlaps
     * for its components. When that fails, say which one it was: a placement
     * that throws halfway leaves its building on this machine alone.
     */
    [HarmonyPatch(typeof(OverridenBlockObjectService), "ProcessIntersectingBlockObject")]
    public class BlockOverrideDiagnostic
    {
        static Exception Finalizer(BlockObject blockObject, BlockObject intersection, Exception __exception)
        {
            if (__exception == null) return null;
            try
            {
                string Describe(BlockObject o)
                {
                    if (o == null) return "null";
                    var go = o.GameObject;
                    return $"{(go ? go.name : "(no GameObject)")} at {o.Coordinates}, finished {o.IsFinished}, positioned {o.Positioned}, GameObject active {(go ? go.activeInHierarchy.ToString() : "-")}";
                }
                Plugin.LogError($"[Override] {__exception.GetType().Name} placing {Describe(blockObject)} over {Describe(intersection)}");
            }
            catch (Exception e)
            {
                Plugin.LogError($"[Override] couldn't describe it: {e.Message}");
            }
            return __exception;
        }
    }
}
