using System;

namespace BeaverBuddies.Steam
{
    /**
     * Whether this copy of Timberborn has Steam at all. The Epic Games build
     * ships without Timberborn's Steam assemblies, so nothing that touches
     * them may load there: no Steam patches, singletons or calls. This class
     * stays free of Steam types so anything can ask it.
     */
    public static class SteamSupport
    {
        public static readonly bool Installed =
            Type.GetType("Timberborn.SteamOverlaySystem.SteamOverlayInputBlocker, Timberborn.SteamOverlaySystem") != null
            && Type.GetType("Timberborn.SteamStoreSystem.SteamManager, Timberborn.SteamStoreSystem") != null
            && Type.GetType("Steamworks.SteamAPI, com.rlabrecque.steamworks.net") != null;

        // Set once Steam is up (see SteamOverlayConnectionService)
        public static bool IsSteamEnabled { get; internal set; } = false;
    }
}
