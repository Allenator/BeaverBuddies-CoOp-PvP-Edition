using BeaverBuddies.Connect;
﻿using Newtonsoft.Json.Linq;
using System;
using TimberNet;
using System.Threading.Tasks;
using BeaverBuddies.Events;
using BeaverBuddies.Steam;
using System.Net.Sockets;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace BeaverBuddies.IO
{
    // With TimberBorn's current architecture, a player can't simply be sent
    // a save of the game being played. This is because a number
    // of variables are randomly initialized during load (e.g. tree lifespans)
    // rather than serialized, since they aren't that important. As a result, the
    // save won't create the same gamestate as a currently loaded game. The only
    // way to ensure that the gamestate is the same is to have all clients join
    // at load time.
    // So a player joining a game in progress has it saved and reloaded from
    // that save by everyone: the Host, the players already in (who are sent
    // it over their connection) and them (see Reload).
    public class ServerEventIO : NetIOBase<TimberServer>
    {
        // Anything that happens on the server should be recorded and
        // sent to the clients.
        public override bool RecordReplayedEvents => true;

        // Servers need to send heartbeats so clients know to progress.
        public override bool ShouldSendHeartbeat => true;

        // The server should wait until the next update to play a
        // user-initiated event, to make sure that the events
        // happen in the same order for the server and clients.
        public override UserEventBehavior UserEventBehavior => UserEventBehavior.QueuePlay;

        public ISocketListener SocketListener { get; private set; }

        private readonly object mapLock = new object();
        // The map anyone joining is sent, until the game starts
        private byte[] mapBytes;
        // Once it has, the map they wait for: the game's, saved for them
        private TaskCompletionSource<byte[]> nextMap;
        private volatile bool joinWaiting;
        // Started before the game it hosts has a map: anyone joining waits for SetMap
        private bool preparing;

        /**
         * True once someone joined the game in progress: it's to be saved
         * and reloaded for them (see Reload).
         */
        public bool JoinWaiting => joinWaiting;

        // Where a saved game hosted by loading it twice is (see Connect.SavedGameHosting)
        public enum HostingStage { Running, Waiting, Loading }

        // Running, unless this hosts a saved game: Waiting from before it starts listening until the
        // host presses Start, then Loading until the game it loads is in. Read on the joining threads
        private volatile HostingStage stage = HostingStage.Running;
        public HostingStage Stage { get => stage; set => stage = value; }

        /** The host hasn't started the saved game it hosts: its guests wait in its lobby. */
        public bool Waiting => stage != HostingStage.Running;

        // See note above
        // False if it couldn't start (e.g. the port is taken): nothing is left listening
        public bool Start(byte[] mapBytes)
        {
            this.mapBytes = mapBytes;
            Connect.HostLobby.ClearHellos();
            if (mapBytes == null)
            {
                preparing = true;
                nextMap = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            try
            {
                List<ISocketListener> listeners = [
                    new TCPListenerWrapper(Settings.Port),
                    // Anyone, Steam or not, joins by the game's code
                    new WebRtc.WebRtcListener(WebRtc.JoinCode.New()),
                ];
                if (SteamSupport.IsSteamEnabled && Settings.EnableSteam)
                {
                    listeners.Add(new SteamListener());
                }
                SocketListener = new MultiSocketListener(listeners.ToArray());
                if (SocketListener is MultiSocketListener)
                {
                    foreach (ISocketListener child in ((MultiSocketListener)SocketListener).Listeners)
                    {
                        TryRegisterSteamPacketReceiver(child);
                    }
                }
                else
                {
                    TryRegisterSteamPacketReceiver(SocketListener);
                }
                NetBase = new TimberServer(
                    SocketListener,
                    ProvideMap,
                    CreateInitEvent()
                );
                //netBase = new TimberServer(port, mapProvider, null);
                // The host is one of them
                NetBase.MaxClients = () => PlayerLimit.Setting.Current - 1;
                NetBase.OnLog += Plugin.Log;
                NetBase.OnTransientMessage += EventIO.RaiseTransientMessageReceived;
                NetBase.OnTransientMessage += Connect.HostLobby.OnTransientMessage;
                NetBase.OnMapReceived += NetBase_OnClientConnected;
                NetBase.AcceptTransientFrom = CheckBuild;
                NetBase.AcceptEventsFrom = client => !refused.Contains(client) && (verified.Contains(client) || !Settings.StrictBuild);
                NetBase.PrepareEventFrom = StampPlayer;
                NetBase.OnClientGone += client =>
                {
                    Plugin.Log($"{(names.TryGetValue(client, out string name) ? name : client.Name ?? "A player")}'s connection ended: they left, or it dropped");
                    if (playerIDs.TryRemove(client, out string id)) gone.Enqueue(id);
                };
                // Starts listening, which throws if the port is taken
                NetBase.Start();
                return true;
            }
            catch (Exception e)
            {
                Plugin.LogError("Failed to start server");
                Plugin.LogError(e.ToString());
                try { NetBase?.Close(); }
                catch (Exception closeError) { Plugin.LogWarning($"Couldn't stop the server: {closeError.Message}"); }
                NetBase = null;
                return false;
            }
        }

        private Func<JObject> CreateInitEvent()
        {
            // It should be ok to send an init event even if the client is joining before
            // the server, since a) it won't do much on the Host (just set the random seed)
            // and b) the client will overwrite these values later whent he Host finished
            // loading the map.
            return () =>
            {
                var message = InitializeClientEvent.Create();
                message.ticksSinceLoad = 0;
                // A saved game's host waits too, though not in a new game's box
                message.hostWaiting |= Waiting;
                Plugin.Log($"Sending start state: {JsonSettings.Serialize(message)}");
                return JObject.Parse(JsonSettings.Serialize(message));
            };
        }

        // Called from the threads clients join on
        private Task<byte[]> ProvideMap()
        {
            lock (mapLock)
            {
                if (nextMap == null) return Task.FromResult(mapBytes);
                if (!preparing) joinWaiting = true;
                return nextMap.Task;
            }
        }

        /** The map of the game this server was started for, once it's saved: those waiting to join get it. */
        public void SetMap(byte[] mapBytes)
        {
            TaskCompletionSource<byte[]> waiting;
            lock (mapLock)
            {
                this.mapBytes = mapBytes;
                preparing = false;
                waiting = nextMap;
                nextMap = null;
            }
            waiting?.TrySetResult(mapBytes);
        }

        public void OnGameStarted()
        {
            // The game behind a saved game's box is loaded again when the host starts: it never
            // should tick, and if it does, those joining still get the save, not a reload
            if (Waiting)
            {
                Plugin.LogWarning($"The game ticked while the host waits ({stage}): not started");
                return;
            }
            Plugin.Log("Game started: a player joining now has it reloaded for them");
            lock (mapLock)
            {
                // Not to be sent again: the game has moved on from it
                mapBytes = null;
                nextMap ??= new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            NetBase?.ForgetReloadedMap();
        }

        /**
         * The game was saved for the players waiting to join it, and is
         * about to be loaded from that save: the players already in are
         * sent it to load too, and those waiting get it as they would
         * before the game started, until it starts again.
         */
        public void Reload(byte[] mapBytes)
        {
            TaskCompletionSource<byte[]> waiting;
            lock (mapLock)
            {
                this.mapBytes = mapBytes;
                waiting = nextMap;
                nextMap = null;
                joinWaiting = false;
            }
            Plugin.Log($"Reloading the game for everyone from a map with length {mapBytes.Length}");
            NetBase.ReloadClients(mapBytes);
            waiting?.TrySetResult(mapBytes);
        }

        /**
         * What the guests did while the host waited, sent for a game that hasn't started: dropped
         * rather than played at its first tick. What has arrived is taken first.
         */
        public void DiscardPendingClientEvents()
        {
            if (NetBase == null) return;
            Update();
            int count = NetBase.DiscardReceivedEvents();
            if (count > 0) Plugin.Log($"Discarded {count} events the guests sent before the game started");
        }

        /** The game couldn't be saved for the players waiting: they're turned away. */
        public void TurnAwayJoins()
        {
            TaskCompletionSource<byte[]> waiting;
            lock (mapLock)
            {
                waiting = nextMap;
                if (waiting == null) return;
                nextMap = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                joinWaiting = false;
            }
            waiting.TrySetException(new Exception("The Host couldn't save the game"));
        }

        private void NetBase_OnClientConnected(byte[] mapBytes)
        {

        }

        private float _nextListingCheck;

        // A hosted game stays listed while it runs, until it has no room left
        // Nobody can join a game no longer hosted: off the list
        protected override void Closed()
        {
#if IS_STEAM
            Matchmaking.HostedListing.Unlist();
#endif
            WebRtc.JoinCode.Clear();
        }

        // ---- Who's on this same build (see Connect.BuildIdentity), all on the Update() thread

        // Each guest's connection once its hello or presence says its build is this one's, or once
        // it says another to a host that doesn't mind; those turned away, whatever they say after
        private readonly HashSet<ISocketStream> verified = new();
        private readonly HashSet<ISocketStream> refused = new();
        // When each guest's connection was first seen, to turn away one that never says its build: a
        // build from before it said, or one so slow its last hello came long after its map
        private readonly Dictionary<ISocketStream, float> firstSeen = new();
        private const float SayBuildWithin = 120;
        // The name each guest's hello or presence gives, for saying whose connection ended, which a connection's
        // listening thread does
        private readonly System.Collections.Concurrent.ConcurrentDictionary<ISocketStream, string> names = new();
        // And their player IDs, for those whose connection ended, to be played as left on the Update() thread
        private readonly System.Collections.Concurrent.ConcurrentDictionary<ISocketStream, string> playerIDs = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> gone = new();

        // ---- Whose events they are (D7): one connection is one player, whatever its events say

        // The first player ID each connection gave, which its events are played and sent on as (see StampPlayer).
        // Kept once it ends, since its last events can still be on their way to the Update() thread
        private readonly System.Collections.Concurrent.ConcurrentDictionary<ISocketStream, string> playingAs = new();
        // What's been said once about each connection, so it isn't said every event
        private readonly HashSet<(ISocketStream, string)> toldOnce = new();

        private string Who(ISocketStream client) => names.TryGetValue(client, out string name) ? name : client.Name ?? "A player";

        private void SayOnce(ISocketStream client, string what, string message)
        {
            if (toldOnce.Add((client, what))) Plugin.LogWarning(message);
        }

        // The first ID a connection gives is the one it plays as, and the one let go of when it ends
        private void SaysPlayer(ISocketStream client, string id)
        {
            string first = playingAs.GetOrAdd(client, id);
            playerIDs[client] = first;
            if (first != id) SayOnce(client, "second ID", $"{Who(client)} now says they're {id}, after {first}: still played as {first}");
        }

        // A guest's event, before it's played and sent on: as the player its connection said it is, whatever the event
        // itself says. One from before the connection said (only with "same build only" off, which otherwise drops
        // them) is played as sent, as before
        private void StampPlayer(ISocketStream client, JObject message)
        {
            if (!playingAs.TryGetValue(client, out string id))
            {
                SayOnce(client, "unsaid", $"{Who(client)}'s events came before they said who they are: played as sent");
                return;
            }
            Stamp(client, message, id);
        }

        private void Stamp(ISocketStream client, JObject message, string id)
        {
            string sentAs = message.Value<string>("playerID");
            if (sentAs != id)
            {
                if (sentAs != null) SayOnce(client, "other ID", $"{Who(client)} sent {message[TimberNetBase.TYPE_KEY]} as {sentAs}: played as {id}, who they said they are");
                message["playerID"] = id;
            }
            // A tick's events can come grouped
            if (message["events"] is JArray grouped)
            {
                foreach (JToken inner in grouped)
                {
                    if (inner is JObject innerEvent) Stamp(client, innerEvent, id);
                }
            }
        }

        // What a guest says of itself as it joins, and once it's in, says which build it plays
        private bool CheckBuild(ISocketStream client, JObject message)
        {
            string type = message[TimberNetBase.TYPE_KEY]?.ToString();
            bool saysWho = type == Connect.HostLobby.HelloType || type == Players.PlayerPresence.MessageType;
            if (saysWho && message.Value<string>("name") is string said) names[client] = said;
            if (saysWho && message.Value<string>("playerID") is string id) SaysPlayer(client, id);
            if (refused.Contains(client)) return false;
            if (verified.Contains(client)) return true;
            if (!saysWho) return true;
            string theirs = message.Value<string>("compat");
            if (theirs == Connect.BuildIdentity.CompatValue)
            {
                verified.Add(client);
                return true;
            }
            string name = message.Value<string>("name") ?? client.Name ?? "A player";
            string build = message.Value<string>("build") ?? "an older build that doesn't say it";
            if (!Settings.StrictBuild)
            {
                Plugin.LogWarning($"Letting in {name}, who plays BeaverBuddies {build}, though this hosts {Connect.BuildIdentity.Describe}");
                verified.Add(client);
                return true;
            }
            Refuse(client, name, build);
            return false;
        }

        // Turned away for another build, or for never saying which (build null): each told apart, to the player and the host
        private void Refuse(ISocketStream client, string name, string build)
        {
            Plugin.LogWarning(build == null
                ? $"Turning away {name}: their game never said its build in {SayBuildWithin} s"
                : $"Turning away {name}: they play BeaverBuddies {build}, this hosts {Connect.BuildIdentity.Describe}");
            refused.Add(client);
            NetBase.Refuse(client, new JObject
            {
                [TimberNetBase.TYPE_KEY] = Connect.BuildIdentity.RefusedType,
                ["host"] = Connect.BuildIdentity.Describe,
                ["reason"] = build == null ? Connect.BuildIdentity.RefusedSilent : Connect.BuildIdentity.RefusedOtherBuild,
            });
            Connect.BuildNotice.Show(name, build);
        }

        private void TurnAwayUnsaid()
        {
            if (NetBase == null || !Settings.StrictBuild) return;
            float now = Time.unscaledTime;
            foreach (ISocketStream client in NetBase.ConnectedStreams())
            {
                if (verified.Contains(client) || refused.Contains(client)) continue;
                if (!firstSeen.TryGetValue(client, out float since)) firstSeen[client] = now;
                else if (now - since >= SayBuildWithin) Refuse(client, client.Name ?? "A player", null);
            }
        }

        protected override void Updated()
        {
            TurnAwayUnsaid();
            while (gone.TryDequeue(out string id)) ReplayEvent.GetReplayServiceIfReady()?.RecordEvent(new PlayerLeftEvent { leftID = id });
#if IS_STEAM
            if (UnityEngine.Time.unscaledTime < _nextListingCheck) return;
            _nextListingCheck = UnityEngine.Time.unscaledTime + 2f;
            Matchmaking.HostedListing.Joined(NetBase.GetConnectedClients().Count);
#endif
        }
    }
}
