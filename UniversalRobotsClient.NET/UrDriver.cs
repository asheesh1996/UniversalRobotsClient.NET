// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalRobotics.Control;
using UniversalRobotics.Dashboard;
using UniversalRobotics.Models;
using UniversalRobotics.Primary;
using UniversalRobotics.Protocol;
using UniversalRobotics.Rtde;
using UniversalRobotics.Rtde.Packets;

namespace UniversalRobotics
{
    /// <summary>
    /// The central facade for the Universal Robots client library.
    /// <para>
    /// Aggregates all communication subsystems into a single object:
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="RtdeClient"/> — real-time data at up to 500 Hz</item>
    ///   <item><see cref="PrimaryClient"/> — robot state, script execution, error codes</item>
    ///   <item><see cref="ReverseInterface"/> — high-frequency motion command streaming</item>
    ///   <item><see cref="TrajectoryPointInterface"/> — full trajectory forwarding</item>
    ///   <item><see cref="ScriptCommandInterface"/> — force mode, payload, tool contact</item>
    ///   <item><see cref="ScriptSender"/> — URScript delivery to the robot</item>
    ///   <item><see cref="ScriptReader"/> — Jinja2-like URScript template processing</item>
    ///   <item><see cref="DashboardClientG5"/> / <see cref="DashboardClientX"/> — robot management</item>
    /// </list>
    /// Replaces the C++ <c>UrDriver</c> class.
    /// </summary>
    public sealed class UrDriver : IDisposable
    {
        private readonly UrDriverConfiguration _config;
        private readonly ILogger _logger;
        private readonly ILoggerFactory? _loggerFactory;

        private RtdeClient? _rtdeClient;
        private PrimaryClient? _primaryClient;
        private ReverseInterface? _reverseInterface;
        private TrajectoryPointInterface? _trajectoryInterface;
        private ScriptCommandInterface? _scriptCommandInterface;
        private ScriptSender? _scriptSender;
        private ScriptReader? _scriptReader;
        private DashboardClientG5? _dashboardG5;
        private DashboardClientX? _dashboardX;

        private VersionInformation? _softwareVersion;
        private bool _disposed;

        // ─── Events ───────────────────────────────────────────────────────────────

        /// <summary>Raised when the robot's external control program starts or stops.</summary>
        public event EventHandler<bool>? ProgramStateChanged;

        /// <summary>Raised when the robot reports the result of a trajectory execution.</summary>
        public event EventHandler<TrajectoryResult>? TrajectoryDone;

        /// <summary>Raised when the trajectory interface disconnects unexpectedly.</summary>
        public event EventHandler<bool>? TrajectoryInterfaceDisconnected;

        /// <summary>Raised when the robot reports a tool contact result.</summary>
        public event EventHandler<ToolContactResult>? ToolContactResultReceived;

        // ─── Accessors ────────────────────────────────────────────────────────────

        /// <summary>Gets the underlying RTDE client.</summary>
        public RtdeClient? RtdeClient => _rtdeClient;

        /// <summary>Gets the primary interface client.</summary>
        public PrimaryClient? PrimaryClient => _primaryClient;

        /// <summary>Gets the reverse interface (motion command streaming).</summary>
        public ReverseInterface? ReverseInterface => _reverseInterface;

        /// <summary>Gets the trajectory point interface.</summary>
        public TrajectoryPointInterface? TrajectoryInterface => _trajectoryInterface;

        /// <summary>Gets the script command interface.</summary>
        public ScriptCommandInterface? ScriptCommandInterface => _scriptCommandInterface;

        /// <summary>Gets the dashboard client for CB3/e-Series robots.</summary>
        public DashboardClientG5? DashboardG5 => _dashboardG5;

        /// <summary>Gets the dashboard client for PolyScope X robots.</summary>
        public DashboardClientX? DashboardX => _dashboardX;

        /// <summary>Gets the detected software version of the connected robot.</summary>
        public VersionInformation? SoftwareVersion => _softwareVersion ?? _primaryClient?.SoftwareVersion;

