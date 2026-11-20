using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UniversalRobotics.Rtde;
using UniversalRobotics.Rtde.Packets;

namespace UniversalRobotics.Tests;

public sealed class RtdeClientTests
{
    [Fact]
    public async Task InitAsync_NegotiatesV2_QueriesVersionAndSetsUpTypedRecipes()
    {
        using var server = new FakeRtdeServer(new[] { "timestamp", "target_base_wrench" }, new[] { "speed_slider_fraction", "speed_slider_mask" });
        using var client = new RtdeClient("127.0.0.1", new[] { "target_base_wrench" }, new[] { "speed_slider_fraction", "speed_slider_mask" }, port: server.Port);

        await client.InitAsync(maxConnectionAttempts: 1, maxInitializationAttempts: 1, initializationTimeout: TimeSpan.FromMilliseconds(50));

        Assert.Equal(ClientState.Initialized, client.State);
        Assert.Equal(new[] { "timestamp", "target_base_wrench" }, client.OutputRecipe);
        Assert.Equal(new[] { "speed_slider_fraction", "speed_slider_mask" }, client.InputRecipe);
        Assert.NotNull(client.Writer);
        Assert.Equal(2, server.ProtocolVersion);
        Assert.Equal(500.0, server.OutputFrequency);
        Assert.Equal(new[] { "timestamp", "target_base_wrench" }, server.OutputRecipe);
        Assert.Equal(new[] { "speed_slider_fraction", "speed_slider_mask" }, server.InputRecipe);
        Assert.Equal((byte)7, server.InputRecipeId);

        Assert.True(client.Writer!.SendSpeedSlider(0.25));
        var input = await server.InputPacket.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal((byte)7, input[3]);
        Assert.Equal(0.25, ReadDouble(input, 4), precision: 10);
        Assert.Equal((uint)1, BinaryPrimitives.ReadUInt32BigEndian(input.AsSpan(12, 4)));
    }

    [Fact]
    public async Task StartAsync_FalseReadsTimestampAndVector6d_AndPauseChangesState()
    {
        using var server = new FakeRtdeServer(new[] { "timestamp", "target_base_wrench" }, Array.Empty<string>(), sendDataAfterStart: true);
        using var client = new RtdeClient("127.0.0.1", new[] { "target_base_wrench" }, port: server.Port);
        await client.InitAsync(maxConnectionAttempts: 1, maxInitializationAttempts: 1);

        await client.StartAsync(readInBackground: false);
        var package = await client.GetNextDataPackageAsync(new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token);

        Assert.NotNull(package);
        Assert.True(package!.GetData("timestamp", out double timestamp));
        Assert.Equal(42.5, timestamp);
        Assert.True(package.GetData("target_base_wrench", out double[]? wrench));
        Assert.Equal(new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 }, wrench);
        Assert.Equal(ClientState.Running, client.State);

