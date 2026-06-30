// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalRobotics.Exceptions;

namespace UniversalRobotics.Dashboard
{
    /// <summary>
    /// Contains the result of a dashboard server command.
    /// </summary>
    public sealed class DashboardResponse
    {
        /// <summary>Gets whether the command completed successfully.</summary>
        public bool Success { get; }
        /// <summary>Gets the raw response string from the robot.</summary>
        public string? RawResponse { get; }
        /// <summary>Gets additional data fields parsed from the response (e.g. JSON for PolyScope X).</summary>
        public IReadOnlyDictionary<string, string> Data { get; }

        /// <summary>Creates a successful response.</summary>
        public static DashboardResponse Ok(string? raw = null, IReadOnlyDictionary<string, string>? data = null)
            => new DashboardResponse(true, raw, data);

        /// <summary>Creates a failure response.</summary>
        public static DashboardResponse Fail(string? raw = null)
            => new DashboardResponse(false, raw, null);

        private DashboardResponse(bool success, string? raw, IReadOnlyDictionary<string, string>? data)
        {
            Success = success;
            RawResponse = raw;
            Data = data ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    // ─── CB3 / e-Series Dashboard Client (text-line protocol) ────────────────────

    /// <summary>
    /// Connects to the UR dashboard server on port 29999 using the line-based text protocol.
    /// Supports CB3 (PolyScope 3.x) and e-Series (PolyScope 5.x) robots.
    /// Replaces the C++ <c>DashboardClient</c> / <c>DashboardClientImplementationG5</c> classes.
    /// </summary>
    public sealed class DashboardClientG5 : IDisposable
    {
        private const int DefaultDashboardPort = 29999;

        private System.Net.Sockets.TcpClient? _tcp;
        private System.IO.StreamReader? _reader;
        private System.IO.StreamWriter? _writer;
        private readonly string _host;
        private readonly int _port;
        private readonly ILogger _logger;
        private readonly object _lock = new object();

        /// <summary>Gets the remote host this client connects to.</summary>
        public string Host => _host;

        /// <summary>
        /// Creates a dashboard client for a CB3 or e-Series robot.
        /// </summary>
        public DashboardClientG5(string host, int port = DefaultDashboardPort, ILogger? logger = null)
        {
            _host = host;
            _port = port;
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>Opens a connection to the dashboard server.</summary>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            var tcp = new System.Net.Sockets.TcpClient();
            tcp.NoDelay = true;
            await tcp.ConnectAsync(_host, _port).ConfigureAwait(false);

            lock (_lock)
            {
                _tcp?.Close();
                _tcp = tcp;
                var stream = tcp.GetStream();
                _reader = new System.IO.StreamReader(stream, Encoding.ASCII);
                _writer = new System.IO.StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\n" };
            }

            // Read the welcome banner
            var banner = await _reader!.ReadLineAsync().ConfigureAwait(false);
            _logger.LogDebug("Dashboard connected: {Banner}", banner);
        }

        /// <summary>Closes the connection to the dashboard server.</summary>
        public void Disconnect()
        {
            lock (_lock)
            {
                _reader?.Close();
                _writer?.Close();
                _tcp?.Close();
                _reader = null;
                _writer = null;
                _tcp = null;
            }
        }

        /// <summary>
        /// Sends a raw command and returns the first response line.
        /// </summary>
        public async Task<string> SendAndReceiveAsync(string command, CancellationToken cancellationToken = default)
        {
            System.IO.StreamReader reader;
            System.IO.StreamWriter writer;
            lock (_lock)
            {
                if (_reader == null || _writer == null) throw new NotConnectedException("Dashboard client is not connected.");
                reader = _reader;
                writer = _writer;
            }

            await writer.WriteLineAsync(command).ConfigureAwait(false);
            return await reader.ReadLineAsync().ConfigureAwait(false) ?? string.Empty;
        }

        // ─── Robot control commands ───────────────────────────────────────────────

        /// <summary>Powers on the robot and waits until it reaches IDLE mode.</summary>
        public async Task<DashboardResponse> PowerOnAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            => await SendCommandWithTimeoutAsync("power on", "Powering on", timeout ?? TimeSpan.FromSeconds(30), cancellationToken);

        /// <summary>Powers off the robot.</summary>
        public async Task<DashboardResponse> PowerOffAsync(CancellationToken cancellationToken = default)
        {
            var resp = await SendAndReceiveAsync("power off", cancellationToken).ConfigureAwait(false);
            return DashboardResponse.Ok(resp);
        }

        /// <summary>Releases the brakes and transitions the robot to IDLE.</summary>
        public async Task<DashboardResponse> BrakeReleaseAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            => await SendCommandWithTimeoutAsync("brake release", "Brake releasing", timeout ?? TimeSpan.FromSeconds(15), cancellationToken);

        /// <summary>Closes the safety popup if one is displayed.</summary>
        public async Task<DashboardResponse> CloseSafetyPopupAsync(CancellationToken cancellationToken = default)
        {
            var resp = await SendAndReceiveAsync("close safety popup", cancellationToken).ConfigureAwait(false);
            return DashboardResponse.Ok(resp);
        }

        /// <summary>Unlocks the protective stop.</summary>
        public async Task<DashboardResponse> UnlockProtectiveStopAsync(CancellationToken cancellationToken = default)
        {
            var resp = await SendAndReceiveAsync("unlock protective stop", cancellationToken).ConfigureAwait(false);
            return DashboardResponse.Ok(resp);
        }

        /// <summary>Loads a program from the robot's storage.</summary>
        public async Task<DashboardResponse> LoadProgramAsync(string programPath, CancellationToken cancellationToken = default)
        {
            var resp = await SendAndReceiveAsync($"load {programPath}", cancellationToken).ConfigureAwait(false);
            return resp.StartsWith("Loading program:", StringComparison.OrdinalIgnoreCase)
                ? DashboardResponse.Ok(resp)
                : DashboardResponse.Fail(resp);
        }

        /// <summary>Starts the currently loaded program.</summary>
        public async Task<DashboardResponse> PlayAsync(CancellationToken cancellationToken = default)
        {
            var resp = await SendAndReceiveAsync("play", cancellationToken).ConfigureAwait(false);
            return resp.StartsWith("Starting program", StringComparison.OrdinalIgnoreCase)
                ? DashboardResponse.Ok(resp)
                : DashboardResponse.Fail(resp);
        }

        /// <summary>Pauses the currently running program.</summary>
        public async Task<DashboardResponse> PauseAsync(CancellationToken cancellationToken = default)
        {
            var resp = await SendAndReceiveAsync("pause", cancellationToken).ConfigureAwait(false);
            return DashboardResponse.Ok(resp);
        }

        /// <summary>Stops the currently running program.</summary>
        public async Task<DashboardResponse> StopAsync(CancellationToken cancellationToken = default)
        {
            var resp = await SendAndReceiveAsync("stop", cancellationToken).ConfigureAwait(false);
            return DashboardResponse.Ok(resp);
        }

        /// <summary>Queries the current robot mode.</summary>
        public async Task<string> GetRobotModeAsync(CancellationToken cancellationToken = default)
            => await SendAndReceiveAsync("robotmode", cancellationToken).ConfigureAwait(false);

        /// <summary>Queries the currently loaded program name.</summary>
        public async Task<string> GetLoadedProgramAsync(CancellationToken cancellationToken = default)
            => await SendAndReceiveAsync("get loaded program", cancellationToken).ConfigureAwait(false);

        /// <summary>Queries the program state.</summary>
        public async Task<string> GetProgramStateAsync(CancellationToken cancellationToken = default)
            => await SendAndReceiveAsync("programState", cancellationToken).ConfigureAwait(false);

        /// <summary>Queries whether the robot is in remote control mode.</summary>
        public async Task<bool> IsInRemoteControlAsync(CancellationToken cancellationToken = default)
        {
            var resp = await SendAndReceiveAsync("is in remote control", cancellationToken).ConfigureAwait(false);
            return resp.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Queries the robot's serial number.</summary>
        public async Task<string> GetSerialNumberAsync(CancellationToken cancellationToken = default)
            => await SendAndReceiveAsync("get serial number", cancellationToken).ConfigureAwait(false);

        /// <summary>Queries the robot model (e.g. "UR5").</summary>
        public async Task<string> GetRobotModelAsync(CancellationToken cancellationToken = default)
            => await SendAndReceiveAsync("get robot model", cancellationToken).ConfigureAwait(false);

        private async Task<DashboardResponse> SendCommandWithTimeoutAsync(
            string command, string expectedPrefix, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var resp = await SendAndReceiveAsync(command, cancellationToken).ConfigureAwait(false);
            if (!resp.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                return DashboardResponse.Fail(resp);
            return DashboardResponse.Ok(resp);
        }

        /// <inheritdoc />
        public void Dispose() => Disconnect();
    }

    // ─── PolyScope X Dashboard Client (REST/JSON) ─────────────────────────────────

    /// <summary>
    /// Connects to the UR Robot API (PolyScope X 10.11.0+) via HTTP REST on port 29999.
    /// All responses are JSON-encoded. Replaces the C++ <c>DashboardClientImplementationX</c> class.
    /// </summary>
    public sealed class DashboardClientX : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly ILogger _logger;

        /// <summary>
        /// Creates a PolyScope X dashboard client.
        /// </summary>
        /// <param name="host">Robot IP address.</param>
        /// <param name="port">HTTP port (default 29999).</param>
        /// <param name="logger">Optional logger.</param>
        public DashboardClientX(string host, int port = 29999, ILogger? logger = null)
        {
            _baseUrl = $"http://{host}:{port}";
            _logger = logger ?? NullLogger.Instance;
            _http = new HttpClient { BaseAddress = new Uri(_baseUrl), Timeout = TimeSpan.FromSeconds(10) };
        }

        /// <summary>Verifies connectivity to the Robot API (no-op; HTTP is connectionless).</summary>
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        // ─── Commands ────────────────────────────────────────────────────────────

        /// <summary>Requests the robot to power on.</summary>
        public Task<DashboardResponse> PowerOnAsync(CancellationToken ct = default)
            => PostAsync("/universal-robots/v1/robot/power-on", ct);

        /// <summary>Requests the robot to power off.</summary>
        public Task<DashboardResponse> PowerOffAsync(CancellationToken ct = default)
            => PostAsync("/universal-robots/v1/robot/power-off", ct);

        /// <summary>Releases the brakes.</summary>
        public Task<DashboardResponse> BrakeReleaseAsync(CancellationToken ct = default)
            => PostAsync("/universal-robots/v1/robot/brake-release", ct);

        /// <summary>Pauses the currently running program.</summary>
        public Task<DashboardResponse> PauseAsync(CancellationToken ct = default)
            => PostAsync("/universal-robots/v1/robot/pause", ct);

        /// <summary>Resumes a paused program.</summary>
        public Task<DashboardResponse> ResumeAsync(CancellationToken ct = default)
            => PostAsync("/universal-robots/v1/robot/play", ct);

        /// <summary>Stops the currently running program.</summary>
        public Task<DashboardResponse> StopAsync(CancellationToken ct = default)
            => PostAsync("/universal-robots/v1/robot/stop", ct);

        /// <summary>Queries the robot state.</summary>
        public Task<DashboardResponse> GetRobotStateAsync(CancellationToken ct = default)
            => GetAsync("/universal-robots/v1/robot/state", ct);

        /// <summary>Closes the safety popup.</summary>
        public Task<DashboardResponse> CloseSafetyPopupAsync(CancellationToken ct = default)
            => PostAsync("/universal-robots/v1/safety/close-popup", ct);

        // ─── HTTP helpers ────────────────────────────────────────────────────────

        private async Task<DashboardResponse> GetAsync(string path, CancellationToken ct)
        {
            try
            {
                var response = await _http.GetAsync(path, ct).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var data = ParseJsonResponse(body);
                return response.IsSuccessStatusCode
                    ? DashboardResponse.Ok(body, data)
                    : DashboardResponse.Fail(body);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GET {Path} failed.", path);
                return DashboardResponse.Fail(ex.Message);
            }
        }

        private async Task<DashboardResponse> PostAsync(string path, CancellationToken ct, string? jsonBody = null)
        {
            try
            {
                var content = jsonBody != null
                    ? new StringContent(jsonBody, Encoding.UTF8, "application/json")
                    : new StringContent(string.Empty);
                var response = await _http.PostAsync(path, content, ct).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var data = ParseJsonResponse(body);
                return response.IsSuccessStatusCode
                    ? DashboardResponse.Ok(body, data)
                    : DashboardResponse.Fail(body);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "POST {Path} failed.", path);
                return DashboardResponse.Fail(ex.Message);
            }
        }

        private static IReadOnlyDictionary<string, string> ParseJsonResponse(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                    result[prop.Name] = prop.Value.ToString();
            }
            catch { /* Not valid JSON — return empty dict */ }
            return result;
        }

        /// <inheritdoc />
        public void Dispose() => _http.Dispose();
    }
}