        /// <summary>
        /// Creates a new <see cref="UrDriver"/> from a configuration.
        /// Call <see cref="ConnectAsync"/> to establish all connections.
        /// </summary>
        public UrDriver(UrDriverConfiguration config, ILoggerFactory? loggerFactory = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _loggerFactory = loggerFactory;
            _logger = loggerFactory?.CreateLogger<UrDriver>() ?? NullLogger<UrDriver>.Instance;
        }

        // ─── Lifecycle ────────────────────────────────────────────────────────────

        /// <summary>
        /// Connects to all robot subsystems and starts data streaming.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the connection process.</param>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Connecting to robot at {RobotIp}...", _config.RobotIp);

            // 1. Primary client — reads robot state, version, and error codes
            _primaryClient = new PrimaryClient(_config.RobotIp, logger: MakeLogger<PrimaryClient>());
            await _primaryClient.StartAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            // Wait briefly for the version message
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            _softwareVersion = _primaryClient.SoftwareVersion;
            _logger.LogInformation("Robot software version: {Version}.", _softwareVersion?.ToString() ?? "unknown");

            // 2. RTDE client — real-time data
            var outputRecipe = ResolveRecipe(_config.OutputRecipe, _config.OutputRecipeFile);
            var inputRecipe = ResolveRecipe(_config.InputRecipe, _config.InputRecipeFile);

            _rtdeClient = new RtdeClient(
                _config.RobotIp, outputRecipe, inputRecipe,
                _config.TargetFrequency, _config.IgnoreUnavailableOutputs,
                logger: MakeLogger<RtdeClient>());

