using BeaverBuddies.Events;
using System;

namespace BeaverBuddies.DevTools
{
    // A harness command played on every machine, like a player's action: on the harness's copies only. A player's game
    // that gets one from a test copy plays nothing (making goods piles from nothing, say), and the two fall out of sync.
    // It stays in BeaverBuddies, whose name it's sent under, so a game without the harness can still read one
    [Serializable]
    class HarnessEvent : ReplayEvent
    {
        public string command;

        // What a harness command does on every machine (TestHarness.RunReplayed), set as the harness starts: nothing in
        // a player's game
        internal static Action<IReplayContext, string> Run;

        public override void Replay(IReplayContext context)
        {
            Run?.Invoke(context, command);
        }

        public override string ToActionString() => $"Harness: {command}";
    }
}
