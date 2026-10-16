using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TimberNet
{
    public class ConnectionFailureException : Exception
    {
        public ConnectionFailureException() : base("Client connection timed out") { }
    }

    public class TimberClient : TimberNetBase
    {

        private readonly ISocketStream client;

        public override bool ShouldTick => base.ShouldTick && receivedEvents.Count > 0;

        public TimberClient(ISocketStream client) : base()
        {
            this.client = client;
        }

        public override void DoUserInitiatedEvent(JObject message)
        {
            // Don't actually do the event (i.e. add it to the hash)
            // Wait for the server to confirm w/ adjusted Tick
            message[EPOCH_KEY] = Epoch;
            SendEvent(client, message);
        }

        public override void SendTransientMessage(JObject message)
        {
            message[TRANSIENT_KEY] = true;
            SendTransientMessage(client, message);
        }

        protected override void ProcessReceivedEvent(JObject message)
        {
            base.ProcessReceivedEvent(message);
            Log($"Received event: {message[TYPE_KEY]?.ToString() ?? "<null>"}");
            AddEventToHash(message);
        }

        /**
         * Connects in the background, so the game doesn't stand still while
         * it does (a WebRTC connection takes seconds): a failure arrives as
         * OnError on the Update() thread, like any other.
         */
        public override void Start()
        {
            base.Start();
            Task.Run(async () =>
            {
                try
                {
                    Task connect = client.ConnectAsync();
                    if (await Task.WhenAny(connect, Task.Delay(client.ConnectTimeoutMs)) != connect) throw new ConnectionFailureException();
                    await connect;
                }
                catch (Exception e)
                {
                    client.Close();
                    ReportError((e as AggregateException)?.InnerException?.Message ?? e.Message);
                    return;
                }
                // On a thread of its own: this one may be the connection's, which delivers what's read
                _ = Task.Run(() => StartListening(client, true));
            });
        }


        public override void Close()
        {
            base.Close();
            client.Close();
        }
    }
}
