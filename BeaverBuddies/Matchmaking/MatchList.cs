#if IS_STEAM
using BeaverBuddies.Steam;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BeaverBuddies.Matchmaking
{
    // A player waiting in a match lobby of their own, or a hosted game, for anyone to join
    public class OpenMatch
    {
        // Its Steam lobby, or its Worker lobby for a Quick match; 0 for a hosted game without Steam
        public ulong lobby;
        // Its WebRTC join code, null for a Quick match, which only Steam lists
        public string joinCode;
        // The host's store: steam, epic or other
        public string platform;
        public MatchPicks picks;
        // How many play in a hosted game now, and how many its map has room for: 0 where it doesn't say
        public int players, maxPlayers;
        // A hosted game (see HostedListing): joining it connects to it
        public bool hosted;
        // Whether joining needs their mods from the Workshop first
        public bool otherMods;
        // The mods they play with
        public List<MatchMods.Entry> mods;
        // Whether the two can play at all: the same game version, detailed
        // logging and matchmaking, and for a hosted game the same mods too,
        // since there's no match to get them in. When not, what the host
        // has for each, null where their BeaverBuddies is too old to say.
        public bool compatible = true;
        public string protocol, game, debug;

        // Which one it is, wherever it's listed
        public string Key => lobby != 0 ? lobby.ToString() : joinCode;
    }

    /**
     * The open matches: every player waiting for an opponent in a lobby of
     * their own (see MatchmakingSession), and every hosted game waiting for
     * players (see HostedListing), with the same game version and detailed
     * logging, whatever map and difficulty they picked. A player
     * can pick one to join instead of letting the search pick for them.
     * With ShowIncompatible on, those who differ in any of those are listed
     * too, after the rest, so a player can see why they can't play them.
     *
     * Two places list them: Steam's lobbies, for players with Steam, and the
     * Worker's (see WorkerLobbies), where every hosted game is too, Steam or
     * not. A game in both shows once, as its Steam lobby when this player has
     * Steam. Without Steam (the Epic Games build, or Steam networking off)
     * it's the Worker's alone, and nothing here may touch Steam.
     */
    public static class MatchList
    {
        public static bool Loading { get; private set; }
        public static bool Failed { get; private set; }
        public static List<OpenMatch> Matches { get; } = new();
        public static event Action Changed;
        public static bool ShowIncompatible { get; set; }

        public static bool SteamListed => Lobbies.SteamUp;

        // What each place last gave, merged once both are in; null where it failed
        private static List<OpenMatch> fromSteam, fromWorker;
        private static int waiting;

        // Made-up matches the test harness shows instead (see DevTools.TestHarness): no refresh replaces them while the list is open
        internal static bool Mocked { get; set; }

        internal static void ShowMock(List<OpenMatch> matches)
        {
            Mocked = true;
            Loading = Failed = false;
            Matches.Clear();
            Matches.AddRange(matches.Where(m => m.compatible).Concat(matches.Where(m => !m.compatible)));
            Changed?.Invoke();
        }

        public static void Refresh()
        {
            if (Loading || Mocked) return;
            Loading = true;
            Failed = false;
            fromSteam = fromWorker = null;
            var filter = new LobbyFilter { Most = 50 };
            filter.Equal[MatchKeys.Match] = "1";
            filter.Equal[MatchKeys.Open] = "1";
            if (!ShowIncompatible) filter.Equal[MatchKeys.Base] = MatchKeys.BaseValue;
            ISteamLobbies steam = Lobbies.Steam;
            waiting = steam != null ? 2 : 1;
            steam?.Search(filter, ids =>
            {
                fromSteam = ids?.Select(id => Read(id, steam.Owner(id), key => steam.Data(id, key), fromSteamLobby: true)).Where(m => m != null).ToList();
                Merge();
            });
            WorkerLobbies.Search(filter, lobbies =>
            {
                fromWorker = lobbies?.Select(l => Read(l.Id, l.Owner, key => l.Data.TryGetValue(key, out string value) ? value : null, fromSteamLobby: false))
                    .Where(m => m != null).ToList();
                Merge();
            });
            Changed?.Invoke();
        }

        /**
         * An open match from a lobby's data, Steam's or the Worker's. A hosted
         * game's Worker lobby only lists it: it's joined by its Steam lobby or
         * its join code.
         */
        private static OpenMatch Read(ulong id, ulong owner, Func<string, string> raw, bool fromSteamLobby)
        {
            // Two test copies list theirs side by side (see DevTools.TestHarness)
            if (owner == Lobbies.Me && !DevTools.TestHarness.Active) return null;
            string Data(string key) => raw(key) is string value && value != "" ? value : null;
            MatchPicks picks = MatchPicks.FromJson(Data(MatchKeys.Picks));
            if (picks == null) return null;
            bool hosted = Data(MatchKeys.Kind) == MatchKeys.HostedKind;
            if (hosted && !fromSteamLobby && Data(MatchKeys.Code) == null) return null;
            bool otherMods = Data(MatchKeys.Compat) != MatchKeys.CompatValue;
            ulong.TryParse(Data(MatchKeys.SteamLobby), out ulong steamLobby);
            return new OpenMatch
            {
                lobby = fromSteamLobby || !hosted ? id : steamLobby,
                joinCode = hosted ? Data(MatchKeys.Code) : null,
                platform = Data(MatchKeys.Platform) ?? (fromSteamLobby ? "steam" : "other"),
                players = int.TryParse(Data(MatchKeys.Players), out int players) ? players : 0,
                maxPlayers = MaxPlayers(picks),
                picks = picks,
                hosted = hosted,
                otherMods = otherMods,
                mods = MatchMods.FromJson(Data(MatchKeys.Mods)),
                // A hosted game has no match to get its mods in, and a Quick match's come from the Workshop, which needs Steam
                compatible = Data(MatchKeys.Base) == MatchKeys.BaseValue && (!otherMods || !hosted && SteamListed),
                protocol = Data(MatchKeys.Protocol),
                game = Data(MatchKeys.Game),
                debug = Data(MatchKeys.Debug),
            };
        }

        // A game's room: the players its multiplayer mode was made for (2 for a Quick match), up to its player limit
        internal static int MaxPlayers(MatchPicks picks)
        {
            int limit = Connect.PlayerLimit.Of(picks.settings);
            return picks.mode != null && picks.mode.TryGetValue(nameof(MultiStart.MultiplayerNewGameModeSpec.Players), out float players)
                ? System.Math.Min((int)players, limit) : limit;
        }

        private static void Merge()
        {
            if (--waiting > 0 || Mocked) return;
            Loading = false;
            Failed = fromSteam == null && fromWorker == null;
            var all = new List<OpenMatch>(fromSteam ?? new List<OpenMatch>());
            foreach (OpenMatch listed in fromWorker ?? new List<OpenMatch>())
            {
                OpenMatch same = listed.lobby != 0 ? all.FirstOrDefault(m => m.lobby == listed.lobby) : null;
                if (same != null)
                {
                    // Its Steam lobby says it all; the Worker's adds the way in for players without Steam
                    same.joinCode = listed.joinCode;
                    same.platform = listed.platform;
                    continue;
                }
                all.Add(listed);
            }
            // The lobby filter only narrows what the places return; a match can still be incompatible in ways it can't see
            if (!ShowIncompatible) all.RemoveAll(m => !m.compatible);
            // The ones that can be played first, keeping each list's order within each
            Matches.Clear();
            Matches.AddRange(all.Where(m => m.compatible).Concat(all.Where(m => !m.compatible)));
            Plugin.Log($"[Match] {Matches.Count(m => m.compatible)} open matches, {Matches.Count(m => !m.compatible)} incompatible; {fromWorker?.Count ?? 0} on the Worker");
            Changed?.Invoke();
        }
    }

}
#endif
