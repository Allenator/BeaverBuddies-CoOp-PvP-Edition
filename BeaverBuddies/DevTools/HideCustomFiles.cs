using System;
using System.Collections.Generic;
using HarmonyLib;
using Timberborn.AutomationBuildings;
using Timberborn.DecalSystem;

namespace BeaverBuddies.DevTools
{
    /**
     * A test copy without the player's custom sounds and images
     * (BB_HIDE_CUSTOM_FILES=1, see BB_HIDE_CUSTOM_<COPY> in Tools/Tests/lib.sh):
     * a scenario's two copies share one Documents folder, so this is how one
     * of them lacks a file the other has (see Cosmetics.CustomFileNames).
     * Refresh finds nothing either. Only in the harness's copies.
     */
    static class HideCustomFiles
    {
        internal static bool Active => TestHarness.Active && Environment.GetEnvironmentVariable("BB_HIDE_CUSTOM_FILES") == "1";
    }

    [HarmonyPatch(typeof(UserDecalService), nameof(UserDecalService.GetCustomDecals))]
    static class HideCustomDecals
    {
        static bool Prepare() => HideCustomFiles.Active;

        static bool Prefix(ref IEnumerable<DecalSpec> __result)
        {
            __result = Array.Empty<DecalSpec>();
            return false;
        }
    }

    [HarmonyPatch(typeof(SpeakerSoundService), nameof(SpeakerSoundService.LoadCustomSounds))]
    static class HideCustomSounds
    {
        static bool Prepare() => HideCustomFiles.Active;

        static bool Prefix() => false;
    }
}
