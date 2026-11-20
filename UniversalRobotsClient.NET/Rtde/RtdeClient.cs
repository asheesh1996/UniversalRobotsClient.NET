// Licensed under the Apache License, Version 2.0

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalRobotics.Exceptions;
using UniversalRobotics.Protocol;
using UniversalRobotics.Rtde.Packets;
using UniversalRobotics.Transport;

namespace UniversalRobotics.Rtde
{
    /// <summary>Indicates the RTDE client's connection and initialization lifecycle state.</summary>
    public enum ClientState
    {
        /// <summary>Not yet initialized.</summary>
        Uninitialized = 0,
        /// <summary>Handshake is in progress.</summary>
        Initializing = 1,
        /// <summary>Handshake completed; data streaming not yet started.</summary>
        Initialized = 2,
        /// <summary>Data streaming is active.</summary>
        Running = 3,
        /// <summary>Data streaming is paused.</summary>
        Paused = 4,
        /// <summary>Connection to robot has been lost.</summary>
        ConnectionLost = 5
    }

    /// <summary>Status bits for RTDE robot status output field.</summary>
    [Flags]
    public enum RtdeRobotStatusBits
    {
        /// <summary>Robot power is on.</summary>
        IsPowerOn = 1 << 0,
        /// <summary>A program is running on the robot.</summary>
        IsProgramRunning = 1 << 1,
        /// <summary>The Teach Pendant button is pressed.</summary>
        IsTeachButtonPressed = 1 << 2,
        /// <summary>The power button is pressed.</summary>
        IsPowerButtonPressed = 1 << 3
    }

    /// <summary>Status bits for RTDE safety status output field.</summary>
    [Flags]
    public enum RtdeSafetyStatusBits
    {
        /// <summary>Normal safety mode.</summary>
        IsNormalMode = 1 << 0,
        /// <summary>Reduced safety mode.</summary>
        IsReducedMode = 1 << 1,
        /// <summary>Protective stop is active.</summary>
        IsProtectiveStopped = 1 << 2,
        /// <summary>Recovery mode is active.</summary>
        IsRecoveryMode = 1 << 3,
        /// <summary>Safeguard stop is active.</summary>
        IsSafeguardStopped = 1 << 4,
        /// <summary>System emergency stop is active.</summary>
        IsSystemEmergencyStopped = 1 << 5,
        /// <summary>Robot emergency stop is active.</summary>
        IsRobotEmergencyStopped = 1 << 6,
        /// <summary>An emergency stop is active.</summary>
        IsEmergencyStopped = 1 << 7,
        /// <summary>A safety violation is reported.</summary>
        IsViolation = 1 << 8,
        /// <summary>A safety fault is reported.</summary>
        IsFault = 1 << 9,
        /// <summary>The robot is stopped by a safety condition.</summary>
        IsStoppedDueToSafety = 1 << 10,
        /// <summary>The three-position enabling input is active.</summary>
        Is3PeInputActive = 1 << 11
    }

    /// <summary>
    /// Manages real-time data exchange (RTDE) with a UR robot controller.
    /// Handles protocol handshake, recipe setup, data reading, and data writing.
    /// Replaces the C++ <c>RTDEClient</c> class.
    /// </summary>
    public sealed class RtdeClient : IDisposable
    {
        private const int DefaultRtdePort = 30004;
        private const ushort MaxProtocolVersion = 2;
        private const int MaxRequestRetries = 5;

        private readonly UrStream<RtdePackage> _stream;
        private readonly RtdeParser _parser;
        private readonly ILogger _logger;
        private readonly bool _ignoreUnavailableOutputs;
        private readonly double _targetFrequency;

        private List<string> _outputRecipe;
        private List<string> _inputRecipe;
        private DataPackage? _dataPackage;
        private RtdeWriter? _writer;
        private ushort _protocolVersion;
        // Maximum RTDE output frequency — CB3 (PolyScope < 5) = 125 Hz, e-Series/PolyScope X = 500 Hz.
        // Set after QueryUrControlVersionAsync; defaults to 500 Hz until then.
        private double _maxFrequency = 500.0;

        // Background reader state
        private CancellationTokenSource? _backgroundReaderCts;
        private Task? _backgroundReaderTask;
        private DataPackage? _latestPackage;
        private readonly SemaphoreSlim _newDataSignal = new SemaphoreSlim(0, 1);
        private readonly object _packageLock = new object();

        private volatile ClientState _clientState = ClientState.Uninitialized;

