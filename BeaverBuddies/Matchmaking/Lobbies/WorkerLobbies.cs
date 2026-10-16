#if IS_STEAM
using BeaverBuddies.WebRtc;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BeaverBuddies.Matchmaking
{
    // A lobby as the Worker's search lists it
    public class LobbySummary
    {
        public ulong Id;
        public ulong Owner;
        public Dictionary<string, string> Data;
    }

    /**
     * Lobbies on the signaling Worker (Signaling/src/lobby.ts), for players
     * without Steam: a WebSocket per member, which the Worker tells every
     * change in the lobby. Messages come in on another thread and are
     * applied on Unity's (see Lobbies.Pump), where its events fire.
     */
    public static class WorkerLobbies
    {
        private static readonly string Http = Signaling.Server.Replace("wss://", "https://");
        private static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

        private static ulong me;

        // This player's id in lobbies, from their BeaverBuddies id: below 2^56, under every Steam id
        public static ulong Me
        {
            get
            {
                if (me != 0) return me;
                using var sha = SHA1.Create();
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Players.PlayerIdentity.LocalID));
                ulong id = 0;
                for (int i = 0; i < 7; i++) id = (id << 8) | hash[i];
                return me = Math.Max(id, 1);
            }
        }

        public static void Search(LobbyFilter filter, Action<List<LobbySummary>> found)
        {
            string query = string.Join("&", filter.Equal.Select(p => $"eq.{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value ?? "")}"));
            Task.Run(async () =>
            {
                List<LobbySummary> lobbies = null;
                try
                {
                    string json = await http.GetStringAsync($"{Http}/lobbies?most={filter.Most}&{query}");
                    lobbies = JObject.Parse(json)["lobbies"]?.Select(l => new LobbySummary
                    {
                        Id = ulong.Parse((string)l["id"]),
                        Owner = ulong.TryParse((string)l["owner"], out ulong owner) ? owner : 0,
                        Data = l["data"]?.ToObject<Dictionary<string, string>>() ?? new Dictionary<string, string>(),
                    }).ToList() ?? new List<LobbySummary>();
                }
                catch (Exception e)
                {
                    Plugin.LogWarning($"[Match] Couldn't search the Worker's lobbies: {e.Message}");
                }
                Lobbies.Post(() => found(lobbies));
            });
        }

        public static void Create(int max, Action<ILobby> created)
        {
            byte[] bytes = new byte[8];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            ulong id = Math.Max(BitConverter.ToUInt64(bytes, 0) % Lobbies.WorkerIdsBelow, 1);
            Open(id, $"&create=1&max={max}", created);
        }

        public static void Join(ulong id, Action<ILobby> entered) => Open(id, "", entered);

        private static void Open(ulong id, string extra, Action<ILobby> done)
        {
            string name = Uri.EscapeDataString(Players.PlayerIdentity.LocalName ?? "");
            var uri = new Uri($"{Signaling.Server}/lobby/{id}?member={Lobbies.Me}&name={name}{extra}");
            Task.Run(async () =>
            {
                Signaling socket;
                try
                {
                    socket = await Signaling.Connect(uri, ConnectTimeout, start: false);
                }
                catch (Exception e)
                {
                    // Gone, full or closed: the Worker refuses the WebSocket
                    Plugin.Log($"[Match] Couldn't open the Worker's lobby {id}: {e.Message}");
                    Lobbies.Post(() => done(null));
                    return;
                }
                var lobby = new WorkerLobby(id, socket);
                lobby.Ready += () => done(lobby);
                lobby.Failed += () => done(null);
                socket.Start();
            });
        }
    }

    internal class WorkerLobby : ILobby
    {
        private class Member
        {
            public ulong Id;
            public string Name;
            public readonly Dictionary<string, string> Data = new();
        }

        public ulong Id { get; }
        public ulong Owner { get; private set; }
        public IReadOnlyList<ulong> Members => members.Select(m => m.Id).ToList();

        public event Action<ulong> MemberEntered;
        public event Action<ulong> MemberLeft;
        public event Action DataChanged;
        public event Action Lost;
        // The lobby as it is when this player came in, or the Worker said no
        internal event Action Ready, Failed;

        private readonly Signaling socket;
        private readonly Dictionary<string, string> data = new();
        private readonly List<Member> members = new();
        private bool ready, left;

        public WorkerLobby(ulong id, Signaling socket)
        {
            Id = id;
            this.socket = socket;
            socket.OnMessage += message => Lobbies.Post(() => Apply(message));
            socket.OnClosed += () => Lobbies.Post(Dropped);
        }

        private static ulong Parse(JToken token) => ulong.TryParse((string)token, out ulong id) ? id : 0;

        private void Apply(JObject message)
        {
            if (left) return;
            switch ((string)message["type"])
            {
                case "state":
                    Owner = Parse(message["owner"]);
                    data.Clear();
                    foreach (var pair in (JObject)message["data"] ?? new JObject()) data[pair.Key] = (string)pair.Value;
                    members.Clear();
                    foreach (JToken m in (JArray)message["members"] ?? new JArray())
                    {
                        var member = new Member { Id = Parse(m["id"]), Name = (string)m["name"] };
                        foreach (var pair in (JObject)m["data"] ?? new JObject()) member.Data[pair.Key] = (string)pair.Value;
                        members.Add(member);
                    }
                    if (!ready)
                    {
                        ready = true;
                        Ready?.Invoke();
                    }
                    break;
                case "entered":
                {
                    ulong id = Parse(message["member"]);
                    members.RemoveAll(m => m.Id == id);
                    members.Add(new Member { Id = id, Name = (string)message["name"] });
                    MemberEntered?.Invoke(id);
                    break;
                }
                case "left":
                {
                    ulong id = Parse(message["member"]);
                    members.RemoveAll(m => m.Id == id);
                    Owner = Parse(message["owner"]);
                    MemberLeft?.Invoke(id);
                    break;
                }
                case "data":
                    data[(string)message["key"]] = (string)message["value"];
                    DataChanged?.Invoke();
                    break;
                case "memberData":
                {
                    Member member = Find(Parse(message["member"]));
                    if (member == null) return;
                    member.Data[(string)message["key"]] = (string)message["value"];
                    DataChanged?.Invoke();
                    break;
                }
            }
        }

        // The connection to the Worker dropped: as far as this player knows, everyone else left
        private void Dropped()
        {
            if (left) return;
            left = true;
            if (!ready)
            {
                Failed?.Invoke();
                return;
            }
            Plugin.LogWarning($"[Match] Lost the connection to the Worker's lobby {Id}");
            foreach (ulong other in members.Select(m => m.Id).Where(m => m != Lobbies.Me).ToList()) MemberLeft?.Invoke(other);
            Lost?.Invoke();
        }

        private Member Find(ulong id) => members.FirstOrDefault(m => m.Id == id);

        public string GetData(string key) => data.TryGetValue(key, out string value) ? value : "";

        public void SetData(string key, string value)
        {
            if (Owner != Lobbies.Me) return;
            data[key] = value;
            Send(new JObject { ["type"] = "data", ["key"] = key, ["value"] = value });
        }

        public string GetMemberData(ulong member, string key) =>
            Find(member)?.Data.TryGetValue(key, out string value) == true ? value : "";

        public void SetMemberData(string key, string value)
        {
            Member mine = Find(Lobbies.Me);
            if (mine != null) mine.Data[key] = value;
            Send(new JObject { ["type"] = "memberData", ["key"] = key, ["value"] = value });
        }

        public string MemberName(ulong member) => Find(member)?.Name ?? "";

        public void SetJoinable(bool joinable) => Send(new JObject { ["type"] = "joinable", ["value"] = joinable });

        private void Send(JObject message)
        {
            if (left) return;
            socket.Send(message).ContinueWith(t => Plugin.LogWarning($"[Match] Couldn't tell the Worker's lobby {Id}: {t.Exception?.InnerException?.Message}"),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        public void Leave()
        {
            if (left) return;
            left = true;
            socket.Dispose();
        }
    }
}
#endif
