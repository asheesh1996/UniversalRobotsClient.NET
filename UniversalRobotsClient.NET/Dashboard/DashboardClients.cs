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
    /// Connects to the UR Robot API (PolyScope X) via HTTP REST on port 80.
    /// All responses are JSON-encoded. Replaces the C++ <c>DashboardClientImplementationX</c> class.
    /// </summary>
    public sealed partial class DashboardClientX : IDisposable
    {
        private readonly HttpClient _http;
        private readonly ILogger _logger;
        private readonly bool _ownsHttpClient;
        private const string ApiRoot = "/universal-robots/robot-api";

        /// <summary>Robot API version discovered from its OpenAPI document.</summary>
        public UniversalRobotics.Models.VersionInformation? ApiVersion { get; private set; }

        /// <summary>Per-request timeout, including response body transfer.</summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Creates a PolyScope X dashboard client.
        /// </summary>
        /// <param name="host">Robot IP address.</param>
        /// <param name="port">HTTP port (default 80, distinct from the G5 dashboard port).</param>
        /// <param name="logger">Optional logger.</param>
        public DashboardClientX(string host, int port = 80, ILogger? logger = null)
        {
            _logger = logger ?? NullLogger.Instance;
            _http = new HttpClient { BaseAddress = new UriBuilder("http", host, port).Uri, Timeout = Timeout.InfiniteTimeSpan };
            _ownsHttpClient = true;
        }

        /// <summary>Uses a caller-owned HTTP client, whose BaseAddress identifies the controller.</summary>
        public DashboardClientX(HttpClient httpClient, ILogger? logger = null)
        {
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            if (_http.BaseAddress == null) throw new ArgumentException("BaseAddress is required.", nameof(httpClient));
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>Verifies the Robot API endpoint and discovers its version.</summary>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            var response = await SendAsync(HttpMethod.Get, "/openapi.json", cancellationToken).ConfigureAwait(false);
            if (!response.Success) throw new CommunicationException(response.RawResponse ?? "Robot API connection failed.");
            using var document = JsonDocument.Parse(response.RawResponse!);
            ApiVersion = UniversalRobotics.Models.VersionInformation.Parse(document.RootElement.GetProperty("info").GetProperty("version").GetString()!);
        }

        // ─── Commands ────────────────────────────────────────────────────────────

        /// <summary>Requests the robot to power on.</summary>
        public Task<DashboardResponse> PowerOnAsync(CancellationToken ct = default)
            => SendAsync(HttpMethod.Put, "/robotstate/v1/state", ct, new { action = "POWER_ON" });

        /// <summary>Requests the robot to power off.</summary>
        public Task<DashboardResponse> PowerOffAsync(CancellationToken ct = default)
            => SendAsync(HttpMethod.Put, "/robotstate/v1/state", ct, new { action = "POWER_OFF" });

        /// <summary>Releases the brakes.</summary>
        public Task<DashboardResponse> BrakeReleaseAsync(CancellationToken ct = default)
            => SendAsync(HttpMethod.Put, "/robotstate/v1/state", ct, new { action = "BRAKE_RELEASE" });

        /// <summary>Pauses the currently running program.</summary>
        public Task<DashboardResponse> PauseAsync(CancellationToken ct = default)
            => SendAsync(HttpMethod.Put, "/program/v1/state", ct, new { action = "pause" });

        /// <summary>Resumes a paused program.</summary>
        public Task<DashboardResponse> ResumeAsync(CancellationToken ct = default)
            => SendAsync(HttpMethod.Put, "/program/v1/state", ct, new { action = "resume" });

        /// <summary>Stops the currently running program.</summary>
        public Task<DashboardResponse> StopAsync(CancellationToken ct = default)
            => SendAsync(HttpMethod.Put, "/program/v1/state", ct, new { action = "stop" });

        /// <summary>Queries the robot state.</summary>
        public Task<DashboardResponse> GetRobotStateAsync(CancellationToken ct = default)
            => SendAsync(HttpMethod.Get, "/robotstate/v1/state", ct);

        /// <summary>Closes the safety popup.</summary>
        public Task<DashboardResponse> CloseSafetyPopupAsync(CancellationToken ct = default)
            => SendVersionedAsync(HttpMethod.Delete, "/popup/v1/safety", "5.0.107", ct);

        // ─── HTTP helpers ────────────────────────────────────────────────────────

        private async Task<DashboardResponse> SendAsync(HttpMethod method, string path, CancellationToken ct, object? payload = null, TimeSpan? timeout = null)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout ?? RequestTimeout);
            using var request = new HttpRequestMessage(method, ApiRoot + path);
            if (payload != null)
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            try
            {
                using var response = await _http.SendAsync(request, linked.Token).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var data = ParseJsonResponse(body);
                return response.IsSuccessStatusCode
                    ? DashboardResponse.Ok(body, data)
                    : DashboardResponse.Fail(body);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "{Method} {Path} failed.", method, path);
                return DashboardResponse.Fail(ex.Message);
            }
        }

        private async Task RequireVersionAsync(string minimum, CancellationToken ct)
        {
            if (ApiVersion == null) await ConnectAsync(ct).ConfigureAwait(false);
            var version = ApiVersion ?? throw new CommunicationException("Robot API version is unavailable.");
            if (version < UniversalRobotics.Models.VersionInformation.Parse(minimum))
                throw new NotSupportedException($"This command requires Robot API {minimum} or newer; connected version is {ApiVersion}.");
        }

        private async Task<DashboardResponse> SendVersionedAsync(HttpMethod method, string path, string minimum, CancellationToken ct, object? payload = null, TimeSpan? timeout = null)
        {
            await RequireVersionAsync(minimum, ct).ConfigureAwait(false);
            return await SendAsync(method, path, ct, payload, timeout).ConfigureAwait(false);
        }

        private static IReadOnlyDictionary<string, string> ParseJsonResponse(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        result[prop.Name] = prop.Value.ToString();
            }
            catch (JsonException) { return result; }
            return result;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_ownsHttpClient) _http.Dispose();
        }
    }
}
