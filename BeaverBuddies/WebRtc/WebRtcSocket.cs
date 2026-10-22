using Newtonsoft.Json.Linq;
using SIPSorcery.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using TimberNet;

namespace BeaverBuddies.WebRtc
{
    /**
     * A connection to another player over a WebRTC data channel, reliable and
     * in order, so it reads like the TCP stream it stands in for. It works
     * between any two copies of the game, Steam or not, without anyone
     * forwarding ports: the two find each other through the signaling Worker
     * by the host's join code, then talk directly. A player joining creates
     * it with the code; the host gets one from WebRtcListener for each
     * player that joins.
     */
    public class WebRtcSocket : ISocketStream
    {
        // Public STUN servers tell each side its address as the internet sees
        // it. There's no TURN relay: the few players behind NATs that can't be
        // punched through join by address instead.
        internal static readonly string[] StunServers = { "stun:stun.cloudflare.com:3478", "stun:stun.l.google.com:19302" };
        // Two copies connect in a second or two; through a hard NAT it can take a while longer, or never happen
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
        // A match's host passes its code on as it starts hosting, a moment before its room is open
        private static readonly TimeSpan RoomWait = TimeSpan.FromSeconds(4);

        public int MaxChunkSize => 16 * 1024;
        public int MaxBytesPerSecond => 1024 * 1024;
        // Finding a way through both sides' NATs takes a few seconds; ConnectAsync gives up first
        public int ConnectTimeoutMs => (int)(RoomWait + ConnectTimeout).TotalMilliseconds + 1000;
        public bool Connected { get; private set; }
        public string Name { get; private set; }

        private readonly string code;
        // Who's at the other end, for the log: the host by its code, or a player by the id they joined with
        private readonly string other;
        private RTCPeerConnection peer;
        private RTCDataChannel channel;
        private readonly ConcurrentQueueWithWait<byte[]> readBuffer = new ConcurrentQueueWithWait<byte[]>();
        // SIPSorcery's threads and the game's can each end it, and it ends once
        private readonly object ending = new object();
        private byte[] pending;
        private int readOffset;

        // A player joining the game with this join code
        public WebRtcSocket(string code)
        {
            this.code = code;
            other = $"host {code}";
        }

        // The host's side of a player who joined (see WebRtcListener)
        internal WebRtcSocket(RTCPeerConnection peer, RTCDataChannel channel, string from)
        {
            other = $"player {from}";
            Attach(peer, channel);
            Connected = true;
        }

        // Every step the connection to the other end takes is logged, and the way it settles on, for a log that shows
        // how one that ended went (Ended says why it did)
        internal static RTCPeerConnection NewPeer(string other)
        {
            var peer = new RTCPeerConnection(new RTCConfiguration
            {
                iceServers = StunServers.Select(url => new RTCIceServer { urls = url }).ToList(),
            });
            peer.oniceconnectionstatechange += state =>
            {
                Plugin.Log($"[WebRTC] {other}: ICE {state}");
                if (state == RTCIceConnectionState.connected) Plugin.Log($"[WebRTC] {other}: {Route(peer)}");
            };
            peer.onconnectionstatechange += state => Plugin.Log($"[WebRTC] {other}: connection {state}");
            return peer;
        }

        // What this end makes of the connection, for a log
        public string State =>
            $"WebRTC with {other}: connection {peer?.connectionState}, ICE {peer?.iceConnectionState}, data channel {channel?.readyState}, {(Connected ? "open" : "ended")} here";

        // The way the two settled on, by kind, never the addresses, as players post their logs: each side's own
        // address on its network (host), or the one its router shows the internet (srflx, prflx)
        private static string Route(RTCPeerConnection peer)
        {
            ChecklistEntry pair = peer.GetRtpChannel()?.NominatedEntry;
            return pair == null ? "no route chosen" : $"through {Kind(pair.LocalCandidate)} here, {Kind(pair.RemoteCandidate)} there";
        }

        private static string Kind(RTCIceCandidate candidate) =>
            candidate == null ? "?" : $"{candidate.type} {candidate.protocol} ({AddressKind(candidate.address)})";

