using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BeaverBuddies.WebRtc
{
    /**
     * A WebSocket to the signaling Worker (Signaling/ in this repo). Mostly
     * a room per join code, where the host and the players joining it trade
     * the offers, answers and ICE candidates that set up a WebRTC connection;
     * also a matchmaking lobby (see WorkerLobbies). Nothing of the game
     * goes through it.
     */
    public class Signaling : IDisposable
    {
        public const string Server = "wss://beaverbuddies-signaling.federico-zivolo-bf8.workers.dev";

        private readonly ClientWebSocket socket = new ClientWebSocket();
        private readonly CancellationTokenSource closing = new CancellationTokenSource();
        private readonly SemaphoreSlim sending = new SemaphoreSlim(1, 1);

        public event Action<JObject> OnMessage;
        // The room or the connection to it is gone
        public event Action OnClosed;

        public static Task<Signaling> Open(string code, bool host, TimeSpan? timeout = null) =>
            Connect(new Uri($"{Server}/room/{code}?role={(host ? "host" : "guest")}"), timeout ?? TimeSpan.FromSeconds(15));

        /**
         * A WebSocket to the Worker at that address. Messages flow once
         * Start is called, so whoever subscribes first misses none; with
         * start, at once.
         */
        public static async Task<Signaling> Connect(Uri uri, TimeSpan timeout, bool start = true)
        {
            var signaling = new Signaling();
            using (var giveUp = new CancellationTokenSource(timeout))
            {
                try
                {
                    await signaling.socket.ConnectAsync(uri, giveUp.Token);
                }
                catch (Exception)
                {
                    signaling.socket.Dispose();
                    throw;
                }
            }
            if (start) signaling.Start();
            return signaling;
        }

        private int started;

        public void Start()
        {
            if (Interlocked.Exchange(ref started, 1) == 0) _ = Task.Run(ReceiveLoop);
        }

        public async Task Send(JObject message)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message.ToString(Newtonsoft.Json.Formatting.None));
            await sending.WaitAsync();
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, closing.Token);
            }
            finally
            {
                sending.Release();
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[16 * 1024];
            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    using var message = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), closing.Token);
                        message.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    JObject parsed;
                    try
                    {
                        parsed = JObject.Parse(Encoding.UTF8.GetString(message.ToArray()));
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    OnMessage?.Invoke(parsed);
                }
            }
            catch (Exception e) when (!closing.IsCancellationRequested)
            {
                Plugin.LogWarning($"[WebRTC] Signaling dropped: {e.Message}");
            }
            catch (Exception) { }
            OnClosed?.Invoke();
        }

        public void Dispose()
        {
            if (closing.IsCancellationRequested) return;
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None).Wait(1000);
                }
            }
            catch (Exception) { }
            closing.Cancel();
            socket.Dispose();
        }
    }
}
