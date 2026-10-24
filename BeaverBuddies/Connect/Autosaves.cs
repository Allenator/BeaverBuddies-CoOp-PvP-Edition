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
     * settlement, Multiplayer, so the game's three were three for all of
     * them, and a game of that settlement opened alone goes by the settings
     * too: otherwise its first autosave, or the save as it's left, deleted
     * all but the newest three of every co-op game's. Other games alone
     * autosave as the game does.
     */
    public static class Autosaves
    {
        // The settlement every game joined is saved under (see ClientConnectionService), as is a game hosted from one
        // of those saves: its autosaves are all the co-op games', on this machine
        internal const string SharedSettlement = "Multiplayer";

        private static bool InMultiplayer => EventIO.Get() is ServerEventIO || EventIO.Get() is ClientEventIO;

        private static bool Shared(Autosaver autosaver) =>
            autosaver._settlementReferenceService.SettlementReference?.SettlementName == SharedSettlement;

        // The game reads its spec as it schedules the next autosave and as it deletes the oldest
        internal static void Apply(Autosaver autosaver)
        {
            if (!InMultiplayer && !Shared(autosaver)) return;
            autosaver._autosaverSpec = autosaver._autosaverSpec with
            {
                FrequencyInMinutes = Settings.AutosaveMinutes,
                AutosavesPerSettlement = Settings.AutosavesKept,
            };
        }

        // Paused as the players chose (a guest keeping behind the host pauses for moments on its own, which doesn't
        // count), or alone, as the player did (the game's pause stops Unity's time)
        internal static bool WaitForPlay(Autosaver autosaver) =>
            Settings.SkipPausedAutosaves && (InMultiplayer ? SingletonManager.GetSingleton<ReplayService>()?.TargetSpeed == 0
                : Shared(autosaver) && UnityEngine.Time.timeScale == 0);
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
        static void Postfix(Autosaver __instance, ref bool __result)
        {
            if (__result && Autosaves.WaitForPlay(__instance)) __result = false;
        }
    }
}
