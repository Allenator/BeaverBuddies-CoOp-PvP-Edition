using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Timberborn.AutomationBuildings;
using Timberborn.DecalSystem;

namespace BeaverBuddies.Cosmetics
{
    /**
     * Custom files: a speaker's sound and a banner's or a tail's image are
     * synced by name (a file in Documents/Timberborn's Sounds, Banners or
     * Tails), and every machine keeps the name it's given, file or no file.
     * The game swaps a name it has no file for for its first built-in one
     * wherever it meets it (set, placed, loaded, Refresh), so a machine
     * without the file stored the default, and a host without it saved the
     * default for everyone. Here the name stays, and a machine without the
     * file plays or shows the default, until the file is added and Refresh
     * pressed. In a game alone too: a co-op save opened alone, or loaded
     * alone to host it from in the game, would otherwise lose a name whose
     * file that player doesn't have, and save the default for everyone.
     */
    static class CustomFileNames
    {
        // The sound a speaker plays for a name: its own, or the first built-in one where this machine has no such file
        internal static string Playable(SpeakerSoundService sounds, string soundId) =>
            sounds.BuiltInSounds.Contains(soundId) || sounds.CustomSounds.Contains(soundId) ? soundId : sounds.BuiltInSounds.First();
    }

    // A speaker keeps any sound name it's given: the synced SetSoundId's replay, and ValidateSoundId as it's placed,
    // loaded or on Refresh, all come through here
    [HarmonyPatch(typeof(SpeakerSoundService), nameof(SpeakerSoundService.GetValidatedSoundId))]
    static class SpeakerSoundNameKeeper
    {
        static bool Prefix(string soundName, ref string __result)
        {
            if (string.IsNullOrWhiteSpace(soundName)) return true;
            __result = soundName;
            return false;
        }
    }

    // ...and plays the first built-in sound for one this machine has no file for. Its sound list shows the name, as a
    // custom sound's
    [HarmonyPatch]
    static class SpeakerSoundFallback
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Speaker), nameof(Speaker.PlayOnce));
            yield return AccessTools.Method(typeof(Speaker), nameof(Speaker.PlayLooped));
        }

        static void Prefix(Speaker __instance, ref string soundId) =>
            soundId = CustomFileNames.Playable(__instance._speakerSoundService, soundId);
    }

    // A building keeps any image name it's given in a category it has, placed, loaded or on Refresh, and so does a
    // beaver's tail
    [HarmonyPatch(typeof(DecalService), nameof(DecalService.GetValidatedDecal))]
    static class DecalNameKeeper
    {
        static bool Prefix(DecalService __instance, Decal decal, ref Decal __result)
        {
            if (decal.IsEmpty || decal.Category == null || !__instance._decalCategories.ContainsKey(decal.Category)) return true;
            __result = decal;
            return false;
        }
    }

    // ...and shows the category's first image for one this machine has no file for: a building's image, a beaver's
    // tail and the picker all draw through here
    [HarmonyPatch(typeof(DecalService), nameof(DecalService.GetDecalTexture))]
    static class DecalImageFallback
    {
        static void Prefix(DecalService __instance, ref Decal decal)
        {
            if (decal.IsEmpty || decal.Category == null) return;
            if (!__instance._decalCategories.TryGetValue(decal.Category, out DecalCategory category) || category.TryGet(decal.Id, out _)) return;
            decal = new Decal(category.CategorySpecs.First().Id, decal.Category);
        }
    }
}
