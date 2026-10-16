#if IS_STEAM
using BeaverBuddies.Steam;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace BeaverBuddies.Matchmaking
{
    // A player waiting in a match lobby of their own, or a hosted game, for anyone to join
    public class OpenMatch
    {
        // Its Steam lobby, 0 for a game only in the directory
        public ulong lobby;
        // Its WebRTC join code, null for a Quick match, which only Steam lists
        public string joinCode;
        // The host's store: steam, epic or other
        public string platform;
        public MatchPicks picks;
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
     * directory (see OpenListings), where every hosted game is, Steam or not.
     * A game in both shows once, as its Steam lobby when this player has
     * Steam. Without Steam (the Epic Games build, or Steam networking off)
     * it's the directory alone, and nothing here may touch Steam.
     */
    public static class MatchList
    {
        public static bool Loading { get; private set; }
        public static bool Failed { get; private set; }
        public static List<OpenMatch> Matches { get; } = new();
        public static event Action Changed;
        public static bool ShowIncompatible { get; set; }

        public static bool SteamListed => Lobbies.SteamUp;

        // What each place last gave, merged once both are in. Null while waiting, or when it failed
        private static List<OpenMatch> fromSteam, fromDirectory, fromWorker;
        private static bool steamPending, directoryPending, workerPending, steamFailed, directoryFailed;
        // The directory answers on another thread: picked up on Unity's, at the next Poll
        private static volatile bool directoryArrived;
        private static List<DirectoryListing> arrived;

        public static void Refresh()
        {
            if (Loading) return;
            Loading = true;
            Failed = false;
            steamFailed = directoryFailed = false;
            fromSteam = fromDirectory = fromWorker = null;
            steamPending = SteamListed;
            directoryPending = workerPending = true;
            if (steamPending) RequestSteam();
            // Players without Steam waiting for a Quick match
            var filter = new LobbyFilter { Most = 50 };
            filter.Equal[MatchKeys.Match] = "1";
            filter.Equal[MatchKeys.Kind] = MatchKeys.QuickKind;
            filter.Equal[MatchKeys.Open] = "1";
            if (!ShowIncompatible) filter.Equal[MatchKeys.Base] = MatchKeys.BaseValue;
            WorkerLobbies.Search(filter, lobbies =>
            {
                workerPending = false;
                fromWorker = lobbies?.Select(FromLobby).Where(m => m != null).ToList();
                Merge();
            });
            Task.Run(async () =>
            {
                arrived = await OpenListings.Fetch();
                directoryArrived = true;
            });
            Changed?.Invoke();
        }

        // Apart, so that compiling Refresh never loads Steam's types in a copy without them
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RequestSteam() => SteamLobbyList.Request(ShowIncompatible, OnSteamListed);

        /** Every frame the list is open (see HostBrowser): takes in the directory's answer. */
        public static void Poll()
        {
            if (!directoryArrived) return;
            directoryArrived = false;
            directoryPending = false;
            directoryFailed = arrived == null;
            fromDirectory = arrived?.Select(FromListing).Where(m => m != null).ToList();
            Merge();
        }

        private static void OnSteamListed(List<OpenMatch> matches)
        {
            steamPending = false;
            steamFailed = matches == null;
            fromSteam = matches;
            Merge();
        }

        private static OpenMatch FromLobby(LobbySummary lobby)
        {
            // Two test copies list theirs side by side (see DevTools.TestHarness)
            if (lobby.Owner == Lobbies.Me && !DevTools.TestHarness.Active) return null;
            string Data(string key) => lobby.Data.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value) ? value : null;
            MatchPicks picks = MatchPicks.FromJson(Data(MatchKeys.Picks));
            if (picks == null) return null;
            bool otherMods = Data(MatchKeys.Compat) != MatchKeys.CompatValue;
            return new OpenMatch
            {
                lobby = lobby.Id,
                platform = Data(MatchKeys.Platform) ?? "other",
                picks = picks,
                otherMods = otherMods,
                mods = MatchMods.FromJson(Data(MatchKeys.Mods)),
                // Their mods come from the Workshop, which needs Steam
                compatible = Data(MatchKeys.Base) == MatchKeys.BaseValue && (!otherMods || SteamListed),
                protocol = Data(MatchKeys.Protocol),
                game = Data(MatchKeys.Game),
                debug = Data(MatchKeys.Debug),
            };
        }

        private static OpenMatch FromListing(DirectoryListing listing)
        {
            // Two test copies list their games side by side (see DevTools.TestHarness)
            if (listing.id == OpenListings.MineId && !DevTools.TestHarness.Active) return null;
            var data = listing.data ?? new Dictionary<string, string>();
            string Data(string key) => data.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value) ? value : null;
            if (Data(MatchKeys.Open) != "1") return null;
            MatchPicks picks = MatchPicks.FromJson(Data(MatchKeys.Picks));
            if (picks == null) return null;
            bool sameBase = Data(MatchKeys.Base) == MatchKeys.BaseValue;
            if (!sameBase && !ShowIncompatible) return null;
            bool otherMods = Data(MatchKeys.Compat) != MatchKeys.CompatValue;
            ulong.TryParse(listing.steamLobby, out ulong lobby);
            return new OpenMatch
            {
                lobby = lobby,
                joinCode = listing.joinCode,
                platform = listing.platform,
                picks = picks,
                hosted = true,
                otherMods = otherMods,
                mods = MatchMods.FromJson(Data(MatchKeys.Mods)),
                // Without Steam there's no Workshop to get their mods from
                compatible = sameBase && !otherMods,
                protocol = Data(MatchKeys.Protocol),
                game = Data(MatchKeys.Game),
                debug = Data(MatchKeys.Debug),
            };
        }

        private static void Merge()
        {
            if (steamPending || directoryPending || workerPending) return;
            Loading = false;
            Failed = (steamFailed || !SteamListed) && directoryFailed;
            var all = new List<OpenMatch>(fromSteam ?? new List<OpenMatch>());
            all.AddRange(fromWorker ?? new List<OpenMatch>());
            var inSteam = new HashSet<ulong>(all.Select(m => m.lobby));
            foreach (OpenMatch listed in fromDirectory ?? new List<OpenMatch>())
            {
                OpenMatch same = listed.lobby != 0 ? all.FirstOrDefault(m => m.lobby == listed.lobby) : null;
                if (same != null)
                {
                    // Its Steam lobby says it all; the directory adds the way in for players without Steam
                    same.joinCode = listed.joinCode;
                    same.platform = listed.platform;
                    continue;
                }
                all.Add(listed);
            }
            // The ones that can be played first, keeping each list's order within each
            Matches.Clear();
            Matches.AddRange(all.Where(m => m.compatible).Concat(all.Where(m => !m.compatible)));
            Plugin.Log($"[Match] {Matches.Count(m => m.compatible)} open matches, {Matches.Count(m => !m.compatible)} incompatible; {fromDirectory?.Count ?? 0} in the directory");
            Changed?.Invoke();
        }
    }

    // The Steam lobbies listed, only asked for with Steam there
    internal static class SteamLobbyList
    {
        private const int Most = 50;
        private static CallResult<LobbyMatchList_t> listed;
        private static Action<List<OpenMatch>> done;

        public static void Request(bool showIncompatible, Action<List<OpenMatch>> then)
        {
            done = then;
            listed ??= CallResult<LobbyMatchList_t>.Create(OnListed);
            SteamMatchmaking.AddRequestLobbyListStringFilter(MatchmakingSession.MatchKey, "1", ELobbyComparison.k_ELobbyComparisonEqual);
            if (!showIncompatible) SteamMatchmaking.AddRequestLobbyListStringFilter(MatchmakingSession.BaseKey, MatchmakingSession.Base, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(MatchmakingSession.OpenKey, "1", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(Most);
            listed.Set(SteamMatchmaking.RequestLobbyList());
        }

        private static void OnListed(LobbyMatchList_t list, bool failed)
        {
            if (failed)
            {
                done?.Invoke(null);
                return;
            }
            var matches = new List<OpenMatch>();
            ulong me = SteamUser.GetSteamID().m_SteamID;
            for (int i = 0; i < list.m_nLobbiesMatching; i++)
            {
                CSteamID id = SteamMatchmaking.GetLobbyByIndex(i);
                // Two test copies of the game share one Steam account (see DevTools.TestHarness)
                if (SteamMatchmaking.GetLobbyOwner(id).m_SteamID == me && !DevTools.TestHarness.Active) continue;
                MatchPicks picks = MatchPicks.FromJson(SteamMatchmaking.GetLobbyData(id, MatchmakingSession.PicksKey));
                if (picks == null) continue;
                bool hosted = SteamMatchmaking.GetLobbyData(id, MatchmakingSession.KindKey) == MatchmakingSession.HostedKind;
                bool otherMods = SteamMatchmaking.GetLobbyData(id, MatchmakingSession.CompatKey) != MatchmakingSession.Compat;
                matches.Add(new OpenMatch
                {
                    lobby = id.m_SteamID,
                    platform = "steam",
                    picks = picks,
                    hosted = hosted,
                    otherMods = otherMods,
                    mods = MatchMods.FromJson(SteamMatchmaking.GetLobbyData(id, MatchmakingSession.ModsKey)),
                    compatible = SteamMatchmaking.GetLobbyData(id, MatchmakingSession.BaseKey) == MatchmakingSession.Base && !(hosted && otherMods),
                    protocol = Data(id, MatchmakingSession.ProtocolKey),
                    game = Data(id, MatchmakingSession.GameKey),
                    debug = Data(id, MatchmakingSession.DebugKey),
                });
            }
            done?.Invoke(matches);
        }

        private static string Data(CSteamID id, string key)
        {
            string value = SteamMatchmaking.GetLobbyData(id, key);
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
#endif
