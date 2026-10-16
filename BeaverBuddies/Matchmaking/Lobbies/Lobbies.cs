#if IS_STEAM
using BeaverBuddies.Steam;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace BeaverBuddies.Matchmaking
{
    /**
     * A lobby two players meet in to set up a match (see MatchmakingSession):
     * Steam's, or the Worker's for players without Steam (see WorkerLobbies).
     * Members are known by a 64-bit id, Steam's own for a player with Steam.
     * Its events come on Unity's thread.
     */
    public interface ILobby
    {
        ulong Id { get; }
        ulong Owner { get; }
        IReadOnlyList<ulong> Members { get; }
        // What the owner says about the lobby: only they write it
        string GetData(string key);
        void SetData(string key, string value);
        // Many at once: the Worker's lobby takes them in one message
        void SetData(IReadOnlyDictionary<string, string> values);
        // What each member says about themselves
        string GetMemberData(ulong member, string key);
        void SetMemberData(string key, string value);
        string MemberName(ulong member);
        // Whether anyone else can come in: the owner's to say
        void SetJoinable(bool joinable);
        void Leave();
        event Action<ulong> MemberEntered;
        event Action<ulong> MemberLeft;
        // Its data or a member's changed
        event Action DataChanged;
        // This player lost the lobby without leaving it (the Worker's: the connection dropped)
        event Action Lost;
    }

    /** What matchmaking needs of Steam (see Lobbies.Steam), in types a copy without Steam has too. */
    public interface ISteamLobbies
    {
        ulong Me { get; }
        void Search(LobbyFilter filter, Action<List<ulong>> found);
        void Create(int max, Action<ILobby> created);
        void Join(ulong id, Action<ILobby> entered);
        // A listed lobby's data, which a search brings along, and its owner
        string Data(ulong lobby, string key);
        ulong Owner(ulong lobby);
        // A hosted game's lobby: listed, described, entered to join the game
        void MakePublic(ulong lobby);
        void SetData(ulong lobby, IReadOnlyDictionary<string, string> values);
        void EnterHostedGame(ulong lobby, Action<bool> done = null);
        // Online friends to invite to the hosted game, as buttons; false without a lobby to invite to
        bool AddFriends(UnityEngine.UIElements.VisualElement content, Timberborn.Localization.ILoc loc, out string message);
    }

    // Lobbies whose data has each of these, with room for one more
    public class LobbyFilter
    {
        public readonly Dictionary<string, string> Equal = new();
        public int Most = 20;
    }

    /**
     * Where lobbies are: Steam's when this player has Steam, so two Steam
     * players meet in a Steam lobby as always, and the Worker's otherwise.
     * A search looks in both when it can, so a player with Steam finds those
     * without it too. Worker lobbies' ids are below 2^56, under every Steam
     * id: a player waiting in a Steam lobby moves into a lower one, so they
     * and a player without Steam waiting in a Worker lobby still meet.
     * Nothing here touches Steam's types, so the Epic Games build can use it.
     */
    public static class Lobbies
    {
        public const ulong WorkerIdsBelow = 1UL << 56;

        public static bool SteamUp => SteamSupport.IsSteamEnabled && Settings.EnableSteam;

        /**
         * This copy's store, for the icon beside a player's name: the game's own
         * store module says (Timberborn.SteamStoreSystem in the Steam build), never
         * a mod's assembly. One we don't know gets no icon rather than a wrong one.
         */
        public static readonly string Platform = DetectPlatform();

        private static string DetectPlatform()
        {
            if (SteamSupport.Installed) return "steam";
            string[] store = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name)
                .Where(n => n.StartsWith("Timberborn.", StringComparison.Ordinal) && n.EndsWith("StoreSystem", StringComparison.Ordinal) && n != "Timberborn.StoreSystem")
                .ToArray();
            Plugin.Log($"[Match] Store module: {(store.Length == 0 ? "none" : string.Join(", ", store))}");
            return store.Any(n => n.IndexOf("Epic", StringComparison.OrdinalIgnoreCase) >= 0) ? "epic" : "other";
        }

        /**
         * Steam's half of matchmaking, when Steam is up; null when not. The one
         * way to Steam from code that also runs without it: only the factory
         * names the Steam class, so a copy without Steam never loads it.
         */
        public static ISteamLobbies Steam => SteamUp ? steam ??= CreateSteam() : null;
        private static ISteamLobbies steam;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ISteamLobbies CreateSteam() => new SteamLobbies();

        public static ulong Me => Steam?.Me ?? WorkerLobbies.Me;

        public static bool IsWorker(ulong id) => id < WorkerIdsBelow;

        /** Every lobby the filter finds, lowest id first; null if no place could be searched. */
        public static void Search(LobbyFilter filter, Action<List<ulong>> found)
        {
            ISteamLobbies steam = Steam;
            List<ulong> fromSteam = null, fromWorker = null;
            int waiting = steam != null ? 2 : 1;
            void Done()
            {
                if (--waiting > 0) return;
                found(fromSteam == null && fromWorker == null ? null
                    : (fromSteam ?? new List<ulong>()).Concat(fromWorker ?? new List<ulong>()).Distinct().OrderBy(id => id).ToList());
            }
            steam?.Search(filter, ids => { fromSteam = ids; Done(); });
            WorkerLobbies.Search(filter, lobbies => { fromWorker = lobbies?.Select(l => l.Id).ToList(); Done(); });
        }

        /** A new lobby this player owns, public and joinable; null if it couldn't be made. */
        public static void Create(int max, Action<ILobby> created)
        {
            if (Steam != null) Steam.Create(max, created);
            else WorkerLobbies.Create(max, created);
        }

        /** Into the lobby with that id; null if it's gone, full or closed. */
        public static void Join(ulong id, Action<ILobby> entered)
        {
            if (IsWorker(id)) WorkerLobbies.Join(id, entered);
            else if (Steam != null) Steam.Join(id, entered);
            else entered(null);
        }

        // ---- Unity's thread ----

        private static readonly ConcurrentQueue<Action> queue = new();

        // For what arrives on other threads: runs at the next Pump
        public static void Post(Action action) => queue.Enqueue(action);

        // Every frame, from MatchmakingSession.Update
        public static void Pump()
        {
            while (queue.TryDequeue(out Action action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Plugin.LogError($"[Match] {e}");
                }
            }
        }
    }
}
#endif
