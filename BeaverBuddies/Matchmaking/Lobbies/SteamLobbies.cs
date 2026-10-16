#if IS_STEAM
using Steamworks;
using System;
using System.Collections.Generic;

namespace BeaverBuddies.Matchmaking
{
    /**
     * Steam's lobbies, behind ILobby. The only part of matchmaking that
     * touches Steam: only called when Steam is up (see Lobbies), so the Epic
     * Games build never loads it. Steam's callbacks come on Unity's thread.
     */
    internal static class SteamLobbies
    {
        private static CallResult<LobbyMatchList_t> listed;
        private static CallResult<LobbyCreated_t> created;
        private static CallResult<LobbyEnter_t> entered;
        private static CallResult<LobbyEnter_t> hostedEntered;
        private static Callback<LobbyChatUpdate_t> chatUpdated;
        private static Callback<LobbyDataUpdate_t> dataUpdated;
        private static Action<List<ulong>> onListed;
        private static Action<ILobby> onCreated, onEntered;
        private static Action<bool> onHostedEntered;
        // The lobbies this player is in, for Steam's callbacks to reach
        private static readonly Dictionary<ulong, SteamLobby> open = new();

        public static ulong Me => SteamUser.GetSteamID().m_SteamID;

        private static void Listen()
        {
            if (chatUpdated != null) return;
            listed = CallResult<LobbyMatchList_t>.Create(OnListed);
            created = CallResult<LobbyCreated_t>.Create(OnCreated);
            entered = CallResult<LobbyEnter_t>.Create(OnEntered);
            chatUpdated = Callback<LobbyChatUpdate_t>.Create(OnChatUpdated);
            dataUpdated = Callback<LobbyDataUpdate_t>.Create(OnDataUpdated);
        }

        public static void Search(LobbyFilter filter, Action<List<ulong>> found)
        {
            Listen();
            onListed = found;
            foreach (var pair in filter.Equal) SteamMatchmaking.AddRequestLobbyListStringFilter(pair.Key, pair.Value, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(filter.Most);
            listed.Set(SteamMatchmaking.RequestLobbyList());
        }

        private static void OnListed(LobbyMatchList_t list, bool failed)
        {
            var found = onListed;
            onListed = null;
            if (failed)
            {
                found?.Invoke(null);
                return;
            }
            var ids = new List<ulong>();
            for (int i = 0; i < list.m_nLobbiesMatching; i++) ids.Add(SteamMatchmaking.GetLobbyByIndex(i).m_SteamID);
            found?.Invoke(ids);
        }

        public static void Create(int max, Action<ILobby> done)
        {
            Listen();
            onCreated = done;
            created.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, max));
        }

        private static void OnCreated(LobbyCreated_t result, bool failed)
        {
            var done = onCreated;
            onCreated = null;
            if (failed || result.m_eResult != EResult.k_EResultOK)
            {
                Plugin.Log($"[Match] Steam couldn't make a lobby: {result.m_eResult}");
                done?.Invoke(null);
                return;
            }
            done?.Invoke(Track(result.m_ulSteamIDLobby));
        }

        public static void Join(ulong id, Action<ILobby> done)
        {
            Listen();
            onEntered = done;
            entered.Set(SteamMatchmaking.JoinLobby(new CSteamID(id)));
        }

        private static void OnEntered(LobbyEnter_t result, bool failed)
        {
            var done = onEntered;
            onEntered = null;
            if (failed || result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                // Someone else got there first, or it closed
                Plugin.Log($"[Match] Couldn't join {result.m_ulSteamIDLobby}: {(EChatRoomEnterResponse)result.m_EChatRoomEnterResponse}");
                done?.Invoke(null);
                return;
            }
            done?.Invoke(Track(result.m_ulSteamIDLobby));
        }

        private static SteamLobby Track(ulong id)
        {
            var lobby = new SteamLobby(new CSteamID(id));
            open[id] = lobby;
            return lobby;
        }

