// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalRobotics.Exceptions;
using UniversalRobotics.Primary.Packets;
using UniversalRobotics.Protocol;
using UniversalRobotics.Models;
using UniversalRobotics.Transport;

namespace UniversalRobotics.Primary
{
    /// <summary>
    /// Parses incoming primary interface bytes into typed <see cref="PrimaryPackage"/> objects.
    /// </summary>
    public sealed class PrimaryParser : IParser<PrimaryPackageHeader, PrimaryPackage>
    {
        /// <inheritdoc />
        public bool TryParse(byte[] buffer, int length, out PrimaryPackage? package)
        {
            if (length < 5) { package = null; return false; }

            // Header: 4-byte big-endian size + 1-byte type
            var headerParser = new BinaryParser(buffer, 0, 5);
            int size = headerParser.ReadInt32();
            var type = (RobotPackageType)headerParser.ReadSByte();

            // Payload starts at byte 5, length is totalSize - 5
            int payloadLength = Math.Max(0, size - 5);
            if (payloadLength > length - 5) payloadLength = length - 5;

            var payloadParser = new BinaryParser(buffer, 5, payloadLength);

            package = type switch
            {
                RobotPackageType.RobotState => ParseRobotState(payloadParser),
                RobotPackageType.RobotMessage => ParseRobotMessage(payloadParser),
                _ => new UnknownPrimaryPackage(type)
            };

            return package != null;
        }

        private static RobotStatePackage ParseRobotState(BinaryParser parser)
        {
            var pkg = new RobotStatePackage();
            pkg.ParseFrom(parser);
            return pkg;
        }

        private static RobotMessagePackage? ParseRobotMessage(BinaryParser parser)
        {
            if (parser.RemainingBytes < 10) return null;

            long timestamp = parser.ReadInt64();
            sbyte source = parser.ReadSByte();
            byte msgType = parser.ReadByte();

            RobotMessagePackage msg = msgType switch
            {
                6 => new ErrorCodeMessage(),
                10 => new RuntimeExceptionMessage(),
                3 => new VersionMessage(),
                0 => new PrimaryTextMessage(),
                5 => new SafetyModeMessage(),
                _ => new PrimaryTextMessage()
            };

            msg.ParseFrom(new BinaryParser(GetRemainingBytes(parser), 0, parser.RemainingBytes));
            return msg;
        }

        private static byte[] GetRemainingBytes(BinaryParser parser) => parser.ReadRemaining();
    }

    /// <summary>
    /// Manages the connection to the robot's primary interface (TCP port 30001).
    /// Continuously reads packages and dispatches them to registered consumers.
    /// Provides methods to send URScript programs to the robot.
    /// Replaces the C++ <c>PrimaryClient</c> class.
    /// </summary>
    public sealed class PrimaryClient : IDisposable
    {
        private const int DefaultPrimaryPort = 30001;

        private readonly string _robotIp;
        private readonly int _port;
        private readonly PrimaryParser _parser;
        private readonly ILogger _logger;
        private readonly INotifier _notifier;

        private UrStream<PrimaryPackage>? _stream;
        private CancellationTokenSource? _cts;
        private Task? _readerTask;

        private readonly List<IConsumer<PrimaryPackage>> _consumers = new List<IConsumer<PrimaryPackage>>();
        private readonly ConcurrentQueue<ErrorCode> _errorQueue = new ConcurrentQueue<ErrorCode>();

        private volatile VersionInformation? _softwareVersion;
        private volatile RobotModeDataPackage? _latestRobotMode;
        private volatile JointDataPackage? _latestJointData;
        private volatile MasterboardDataPackage? _latestMasterboard;

        /// <summary>Gets the robot's software version, available after the first message is received.</summary>
        public VersionInformation? SoftwareVersion => _softwareVersion;

        /// <summary>Gets the latest robot mode data, or null if not yet received.</summary>
        public RobotModeDataPackage? LatestRobotMode => _latestRobotMode;

        /// <summary>Gets the latest joint data, or null if not yet received.</summary>
        public JointDataPackage? LatestJointData => _latestJointData;

        /// <summary>Raised when a new data package is received from the primary interface.</summary>
        public event EventHandler<PrimaryPackage>? PackageReceived;

        /// <summary>
        /// Creates a new <see cref="PrimaryClient"/>.
        /// </summary>
        public PrimaryClient(string robotIp, INotifier? notifier = null, int port = DefaultPrimaryPort, ILogger? logger = null)
        {
            _robotIp = robotIp;
            _port = port;
            _parser = new PrimaryParser();
            _logger = logger ?? NullLogger.Instance;
            _notifier = notifier ?? NullNotifier.Instance;
        }

        /// <summary>Adds a consumer that will receive all primary packages.</summary>
        public void AddConsumer(IConsumer<PrimaryPackage> consumer)
        {
            lock (_consumers) _consumers.Add(consumer);
        }

        /// <summary>Removes a previously registered consumer.</summary>
        public void RemoveConsumer(IConsumer<PrimaryPackage> consumer)
        {
            lock (_consumers) _consumers.Remove(consumer);
        }

