using BeaverBuddies.IO;
using HarmonyLib;
using System;
using Timberborn.BaseComponentSystem;
using Timberborn.EntitySystem;
using Timberborn.NaturalResourcesMoisture;

namespace BeaverBuddies.Fixes
{
    /**
     * The time a tree or crop takes to die from drought, flooding or a lack of
     * water is rolled from the random generator when it wakes up, and the game
     * saves only how far along it is, not the delay. A machine that loads the
     * host's save rolls its own delays from its own place in the random
     * sequence, so a plant that starts dying later has a different time left on
     * each machine. Rolling them from the plant's own id instead gives every
     * machine the same delay, and leaves the shared random sequence alone.
     */
    static class NaturalResourceDelayFix
    {
        public static void Begin(BaseComponent plant, out UnityEngine.Random.State? saved)
        {
            saved = null;
            if (EventIO.IsNull) return;
            Guid id = plant.GetComponent<EntityComponent>().EntityId;
            if (id == Guid.Empty) return;
            saved = UnityEngine.Random.state;
            UnityEngine.Random.InitState(BitConverter.ToInt32(id.ToByteArray(), 0));
        }

        public static void End(UnityEngine.Random.State? saved)
        {
            if (saved is { } state) UnityEngine.Random.state = state;
        }
    }

    [HarmonyPatch(typeof(WateredNaturalResource), nameof(WateredNaturalResource.Awake))]
    class WateredNaturalResourceDelayPatcher
    {
        static void Prefix(WateredNaturalResource __instance, out UnityEngine.Random.State? __state) => NaturalResourceDelayFix.Begin(__instance, out __state);
        static void Postfix(UnityEngine.Random.State? __state) => NaturalResourceDelayFix.End(__state);
    }

    [HarmonyPatch(typeof(AridNaturalResource), nameof(AridNaturalResource.Awake))]
    class AridNaturalResourceDelayPatcher
    {
        static void Prefix(AridNaturalResource __instance, out UnityEngine.Random.State? __state) => NaturalResourceDelayFix.Begin(__instance, out __state);
        static void Postfix(UnityEngine.Random.State? __state) => NaturalResourceDelayFix.End(__state);
    }

    [HarmonyPatch(typeof(LivingWaterNaturalResource), nameof(LivingWaterNaturalResource.Awake))]
    class LivingWaterNaturalResourceDelayPatcher
    {
        static void Prefix(LivingWaterNaturalResource __instance, out UnityEngine.Random.State? __state) => NaturalResourceDelayFix.Begin(__instance, out __state);
        static void Postfix(UnityEngine.Random.State? __state) => NaturalResourceDelayFix.End(__state);
    }
}
