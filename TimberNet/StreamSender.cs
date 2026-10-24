using System;
using System.Threading;

namespace TimberNet
{
    /**
     * Writes to one stream from its own thread, in the order data was queued,
     * so the game's thread never waits on the network or the rate limit.
     */
    public class StreamSender
    {
        private readonly ISocketStream stream;
        private readonly Action<string> log;
        // Data to send with its length in front, or a length alone (a
        // marker, the reload's); null asks the thread to stop
        private class Item
        {
            public byte[]? data;
            public int length;
        }
        private readonly ConcurrentQueueWithWait<Item?> queue = new ConcurrentQueueWithWait<Item?>();
        private volatile bool stopped = false;
        private readonly Thread thread;

        public StreamSender(ISocketStream stream, Action<string> log)
        {
            this.stream = stream;
            this.log = log;
            thread = new Thread(Run)
            {
                IsBackground = true,
                Name = $"TimberNet sender ({stream.Name ?? "TCP"})",
            };
            thread.Start();
        }

        /**
         * Queues data to be sent with its length in front.
         */
        public void Send(byte[] data)
        {
            if (stopped) return;
            queue.Enqueue(new Item { data = data });
        }

        /**
         * Queues a length alone, with no data after it.
         */
        public void SendLength(int length)
        {
            if (stopped) return;
            queue.Enqueue(new Item { length = length });
        }

        /**
         * Ends the thread once what's already queued has gone out.
         */
        public void Stop()
        {
            if (stopped) return;
            stopped = true;
            queue.Enqueue(null);
        }

        /**
         * Waits, for a while at most, for what was queued before Stop to go out.
         */
        public bool WaitStopped(int milliseconds) => thread.Join(milliseconds);

        private void Run()
        {
            bool wasConnected = false;
            while (true)
            {
                if (!queue.WaitAndTryDequeue(out Item? item)) continue;
                if (item == null) return;
                if (!stream.Connected)
                {
                    // Still connecting (WebRTC takes seconds): what's queued meanwhile is the
                    // client's hello, sent again every second, so it's dropped and the thread waits on
                    if (!wasConnected) continue;
                    // Gone: nothing more can reach them
                    stopped = true;
                    return;
                }
                wasConnected = true;
                try
                {
                    if (item.data != null) WriteWithLength(stream, item.data);
                    else WriteLength(stream, item.length);
                }
                catch (Exception e)
                {
                    log($"Error sending data: {e.Message}");
                }
            }
        }

        public static void WriteLength(ISocketStream stream, int length)
        {
            byte[] buffer = BitConverter.GetBytes(length);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(buffer);
            stream.Write(buffer, 0, buffer.Length);
        }

        /**
         * Writes the length and then the data in chunks, pausing between
         * them to keep under the stream's rate limit. Blocks until done. The
         * length goes in one write with the first chunk: written alone, it
         * was a packet of its own for every message, and on a data channel
         * one more to lose and wait for. The other end reads a stream either way.
         */
        public static void WriteWithLength(ISocketStream stream, byte[] data)
        {
            int chunkSize = stream.MaxChunkSize;
            // How long to sleep between chunks (may be 0)
            int sleepMS = stream.MaxChunkSize * 1000 / stream.MaxBytesPerSecond;
            byte[] length = BitConverter.GetBytes(data.Length);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(length);
            int first = Math.Min(data.Length, chunkSize - length.Length);
            byte[] head = new byte[length.Length + first];
            Array.Copy(length, head, length.Length);
            Array.Copy(data, 0, head, length.Length, first);
            stream.Write(head, 0, head.Length);
            for (int i = first; i < data.Length; i += chunkSize)
            {
                Thread.Sleep(sleepMS);
                // Gone halfway through: the rest of a map would only take time
                if (!stream.Connected) return;
                int count = Math.Min(chunkSize, data.Length - i);
                stream.Write(data, i, count);
            }
        }
    }
}
