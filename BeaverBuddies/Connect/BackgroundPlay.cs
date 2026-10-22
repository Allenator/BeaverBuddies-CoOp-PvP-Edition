using BeaverBuddies.IO;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace BeaverBuddies.Connect
{
    /**
     * Keeps the game running while another app is in front during
     * multiplayer (Settings.PlayInBackground), whatever the game's own "Run
     * in background" says: a host whose game stops stops everyone's, and a
     * guest whose game stops falls behind and has to catch up. The player's
     * own setting isn't changed, and applies again once the game isn't
     * hosted or joined.
     *
     * The game applies the player's setting as each scene loads and when it's
     * changed, over the mod's: after each scene's loading, and on any frame
     * that finds it changed, the mod applies its own again.
     */
    public class BackgroundPlay : IPostLoadableSingleton, IUpdatableSingleton
    {
        // The player's own setting, as the game last applied it, and what the mod last set
        private static bool _playersOwn;
        private static bool? _lastSet;

        public void PostLoad() => Apply();

        public void UpdateSingleton()
        {
            if (Application.runInBackground != _lastSet) Apply();
        }

        /** Runs in the background if the player has it so, or while hosting or joined if they let the mod. */
        public static void Apply()
        {
            // Set since the mod last did: the game applied the player's setting
            if (Application.runInBackground != _lastSet) _playersOwn = Application.runInBackground;
            bool multiplayer = EventIO.Get() is ServerEventIO or ClientEventIO;
            bool run = _playersOwn || multiplayer && Settings.PlayInBackground;
            _lastSet = run;
            if (Application.runInBackground == run) return;
            Application.runInBackground = run;
            Plugin.Log(run ? "Playing on while another app is in front, during multiplayer" : "The game's own background setting applies again");
        }
    }
}
