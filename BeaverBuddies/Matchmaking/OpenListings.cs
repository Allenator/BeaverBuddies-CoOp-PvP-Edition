#if IS_STEAM
using BeaverBuddies.Steam;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BeaverBuddies.Matchmaking
{
    // A hosted game in the directory
    public class DirectoryListing
    {
        public string id;
        public string platform;
        public string joinCode;
        public string steamLobby;
        public Dictionary<string, string> data;
    }

    /**
     * The directory of hosted games on the signaling Worker (Signaling/ in
     * this repo), for the players who can't see Steam lobbies: the Epic Games
     * build, and anyone with Steam networking off. Every host lists its game
     * here, Steam or not, with its join code, and lists it again every 30
     * seconds while it's open: the Worker drops a listing nobody refreshes.
     * Free of Steam, so the Epic Games build can use it.
     */
    public static class OpenListings
    {
        private static readonly string Server = WebRtc.Signaling.Server.Replace("wss://", "https://");
        private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(30);
        private static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        // This copy's store, for the icon beside a host's name
        public static readonly string Platform = SteamSupport.Installed ? "steam"
            : AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name.IndexOf("Epic", StringComparison.OrdinalIgnoreCase) >= 0) ? "epic"
            : "other";

        private static readonly object gate = new object();
        private static DirectoryListing mine;
        private static string key;
        private static Timer refresh;
        // One request at a time: a refresh in flight when the game is unlisted would land after the delete and put it back
        private static readonly SemaphoreSlim writing = new SemaphoreSlim(1, 1);

        public static string MineId => mine?.id;

        /** Lists the hosted game: until Unlist, or until this copy stops refreshing it. */
        public static void Publish(Dictionary<string, string> data, string joinCode)
        {
            lock (gate)
            {
                mine = new DirectoryListing { id = RandomHex(16), platform = Platform, joinCode = joinCode, data = data };
                key = RandomHex(24);
                refresh?.Dispose();
                refresh = new Timer(_ => Put(), null, TimeSpan.Zero, RefreshEvery);
            }
            Plugin.Log($"[Match] Listing the hosted game in the directory as {mine.id} with code {joinCode}");
        }

        // Steam players see the game in its lobby and join through it
        public static void SetSteamLobby(ulong lobby)
        {
            lock (gate)
            {
                if (mine == null) return;
                mine.steamLobby = lobby.ToString();
            }
            Task.Run(Put);
        }

        public static void Unlist()
        {
            DirectoryListing gone;
            string goneKey;
            lock (gate)
            {
                gone = mine;
                goneKey = key;
                mine = null;
                refresh?.Dispose();
                refresh = null;
            }
            if (gone == null) return;
            Plugin.Log($"[Match] {gone.id} is off the directory");
            Task.Run(async () =>
            {
                await writing.WaitAsync();
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Delete, $"{Server}/listings/{gone.id}");
                    request.Headers.Add("X-Listing-Key", goneKey);
                    await http.SendAsync(request);
                }
                catch (Exception e)
                {
                    Plugin.LogWarning($"[Match] Couldn't take {gone.id} off the directory: {e.Message}");
                }
                finally
                {
                    writing.Release();
                }
            });
        }

        private static async Task Put()
        {
            await writing.WaitAsync();
            string body, listingKey, id;
            lock (gate)
            {
                if (mine == null)
                {
                    writing.Release();
                    return;
                }
                body = JsonConvert.SerializeObject(mine);
                listingKey = key;
                id = mine.id;
            }
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Put, $"{Server}/listings/{id}")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                request.Headers.Add("X-Listing-Key", listingKey);
                HttpResponseMessage response = await http.SendAsync(request);
                if (!response.IsSuccessStatusCode) Plugin.LogWarning($"[Match] The directory refused {id}: {(int)response.StatusCode}");
            }
            catch (Exception e)
            {
                Plugin.LogWarning($"[Match] Couldn't reach the directory: {e.Message}");
            }
            finally
            {
                writing.Release();
            }
        }

        // The hosted games listed now; null if the directory can't be reached
        public static async Task<List<DirectoryListing>> Fetch()
        {
            try
            {
                string json = await http.GetStringAsync($"{Server}/listings");
                return JObject.Parse(json)["listings"]?.ToObject<List<DirectoryListing>>() ?? new List<DirectoryListing>();
            }
            catch (Exception e)
            {
                Plugin.LogWarning($"[Match] Couldn't read the directory: {e.Message}");
                return null;
            }
        }

        // Not Guid.NewGuid, which draws on the game's random (see GuidPatcher)
        private static string RandomHex(int bytes)
        {
            byte[] buffer = new byte[bytes];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(buffer);
            return string.Concat(buffer.Select(b => b.ToString("x2")));
        }
    }
}
#endif
