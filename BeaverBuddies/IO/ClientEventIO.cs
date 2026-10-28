using System;
using TimberNet;
using static TimberNet.TimberNetBase;

namespace BeaverBuddies.IO
{
    public class ClientEventIO : NetIOBase<TimberClient>
    {
        // If the client receives an event to replay, no matter where it
        // originated, it shouldn't send it *back* to the server, since the
        // server is what sent the event.
        public override bool RecordReplayedEvents => false;

        // Clients don't need to send heartbeats
        public override bool ShouldSendHeartbeat => false;

        // The client doesn't get to do anything from the user directly.
        // The client should send user-initiated events to the server.
        // It has to wait until an event is received from the server.
        public override UserEventBehavior UserEventBehavior => UserEventBehavior.Send;

        private MapReceived mapReceivedCallback;
        private bool FailedToConnect = false;
        private bool _mapReceived;
        private float _nextHello;
        private bool _saidHello;
        private ISocketStream _socket;
        // Since when the host has said nothing, though it says it's there every 2 s (Players.PlayerPresence): a host
        // whose end dropped this player, and this end didn't notice, leaves it waiting in silence. Logged with this
        // end's state when the guest is told (Connect.HostSilence), which says nothing of it
        private DateTime? _silentSince;
        private const double SilentSeconds = Connect.HostSilence.SilentSeconds;

        protected override void Updated()
        {
            CheckHostHeard();
            SayHello();
        }

        private void CheckHostHeard()
        {
            if (!_mapReceived || HostLeft || NetBase == null) return;
            // The host's lobby is quiet on purpose, and the host is silent while it loads the game it starts (see
            // Connect.GuestLobby): not a connection gone quiet
            if (Connect.GuestLobby.IsOpen)
            {
                _silentSince = null;
                return;
            }
            DateTime heard = DateTime.UtcNow.AddSeconds(-NetBase.SecondsSinceHeard);
            double silent = (DateTime.UtcNow - heard).TotalSeconds;
            if (_silentSince == null && silent > SilentSeconds)
            {
                _silentSince = heard;
                Plugin.LogWarning($"Nothing from the host for {silent:0} s; this end of the connection: " +
                    (_socket is WebRtc.WebRtcSocket webRtc ? webRtc.State : $"{(_socket.Connected ? "open" : "ended")}"));
            }
            else if (_silentSince != null && silent < SilentSeconds)
            {
                Plugin.Log($"Heard from the host again, after {(heard - _silentSince.Value).TotalSeconds:0} s");
                _silentSince = null;
            }
        }

        // Who this is, until the map comes: the host's box lists them from the moment they connect, not once they've loaded
        private void SayHello()
        {
            if (_saidHello || NetBase == null) return;
            // Those sent while a WebRTC connection was still opening never left: with the map in it's open, so one last
            if (_mapReceived) _saidHello = true;
            else if (UnityEngine.Time.unscaledTime < _nextHello) return;
            _nextHello = UnityEngine.Time.unscaledTime + 1f;
            try { NetBase.SendTransientMessage(Connect.HostLobby.Hello()); }
            catch (Exception e) { Plugin.LogWarning($"Couldn't say hello to the host: {e.Message}"); }
        }
        // The host's end closed mid-game: the game can't go on without it
        public bool HostLeft { get; private set; }
        // The host turned this player away for another build: the one it plays (see Connect.BuildIdentity)
        public string RefusedByHost { get; private set; }
        // ...or because it never heard which build this one is, in time
        public bool RefusedSilently { get; private set; }
        // What pauses everyone, as the host chose it for every player in its game (see Settings.PauseReductionSetting);
        // null until the host's welcome or presence says, or from a host that doesn't
        public int? HostPauses { get; set; }
        // How much of the game's slowing of fast speeds the host lifts for every player (see Settings.SpeedThrottleSetting)
        public int? HostThrottle { get; set; }

        private void OnTransientMessage(Newtonsoft.Json.Linq.JObject message)
        {
            string type = message[TYPE_KEY]?.ToString();
            // The host says it again with its presence, should it change its mind mid-game
            if (type == Players.PlayerPresence.MessageType && message.Value<bool?>("host") == true)
            {
                if (message.Value<int?>("pauses") is int pauses) HostPauses = pauses;
                if (message.Value<int?>("throttle") is int throttle) HostThrottle = throttle;
                return;
            }
            if (type != Connect.BuildIdentity.RefusedType) return;
            RefusedSilently = message.Value<string>("reason") == Connect.BuildIdentity.RefusedSilent;
            RefusedByHost = message.Value<string>("host") ?? "?";
            Plugin.LogWarning(RefusedSilently
                ? "Turned away by the host, which didn't hear this player's build in time"
                : $"Turned away by the host, which plays {RefusedByHost}");
        }

        /** Seconds since anything last came from the host. */
        public double SecondsSinceHostHeard => NetBase?.SecondsSinceHeard ?? 0;

        private ClientEventIO(ISocketStream socket, MapReceived mapReceivedCallback,
            Action<string> onError)
        {
            this.mapReceivedCallback = mapReceivedCallback;
            _socket = socket;

            TryRegisterSteamPacketReceiver(socket);

            NetBase = new TimberClient(socket);
            NetBase.OnMapReceived += mapReceivedCallback;
            NetBase.OnMapReceived += _ => _mapReceived = true;
            NetBase.OnReload += Connect.ResyncService.ReceiveSave;
            NetBase.OnLog += Plugin.Log;
            NetBase.OnTransientMessage += EventIO.RaiseTransientMessageReceived;
            NetBase.OnTransientMessage += OnTransientMessage;
            NetBase.OnDisconnected += () => HostLeft = true;
            NetBase.OnError += (error) =>
            {
                Plugin.LogError(error);
                CleanUp();
                FailedToConnect = true;
                // A dead connection mustn't stay the game's IO, or the next
                // game started here loads as a client with no host
                if (EventIO.Get() == this) EventIO.Reset();
                onError(error);
            };
            try
            {
                NetBase.Start();
            }
            catch (Exception ex)
            {
                onError(ex.Message);
                Plugin.LogError(ex.ToString());
                CleanUp();
                FailedToConnect = true;
            }
        }

        private void CleanUp()
        {
            if (NetBase == null) return;
            NetBase.OnMapReceived -= mapReceivedCallback;
            NetBase.OnReload -= Connect.ResyncService.ReceiveSave;
            NetBase.OnLog -= Plugin.Log;
            NetBase.OnTransientMessage -= EventIO.RaiseTransientMessageReceived;
            NetBase.OnTransientMessage -= OnTransientMessage;
            // Let go of the socket (and a Steam P2P session) too
            try { NetBase.Close(); }
            catch (Exception e) { Plugin.LogWarning($"Couldn't close the connection: {e.Message}"); }
            NetBase = null;
        }

        public static ClientEventIO Create(ISocketStream socket, MapReceived mapReceivedCallback, Action<string> onError)
        {
            ClientEventIO eventIO = new ClientEventIO(socket, mapReceivedCallback, onError);
            if (eventIO.FailedToConnect) return null;
            return eventIO;
        }
    }
}
