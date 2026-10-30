using BeaverBuddies.Events;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Timberborn.Versioning;

namespace BeaverBuddies.Connect
{
    /**
     * What two players need alike to play together, and what tells this
     * build apart from any other: whoever's on another one is turned away
     * (see Settings.StrictBuild), as a Steam lobby's player already is (see
     * Matchmaking.MatchKeys, which uses the same). Free of Steam, so the Epic
     * Games build has it too.
     */
    public static class BuildIdentity
    {
        // What a host says to a player it turns away, with its own build and why: another build, or none said in time
        public const string RefusedType = "BuildRefused";
        public const string RefusedOtherBuild = "build", RefusedSilent = "silent";

        // This exact build: two builds of one version can still play differently. A test copy can be told
        // it's another (BB_FAKE_BUILD, which the test harness reads), as two copies of one build on one Mac
        // couldn't otherwise be
        public static string Build => DevTools.HarnessFlags.FakeBuild ?? InitializeClientEvent.ModBuild;

        // The game's version, this build, the add-on mods and the factions
        public static string CompatParts =>
            $"{GameVersions.CurrentVersion}|{Plugin.Version} {Build}|{ReplayHooks.AddOnList}|{ReplayHooks.FactionList}";

        public static string CompatValue => Hash(CompatParts);

        // For people: the release and the build
        public static string Describe => $"{Plugin.Version} (build {Build})";

        // Who has which, for a player turned away
        public static string Mismatch(string theirs) => $"Host: {theirs}; you: {Describe}";

        public static string Hash(string text)
        {
            using var sha = SHA1.Create();
            return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text)).Take(8).Select(b => b.ToString("x2")));
        }
    }
}