        /// <summary>Gets the current lifecycle state of the RTDE client.</summary>
        public ClientState State => _clientState;

        /// <summary>Local interface address used to reach the robot.</summary>
        public string? LocalIpAddress => _stream.LocalIpAddress;

        /// <summary>Raised when the connection to the robot is lost.</summary>
        public event EventHandler? ConnectionLost;

        /// <summary>
        /// Creates an <see cref="RtdeClient"/> that reads a recipe from files.
        /// </summary>
        public RtdeClient(
            string robotIp,
            string outputRecipeFile,
            string inputRecipeFile,
            double targetFrequency = 0.0,
            bool ignoreUnavailableOutputs = false,
            int port = DefaultRtdePort,
            ILogger? logger = null)
            : this(robotIp,
                   EnsureTimestampPresent(ReadRecipeFile(outputRecipeFile)),
                   ReadRecipeFile(inputRecipeFile),
                   targetFrequency, ignoreUnavailableOutputs, port, logger)
        { }

        /// <summary>
        /// Creates an <see cref="RtdeClient"/> with inline recipe lists.
        /// </summary>
        public RtdeClient(
            string robotIp,
            IEnumerable<string> outputRecipe,
            IEnumerable<string>? inputRecipe = null,
            double targetFrequency = 0.0,
            bool ignoreUnavailableOutputs = false,
            int port = DefaultRtdePort,
            ILogger? logger = null)
        {
            _logger = logger ?? NullLogger.Instance;
            _outputRecipe = EnsureTimestampPresent(outputRecipe.ToList());
            _inputRecipe = inputRecipe?.ToList() ?? new List<string>();
            _targetFrequency = targetFrequency;
            _ignoreUnavailableOutputs = ignoreUnavailableOutputs;

            _parser = new RtdeParser(_outputRecipe);
            _stream = new UrStream<RtdePackage>(robotIp, port, new RtdePacketFramer(), logger);
        }

        /// <summary>Gets the recipe that was configured for writing (input) data.</summary>
        public IReadOnlyList<string> InputRecipe => _inputRecipe;

        /// <summary>Gets the recipe that was configured for reading (output) data.</summary>
        public IReadOnlyList<string> OutputRecipe => _outputRecipe;

        /// <summary>Gets the <see cref="RtdeWriter"/> for sending data to the robot.</summary>
        public RtdeWriter? Writer => _writer;

        // ─── Lifecycle ────────────────────────────────────────────────────────────