        /// <summary>
        /// Opens the connection to the primary interface and starts the background reader.
        /// </summary>
        public async Task StartAsync(
            int maxConnectionAttempts = 0,
            TimeSpan? reconnectionTimeout = null,
            CancellationToken cancellationToken = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _stream = new UrStream<PrimaryPackage>(_robotIp, _port, new PrimaryPacketFramer(), _logger);

            bool connected = await _stream.ConnectAsync(maxConnectionAttempts, reconnectionTimeout, _cts.Token)
                .ConfigureAwait(false);
            if (!connected) throw new CommunicationException("Failed to connect to the primary interface.");

            _notifier.Started();
            _readerTask = Task.Run(() => ReaderLoopAsync(_cts.Token), _cts.Token);
            _logger.LogInformation("Primary interface connected to {Host}:{Port}.", _robotIp, _port);
        }

        /// <summary>Stops the background reader and closes the connection.</summary>
        public async Task StopAsync()
        {
            _cts?.Cancel();
            if (_readerTask != null)
                try { await _readerTask.ConfigureAwait(false); } catch { }
            _stream?.Disconnect();
            _notifier.Stopped();
        }

        /// <summary>
        /// Sends a URScript program to the robot through the primary interface (non-blocking).
        /// The robot begins executing the program as soon as it is received.
        /// </summary>
        /// <param name="program">Valid URScript source code.</param>
        public async Task<bool> SendScriptAsync(string program, CancellationToken cancellationToken = default)
        {
            if (_stream == null || !_stream.IsConnected)
                throw new NotConnectedException("Primary interface is not connected.");

            var bytes = System.Text.Encoding.UTF8.GetBytes(program);
            try
            {
                await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Script sent ({Bytes} bytes).", bytes.Length);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send script.");
                return false;
            }
        }

        /// <summary>
        /// Sends a URScript program and waits until the robot confirms it has started or finished.
        /// </summary>
        public async Task<bool> SendScriptBlockingAsync(
            string program,
            string scriptName = "",
            TimeSpan? startTimeout = null,
            bool failOnWarnings = true,
            CancellationToken cancellationToken = default)
        {
            var timeout = startTimeout ?? TimeSpan.FromSeconds(1);
            var tcs = new TaskCompletionSource<bool>();

            void OnPackage(object? sender, PrimaryPackage pkg)
            {
                if (pkg is RuntimeExceptionMessage ex)
                    tcs.TrySetException(new UrScriptException(ex.ExceptionMessage, scriptName));
                else if (pkg is VersionMessage)
                    tcs.TrySetResult(true);
            }

            PackageReceived += OnPackage;
            try
            {
                await SendScriptAsync(program, cancellationToken).ConfigureAwait(false);
                using var timeoutCts = new CancellationTokenSource(timeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                linked.Token.Register(() => tcs.TrySetException(new UrTimeoutException($"Script '{scriptName}' did not start within {timeout.TotalSeconds}s.")));
                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                PackageReceived -= OnPackage;
            }
        }

        /// <summary>Retrieves and clears recorded error codes from the primary interface.</summary>
        public IReadOnlyList<ErrorCode> GetErrorCodes()
        {
            var result = new List<ErrorCode>();
            while (_errorQueue.TryDequeue(out var code)) result.Add(code);
            return result;
        }

        private async Task ReaderLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var (buffer, length) = await _stream!.ReadPacketAsync(ct).ConfigureAwait(false);
                    if (!_parser.TryParse(buffer, length, out var pkg) || pkg is null) continue;

                    // Cache important state locally
                    if (pkg is RobotStatePackage state)
                    {
                        foreach (var sub in state.SubPackages)
                        {
                            if (sub is RobotModeDataPackage mode) _latestRobotMode = mode;
                            else if (sub is JointDataPackage joint) _latestJointData = joint;
                            else if (sub is MasterboardDataPackage mb) _latestMasterboard = mb;
                        }
                    }
                    else if (pkg is VersionMessage ver)
                    {
                        _softwareVersion = ver.Version;
                    }
                    else if (pkg is ErrorCodeMessage err)
                    {
                        _errorQueue.Enqueue(new ErrorCode(err.ErrorCode, err.ErrorArgument));
                    }

                    PackageReceived?.Invoke(this, pkg);

                    // Dispatch to registered consumers
                    IConsumer<PrimaryPackage>[] consumers;
                    lock (_consumers) consumers = _consumers.ToArray();
                    foreach (var c in consumers)
                        try { await c.ConsumeAsync(pkg, ct).ConfigureAwait(false); } catch { }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Primary interface read error.");
                    await Task.Delay(100, ct).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public void Dispose() => StopAsync().GetAwaiter().GetResult();
    }

    /// <summary>Carries an error code + argument pair reported by the primary interface.</summary>
    public sealed class ErrorCode
    {
        /// <summary>Gets the error code value.</summary>
        public uint Code { get; }
        /// <summary>Gets the error argument.</summary>
        public uint Argument { get; }

        /// <summary>Creates a new error code record.</summary>
        public ErrorCode(uint code, uint argument) { Code = code; Argument = argument; }

        /// <inheritdoc />
        public override string ToString() => $"ErrorCode {{ Code=0x{Code:X8}, Arg={Argument} }}";
    }

    // Make VersionInformation accessible in this namespace
}
