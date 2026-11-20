using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UniversalRobotics.Control;
using UniversalRobotics.Models;
using UniversalRobotics.Protocol;

namespace UniversalRobotics.Tests
{
    public class TrajectoryPointInterfaceTests
    {
        [Fact]
        public async Task WriteMotionPrimitiveAsync_QuinticSpline_WritesFullWireMessage()
        {
            var spline = new SplinePrimitive
            {
                Positions = new[] { 1.0, -2.0, 0.000001, 0.0, 0.0, 0.0 },
                Velocities = new[] { 0.00000001, -0.00000002, 0.0, 0.0, 0.0, 0.0 },
                Accelerations = new[] { 0.00000003, -0.00000004, 0.0, 0.0, 0.0, 0.0 },
                Duration = TimeSpan.FromSeconds(1.25),
                SplineType = TrajectorySplineType.Quintic
            };

            var values = await SendAndReadAsync(spline);

            Assert.Equal(new[] { 1_000_000, -2_000_000, 1, 0, 0, 0,
                1, -2, 0, 0, 0, 0, 3, -4, 0, 0, 0, 0, 1_250_000, 2, 51 }, values);
        }

        [Fact]
        public async Task WriteMotionPrimitiveAsync_CubicSpline_UsesZeroAccelerationBlock()
        {
            var spline = new SplinePrimitive
            {
                Positions = new[] { 0.5, 0.0, 0.0, 0.0, 0.0, 0.0 },
                Velocities = new[] { 0.00000001, 0.0, 0.0, 0.0, 0.0, 0.0 },
                Accelerations = new[] { 9.0, 9.0, 9.0, 9.0, 9.0, 9.0 },
                Duration = TimeSpan.FromSeconds(0.5),
                SplineType = TrajectorySplineType.Cubic
            };

            var values = await SendAndReadAsync(spline);

            Assert.Equal(500_000, values[0]);
            Assert.Equal(1, values[6]);
            Assert.Equal(0, values[12]);
            Assert.Equal(500_000, values[18]);
            Assert.Equal(1, values[19]);
            Assert.Equal(51, values[20]);
        }

        [Fact]
        public async Task VelocityAccelerationMultiplier_LegacyValue_ChangesSplineEncoding()
        {
            var spline = new SplinePrimitive
            {
                Velocities = new[] { 0.000001, 0, 0, 0, 0, 0 },
                Accelerations = new[] { 0.000002, 0, 0, 0, 0, 0 },
                SplineType = TrajectorySplineType.Quintic
            };
            var values = await SendAndReadAsync(spline, 1_000_000);

            Assert.Equal(1, values[6]);
            Assert.Equal(2, values[12]);
        }

        [Fact]
        public async Task WriteMotionPrimitiveAsync_MovePWithQNear_WritesMotionBlocks()
        {
            var pose = new CartesianPose(1, 2, 3, 4, 5, 6, new JointAngles(new[] { 7.0, 8, 9, 10, 11, 12 }));
            var primitive = new MovePPrimitive(pose) { Velocity = 0.25, Acceleration = 0.5 };

            var values = await SendAndReadAsync(primitive);

            Assert.Equal(new[] { 1_000_000, 2_000_000, 3_000_000, 4_000_000, 5_000_000, 6_000_000,
                7_000_000, 8_000_000, 9_000_000, 10_000_000, 11_000_000, 12_000_000,
                250_000, 500_000, 1_000_000, 0, 0, 0, 0, 0, 2 }, values);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(21.47483648)]
        public async Task WriteMotionPrimitiveAsync_InvalidVelocity_ThrowsBeforeSending(double velocity)
        {
            var primitive = new SplinePrimitive { Velocities = new[] { velocity, 0, 0, 0, 0, 0 } };
            using var trajectory = new TrajectoryPointInterface(ReservePort());

            var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                trajectory.WriteMotionPrimitiveAsync(primitive));

            Assert.Equal("value", exception.ParamName);
        }

        [Fact]
        public async Task WriteMotionPrimitiveAsync_StreamingEnumValues_AreWrittenToWire()
        {
            var cubicValues = await SendAndReadAsync(new SplinePrimitive { SplineType = TrajectorySplineType.Cubic });
            var quinticValues = await SendAndReadAsync(new SplinePrimitive { SplineType = TrajectorySplineType.Quintic });

            Assert.Equal(1, cubicValues[19]);
            Assert.Equal(2, quinticValues[19]);
        }

        [Fact]
        public async Task WriteTrajectoryControlAsync_StreamingCommands_WriteValuesTwoAndThree()
        {
            int streamStart = await SendControlAndReadMessageAsync(TrajectoryControlMessage.StreamStart);
            int streamEnd = await SendControlAndReadMessageAsync(TrajectoryControlMessage.StreamEnd);

            Assert.Equal(2, streamStart);
            Assert.Equal(3, streamEnd);
        }

        private static async Task<int[]> SendAndReadAsync(MotionPrimitive primitive, int multiplier = TrajectoryPointInterface.MultVelocityAcceleration)
        {
            int port = ReservePort();
            using var trajectory = new TrajectoryPointInterface(port)
            {
                VelocityAccelerationMultiplier = multiplier
            };
            var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            trajectory.ProgramStateChanged += (_, state) => { if (state) connected.TrySetResult(true); };
            await trajectory.StartAsync();
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(await trajectory.WriteMotionPrimitiveAsync(primitive));
            return await ReadMessageAsync(client.GetStream());
        }

        private static async Task<int[]> ReadMessageAsync(NetworkStream stream)
        {
            var buffer = new byte[TrajectoryPointInterface.MessageLength * 4];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await stream.ReadExactlyAsync(buffer, timeout.Token);
            var parser = new BinaryParser(buffer);
            var values = new int[TrajectoryPointInterface.MessageLength];
            for (int index = 0; index < values.Length; index++) values[index] = parser.ReadInt32();
            return values;
        }

        private static async Task<int> SendControlAndReadMessageAsync(TrajectoryControlMessage message)
        {
            int port = ReservePort();
            using var reverse = new ReverseInterface(new ReverseInterfaceConfig { Port = port });
            var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            reverse.ProgramStateChanged += (_, state) => { if (state) connected.TrySetResult(true); };
            await reverse.StartAsync();
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(await reverse.WriteTrajectoryControlAsync(message));

            var buffer = new byte[32];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.GetStream().ReadExactlyAsync(buffer, timeout.Token);
            var parser = new BinaryParser(buffer);
            parser.ReadInt32();
            return parser.ReadInt32();
        }

        private static int ReservePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}