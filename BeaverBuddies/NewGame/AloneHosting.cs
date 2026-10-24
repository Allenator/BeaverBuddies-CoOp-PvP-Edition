using System;
using System.Collections.Generic;
using BeaverBuddies.IO;

namespace BeaverBuddies.NewGame
{
    /**
     * Games played alone that still go through events, as a host nobody
     * joins (LocalEventIO): those against players a mod runs on this
     * machine, like AI opponents, so they act the way anyone does. Settled
     * as the game starts loading, before its services are put together,
     * from what it was created with: a new game's settings, or the record
     * in a save's metadata (told by GameModes, see MultiplayerRules).
     */
    public static class AloneHosting
    {
        // Whether a game with this record is hosted alone; any true hosts it
        public static readonly List<Func<IReadOnlyDictionary<string, string>, bool>> Hooks = new();

        internal static void Starting(IReadOnlyDictionary<string, string> record)
        {
            bool alone = false;
            foreach (var hook in Hooks)
            {
                try
                {
                    alone |= hook(record ?? new Dictionary<string, string>());
                }
                catch (Exception e)
                {
                    Plugin.LogError($"A mod failed to say whether a game is hosted alone: {e}");
                }
            }
            if (alone && EventIO.IsNull)
            {
                Plugin.Log("Hosting this game alone");
                EventIO.Set(new LocalEventIO());
            }
            else if (!alone && EventIO.Get() is LocalEventIO)
            {
                EventIO.Reset();
            }
        }
    }
}
