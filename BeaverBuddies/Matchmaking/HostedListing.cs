#if IS_STEAM
using BeaverBuddies.NewGame;
using BeaverBuddies.Players;
using BeaverBuddies.Steam;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Timberborn.GameFactionSystem;
using Timberborn.GameSaveRepositorySystem;
using Timberborn.NewGameConfigurationSystem;

namespace BeaverBuddies.Matchmaking
{
    /**
     * A hosted game in the list of open matches, beside the players waiting
     * for a Quick match (see MatchList): its lobby made public and given the
     * same data a match's lobby has, with its map, difficulty, mode and
     * settings for picks. Joining it from the list connects straight to the
     * game, as an invite would. It stays listed while the host waits for
     * players, until it's full or the game starts.
     *
     * The picks come from the New Game screens when the game was hosted
     * from them, and otherwise from the save: its mode and settings, with
     * the map and faction from the game when one is loaded.
     *
     * It has a Worker lobby too (see WorkerLobbies), with the game's join
     * code, for players without Steam. Only the host is in it, and it lasts
     * as long as they host. The Epic Games build can host and list a game as
     * well, so it reaches Steam only through Lobbies.Steam.
     */
    public static class HostedListing
    {
        // The game hosted from the New Game screens, for when it's saved and hosted
        private static MatchPicks newGame;
        private static string newGameSettlement;
        private static bool newGameFriendsOnly, friendsOnly;
        // The game about to be hosted, for its lobby
        private static MatchPicks hosting;
        private static ulong lobby;
        private static ILobby workerLobby;
        // Bumped whenever the listing ends, so a Worker lobby still on its way is left at once
        private static int generation;
        private static bool listed;
        private static int players;

        public static void HostingNewGame(NewGameConfiguration configuration, string mapTitle, string modeLocKey)
        {
            newGame = MatchPicks.Create(PlayerIdentity.LocalName, configuration, mapTitle, null, modeLocKey);
            newGameSettlement = configuration.SettlementName;
            newGameFriendsOnly = FriendsOnlyPanel.FriendsOnly;
        }

        // The save about to be hosted, read before its lobby exists
        public static void Hosting(SaveReference save, byte[] saveFile)
        {
            lobby = 0;
            string settlement = save.SettlementReference.SettlementName;
            bool fromNewGame = newGame != null && newGameSettlement == settlement;
            hosting = fromNewGame ? newGame : FromSave(settlement, saveFile);
            friendsOnly = fromNewGame ? newGameFriendsOnly : FriendsOnlyPanel.FriendsOnly;
        }

        private static MatchPicks FromSave(string settlement, byte[] saveFile)
        {
            var record = ReadRecord(saveFile) ?? new Dictionary<string, string>();
            return new MatchPicks
            {
                name = PlayerIdentity.LocalName,
                faction = SingletonManager.GetSingleton<HostedGameInfo>()?.Faction,
                mapTitle = SingletonManager.GetSingleton<ReplayService>()?.ServerMapName ?? settlement,
                options = record.Where(p => MatchOptions.IsPick(p.Key)).ToDictionary(p => p.Key, p => p.Value),
                settings = record.Where(p => !MatchOptions.IsPick(p.Key) && p.Key != GameModes.Modes.AloneKey).ToDictionary(p => p.Key, p => p.Value),
            };
        }

        // The game's mode and settings from the save's metadata (see GameModes.GameRecord)
        private static Dictionary<string, string> ReadRecord(byte[] saveFile)
        {
            try
            {
                return GameModes.GameRecord.FromSave(new MemoryStream(saveFile));
            }
            catch (Exception e)
            {
                Plugin.LogWarning($"[Match] Couldn't read the hosted save's settings: {e.Message}");
                return null;
            }
        }

        // Anyone can find it: not kept to invites or Steam friends, and not a match's game, which only its other player joins
        private static bool Listable => hosting != null && !friendsOnly && Settings.LobbyJoinable && MatchmakingSession.State != MatchState.Hosting;

        /** Hosting started: in a Worker lobby with the join code, Steam or not. */
        public static void Publish()
        {
            if (!Listable || WebRtc.JoinCode.Current == null) return;
            listed = true;
            players = 1;
            OpenWorkerLobby(WebRtc.JoinCode.Current);
        }

        private static void OpenWorkerLobby(string code)
        {
            int mine = ++generation;
            WorkerLobbies.Create(2, created =>
            {
                if (mine != generation)
                {
                    created?.Leave();
                    return;
                }
                if (created == null)
                {
                    Plugin.LogWarning("[Match] Couldn't list the hosted game for players without Steam");
                    return;
                }
                workerLobby = created;
                // All in one message: the lobby lists itself once
                var data = MatchKeys.Describe(hosting, MatchKeys.HostedKind);
                data[MatchKeys.Code] = code;
                data[MatchKeys.Players] = players.ToString();
                if (lobby != 0) data[MatchKeys.SteamLobby] = lobby.ToString();
                created.SetData(data);
                // The Worker restarted, or the network dropped: listed again
                created.Lost += () =>
                {
                    if (workerLobby != created) return;
                    workerLobby = null;
                    OpenWorkerLobby(code);
                };
                Plugin.Log($"[Match] Listing the hosted game in the Worker's lobby {created.Id} with code {code}");
            });
        }

        /** The hosted game's Steam lobby is up: listed for Steam players, and named in the Worker lobby. */
        public static void Advertise(ulong server)
        {
            if (!Listable) return;
            lobby = server;
            listed = true;
            Lobbies.Steam?.MakePublic(lobby);
            Lobbies.Steam?.SetData(lobby, MatchKeys.Describe(hosting, MatchKeys.HostedKind));
            PublishPlayers();
            workerLobby?.SetData(MatchKeys.SteamLobby, lobby.ToString());
            Plugin.Log($"[Match] Listing the hosted game {lobby}: {hosting.mapTitle}");
        }

        // How many have joined: it's off the list once the game has no room left
        public static void Joined(int clients)
        {
            if (!listed) return;
            if (clients + 1 != players)
            {
                players = clients + 1;
                PublishPlayers();
            }
            if (hosting != null && clients + 1 >= MatchList.MaxPlayers(hosting)) Unlist();
        }

        // How many play now, wherever the game is listed
        private static void PublishPlayers()
        {
            workerLobby?.SetData(MatchKeys.Players, players.ToString());
            if (lobby != 0) Lobbies.Steam?.SetData(lobby, new Dictionary<string, string> { [MatchKeys.Players] = players.ToString() });
        }

        // The game started, or hosting stopped
        public static void Unlist()
        {
            generation++;
            listed = false;
            if (workerLobby != null)
            {
                Plugin.Log($"[Match] The Worker's lobby {workerLobby.Id} is off the list");
                workerLobby.Leave();
                workerLobby = null;
            }
            if (lobby == 0) return;
            Plugin.Log($"[Match] {lobby} is off the list");
            Lobbies.Steam?.SetData(lobby, new Dictionary<string, string> { [MatchKeys.Open] = "0" });
            lobby = 0;
        }
    }

    // What the loaded game is, for listing it when it's hosted
    public class HostedGameInfo : RegisteredSingleton
    {
        private readonly FactionService _factionService;

        public HostedGameInfo(FactionService factionService) => _factionService = factionService;

        public string Faction => _factionService.Current?.Id;
    }
}
#endif
