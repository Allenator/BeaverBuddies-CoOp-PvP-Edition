using BeaverBuddies.IO;
using HarmonyLib;
using System;
using Timberborn.Autosaving;

namespace BeaverBuddies.Connect
{
    /**
     * A game hosted or joined autosaves as the player's settings say (see
     * MultiplayerSettings): how often, how many are kept, and none while the
     * game is paused, which only filled the autosaves with one moment and
     * pushed the ones before it out. Every game joined is saved under one
     * settlement, so the game's three were three for all of them. A game
     * alone autosaves as the game does.
     */
    public static class Autosaves
    {
        private static bool InMultiplayer => EventIO.Get() is ServerEventIO || EventIO.Get() is ClientEventIO;

        // The game reads its spec as it schedules the next autosave and as it deletes the oldest
        internal static void Apply(Autosaver autosaver)
        {
            if (!InMultiplayer) return;
            autosaver._autosaverSpec = autosaver._autosaverSpec with
            {
                FrequencyInMinutes = Settings.AutosaveMinutes,
                AutosavesPerSettlement = Settings.AutosavesKept,
            };
        }

        // Paused as the players chose (a guest keeping behind the host pauses for moments on its own, which doesn't count)
        internal static bool WaitForPlay =>
            InMultiplayer && Settings.SkipPausedAutosaves && SingletonManager.GetSingleton<ReplayService>()?.TargetSpeed == 0;
    }

    [HarmonyPatch(typeof(Autosaver), nameof(Autosaver.ScheduleNextSave))]
    static class AutosaverScheduleNextSavePatcher
    {
        static void Prefix(Autosaver __instance) => Autosaves.Apply(__instance);
    }

    [HarmonyPatch(typeof(Autosaver), nameof(Autosaver.DeleteExcessAutosaves), new Type[0])]
    static class AutosaverDeleteExcessAutosavesPatcher
    {
        static void Prefix(Autosaver __instance) => Autosaves.Apply(__instance);
    }

    /*
     * An autosave that's due waits while the game is paused, as the game has
     * it wait for anything else that blocks one, and is made as soon as it
     * plays again. The save made on leaving the game doesn't ask.
     */
    [HarmonyPatch(typeof(Autosaver), nameof(Autosaver.IsNotBlocked))]
    static class AutosaverIsNotBlockedPatcher
    {
        static void Postfix(ref bool __result)
        {
            if (__result && Autosaves.WaitForPlay) __result = false;
        }
    }
}