        await client.PauseAsync();
        Assert.Equal(ClientState.Paused, client.State);
    }

    [Fact]
    public async Task StartAsync_TrueReturnsIndependentBackgroundSnapshots()
    {
        using var server = new FakeRtdeServer(new[] { "timestamp", "target_base_wrench" }, Array.Empty<string>(), sendDataAfterStart: true);
        using var client = new RtdeClient("127.0.0.1", new[] { "target_base_wrench" }, port: server.Port);
        await client.InitAsync(maxConnectionAttempts: 1, maxInitializationAttempts: 1);
        await client.StartAsync(readInBackground: true);

        var first = await client.GetDataPackageAsync(TimeSpan.FromSeconds(1));
        Assert.NotNull(first);
        Assert.True(first!.GetData("target_base_wrench", out double[]? firstWrench));
        firstWrench![0] = 999;

        await server.SendDataAsync(43.5, new[] { 10.0, 11.0, 12.0, 13.0, 14.0, 15.0 });
        var second = await client.GetDataPackageAsync(TimeSpan.FromSeconds(1));

        Assert.NotNull(second);
        Assert.True(second!.GetData("timestamp", out double timestamp));
        Assert.Equal(43.5, timestamp);
        Assert.True(second.GetData("target_base_wrench", out double[]? secondWrench));
        Assert.Equal(10.0, secondWrench![0]);
        Assert.Equal(999, firstWrench[0]);
    }

    [Fact]
    public async Task BackgroundRead_TimeoutReturnsNull_AndCallerCancellationIsHonored()
    {
        using var server = new FakeRtdeServer(new[] { "timestamp" }, Array.Empty<string>());
        using var client = new RtdeClient("127.0.0.1", new[] { "timestamp" }, port: server.Port);
        await client.InitAsync(maxConnectionAttempts: 1, maxInitializationAttempts: 1);
        await client.StartAsync(readInBackground: true);

        Assert.Null(await client.GetDataPackageAsync(TimeSpan.FromMilliseconds(20)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetDataPackageAsync(TimeSpan.FromSeconds(1), cancellation.Token));
    }

    [Fact]
    public async Task DirectReadBlocking_CopiesTypedDataIntoCallerPackage()
    {
        using var server = new FakeRtdeServer(new[] { "timestamp", "target_base_wrench" }, Array.Empty<string>(), sendDataAfterStart: true);
        using var client = new RtdeClient("127.0.0.1", new[] { "target_base_wrench" }, port: server.Port);
        await client.InitAsync(maxConnectionAttempts: 1, maxInitializationAttempts: 1);
        await client.StartAsync(readInBackground: false);

        var target = new DataPackage(new[] { "timestamp", "target_base_wrench" }, new[] { "DOUBLE", "VECTOR6D" });
        Assert.True(await client.GetDataPackageBlockingAsync(target, new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token));
        Assert.True(target.GetData("target_base_wrench", out double[]? firstWrench));
        Assert.Equal(new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 }, firstWrench);

        await server.SendDataAsync(43.5, new[] { 10.0, 11.0, 12.0, 13.0, 14.0, 15.0 });
        var secondTarget = new DataPackage(new[] { "timestamp", "target_base_wrench" }, new[] { "DOUBLE", "VECTOR6D" });
        Assert.True(await client.GetDataPackageBlockingAsync(secondTarget, new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token));
        Assert.True(secondTarget.GetData("target_base_wrench", out double[]? secondWrench));
        Assert.Equal(10.0, secondWrench![0]);
        Assert.Equal(1.0, firstWrench![0]);
    }

    private static double ReadDouble(byte[] packet, int offset)
        => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(packet.AsSpan(offset, 8)));

    private sealed class FakeRtdeServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _shutdown = new();
        private readonly string[] _outputTypes;
        private readonly string[] _inputTypes;
        private readonly bool _sendDataAfterStart;
        private readonly Task _serverTask;
        private NetworkStream? _stream;
        private readonly object _writeLock = new();
        private bool _bootStartSeen;

        public FakeRtdeServer(string[] outputRecipe, string[] inputRecipe, bool sendDataAfterStart = false)
        {
            OutputRecipe = outputRecipe;
            InputRecipe = inputRecipe;
            _outputTypes = outputRecipe.Select(name => name == "timestamp" ? "DOUBLE" : "VECTOR6D").ToArray();
            _inputTypes = inputRecipe.Select(name => name == "speed_slider_fraction" ? "DOUBLE" : "UINT32").ToArray();
            _sendDataAfterStart = sendDataAfterStart;
            InputPacket = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _listener.Start();
            _serverTask = Task.Run(RunAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public int ProtocolVersion { get; private set; }
        public double OutputFrequency { get; private set; }
        public string[] OutputRecipe { get; }
        public string[] InputRecipe { get; }
        public byte InputRecipeId { get; private set; }
        public TaskCompletionSource<byte[]> InputPacket { get; }

        public async Task SendDataAsync(double timestamp, double[] wrench)
        {
            var payload = new byte[1 + 8 + 48];
            payload[0] = 3;
            WriteDouble(payload, 1, timestamp);
            for (int index = 0; index < 6; index++) WriteDouble(payload, 9 + index * 8, wrench[index]);
            await SendAsync(RtdePackageType.DataPackage, payload);
        }

        private async Task RunAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_shutdown.Token);
                _stream = client.GetStream();
                while (!_shutdown.IsCancellationRequested)
                {
                    var packet = await ReadPacketAsync(_stream, _shutdown.Token);
                    var type = (RtdePackageType)packet[2];
                    switch (type)
                    {
                        case RtdePackageType.RequestProtocolVersion:
                            ProtocolVersion = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(3, 2));
                            await SendAsync(type, new byte[] { 1 });
                            break;
                        case RtdePackageType.GetUrControlVersion:
                            await SendAsync(type, UInt32s(5, 25, 2, 0));
                            break;
                        case RtdePackageType.ControlPackageSetupOutputs:
                            OutputFrequency = ReadDouble(packet, 3);
                            await SendAsync(type, Prefix(4, string.Join(',', _outputTypes)));
                            break;
                        case RtdePackageType.ControlPackageSetupInputs:
                            InputRecipeId = 7;
                            await SendAsync(type, Prefix(InputRecipeId, string.Join(',', _inputTypes)));
                            break;
                        case RtdePackageType.ControlPackageStart:
                            await SendAsync(type, new byte[] { 1 });
                            if (!_bootStartSeen || _sendDataAfterStart)
                                await SendDataAsync(42.5, new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 });
                            _bootStartSeen = true;
                            break;
                        case RtdePackageType.ControlPackagePause:
                            await SendAsync(type, new byte[] { 1 });
                            break;
                        case RtdePackageType.DataPackage:
                            InputPacket.TrySetResult(packet);
                            break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }

        private async Task SendAsync(RtdePackageType type, byte[] payload)
        {
            var stream = _stream ?? throw new InvalidOperationException();
            var packet = new byte[3 + payload.Length];
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(0, 2), (ushort)packet.Length);
            packet[2] = (byte)type;
            Buffer.BlockCopy(payload, 0, packet, 3, payload.Length);
            lock (_writeLock) stream.WriteAsync(packet, _shutdown.Token).GetAwaiter().GetResult();
            await Task.CompletedTask;
        }

        private static async Task<byte[]> ReadPacketAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var header = new byte[3];
            await ReadExactlyAsync(stream, header, cancellationToken);
            var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
            var packet = new byte[length];
            Buffer.BlockCopy(header, 0, packet, 0, 3);
            await ReadExactlyAsync(stream, packet.AsMemory(3), cancellationToken);
            return packet;
        }

        private static async Task ReadExactlyAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            while (!buffer.IsEmpty)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0) throw new EndOfStreamException();
                buffer = buffer[read..];
            }
        }

        private static byte[] Prefix(byte value, string text) => new[] { value }.Concat(Encoding.ASCII.GetBytes(text)).ToArray();
        private static byte[] UInt32s(params uint[] values)
        {
            var result = new byte[values.Length * 4];
            for (int index = 0; index < values.Length; index++) BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(index * 4, 4), values[index]);
            return result;
        }
        private static void WriteDouble(byte[] buffer, int offset, double value)
            => BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(offset, 8), BitConverter.DoubleToInt64Bits(value));

        public void Dispose()
        {
            _shutdown.Cancel();
            _listener.Stop();
            try { _serverTask.GetAwaiter().GetResult(); } catch { }
            _shutdown.Dispose();
        }
    }
}