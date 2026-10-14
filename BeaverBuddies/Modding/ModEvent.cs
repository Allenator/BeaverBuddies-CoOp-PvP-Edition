using BeaverBuddies.Events;
using System;

namespace BeaverBuddies.Modding
{
    /**
     * An event of a mod built on BeaverBuddies: the mod's own object,
     * sent as JSON like any event, and handed back to the mod to play on
     * every machine. Mods never see this class, only their payload.
     */
    [Serializable]
    public class ModEvent : ReplayEvent
    {
        public string mod;
        public object payload;
        // Sent by one of the mod's dev tools, so the host drops it when dev tools are off
        public bool dev;

        public override void Replay(IReplayContext context) => ModBridge.Play(this, context);

        public override string ToActionString() => $"{mod}: {payload}";
    }
}
