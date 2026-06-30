// Licensed under the Apache License, Version 2.0

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalRobotics.Models;
using UniversalRobotics.Protocol;
using UniversalRobotics.Transport;

namespace UniversalRobotics.Control
{
    /// <summary>
    /// Configuration for the <see cref="ReverseInterface"/>.
    /// </summary>
    public sealed class ReverseInterfaceConfig
    {
        /// <summary>Gets or sets the TCP port on which the server listens (default 50001).</summary>
        public int Port { get; set; } = 50001;

        /// <summary>Gets or sets the callback invoked when the program state changes (running/stopped).</summary>
        public Action<bool>? HandleProgramState { get; set; }

        /// <summary>Gets or sets the robot's control step time (default 8 ms for e-Series at 125 Hz).</summary>
        public TimeSpan StepTime { get; set; } = TimeSpan.FromMilliseconds(8);

        /// <summary>
        /// Gets or sets the number of consecutive read timeouts permitted before the robot
        /// considers the connection lost (0 = infinite).
        /// </summary>
        public int KeepaliveCount { get; set; } = 0;

        /// <summary>Gets or sets the robot software version, used to select protocol features.</summary>
        public VersionInformation? RobotSoftwareVersion { get; set; }
    }

    /// <summary>
    /// Opens a TCP server to which the robot's external control script connects.
    /// Sends 8 × int32 big-endian control messages to the robot at each control cycle.
    /// Replaces the C++ <c>ReverseInterface</c> class.
    /// </summary>
    public class ReverseInterface : IDisposable
    {
        /// <summary>Multiplier applied to joint/Cartesian values before integer encoding.</summary>
        public const int MultJointState = 1_000_000;

        /// <summary>Multiplier applied to time values before integer encoding.</summary>
        public const int MultTime = 1_000_000;

        /// <summary>Wire message size: 8 × 4-byte int32 = 32 bytes.</summary>
        private const int MessageSizeBytes = 8 * 4;

        private readonly UrTcpServer _server;
        private readonly ILogger _logger;
        protected readonly ReverseInterfaceConfig _config;
        private byte[]? _latestClientData;

        /// <summary>Gets the port on which this server listens.</summary>
        public int Port => _config.Port;

        /// <summary>Raised when the robot's external control script connects or disconnects.</summary>
        public event EventHandler<bool>? ProgramStateChanged;

        /// <summary>
        /// Creates a <see cref="ReverseInterface"/> from a configuration object.
        /// </summary>
        public ReverseInterface(ReverseInterfaceConfig config, ILogger? logger = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? NullLogger.Instance;
            _server = new UrTcpServer(config.Port, maxClients: 1, logger: logger);
            _server.ClientConnected += OnClientConnected;
            _server.ClientDisconnected += OnClientDisconnected;
            _server.MessageReceived += OnMessageReceived;
        }

        /// <summary>Starts the TCP server.</summary>
        public Task StartAsync(CancellationToken cancellationToken = default)
            => _server.StartAsync(cancellationToken);

        /// <summary>Stops the server and releases resources.</summary>
        public void Stop() => _server.Stop();

        /// <summary>
        /// Sends joint positions or velocities with a control mode to the robot.
        /// Values are multiplied by <see cref="MultJointState"/> before encoding to int32.
        /// </summary>
        /// <param name="positions">6-element array of joint targets (rad or rad/s). Null for idle.</param>
        /// <param name="controlMode">The control mode to report to the robot.</param>
        /// <param name="timeout">How long the robot waits before considering the connection lost.</param>
        /// <returns><see langword="true"/> if the message was sent to a connected client.</returns>
        public virtual async Task<bool> WriteAsync(
            double[]? positions,
            ControlMode controlMode = ControlMode.Idle,
            RobotReceiveTimeout? timeout = null,
            CancellationToken cancellationToken = default)
        {
            var t = timeout ?? RobotReceiveTimeout.Milliseconds(20);
            t.ValidateForControlMode(controlMode);

            var buf = new byte[MessageSizeBytes];
            int offset = 0;
            PackageSerializer.WriteInt32(buf, ref offset, t.GetAsMilliseconds());  // [0] timeout_ms
            for (int i = 0; i < 6; i++)
            {
                int encoded = positions != null
                    ? (int)(positions[i] * MultJointState)
                    : 0;
                PackageSerializer.WriteInt32(buf, ref offset, encoded);            // [1..6] motion targets
            }
            PackageSerializer.WriteInt32(buf, ref offset, (int)controlMode);      // [7] control mode

            return await _server.SendAsync(buf, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a trajectory control message (start/cancel) via the reverse interface.
        /// </summary>
        public async Task<bool> WriteTrajectoryControlAsync(
            TrajectoryControlMessage message,
            int trajectoryPointsLeft = 0,
            RobotReceiveTimeout? timeout = null,
            CancellationToken cancellationToken = default)
        {
            var t = timeout ?? RobotReceiveTimeout.Milliseconds(200);
            var buf = new byte[MessageSizeBytes];
            int offset = 0;
            PackageSerializer.WriteInt32(buf, ref offset, t.GetAsMilliseconds());
            PackageSerializer.WriteInt32(buf, ref offset, (int)message);           // [1] trajectory control message
            PackageSerializer.WriteInt32(buf, ref offset, trajectoryPointsLeft);   // [2] points remaining
            for (int i = 3; i < 7; i++) PackageSerializer.WriteInt32(buf, ref offset, 0);
            PackageSerializer.WriteInt32(buf, ref offset, (int)ControlMode.Forward);
            return await _server.SendAsync(buf, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a freedrive control message (start/stop) via the reverse interface.
        /// </summary>
        public async Task<bool> WriteFreedriveControlAsync(
            FreedriveControlMessage message,
            RobotReceiveTimeout? timeout = null,
            CancellationToken cancellationToken = default)
        {
            var t = timeout ?? RobotReceiveTimeout.Milliseconds(200);
            var buf = new byte[MessageSizeBytes];
            int offset = 0;
            PackageSerializer.WriteInt32(buf, ref offset, t.GetAsMilliseconds());
            PackageSerializer.WriteInt32(buf, ref offset, (int)message);          // [1] freedrive control
            for (int i = 2; i < 7; i++) PackageSerializer.WriteInt32(buf, ref offset, 0);
            PackageSerializer.WriteInt32(buf, ref offset, (int)ControlMode.Freedrive);
            return await _server.SendAsync(buf, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Sends raw bytes to the currently connected robot client.</summary>
        protected Task<bool> SendToRobotAsync(byte[] data, CancellationToken ct = default)
            => _server.SendAsync(data, ct);

        private void OnClientConnected(object? sender, ClientConnectionEventArgs e)
        {
            _logger.LogInformation("Robot connected to reverse interface on port {Port}.", Port);
            ProgramStateChanged?.Invoke(this, true);
            _config.HandleProgramState?.Invoke(true);
        }

        private void OnClientDisconnected(object? sender, ClientConnectionEventArgs e)
        {
            _logger.LogInformation("Robot disconnected from reverse interface on port {Port}.", Port);
            ProgramStateChanged?.Invoke(this, false);
            _config.HandleProgramState?.Invoke(false);
        }

        protected virtual void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            // Derived classes (TrajectoryPointInterface, ScriptCommandInterface) override this
            // to handle robot→PC feedback messages.
        }

        /// <inheritdoc />
        public virtual void Dispose() => _server.Dispose();
    }
}
