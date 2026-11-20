using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UniversalRobotics.Exceptions;
using UniversalRobotics.Protocol;
using UniversalRobotics.Transport;

namespace UniversalRobotics.Tests
{
    public class TransportTests
    {
        [Fact]
        public async Task ReadExactlyAsync_StalledRead_HonorsCancellation()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var accepted = listener.AcceptTcpClientAsync();
            using var client = new UrTcpClient();
            await client.ConnectAsync("127.0.0.1", port, 1);
            using var server = await accepted;
            using var cancellation = new CancellationTokenSource();
            var read = client.ReadExactlyAsync(new byte[1], 0, 1, cancellation.Token);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        }

        [Fact]
        public async Task ConnectAsync_DisconnectDuringRetry_CancelsConnect()
        {
            using var client = new UrTcpClient();
            using var cancellation = new CancellationTokenSource();
            var connect = client.ConnectAsync("127.0.0.1", ReserveUnusedPort(), maxAttempts: 0,
                reconnectionDelay: TimeSpan.FromMinutes(1), cancellationToken: cancellation.Token);

            await Task.Yield();
            client.Disconnect();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
            Assert.Equal(SocketState.Disconnected, client.State);
        }

        [Fact]
        public async Task ReadPacketAsync_OversizedHeader_IsRejectedBeforePayloadAllocation()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var accepted = listener.AcceptTcpClientAsync();
            using var stream = new UrStream<object>("127.0.0.1", port, new RtdePacketFramer())
            {
                MaxPacketLength = 16
            };
            await stream.ConnectAsync(maxAttempts: 1);
            using var server = await accepted;
            await server.GetStream().WriteAsync(new byte[] { 0x00, 0x20, 0x55 });

            var exception = await Assert.ThrowsAsync<CommunicationException>(() => stream.ReadPacketAsync());

            Assert.Contains("outside [3, 16]", exception.Message);
        }

        [Fact]
        public async Task UrTcpClient_ConnectAsync_LoopbackConnection_IsConnected()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var accepted = listener.AcceptTcpClientAsync();
            using var client = new UrTcpClient();
            await client.ConnectAsync("127.0.0.1", port, 1);
            using var server = await accepted;

            Assert.True(client.IsConnected);
        }

        private static int ReserveUnusedPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}