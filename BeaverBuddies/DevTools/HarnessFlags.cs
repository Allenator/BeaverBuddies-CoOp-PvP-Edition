namespace BeaverBuddies.DevTools
{
    /**
     * What BeaverBuddies reads of its test harness. The harness is a DLL of its own, BeaverBuddies.TestHarness (built
     * from the folder of that name beside this project), that only test runs install next to this one: it starts from
     * Plugin.StartMod and sets these as its copies of the game need (Tools/Tests/README.md). A player's game has no
     * harness, so all of them stay off.
     */
    public static class HarnessFlags
    {
        // A copy of the game the harness drives: two on one Mac, each started with BB_INSTANCE
        public static bool Active { get; internal set; }

        // A desync kept as it is, not resynced, to look at what differs (the harness's noresync)
        public static bool NoResync { get; internal set; }

        // This machine hosts a match's new game, started by the harness rather than by matchmaking
        public static bool HostsMatch { get; internal set; }

        // Every tick's hash line logged, as with tracing on, for the scenarios and their helpers to read (see
        // DesyncDetecter.TickLog)
        public static bool EveryTickLogged { get; internal set; }

        // The build this copy says it is, another than its own, as two copies of one build on one Mac couldn't
        // otherwise be (see Connect.BuildIdentity)
        public static string FakeBuild { get; internal set; }

        // The code every game hosted here is joined by, which the copies agree on ahead of time (see WebRtc.JoinCode)
        public static string JoinCode { get; internal set; }

        // Profiling: frames over this many ms reported, with the pace of the frames and the leak finder's lines (see
        // Performance.HitchWatch)
        public static double? HitchMs { get; internal set; }

        // How often a Co-op game's pace line is written, every 10 minutes in a player's game (see Performance.HitchWatch)
        public static double? PaceSeconds { get; internal set; }
    }

    // How Plugin.StartMod starts the harness's DLL, once the mod's own patches are in: the harness's own patches and
    // the rest it sets up
    public interface IHarnessStarter
    {
        void Start();
    }
}
