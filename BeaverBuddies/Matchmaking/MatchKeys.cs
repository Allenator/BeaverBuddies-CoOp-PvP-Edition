#if IS_STEAM
using BeaverBuddies.Events;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Timberborn.Versioning;

namespace BeaverBuddies.Matchmaking
{
    /**
     * What an open match says about itself, by the same keys wherever it's
     * listed: the data of its Steam lobby, and its listing in the directory
     * (see OpenListings) that players without Steam see. Free of Steam, so
     * the Epic Games build can use it.
     */
    public static class MatchKeys
    {
        // Set by the lobby's owner: what the search filters on
        public const string Match = "bb.match", Base = "bb.match.base", Compat = "bb.match.compat", Mods = "bb.match.mods",
            Open = "bb.match.open", Map = "bb.match.map", Mode = "bb.match.mode", Options = "bb.match.options", Picks = "bb.match.picks";
        // What Base is made of, in the clear: the list of open matches
        // shows players it can't match with why
        public const string Protocol = "bb.match.protocol", Game = "bb.match.game", Debug = "bb.match.debug";
        // A player waiting for a Quick match, or a hosted game (see
        // HostedListing): both are listed, only the first is searched
        public const string Kind = "bb.match.kind", QuickKind = "quick", HostedKind = "hosted";
        // Matchmaking's own version: two that differ can't find each other
        public const int ProtocolVersion = 6;

        // What no mod download can fix, short enough for a lobby filter: the
        // game's version, and detailed logging, which a desync report needs
        // on for both players
        public static string BaseValue => Hash($"match {ProtocolVersion}|{GameVersions.CurrentVersion}|{Settings.Debug}");

        // The same game, mod and add-on versions and the same factions,
        // which any two players in a game need
        internal static string CompatParts =>
            $"{GameVersions.CurrentVersion}|{Plugin.Version} {Build}|{ReplayHooks.AddOnList}|{ReplayHooks.FactionList}";

        // This exact build: two builds of the same version can still play differently
        private static string Build => InitializeClientEvent.ModBuild;

        public static string CompatValue => Hash(CompatParts);

        public static string Hash(string text)
        {
            using var sha = SHA1.Create();
            return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text)).Take(8).Select(b => b.ToString("x2")));
        }

        // Everything a match's listing says
        public static Dictionary<string, string> Describe(MatchPicks picks, string kind) => new()
        {
            [Match] = "1",
            [Kind] = kind,
            [Base] = BaseValue,
            [Protocol] = ProtocolVersion.ToString(),
            [Game] = GameVersions.CurrentVersion.ToString(),
            [Debug] = Settings.Debug ? "1" : "0",
            [Compat] = CompatValue,
            [Mods] = MatchMods.ToJson(MatchMods.Local),
            [Map] = picks.MapKey,
            [Mode] = picks.ModeKey,
            [Options] = picks.OptionsKey,
            [Picks] = picks.ToJson(),
            [Open] = "1",
        };
    }
}
#endif
