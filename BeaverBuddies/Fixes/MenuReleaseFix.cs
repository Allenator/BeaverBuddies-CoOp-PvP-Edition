using HarmonyLib;
using System.Reflection;

namespace BeaverBuddies.Fixes
{
    /**
     * Game Modes keeps its Customize dialog in a static property it sets in the main menu and never clears, and the
     * dialog reaches everything the main menu's scene had: it stays in memory through every game played after it
     * (October 27, 2026). A game loaded lets it go. Game Modes makes a new dialog, and sets the property again, each
     * time the main menu opens, and uses it only there, as a mode is picked.
     */
    static class MenuReleaseFix
    {
        private static readonly FieldInfo CustomizeDialog = AccessTools.TypeByName("GameModes.CustomizeDialog")?
            .GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);

        public static void GameLoaded() => CustomizeDialog?.SetValue(null, null);
    }
}
