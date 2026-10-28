using BeaverBuddies.IO;
using HarmonyLib;
using Timberborn.SingletonSystem;
using Timberborn.TimeSystem;
using UnityEngine;

namespace BeaverBuddies.Connect
{
    /**
     * The game plays its fast speeds slower as a colony grows: at 200 characters and more, speed 7 plays at 3.4 times
     * normal, not 7 (GameSpeedThrottler, October 27, 2026), while a tick costs every machine a fraction of that. In a
     * co-op game the host can lift some or all of it, for every player alike (Settings.SpeedThrottleSetting): half of
     * it has speed 7 play at 5.2 times, none at 7 times. Every machine plays the ticks at the pace its own scale gives,
     * a guest's catch-up included, so the host's choice goes to all. Only the pace changes, never what a tick does.
     */
    public class SpeedThrottle : IUpdatableSingleton
    {
        // The game's own scale, as GameSpeedThrottler last set it, and the lift last applied over it
        private static float _gameScale = 1;
        private static float _applied;

        private readonly SpeedManager _speedManager;

        public SpeedThrottle(SpeedManager speedManager)
        {
            _speedManager = speedManager;
        }

        /** How much of the slowing is lifted now: none alone, the host's choice in a co-op game. */
        public static float Lift => EventIO.IsNull ? 0 : Mathf.Clamp(Settings.SpeedThrottleSetting, 0, 2) / 2f;

        /** The scale for the game's own, with the lift: what the game plays its fast speeds at. */
        public static float Lifted(float gameScale) => Mathf.Lerp(gameScale, 1, Lift);

        // The host's choice changed, or reached this guest, or the game is hosted or left: applied again
        public void UpdateSingleton()
        {
            if (Lift == _applied) return;
            Plugin.Log($"Fast speeds in big colonies: {Lift * 100:F0}% of the game's slowing lifted");
            _speedManager.ChangeSpeedScale(_gameScale);
        }

        [HarmonyPatch(typeof(SpeedManager), nameof(SpeedManager.ChangeSpeedScale))]
        class ChangeSpeedScalePatcher
        {
            static void Prefix(ref float speedScale)
            {
                _gameScale = speedScale;
                _applied = Lift;
                speedScale = Lifted(speedScale);
            }
        }
    }
}
