using BeaverBuddies.IO;
using Steamworks;
using System.Collections.Generic;
using System.Linq;
using Timberborn.CoreUI;
using Timberborn.Localization;
using UnityEngine.UIElements;

namespace BeaverBuddies.Steam
{
    /**
     * Steam friends to invite to the hosted game's lobby, for the invite box
     * (see Connect.Invites). Our own list rather than Steam's invite dialog:
     * that lives in its overlay, which often isn't there (it rarely attaches
     * to games on macOS, and players can turn it off), and it has no room
     * for the game's join code. The invite lands in their Steam chat.
     */
    public static class SteamInvites
    {
        /** The online friends, friends already playing first, as buttons that invite them; false without a lobby to invite to. */
        public static bool AddFriends(VisualElement content, ILoc loc, out string message)
        {
            message = null;
            if (!TryGetLobby(out CSteamID lobby)) return false;
            List<CSteamID> friends = OnlineFriends();
            Plugin.Log($"Inviting from {friends.Count} online friends, of {SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate)}");
            message = loc.T(friends.Count > 0 ? "BeaverBuddies.Host.PickFriend" : "BeaverBuddies.Host.NoFriendsOnline");
            var rows = new ScrollView();
            rows.style.maxHeight = 360;
            foreach (CSteamID friend in friends)
            {
                var row = new LocalizableButton { text = Name(friend, loc) };
                row.AddToClassList("menu-button");
                row.style.marginBottom = 6;
                row.RegisterCallback((ClickEvent _) =>
                {
                    bool sent = SteamMatchmaking.InviteUserToLobby(lobby, friend);
                    Plugin.Log($"Invited a friend to {lobby}: {sent}");
                    row.text = loc.T(sent ? "BeaverBuddies.Host.Invited" : "BeaverBuddies.Host.InviteFailed", SteamFriends.GetFriendPersonaName(friend));
                    row.SetEnabled(!sent);
                });
                rows.Add(row);
            }
            content.Add(rows);
            return true;
        }

        // The hosted game's Steam lobby
        private static bool TryGetLobby(out CSteamID lobby)
        {
            lobby = CSteamID.Nil;
            if (EventIO.Get() is not ServerEventIO server) return false;
            SteamListener listener = server.SocketListener as SteamListener
                ?? (server.SocketListener as TimberNet.MultiSocketListener)?.GetListener<SteamListener>();
            if (listener == null || !listener.LobbyID.IsValid()) return false;
            lobby = listener.LobbyID;
            return true;
        }

        // Online friends, those playing this game first, then by name
        private static List<CSteamID> OnlineFriends()
        {
            var friends = new List<CSteamID>();
            int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < count; i++)
            {
                CSteamID friend = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                if (SteamFriends.GetFriendPersonaState(friend) != EPersonaState.k_EPersonaStateOffline) friends.Add(friend);
            }
            return friends.OrderBy(f => PlaysThis(f) ? 0 : 1).ThenBy(SteamFriends.GetFriendPersonaName).ToList();
        }

        private static bool PlaysThis(CSteamID friend) =>
            SteamFriends.GetFriendGamePlayed(friend, out FriendGameInfo_t game) && game.m_gameID.AppID() == SteamUtils.GetAppID();

        private static string Name(CSteamID friend, ILoc loc)
        {
            string name = SteamFriends.GetFriendPersonaName(friend);
            return PlaysThis(friend) ? loc.T("BeaverBuddies.Host.PlayingThis", name) : name;
        }
    }
}