        internal static void Untrack(SteamLobby lobby) => open.Remove(lobby.Id);

        private static void OnChatUpdated(LobbyChatUpdate_t update)
        {
            if (!open.TryGetValue(update.m_ulSteamIDLobby, out SteamLobby lobby)) return;
            bool cameIn = (update.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0;
            lobby.Changed(update.m_ulSteamIDUserChanged, cameIn);
        }

        private static void OnDataUpdated(LobbyDataUpdate_t update)
        {
            if (open.TryGetValue(update.m_ulSteamIDLobby, out SteamLobby lobby)) lobby.DataUpdated();
        }

        // ---- Hosted games ----

        // A match's lobby rather than a hosted game's: entering it doesn't connect to its owner
        public static bool IsMatchLobby(CSteamID id) =>
            SteamMatchmaking.GetLobbyData(id, MatchKeys.Match) == "1" && SteamMatchmaking.GetLobbyData(id, MatchKeys.Kind) != MatchKeys.HostedKind;

        // A hosted game's lobby anyone can join by its id
        public static void MakePublic(ulong lobby) => SteamMatchmaking.SetLobbyType(new CSteamID(lobby), ELobbyType.k_ELobbyTypePublic);

        /**
         * Into a hosted game's lobby, which connects to the game as an invite
         * does (see SteamOverlayConnectionService): false if it's gone.
         */
        public static void EnterHostedGame(ulong lobby, Action<bool> done = null)
        {
            hostedEntered ??= CallResult<LobbyEnter_t>.Create((result, failed) =>
            {
                bool ok = !failed && result.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess;
                if (!ok) Plugin.Log($"[Match] Couldn't join the hosted game {result.m_ulSteamIDLobby}: {(EChatRoomEnterResponse)result.m_EChatRoomEnterResponse}");
                var then = onHostedEntered;
                onHostedEntered = null;
                then?.Invoke(ok);
            });
            onHostedEntered = done;
            hostedEntered.Set(SteamMatchmaking.JoinLobby(new CSteamID(lobby)));
        }
    }

    internal class SteamLobby : ILobby
    {
        private readonly CSteamID id;

        public SteamLobby(CSteamID id) => this.id = id;

        public ulong Id => id.m_SteamID;
        public ulong Owner => SteamMatchmaking.GetLobbyOwner(id).m_SteamID;

        public IReadOnlyList<ulong> Members
        {
            get
            {
                var members = new List<ulong>();
                int count = SteamMatchmaking.GetNumLobbyMembers(id);
                for (int i = 0; i < count; i++) members.Add(SteamMatchmaking.GetLobbyMemberByIndex(id, i).m_SteamID);
                return members;
            }
        }

        public event Action<ulong> MemberEntered;
        public event Action<ulong> MemberLeft;
        public event Action DataChanged;
        // Steam keeps a lobby for as long as the game runs
        public event Action Lost { add { } remove { } }

        internal void Changed(ulong member, bool cameIn)
        {
            if (cameIn) MemberEntered?.Invoke(member);
            else MemberLeft?.Invoke(member);
        }

        internal void DataUpdated() => DataChanged?.Invoke();

        public string GetData(string key) => SteamMatchmaking.GetLobbyData(id, key);
        public void SetData(string key, string value) => SteamMatchmaking.SetLobbyData(id, key, value);
        public string GetMemberData(ulong member, string key) => SteamMatchmaking.GetLobbyMemberData(id, new CSteamID(member), key);
        public void SetMemberData(string key, string value) => SteamMatchmaking.SetLobbyMemberData(id, key, value);
        public string MemberName(ulong member) => SteamFriends.GetFriendPersonaName(new CSteamID(member));
        public void SetJoinable(bool joinable) => SteamMatchmaking.SetLobbyJoinable(id, joinable);

        public void Leave()
        {
            SteamLobbies.Untrack(this);
            SteamMatchmaking.LeaveLobby(id);
        }
    }
}
#endif
