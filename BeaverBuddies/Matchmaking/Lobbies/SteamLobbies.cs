#if IS_STEAM
using Steamworks;
using System;
using System.Collections.Generic;

namespace BeaverBuddies.Matchmaking
{
    /**
     * Steam's lobbies, behind ILobby and ISteamLobbies. The only part of
     * matchmaking that touches Steam: made by Lobbies.Steam when Steam is up,
     * so the Epic Games build never loads it. Steam's callbacks come on
     * Unity's thread.
     */
    internal class SteamLobbies : ISteamLobbies
    {
        // Searches in flight: each has its own call, so the list of open matches and Quick match can search at once
        private static readonly HashSet<CallResult<LobbyMatchList_t>> searching = new();
        private static CallResult<LobbyCreated_t> created;
        private static CallResult<LobbyEnter_t> entered;
        private static CallResult<LobbyEnter_t> hostedEntered;
        private static Callback<LobbyChatUpdate_t> chatUpdated;
        private static Callback<LobbyDataUpdate_t> dataUpdated;
        private static Action<ILobby> onCreated, onEntered;
        private static Action<bool> onHostedEntered;
        // The lobbies this player is in, for Steam's callbacks to reach
        private static readonly Dictionary<ulong, SteamLobby> open = new();

        public ulong Me => SteamUser.GetSteamID().m_SteamID;

        private static void Listen()
        {
            if (chatUpdated != null) return;
            created = CallResult<LobbyCreated_t>.Create(OnCreated);
            entered = CallResult<LobbyEnter_t>.Create(OnEntered);
            chatUpdated = Callback<LobbyChatUpdate_t>.Create(OnChatUpdated);
            dataUpdated = Callback<LobbyDataUpdate_t>.Create(OnDataUpdated);
        }

        public void Search(LobbyFilter filter, Action<List<ulong>> found)
        {
            foreach (var pair in filter.Equal) SteamMatchmaking.AddRequestLobbyListStringFilter(pair.Key, pair.Value, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(filter.Most);
            CallResult<LobbyMatchList_t> call = null;
            call = CallResult<LobbyMatchList_t>.Create((list, failed) =>
            {
                searching.Remove(call);
                if (failed)
                {
                    found(null);
                    return;
                }
                var ids = new List<ulong>();
                for (int i = 0; i < list.m_nLobbiesMatching; i++) ids.Add(SteamMatchmaking.GetLobbyByIndex(i).m_SteamID);
                found(ids);
            });
            searching.Add(call);
            call.Set(SteamMatchmaking.RequestLobbyList());
        }

        // A listed lobby's data, which a search brings along, and its owner
        public string Data(ulong lobby, string key) => SteamMatchmaking.GetLobbyData(new CSteamID(lobby), key);
        public ulong Owner(ulong lobby) => SteamMatchmaking.GetLobbyOwner(new CSteamID(lobby)).m_SteamID;

        public void Create(int max, Action<ILobby> done)
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

        public void Join(ulong id, Action<ILobby> done)
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

        public void SetData(ulong lobby, IReadOnlyDictionary<string, string> values)
        {
            foreach (var pair in values) SteamMatchmaking.SetLobbyData(new CSteamID(lobby), pair.Key, pair.Value);
        }

        public bool AddFriends(UnityEngine.UIElements.VisualElement content, Timberborn.Localization.ILoc loc, out string message) =>
            Steam.SteamInvites.AddFriends(content, loc, out message);

        // ---- Hosted games ----

        // A match's lobby rather than a hosted game's: entering it doesn't connect to its owner
        public static bool IsMatchLobby(CSteamID id) =>
            SteamMatchmaking.GetLobbyData(id, MatchKeys.Match) == "1" && SteamMatchmaking.GetLobbyData(id, MatchKeys.Kind) != MatchKeys.HostedKind;

        // A hosted game's lobby anyone can join by its id
        public void MakePublic(ulong lobby) => SteamMatchmaking.SetLobbyType(new CSteamID(lobby), ELobbyType.k_ELobbyTypePublic);

        /**
         * Into a hosted game's lobby, which connects to the game as an invite
         * does (see SteamOverlayConnectionService): false if it's gone.
         */
        public void EnterHostedGame(ulong lobby, Action<bool> done = null)
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

        public void SetData(IReadOnlyDictionary<string, string> values)
        {
            foreach (var pair in values) SetData(pair.Key, pair.Value);
        }
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