        /// <summary>
        /// Connects to the robot and performs the RTDE handshake (protocol negotiation + recipe setup).
        /// </summary>
        public async Task InitAsync(
            int maxConnectionAttempts = 0,
            TimeSpan? reconnectionTimeout = null,
            int maxInitializationAttempts = 3,
            TimeSpan? initializationTimeout = null,
            CancellationToken cancellationToken = default)
        {
            _clientState = ClientState.Initializing;
            var connTimeout = reconnectionTimeout ?? UrTcpClient.DefaultReconnectionTime;
            var initTimeout = initializationTimeout ?? TimeSpan.FromSeconds(1);

            bool connected = await _stream.ConnectAsync(maxConnectionAttempts, connTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (!connected) throw new CommunicationException("Failed to connect to RTDE interface.");

            for (int attempt = 0; attempt < maxInitializationAttempts; attempt++)
            {
                try
                {
                    await NegotiateProtocolVersionAsync(cancellationToken).ConfigureAwait(false);
                    await QueryUrControlVersionAsync(cancellationToken).ConfigureAwait(false);
                    await SetupOutputRecipeAsync(cancellationToken).ConfigureAwait(false);
                    if (_inputRecipe.Count > 0)
                        await SetupInputRecipeAsync(cancellationToken).ConfigureAwait(false);
                    await EnsureRtdeBootedAsync(cancellationToken).ConfigureAwait(false);

                    _clientState = ClientState.Initialized;
                    _logger.LogInformation("RTDE initialized (protocol v{Version}).", _protocolVersion);
                    return;
                }
                catch (Exception ex) when (attempt < maxInitializationAttempts - 1)
                {
                    _logger.LogWarning(ex, "RTDE initialization attempt {Attempt} failed, retrying.", attempt + 1);
                    await Task.Delay(initTimeout, cancellationToken).ConfigureAwait(false);
                }
            }

            throw new CommunicationException($"RTDE initialization failed after {maxInitializationAttempts} attempt(s).");
        }

        /// <summary>
        /// Starts data streaming from the robot.
        /// </summary>
        /// <param name="readInBackground">
        /// When <see langword="true"/>, a background task continuously reads packets and the latest
        /// can be fetched with <see cref="GetDataPackageAsync"/>. When <see langword="false"/>, the
        /// caller must poll <see cref="GetDataPackageBlockingAsync"/>.
        /// </param>
        /// <param name="cancellationToken">Cancels startup and, in background mode, the reader.</param>
        public async Task StartAsync(bool readInBackground = true, CancellationToken cancellationToken = default)
        {
            var startPkg = new ControlStartPackage();
            await SendAndReceiveAsync(startPkg, cancellationToken).ConfigureAwait(false);

            if (!startPkg.Accepted)
                throw new CommunicationException("Robot did not accept RTDE start request.");

            _clientState = ClientState.Running;
            _logger.LogInformation("RTDE streaming started.");

            if (readInBackground)
                StartBackgroundReader(cancellationToken);
        }

        /// <summary>Pauses RTDE data streaming.</summary>
        public async Task PauseAsync(CancellationToken cancellationToken = default)
        {
            var pkg = new ControlPausePackage();
            await SendAndReceiveAsync(pkg, cancellationToken).ConfigureAwait(false);
            if (pkg.Accepted) _clientState = ClientState.Paused;
        }

        /// <summary>Resumes RTDE data streaming after a pause.</summary>
        public Task ResumeAsync(CancellationToken cancellationToken = default)
            => StartAsync(false, cancellationToken);

        /// <summary>Disconnects from the RTDE interface and stops the background reader.</summary>
        public async Task DisconnectAsync()
        {
            _backgroundReaderCts?.Cancel();
            _stream.Disconnect();
            if (_writer != null) await _writer.StopAsync().ConfigureAwait(false);
            if (_backgroundReaderTask != null)
                try { await _backgroundReaderTask.ConfigureAwait(false); } catch { }
            _backgroundReaderTask = null;
            _backgroundReaderCts?.Dispose();
            _backgroundReaderCts = null;
            lock (_packageLock)
            {
                _latestPackage = null;
                while (_newDataSignal.Wait(0)) { }
            }
            _clientState = ClientState.Uninitialized;
        }

        // ─── Data reading ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the latest data package received in background mode.
        /// Blocks until a new package arrives or <paramref name="timeout"/> elapses.
        /// </summary>
        public async Task<DataPackage?> GetDataPackageAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            try
            {
                await _newDataSignal.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }

            lock (_packageLock)
            {
                var package = _latestPackage;
                _latestPackage = null;
                return package;
            }
        }

        /// <summary>
        /// Reads the next RTDE data package directly from the socket (non-background mode).
        /// </summary>
        public async Task<bool> GetDataPackageBlockingAsync(DataPackage targetPackage, CancellationToken cancellationToken = default)
        {
            if (targetPackage == null) throw new ArgumentNullException(nameof(targetPackage));
            var package = await GetNextDataPackageAsync(cancellationToken).ConfigureAwait(false);
            if (package == null) return false;
            targetPackage.CopyFrom(package);
            return true;
        }

