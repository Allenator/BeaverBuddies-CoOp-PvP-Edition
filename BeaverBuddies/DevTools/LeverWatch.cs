using HarmonyLib;
using Timberborn.Automation;
using Timberborn.AutomationBuildings;

namespace BeaverBuddies.DevTools
{
    /**
     * The test harness's eye on one lever ("leverwatch x y z ticks label"):
     * for that many automation ticks, whether it was on as the tick's
     * automation began, which is what the network evaluates, and as it ended,
     * after a spring-return lever has gone back off. Logged as "[Test] Lever
     * label tick N: start on, end off, pressed False", N being the tick since
     * load in a multiplayer game, the same on every machine, and the
     * automation ticks since the game started alone. "lever x y z hold n"
     * presses a lever's button and lets go of it n ticks later, as a player
     * holding it would. Only in the harness's copies.
     */
    [HarmonyPatch(typeof(AutomationRunner), nameof(AutomationRunner.Tick))]
    static class LeverWatch
    {
        private static Lever _watched;
        private static string _label;
        private static int _ticksLeft;
        private static bool _onAtStart;
        private static int _ticks;

        private static Lever _held;
        private static int _releaseAt;

        static bool Prepare() => TestHarness.Active;

        public static void Watch(Lever lever, int ticks, string label)
        {
            _watched = lever;
            _ticksLeft = ticks;
            _label = label;
        }

        public static void Hold(Lever lever, int ticks)
        {
            lever.Press();
            _held = lever;
            _releaseAt = _ticks + ticks;
        }

        // Every frame, between ticks, where a player's mouse comes up
        public static void Update()
        {
            if (_held == null || _ticks < _releaseAt) return;
            Lever lever = _held;
            _held = null;
            if (lever) lever.Release();
            Plugin.Log("[Test] Lever let go of");
        }

        static void Prefix()
        {
            _ticks++;
            if (_watched) _onAtStart = _watched.IsOn;
        }

        static void Postfix()
        {
            if (_watched == null || _ticksLeft <= 0) return;
            if (_watched)
            {
                int tick = SingletonManager.GetSingleton<ReplayService>()?.TicksSinceLoad ?? _ticks;
                Plugin.Log($"[Test] Lever {_label} tick {tick:D5}: start {OnOff(_onAtStart)}, end {OnOff(_watched.IsOn)}, pressed {_watched._isPressed}");
            }
            if (--_ticksLeft > 0 && _watched) return;
            Plugin.Log($"[Test] Lever {_label} watch done");
            // Held past its game, it would keep the whole game it was in
            _watched = null;
        }

        private static string OnOff(bool on) => on ? "on" : "off";
    }
}
