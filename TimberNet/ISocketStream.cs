using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace TimberNet
{
    public interface ISocketStream
    {
        bool Connected { get; }

        string? Name { get; }

        int MaxChunkSize { get; }
        int MaxBytesPerSecond { get; }

        // How long connecting may take before it counts as failed (it runs in the background, see TimberClient.Start)
        int ConnectTimeoutMs => 3000;

        int Read(byte[] buffer, int offset, int count);
        void Write(byte[] buffer, int offset, int count);
        void Close();
        Task ConnectAsync();

        // Waits, for a while at most, for what's written to go out, before closing on purpose (see TimberNetBase.Close):
        // a connection that keeps what's written and drops it as it closes (a Steam session) has its goodbye go first
        void Flush(int milliseconds) { }

        public byte[] ReadUntilComplete(int count)
        {
            byte[] bytes = new byte[count];
            ReadUntilComplete(bytes, count);
            return bytes;
        }


        public void ReadUntilComplete(byte[] buffer, int count)
        {
            // TODO: How should this fail?
            int totalBytesRead = 0;
            while (totalBytesRead < count)
            {
                int bytesRead = Read(buffer, totalBytesRead, count - totalBytesRead);
                if (bytesRead == 0)
                {
                    // No more data, the connection may be closed or interrupted
                    // TODO: Not exactly sure what the right error handling is here
                    throw new IOException("Unexpected end of stream.");
                }
                totalBytesRead += bytesRead;
            }
        }
    }
}