        private static string AddressKind(string address)
        {
            if (string.IsNullOrEmpty(address)) return "no address";
            if (!IPAddress.TryParse(address, out IPAddress ip)) return address.EndsWith(".local") ? "mDNS name" : "a name";
            string v6 = ip.AddressFamily == AddressFamily.InterNetworkV6 ? " IPv6" : "";
            if (IPAddress.IsLoopback(ip)) return "loopback" + v6;
            if (ip.IsIPv6LinkLocal) return "link-local IPv6";
            byte[] b = ip.GetAddressBytes();
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                if (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] < 32) || (b[0] == 192 && b[1] == 168)) return "private";
                if (b[0] == 169 && b[1] == 254) return "link-local";
                if (b[0] == 100 && b[1] >= 64 && b[1] < 128) return "carrier-grade NAT";
                return "public";
            }
            return (b[0] & 0xFE) == 0xFC ? "private IPv6" : "public IPv6";
        }

        // Off Unity's thread, whoever calls it: nothing here needs to resume there
        public Task ConnectAsync() => Task.Run(Connect);

        private async Task Connect()
        {
            try
            {
                await ConnectThroughRoom();
            }
            catch (Exception)
            {
                // Nothing of a connection that didn't happen keeps running
                Close();
                throw;
            }
        }

        // The host's room, waiting a little for it to open: the Worker refuses a guest until it is
        private async Task<Signaling> OpenRoom()
        {
            DateTime until = DateTime.UtcNow + RoomWait;
            while (true)
            {
                try
                {
                    return await Signaling.Open(code, host: false, TimeSpan.FromSeconds(4));
                }
                catch (Exception) when (DateTime.UtcNow < until)
                {
                    await Task.Delay(500);
                }
            }
        }

        private async Task ConnectThroughRoom()
        {
            using Signaling signaling = await OpenRoom();
            peer = NewPeer(other);
            // Not inline on SIPSorcery's thread, which delivers the channel's messages: whatever awaits this mustn't hold it
            var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            peer.onicecandidate += candidate =>
            {
                if (candidate == null) return;
                _ = signaling.Send(new JObject { ["type"] = "candidate", ["candidate"] = candidate.toJSON() });
            };
            peer.onconnectionstatechange += state =>
            {
                if (state == RTCPeerConnectionState.failed) opened.TrySetException(new IOException("Couldn't reach the host directly"));
            };
            signaling.OnClosed += () => opened.TrySetException(new IOException("No game with that code"));
            signaling.OnMessage += message =>
            {
                switch ((string)message["type"])
                {
                    case "answer":
                        peer.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = (string)message["sdp"] });
                        break;
                    case "candidate":
                        if (RTCIceCandidateInit.TryParse((string)message["candidate"], out RTCIceCandidateInit init)) peer.addIceCandidate(init);
                        break;
                }
            };

            RTCDataChannel dataChannel = await peer.createDataChannel("bb", new RTCDataChannelInit { ordered = true });
            Attach(peer, dataChannel);
            dataChannel.onopen += () => opened.TrySetResult(true);
            RTCSessionDescriptionInit offer = peer.createOffer();
            await peer.setLocalDescription(offer);
            await signaling.Send(new JObject { ["type"] = "offer", ["sdp"] = offer.sdp });

            Task done = await Task.WhenAny(opened.Task, Task.Delay(ConnectTimeout));
            if (done != opened.Task) throw new IOException("Timed out connecting to the host");
            await opened.Task;
            Connected = true;
            Plugin.Log($"[WebRTC] Connected to {code}");
        }

        private void Attach(RTCPeerConnection peer, RTCDataChannel channel)
        {
            this.peer = peer;
            this.channel = channel;
            channel.onmessage += (_, _, data) => readBuffer.Enqueue(data);
            channel.onclose += () => Ended("its data channel closed");
            channel.onerror += error => Plugin.LogWarning($"[WebRTC] {other}: data channel error: {SipLog.Redact(error)}");
            peer.onconnectionstatechange += state =>
            {
                // Not disconnected: that's often a moment's loss on the network, and ICE gets it back
                if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed) Ended($"the connection went {state}");
            };
        }

        // The other side is gone: a read waiting on it ends. Said once, with why, as nothing else on this end tells
        private void Ended(string why)
        {
            lock (ending)
            {
                if (!Connected) return;
                Connected = false;
            }
            Plugin.Log($"[WebRTC] {other}: the connection ended, as {why}; ICE {peer?.iceConnectionState}, data channel {channel?.readyState}");
            readBuffer.Enqueue(new byte[0]);
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            if (pending == null)
            {
                byte[] result;
                while (!readBuffer.WaitAndTryDequeue(out result)) { }
                if (result.Length == 0) return 0;
                pending = result;
                readOffset = 0;
            }
            int bytesToCopy = Math.Min(count, pending.Length - readOffset);
            Array.Copy(pending, readOffset, buffer, offset, bytesToCopy);
            readOffset += bytesToCopy;
            if (readOffset >= pending.Length)
            {
                pending = null;
                readOffset = 0;
            }
            return bytesToCopy;
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            if (!Connected) return;
            if (count > MaxChunkSize)
            {
                throw new IOException($"Attempted to write {count} bytes, which exceeds the max chunk size of {MaxChunkSize} bytes.");
            }
            // Always a copy: SIPSorcery queues what it's given and sends it later, by when the caller
            // has filled its buffer with the next chunk
            byte[] data = new byte[count];
            Array.Copy(buffer, offset, data, 0, count);
            // SIPSorcery's send isn't safe to call from two threads at once
            lock (channel) channel.send(data);
        }

        public void Close()
        {
            bool was;
            lock (ending)
            {
                was = Connected;
                Connected = false;
            }
            if (was) Plugin.Log($"[WebRTC] {other}: closed from this end");
            try { channel?.close(); } catch (Exception) { }
            try { peer?.close(); } catch (Exception) { }
            if (was) readBuffer.Enqueue(new byte[0]);
        }
    }
}