            await _rtdeClient.InitAsync(
                maxInitializationAttempts: _config.RtdeInitializationAttempts,
                initializationTimeout: _config.RtdeInitializationTimeout,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await _rtdeClient.StartAsync(readInBackground: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // 3. Control interfaces — start their TCP servers
            var reverseConfig = new ReverseInterfaceConfig
            {
                Port = _config.ReversePort,
                HandleProgramState = _config.HandleProgramState,
                KeepaliveCount = _config.KeepaliveCount,
                RobotSoftwareVersion = _softwareVersion
            };

            _reverseInterface = new ReverseInterface(reverseConfig, MakeLogger<ReverseInterface>());
            await _reverseInterface.StartAsync(cancellationToken).ConfigureAwait(false);

            var trajectoryConfig = new ReverseInterfaceConfig { Port = _config.TrajectoryPort };
            _trajectoryInterface = new TrajectoryPointInterface(trajectoryConfig, MakeLogger<TrajectoryPointInterface>());
            await _trajectoryInterface.StartAsync(cancellationToken).ConfigureAwait(false);
            _trajectoryInterface.TrajectoryResultReceived += OnTrajectoryResult;
            _trajectoryInterface.ProgramStateChanged += OnTrajectoryProgramStateChanged;

            var cmdConfig = new ReverseInterfaceConfig { Port = _config.ScriptCommandPort };
            _scriptCommandInterface = new ScriptCommandInterface(cmdConfig, MakeLogger<ScriptCommandInterface>());
            await _scriptCommandInterface.StartAsync(cancellationToken).ConfigureAwait(false);
            _scriptCommandInterface.ToolContactResultReceived += OnToolContactResult;

            // 4. Script sender
            _scriptSender = new ScriptSender(_config.ScriptSenderPort, MakeLogger<ScriptSender>());
            _scriptReader = new ScriptReader();

            if (_config.ScriptFile != null && File.Exists(_config.ScriptFile))
            {
                var variables = BuildScriptVariables();
                var scriptContent = _scriptReader.ReadScript(_config.ScriptFile, variables);
                _scriptSender.SetScript(scriptContent);
            }

            await _scriptSender.StartAsync(cancellationToken).ConfigureAwait(false);

            // 5. Dashboard client
            if (_softwareVersion?.IsPolyScopeX == true)
            {
                _dashboardX = new DashboardClientX(_config.RobotIp, logger: MakeLogger<DashboardClientX>());
            }
            else
            {
                _dashboardG5 = new DashboardClientG5(_config.RobotIp, logger: MakeLogger<DashboardClientG5>());
                try { await _dashboardG5.ConnectAsync(cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogWarning(ex, "Dashboard connection failed (non-fatal)."); }
            }

            // 6. Send script in headless mode
            if (_config.HeadlessMode && _config.ScriptFile != null)
                await SendRobotProgramAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("UrDriver connected successfully.");
        }

        /// <summary>Disconnects from all subsystems.</summary>
        public async Task DisconnectAsync()
        {
            _logger.LogInformation("Disconnecting from robot.");

            if (_trajectoryInterface != null)
            {
                _trajectoryInterface.TrajectoryResultReceived -= OnTrajectoryResult;
                _trajectoryInterface.ProgramStateChanged -= OnTrajectoryProgramStateChanged;
                _trajectoryInterface.Dispose();
            }

            if (_scriptCommandInterface != null)
            {
                _scriptCommandInterface.ToolContactResultReceived -= OnToolContactResult;
                _scriptCommandInterface.Dispose();
            }

            _reverseInterface?.Dispose();
            _scriptSender?.Dispose();
            _dashboardG5?.Dispose();
            _dashboardX?.Dispose();
            _rtdeClient?.Dispose();

            if (_primaryClient != null)
                await _primaryClient.StopAsync().ConfigureAwait(false);
        }

        // ─── RTDE data access ─────────────────────────────────────────────────────

        /// <summary>
        /// Retrieves the latest RTDE data package.
        /// Blocks for at most <paramref name="timeout"/> if no new packet has arrived.
        /// </summary>
        public Task<DataPackage?> GetDataPackageAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            => _rtdeClient?.GetDataPackageAsync(timeout, cancellationToken)
               ?? Task.FromResult<DataPackage?>(null);

        /// <summary>Gets the RTDE writer for sending input data to the robot.</summary>
        public RtdeWriter? GetRtdeWriter() => _rtdeClient?.Writer;

        // ─── Script execution ─────────────────────────────────────────────────────

        /// <summary>
        /// Sends the configured robot program via the primary interface.
        /// </summary>
        public async Task<bool> SendRobotProgramAsync(CancellationToken cancellationToken = default)
        {
            if (_primaryClient == null) return false;
            if (_config.ScriptFile == null) return false;

            var variables = BuildScriptVariables();
            var scriptContent = _scriptReader!.ReadScript(_config.ScriptFile, variables);
            return await _primaryClient.SendScriptAsync(scriptContent, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Sends arbitrary URScript code to the robot.</summary>
        public Task<bool> SendScriptAsync(string script, CancellationToken cancellationToken = default)
            => _primaryClient?.SendScriptAsync(script, cancellationToken) ?? Task.FromResult(false);

        // ─── Motion command wrappers ──────────────────────────────────────────────

        /// <summary>Sends joint targets to the robot in SERVOJ mode.</summary>
        public Task<bool> WriteServoJAsync(double[] positions, RobotReceiveTimeout? timeout = null, CancellationToken ct = default)
            => _reverseInterface?.WriteAsync(positions, ControlMode.ServoJ, timeout, ct) ?? Task.FromResult(false);

        /// <summary>Sends joint velocities to the robot in SPEEDJ mode.</summary>
        public Task<bool> WriteSpeedJAsync(double[] velocities, RobotReceiveTimeout? timeout = null, CancellationToken ct = default)
            => _reverseInterface?.WriteAsync(velocities, ControlMode.SpeedJ, timeout, ct) ?? Task.FromResult(false);

        /// <summary>Sends Cartesian pose to the robot in POSE mode.</summary>
        public Task<bool> WritePoseAsync(double[] pose, RobotReceiveTimeout? timeout = null, CancellationToken ct = default)
            => _reverseInterface?.WriteAsync(pose, ControlMode.Pose, timeout, ct) ?? Task.FromResult(false);

        /// <summary>Sends an idle (keep-alive) signal to the robot.</summary>
        public Task<bool> WriteIdleAsync(RobotReceiveTimeout? timeout = null, CancellationToken ct = default)
            => _reverseInterface?.WriteAsync(null, ControlMode.Idle, timeout, ct) ?? Task.FromResult(false);

        /// <summary>Sends a stop command to halt the external control program.</summary>
        public Task<bool> StopControlAsync(CancellationToken ct = default)
            => _reverseInterface?.WriteAsync(null, ControlMode.Stopped, RobotReceiveTimeout.Milliseconds(0), ct) ?? Task.FromResult(false);

        // ─── Force mode / script commands ─────────────────────────────────────────

        /// <summary>Starts force mode on the robot.</summary>
        public Task<bool> StartForceModeAsync(double[] taskFrame, int[] selectionVector, double[] wrench, int forceType, double[] limits, CancellationToken ct = default)
            => _scriptCommandInterface?.StartForceModeAsync(taskFrame, selectionVector, wrench, forceType, limits, ct) ?? Task.FromResult(false);

        /// <summary>Ends force mode.</summary>
        public Task<bool> EndForceModeAsync(CancellationToken ct = default)
            => _scriptCommandInterface?.EndForceModeAsync(ct) ?? Task.FromResult(false);

        /// <summary>Zeros the force/torque sensor.</summary>
        public Task<bool> ZeroFtSensorAsync(CancellationToken ct = default)
            => _scriptCommandInterface?.ZeroFtSensorAsync(ct) ?? Task.FromResult(false);

        /// <summary>Sets the active payload.</summary>
        public Task<bool> SetPayloadAsync(double mass, double[] cog, CancellationToken ct = default)
            => _scriptCommandInterface?.SetPayloadAsync(mass, cog, ct) ?? Task.FromResult(false);

        /// <summary>Sets the TCP offset.</summary>
        public Task<bool> SetTcpOffsetAsync(double[] offset, CancellationToken ct = default)
            => _scriptCommandInterface?.SetTcpOffsetAsync(offset, ct) ?? Task.FromResult(false);

        /// <summary>Starts tool contact detection.</summary>
        public Task<bool> StartToolContactAsync(CancellationToken ct = default)
            => _scriptCommandInterface?.StartToolContactAsync(ct) ?? Task.FromResult(false);

        /// <summary>Ends tool contact detection.</summary>
        public Task<bool> EndToolContactAsync(CancellationToken ct = default)
            => _scriptCommandInterface?.EndToolContactAsync(ct) ?? Task.FromResult(false);

        // ─── Trajectory interface ─────────────────────────────────────────────────

        /// <summary>Waits for the robot to report a trajectory result.</summary>
        public Task<TrajectoryResult> WaitForTrajectoryResultAsync(CancellationToken ct = default)
            => _trajectoryInterface?.WaitForResultAsync(ct) ?? Task.FromResult(TrajectoryResult.Unknown);

        // ─── Event handlers ───────────────────────────────────────────────────────

        private void OnTrajectoryResult(object? sender, TrajectoryResult result)
            => TrajectoryDone?.Invoke(this, result);

        private void OnTrajectoryProgramStateChanged(object? sender, bool running)
            => TrajectoryInterfaceDisconnected?.Invoke(this, running);

        private void OnToolContactResult(object? sender, ToolContactResult result)
            => ToolContactResultReceived?.Invoke(this, result);

        // ─── Utilities ────────────────────────────────────────────────────────────

        private Dictionary<string, object> BuildScriptVariables()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["REVERSE_IP"] = "0.0.0.0",
                ["REVERSE_PORT"] = _config.ReversePort,
                ["SCRIPT_COMMAND_PORT"] = _config.ScriptCommandPort,
                ["TRAJECTORY_PORT"] = _config.TrajectoryPort,
                ["MAX_JOINT_SPEED"] = 1.0,
                ["MAX_JOINT_ACCELERATION"] = 3.0,
                ["KEEPALIVE_COUNT"] = _config.KeepaliveCount,
                ["SERVOJ_GAIN"] = 300,
                ["SERVOJ_LOOKAHEAD_TIME"] = 0.03,
                ["SOFTWARE_VERSION"] = _softwareVersion ?? new VersionInformation()
            };
        }

        private IList<string> ResolveRecipe(IReadOnlyList<string>? inline, string? file)
        {
            if (inline != null && inline.Count > 0) return new List<string>(inline);
            if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
            {
                var lines = new List<string>();
                foreach (var line in File.ReadAllLines(file))
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed)) lines.Add(trimmed);
                }
                return lines;
            }
            return Array.Empty<string>();
        }

        private ILogger<T> MakeLogger<T>()
            => _loggerFactory?.CreateLogger<T>() ?? NullLogger<T>.Instance;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisconnectAsync().GetAwaiter().GetResult();
        }
    }
}