        /// <summary>
        /// Reads the next RTDE <see cref="DataPackage"/> directly from the socket and returns it.
        /// Non-DataPackage packets (e.g., text messages) are silently skipped.
        /// Use this when <see cref="StartAsync"/> was called with <c>readInBackground = false</c>.
        /// Throws on connection errors, allowing callers to handle reconnection.
        /// </summary>
        public async Task<DataPackage?> GetNextDataPackageAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_backgroundReaderTask != null && !_backgroundReaderTask.IsCompleted)
                throw new InvalidOperationException("Direct reads cannot run alongside the background reader.");
            while (!cancellationToken.IsCancellationRequested)
            {
                var (buffer, length) = await _stream.ReadPacketAsync(cancellationToken).ConfigureAwait(false);
                if (_parser.TryParse(buffer, length, out var pkg) && pkg is DataPackage dp)
                    return dp;
                // Skip non-DataPackage packets (TextMessage, etc.)
            }
            return null;
        }

        // ─── Handshake helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Queries the URControl software version (GET_URCONTROL_VERSION) and uses it to set
        /// the maximum allowed RTDE output frequency for this robot type.
        /// Matches C++ RTDEClient::queryURControlVersion() + setTargetFrequency().
        /// </summary>
        private async Task QueryUrControlVersionAsync(CancellationToken ct)
        {
            var pkg = new GetUrControlVersionPackage();
            await SendAndReceiveAsync(pkg, ct).ConfigureAwait(false);
            _logger.LogDebug("URControl version: {Maj}.{Min}.{Bug}.{Bld}",
                pkg.Major, pkg.Minor, pkg.Bugfix, pkg.Build);
            // CB3 (PolyScope < 5) maxes out at 125 Hz; e-Series / PolyScope X support 500 Hz.
            _maxFrequency = pkg.Major < 5 ? 125.0 : 500.0;
        }

        /// <summary>
        /// Checks that streaming works before initialization succeeds, then pauses the stream.
        /// Mirrors the upstream boot check; this is a readiness check, not a protocol priming requirement.
        /// </summary>
        private async Task EnsureRtdeBootedAsync(CancellationToken ct)
        {
            // 1. Send ControlStart
            var startPkg = new ControlStartPackage();
            await SendAndReceiveAsync(startPkg, ct).ConfigureAwait(false);
            if (!startPkg.Accepted)
            {
                throw new CommunicationException("ControlStart was rejected during the boot check.");
            }

            // 2. Read packets for up to 3 s. C++ exits when timestamp >= 40 s (robot fully up)
            //    OR after 2*target_frequency packets. We stop after the first packet whose
            //    timestamp ≥ 40 s, or after the 3-second window closes.
            double timestamp = 0.0;
            int packetsRead = 0;
            using var bootCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, bootCts.Token);

            while (!linked.Token.IsCancellationRequested)
            {
                try
                {
                    var (buf, len) = await _stream.ReadPacketAsync(linked.Token).ConfigureAwait(false);
                    if (_parser.TryParse(buf, len, out var parsed) && parsed is DataPackage dp)
                    {
                        dp.GetData<double>("timestamp", out timestamp);
                        packetsRead++;
                        if (timestamp >= 40.0) break;  // robot has been running ≥ 40 s
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { break; }
            }

            ct.ThrowIfCancellationRequested();
            if (packetsRead == 0) throw new CommunicationException("No RTDE data received during the boot check.");

            _logger.LogDebug("RTDE boot-check: {N} packet(s), timestamp={Ts:F1} s.",
                packetsRead, timestamp);

            // 3. Send ControlPause; drain any in-flight DataPackages before the pause response.
            await SendPauseWithDrainAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends ControlPause and waits up to 5 s for the acceptance response,
        /// discarding any DataPackage packets that arrive before it.
        /// </summary>
        private async Task SendPauseWithDrainAsync(CancellationToken ct)
        {
            var pausePkg = new ControlPausePackage();
            await _stream.WriteAsync(pausePkg.Serialize(), ct).ConfigureAwait(false);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked    = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            while (!linked.Token.IsCancellationRequested)
            {
                byte[] buffer; int length;
                try
                {
                    (buffer, length) = await _stream.ReadPacketAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { break; }

                if (_parser.TryParse(buffer, length, out var resp) &&
                    resp?.PackageType == RtdePackageType.ControlPackagePause)
                {
                    pausePkg.ParseFrom(new BinaryParser(buffer, 3, length - 3));
                    if (!pausePkg.Accepted) throw new CommunicationException("RTDE pause was rejected.");
                    _logger.LogDebug("RTDE paused (accepted={Accepted}).", pausePkg.Accepted);
                    return;
                }
            }

            ct.ThrowIfCancellationRequested();
            throw new CommunicationException("Did not receive ControlPause response within 5 s.");
        }

        private async Task NegotiateProtocolVersionAsync(CancellationToken ct)
        {
            for (ushort version = MaxProtocolVersion; version >= 1; version--)
            {
                var req = new RequestProtocolVersionPackage(version);
                await SendAndReceiveAsync(req, ct).ConfigureAwait(false);
                if (req.Accepted)
                {
                    _protocolVersion = version;
                    _parser.SetProtocolVersion(version);
                    return;
                }
            }
            throw new CommunicationException("Could not negotiate an RTDE protocol version with the robot.");
        }

        private async Task SetupOutputRecipeAsync(CancellationToken ct)
        {
            // Use the max frequency determined from the URControl version when no explicit
            // frequency was requested. Sending 0.0 Hz to URSim causes it to accept the
            // recipe but silently refuse to stream data after ControlStart.
            double frequency = _targetFrequency > 0.0 ? _targetFrequency : _maxFrequency;
            if (double.IsNaN(_targetFrequency) || double.IsInfinity(_targetFrequency) || _targetFrequency < 0 || frequency > _maxFrequency)
                throw new ArgumentOutOfRangeException(nameof(_targetFrequency));
            var pkg = new SetupOutputsPackage(_outputRecipe, frequency, _protocolVersion);
            await SendAndReceiveAsync(pkg, ct).ConfigureAwait(false);

            if (pkg.VariableTypes.Count != _outputRecipe.Count)
                throw new CommunicationException("RTDE output type count does not match the recipe.");
            var unavailable = new List<string>();
            for (int i = 0; i < pkg.VariableTypes.Count; i++)
            {
                if (pkg.VariableTypes[i] == "NOT_FOUND")
                {
                    unavailable.Add(_outputRecipe[i]);
                }
                else if (RtdeTypeMap.Resolve(pkg.VariableTypes[i]) == RtdeValueKind.None)
                    throw new CommunicationException($"Unsupported RTDE field type: {pkg.VariableTypes[i]}.");
            }
            if (unavailable.Count > 0)
            {
                if (!_ignoreUnavailableOutputs) throw new RtdeInvalidKeyException(unavailable);
                _outputRecipe.RemoveAll(unavailable.Contains);
                if (_outputRecipe.Count == 0) throw new CommunicationException("No supported RTDE output fields remain.");
                await SetupOutputRecipeAsync(ct).ConfigureAwait(false);
                return;
            }

            _dataPackage = new DataPackage(_outputRecipe, pkg.VariableTypes, _protocolVersion);
            _parser.SetFieldTypes(pkg.VariableTypes);
        }

        private async Task SetupInputRecipeAsync(CancellationToken ct)
        {
            var pkg = new SetupInputsPackage(_inputRecipe);
            await SendAndReceiveAsync(pkg, ct).ConfigureAwait(false);

            var inputPkg = new DataPackage(_inputRecipe, pkg.VariableTypes, _protocolVersion);
            if (pkg.VariableTypes.Count != _inputRecipe.Count || pkg.VariableTypes.Any(type => RtdeTypeMap.Resolve(type) == RtdeValueKind.None))
                throw new CommunicationException("RTDE input recipe contains unavailable, unsupported or already reserved fields: " + string.Join(", ", _inputRecipe));
            _writer = new RtdeWriter(_stream, _inputRecipe, _logger);
            _writer.SetFieldTypes(pkg.VariableTypes);
            await _writer.StartAsync(pkg.RecipeId, ct).ConfigureAwait(false);
        }

        private async Task SendAndReceiveAsync(RtdePackage request, CancellationToken ct)
        {
            var bytes = request.Serialize();
            await _stream.WriteAsync(bytes, ct).ConfigureAwait(false);

            for (int retry = 0; retry < MaxRequestRetries; retry++)
            {
                var (buffer, length) = await _stream.ReadPacketAsync(ct).ConfigureAwait(false);
                if (_parser.TryParse(buffer, length, out var response) && response?.PackageType == request.PackageType)
                {
                    // The response is the same object type — fill the original package with parsed data
                    var payload = new BinaryParser(buffer, 3, length - 3);
                    request.ParseFrom(payload);
                    return;
                }
                // Received a different packet type; try again
            }
            throw new CommunicationException($"Did not receive expected RTDE response for {request.PackageType}.");
        }

        private void StartBackgroundReader(CancellationToken externalCt)
        {
            _backgroundReaderCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            _backgroundReaderTask = Task.Run(() => BackgroundReadLoopAsync(_backgroundReaderCts.Token));
        }

        private async Task BackgroundReadLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var (buffer, length) = await _stream.ReadPacketAsync(ct).ConfigureAwait(false);
                    if (_parser.TryParse(buffer, length, out var pkg) && pkg is DataPackage dp)
                    {
                        lock (_packageLock)
                        {
                            _latestPackage = dp;
                            if (_newDataSignal.CurrentCount == 0) _newDataSignal.Release();
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "RTDE background read error.");
                    _clientState = ClientState.ConnectionLost;
                    ConnectionLost?.Invoke(this, EventArgs.Empty);
                    break;
                }
            }
        }

        // ─── Utilities ────────────────────────────────────────────────────────────

        private static List<string> EnsureTimestampPresent(List<string> recipe)
        {
            if (!recipe.Contains("timestamp"))
                recipe.Insert(0, "timestamp");
            return recipe;
        }

        private static List<string> ReadRecipeFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return new List<string>();
            var lines = File.ReadAllLines(path);
            var result = new List<string>(lines.Length);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed)) result.Add(trimmed);
            }
            return result;
        }

        /// <inheritdoc />
        public void Dispose() => DisconnectAsync().GetAwaiter().GetResult();
    }
}
