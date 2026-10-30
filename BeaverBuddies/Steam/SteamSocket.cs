using Steamworks;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Timberborn.BuildingsUI;
using Timberborn.Workshops;
using TimberNet;
using UnityEngine.PlayerLoop;

namespace BeaverBuddies.Steam
{
    public class SteamSocket : ISocketStream, ISteamPacketReceiver
    {
        // Steam keeps what's sent until it's gone out, and takes no more
        // once too much is waiting, so a write waits while more than this
        // is (see Write): a save goes as fast as the connection takes it,
        // up to the rate below. At 1/8 of it, a save of 0.5 to 0.8 MB
        // takes 4 to 6 s to reach each player, and a late game's few MB
        // half a minute
        const int BYTES_PER_SECOND = 1024 * 1024;
        const int MAX_QUEUED_BYTES = 256 * 1024;
        // Nothing taken for this long: the connection is gone
        const int SEND_TIMEOUT_MS = 30000;
        // Note this doesn't affect the theoretical issue of multiple
        // events being sent in a single packet - this could split them up
        // but that's fine. So we just choose a moderate size.
        const int MAX_CHUNK_SIZE = 1024 * 8; // 8KB

        public int MaxBytesPerSecond => BYTES_PER_SECOND;
        public int MaxChunkSize => MAX_CHUNK_SIZE;


        public bool Connected { get; private set; }

        public string Name { get; private set; }

        public readonly CSteamID friendID;
        //public readonly CSteamID lobbyID;

        private readonly ConcurrentQueueWithWait<byte[]> readBuffer = new ConcurrentQueueWithWait<byte[]>();
        // The packet being read, when a read took only part of it, and how far
        private byte[] pending;
        private int readOffset = 0;

        private SteamPacketListener packetListener;

        // Each player's connection in use, for a failed session to end it (see SessionFailed)
        private static readonly ConcurrentDictionary<CSteamID, SteamSocket> Live = new ConcurrentDictionary<CSteamID, SteamSocket>();

        public SteamSocket(CSteamID friendID, bool autoconnect = false)
        {
            this.friendID = friendID;
            Name = SteamFriends.GetFriendPersonaName(friendID);
            Connected = autoconnect;
            Live[friendID] = this;
        }

        /**
         * Steam says the session with a player failed: what was on its way is lost, and what comes after can't be read
         * as the stream it was. The connection ends, as a dropped one does; a player can join again. Sessions are seen
         * to fail only as players leave or drop, never during a game.
         */
        public static void SessionFailed(CSteamID player)
        {
            if (!Live.TryGetValue(player, out SteamSocket socket) || !socket.Connected) return;
            Plugin.LogWarning($"The connection with {socket.Name} ends with its Steam session");
            socket.Close();
        }

        public void RegisterSteamPacketListener(SteamPacketListener listener)
        {
            packetListener = listener;
            listener.RegisterSocket(this);
        }

        public Task ConnectAsync()
        {
            // This is the client joining, and this only gets called when
            // we've already joined the lobby. It automatically closes
            // the prior client (I think).
            Connected = true;
            Plugin.Log("SteamSocket requested to connect!");
            return Task.CompletedTask;
        }

        /**
         * Done with this player: what's still queued for them is dropped,
         * so it can't turn up in the next connection with them, and a
         * read waiting on this one ends.
         */
        public void Close()
        {
            Connected = false;
            Live.TryRemove(new KeyValuePair<CSteamID, SteamSocket>(friendID, this));
            packetListener?.UnregisterSocket(this);
            try
            {
                SteamNetworking.CloseP2PSessionWithUser(friendID);
            }
            catch (Exception e)
            {
                Plugin.LogWarning($"Couldn't close the Steam session with {Name}: {e.Message}");
            }
            readBuffer.Enqueue(new byte[0]);
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            if (pending == null)
            {
                // Block until we've read something
                byte[] result;
                while (!readBuffer.WaitAndTryDequeue(out result)) { }
                // Closed
                if (result.Length == 0) return 0;
                pending = result;
                readOffset = 0;
            }
            // A read smaller than the packet leaves the rest for the next one,
            // rather than dropping it and starting that packet partway in
            int bytesToCopy = Math.Min(count, pending.Length - readOffset);
            Array.Copy(pending, readOffset, buffer, offset, bytesToCopy);
            readOffset += bytesToCopy;
            if (readOffset >= pending.Length)
            {
                pending = null;
                readOffset = 0;
            }
            //Plugin.Log($"SteamSocket receiving {bytesToCopy} bytes");

            return bytesToCopy;
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            if (!Connected) return;
            if (count > MaxChunkSize)
            {
                throw new IOException($"Attempted to write {buffer.Length} bytes, which exceeds the max chunk size of {MaxChunkSize} bytes.");
            }
            if (offset > 0)
            {
                // Make a copy to avoid modifying the caller's buffer
                byte[] newBuffer = new byte[count];
                Array.Copy(buffer, offset, newBuffer, 0, count);
                buffer = newBuffer;
            }
            // Not logged: every message is, together, once a minute (see TimberNetBase.LogTraffic)
            var waited = Stopwatch.StartNew();
            // Sent once Steam has room for it: a send it refuses was lost before
            while (QueuedBytes() > MAX_QUEUED_BYTES
                || !SteamNetworking.SendP2PPacket(friendID, buffer, (uint)count, EP2PSend.k_EP2PSendReliable))
            {
                if (!Connected) return;
                if (waited.ElapsedMilliseconds > SEND_TIMEOUT_MS)
                {
                    throw new IOException($"Steam took nothing for {Name} in {SEND_TIMEOUT_MS / 1000} s");
                }
                Thread.Sleep(5);
            }
        }

        // A session closed drops what it hasn't sent, the goodbye last of all. Not
        // on every close: one for a player gone already would wait on what can't go
        public void Flush(int milliseconds)
        {
            var waited = Stopwatch.StartNew();
            while (Connected && waited.ElapsedMilliseconds < milliseconds
                && SteamNetworking.GetP2PSessionState(friendID, out P2PSessionState_t state)
                && state.m_bConnectionActive != 0 && state.m_nBytesQueuedForSend > 0)
            {
                Thread.Sleep(10);
            }
        }

        // Steam's own word on the session: through its relays, or straight between the two
        public string Route =>
            !SteamNetworking.GetP2PSessionState(friendID, out P2PSessionState_t state) ? null
            : state.m_bConnectionActive == 0 ? "Steam connecting"
            : state.m_bUsingRelay != 0 ? "through Steam's relays" : "direct over Steam";

        private int QueuedBytes() =>
            SteamNetworking.GetP2PSessionState(friendID, out P2PSessionState_t state) ? state.m_nBytesQueuedForSend : 0;

        public void ReceiveData(byte[] data)
        {
            readBuffer.Enqueue(data);
        }
    }
}
