using Newtonsoft.Json.Linq;
using SIPSorcery.Net;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using TimberNet;

namespace BeaverBuddies.WebRtc
{
    /**
     * The host's end of WebRTC: it holds the game's room open on the signaling
     * Worker for as long as it hosts, answers each player's offer with a peer
     * connection of its own, and hands the player over as a WebRtcSocket once
     * their data channel opens.
     */
    public class WebRtcListener : ISocketListener
    {
        private readonly string code;
        private readonly ConcurrentQueueWithWait<WebRtcSocket> joined = new ConcurrentQueueWithWait<WebRtcSocket>();
        // The players still setting up a connection, by the id the Worker gave them
        private readonly ConcurrentDictionary<string, RTCPeerConnection> joining = new ConcurrentDictionary<string, RTCPeerConnection>();
        private Signaling signaling;
        private volatile bool stopped;

        public WebRtcListener(string code)
        {
            this.code = code;
        }

        public void Start()
        {
            Task.Run(Connect);
        }

        private async Task Connect()
        {
            try
            {
                signaling = await Signaling.Open(code, host: true);
                signaling.OnMessage += OnMessage;
                signaling.OnClosed += Reconnect;
                Plugin.Log($"[WebRTC] Hosting with code {code}");
            }
            catch (Exception e)
            {
                Plugin.LogWarning($"[WebRTC] Couldn't open the room {code}: {e.Message}");
                Reconnect();
            }
        }

        // The Worker restarts now and then: the room comes back with the host
        private async void Reconnect()
        {
            if (stopped) return;
            await Task.Delay(3000);
            if (!stopped) await Connect();
        }

        private void OnMessage(JObject message)
        {
            string from = (string)message["from"];
            if (from == null) return;
            switch ((string)message["type"])
            {
                case "offer":
                    Answer(from, (string)message["sdp"]);
                    break;
                case "candidate":
                    if (joining.TryGetValue(from, out RTCPeerConnection peer)
                        && RTCIceCandidateInit.TryParse((string)message["candidate"], out RTCIceCandidateInit init))
                    {
                        peer.addIceCandidate(init);
                    }
                    break;
                case "guest-left":
                    // Signaling ends once connected, so only a player who never got through is dropped
                    if (joining.TryRemove(from, out RTCPeerConnection gone) && gone.connectionState != RTCPeerConnectionState.connected)
                    {
                        gone.close();
                    }
                    break;
            }
        }

        private async void Answer(string from, string sdp)
        {
            RTCPeerConnection peer = WebRtcSocket.NewPeer($"player {from}");
            joining[from] = peer;
            peer.onicecandidate += candidate =>
            {
                if (candidate == null) return;
                _ = signaling?.Send(new JObject { ["type"] = "candidate", ["candidate"] = candidate.toJSON(), ["to"] = from });
            };
            peer.ondatachannel += channel =>
            {
                void Opened()
                {
                    joining.TryRemove(from, out _);
                    Plugin.Log($"[WebRTC] Player {from} connected");
                    joined.Enqueue(new WebRtcSocket(peer, channel, from));
                }
                if (channel.readyState == RTCDataChannelState.open) Opened();
                else channel.onopen += Opened;
            };
            try
            {
                peer.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = sdp });
                RTCSessionDescriptionInit answer = peer.createAnswer();
                await peer.setLocalDescription(answer);
                await signaling.Send(new JObject { ["type"] = "answer", ["sdp"] = answer.sdp, ["to"] = from });
            }
            catch (Exception e)
            {
                Plugin.LogWarning($"[WebRTC] Couldn't answer player {from}: {e.Message}");
                joining.TryRemove(from, out _);
                peer.close();
            }
        }

        public ISocketStream AcceptClient()
        {
            WebRtcSocket socket;
            while (!joined.WaitAndTryDequeue(out socket)) { }
            if (socket == null) throw new ObjectDisposedException(nameof(WebRtcListener));
            return socket;
        }

        public void Stop()
        {
            stopped = true;
            signaling?.Dispose();
            foreach (RTCPeerConnection peer in joining.Values) peer.close();
            joining.Clear();
            joined.Enqueue(null);
        }
    }
}
