using BeaverBuddies.IO;
using HarmonyLib;
using System;
using Timberborn.BaseComponentSystem;
using Timberborn.EntitySystem;
using Timberborn.NaturalResourcesContamination;
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
        /** While a plant wakes up: its own roll, which the range patchers use in place of any shared generator. */
        public static System.Random Override;

        public static void Begin(BaseComponent plant, out bool seeded)
        {
            seeded = false;
            if (EventIO.IsNull) return;
            Guid id = plant.GetComponent<EntityComponent>().EntityId;
            if (id == Guid.Empty) return;
            Override = new System.Random(BitConverter.ToInt32(id.ToByteArray(), 0));
            seeded = true;
        }

        public static void End(bool seeded)
        {
            if (seeded) Override = null;
        }
    }

    [HarmonyPatch(typeof(WateredNaturalResource), nameof(WateredNaturalResource.Awake))]
    class WateredNaturalResourceDelayPatcher
    {
        static void Prefix(WateredNaturalResource __instance, out bool __state) => NaturalResourceDelayFix.Begin(__instance, out __state);
        static void Postfix(bool __state) => NaturalResourceDelayFix.End(__state);
    }

    [HarmonyPatch(typeof(AridNaturalResource), nameof(AridNaturalResource.Awake))]
    class AridNaturalResourceDelayPatcher
    {
        static void Prefix(AridNaturalResource __instance, out bool __state) => NaturalResourceDelayFix.Begin(__instance, out __state);
        static void Postfix(bool __state) => NaturalResourceDelayFix.End(__state);
    }

    [HarmonyPatch(typeof(LivingWaterNaturalResource), nameof(LivingWaterNaturalResource.Awake))]
    class LivingWaterNaturalResourceDelayPatcher
    {
        static void Prefix(LivingWaterNaturalResource __instance, out bool __state) => NaturalResourceDelayFix.Begin(__instance, out __state);
        static void Postfix(bool __state) => NaturalResourceDelayFix.End(__state);
    }

    [HarmonyPatch(typeof(ContaminatedNaturalResource), nameof(ContaminatedNaturalResource.Awake))]
    class ContaminatedNaturalResourceDelayPatcher
    {
        static void Prefix(ContaminatedNaturalResource __instance, out bool __state) => NaturalResourceDelayFix.Begin(__instance, out __state);
        static void Postfix(bool __state) => NaturalResourceDelayFix.End(__state);
    }
}
