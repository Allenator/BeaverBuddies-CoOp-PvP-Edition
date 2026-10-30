using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TimberNet;

namespace BeaverBuddies.DevTools
{
    /**
     * For tests: a connection that behaves as a Steam session does as it closes (BB_SIM_STEAM_CLOSE=1). What's written
     * is on its way for a while, 150 ms here, and closing drops what hasn't arrived, the goodbye with it. The test
     * copies can't play over Steam, sharing one account, so the goodbyes lost on October 25, 2026 happen here over TCP.
     */
    public class LossyCloseStream : ISocketStream
    {
        private const int InFlightMs = 150;

        public static bool Active => TestHarness.Active && Environment.GetEnvironmentVariable("BB_SIM_STEAM_CLOSE") == "1";

        public static ISocketStream Wrap(ISocketStream stream) => Active ? new LossyCloseStream(stream) : stream;

        private readonly ISocketStream _inner;
        private readonly ConcurrentQueue<(long at, byte[] bytes)> _inFlight = new ConcurrentQueue<(long, byte[])>();
        private volatile bool _closed;

        private LossyCloseStream(ISocketStream inner)
        {
            _inner = inner;
            Task.Run(Forward);
        }

        // What's been on its way long enough arrives
        private async Task Forward()
        {
            while (!_closed)
            {
                while (_inFlight.TryPeek(out var item) && Stopwatch.GetTimestamp() - item.at >= InFlightMs * Stopwatch.Frequency / 1000)
                {
                    if (_closed) return;
                    _inFlight.TryDequeue(out _);
                    try { _inner.Write(item.bytes, 0, item.bytes.Length); }
                    catch (Exception) { return; }
                }
                await Task.Delay(5);
            }
        }

        public bool Connected => !_closed && _inner.Connected;
        public string Name => _inner.Name;
        public int MaxChunkSize => _inner.MaxChunkSize;
        public int MaxBytesPerSecond => _inner.MaxBytesPerSecond;
        public int ConnectTimeoutMs => _inner.ConnectTimeoutMs;
        public string Route => _inner.Route;

        public int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public void Write(byte[] buffer, int offset, int count)
        {
            var bytes = new byte[count];
            Array.Copy(buffer, offset, bytes, 0, count);
            _inFlight.Enqueue((Stopwatch.GetTimestamp(), bytes));
        }

        // As Steam's: what it was handed is out of the writer's hands, not arrived
        public void Flush(int milliseconds) { }

        public void Close()
        {
            _closed = true;
            int dropped = _inFlight.Count;
            while (_inFlight.TryDequeue(out _)) { }
            if (dropped > 0) Plugin.Log($"[Test] Closed with {dropped} writes still on their way, dropped, as a Steam session drops them");
            _inner.Close();
        }

        public Task ConnectAsync() => _inner.ConnectAsync();
    }

    /**
     * For tests: the host's first connection gets nothing the host sends (BB_SILENT_FIRST=1), as a Steam session that
     * failed and lost the game on its way did on October 25, 2026, so the guest's wait for the game can be seen to end.
     * The connections after it are whole.
     */
    public class SilentFirstStream : ISocketStream
    {
        private static int _accepted;

        public static ISocketStream Wrap(ISocketStream stream) =>
            TestHarness.Active && Environment.GetEnvironmentVariable("BB_SILENT_FIRST") == "1" && Interlocked.Increment(ref _accepted) == 1
                ? new SilentFirstStream(stream) : stream;

        private readonly ISocketStream _inner;

        private SilentFirstStream(ISocketStream inner)
        {
            _inner = inner;
            Plugin.Log("[Test] This connection gets nothing the host sends");
        }

        public bool Connected => _inner.Connected;
        public string Name => _inner.Name;
        public int MaxChunkSize => _inner.MaxChunkSize;
        public int MaxBytesPerSecond => _inner.MaxBytesPerSecond;
        public int ConnectTimeoutMs => _inner.ConnectTimeoutMs;
        public string Route => _inner.Route;
        public int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public void Write(byte[] buffer, int offset, int count) { }
        public void Flush(int milliseconds) { }
        public void Close() => _inner.Close();
        public Task ConnectAsync() => _inner.ConnectAsync();
    }
}
