using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UniversalRobotics.Exceptions;
using UniversalRobotics.Models;

namespace UniversalRobotics.Dashboard
{
    /// <summary>Program metadata returned by the Robot API; fields other than Name may be null.</summary>
    public sealed class ProgramInformation
    {
        /// <summary>Program name.</summary>
        public string Name { get; }
        /// <summary>Optional description.</summary>
        public string? Description { get; }
        /// <summary>Optional creation timestamp.</summary>
        public long? CreatedDate { get; }
        /// <summary>Optional modification timestamp.</summary>
        public long? LastModifiedDate { get; }
        /// <summary>Optional save timestamp.</summary>
        public long? LastSavedDate { get; }
        /// <summary>Optional program state.</summary>
        public string? ProgramState { get; }

        internal ProgramInformation(JsonElement item)
        {
            Name = item.GetProperty("name").GetString() ?? throw new JsonException("Program name is required.");
            Description = ReadString(item, "description");
            ProgramState = ReadString(item, "programState");
            CreatedDate = ReadTimestamp(item, "createdDate");
            LastModifiedDate = ReadTimestamp(item, "lastModifiedDate");
            LastSavedDate = ReadTimestamp(item, "lastSavedDate");
        }

        private static string? ReadString(JsonElement item, string key)
            => item.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

        private static long? ReadTimestamp(JsonElement item, string key)
            => item.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetInt64() : (long?)null;
    }

    public sealed partial class DashboardClientX
    {
        /// <summary>Starts the loaded program.</summary>
        public Task<DashboardResponse> PlayAsync(CancellationToken ct = default)
            => SendAsync(HttpMethod.Put, "/program/v1/state", ct, new { action = "play" });

        /// <summary>Powers on with a request-specific timeout without changing concurrent request settings.</summary>
        public Task<DashboardResponse> PowerOnAsync(TimeSpan timeout, CancellationToken ct = default)
            => SendAsync(HttpMethod.Put, "/robotstate/v1/state", ct, new { action = "POWER_ON" }, timeout);

        /// <summary>Loads a program using the endpoint supported by the controller's API version.</summary>
        public async Task<DashboardResponse> LoadProgramAsync(string name, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(nameof(name));
            if (ApiVersion == null) await ConnectAsync(ct).ConfigureAwait(false);
            var version = ApiVersion ?? throw new CommunicationException("Robot API version is unavailable.");
            return version < VersionInformation.Parse("3.1.4")
                ? await SendAsync(HttpMethod.Put, "/program/v1/load", ct, new { programName = name }).ConfigureAwait(false)
                : await SendAsync(HttpMethod.Put, "/program/v1/loaded", ct, new { name }).ConfigureAwait(false);
        }

        /// <summary>Reads the currently loaded program.</summary>
        public Task<DashboardResponse> GetLoadedProgramAsync(CancellationToken ct = default)
            => SendVersionedAsync(HttpMethod.Get, "/program/v1/loaded", "3.1.4", ct);
        /// <summary>Reads the robot mode.</summary>
        public Task<DashboardResponse> GetRobotModeAsync(CancellationToken ct = default)
            => SendVersionedAsync(HttpMethod.Get, "/robotstate/v1/robotmode", "3.1.4", ct);
        /// <summary>Reads the safety mode.</summary>
        public Task<DashboardResponse> GetSafetyModeAsync(CancellationToken ct = default)
            => SendVersionedAsync(HttpMethod.Get, "/robotstate/v1/safetymode", "3.1.4", ct);
        /// <summary>Reads the operational mode.</summary>
        public Task<DashboardResponse> GetOperationalModeAsync(CancellationToken ct = default)
            => SendVersionedAsync(HttpMethod.Get, "/system/v1/operationalmode", "3.1.4", ct);
        /// <summary>Reads local/remote control mode.</summary>
        public Task<DashboardResponse> GetControlModeAsync(CancellationToken ct = default)
            => SendVersionedAsync(HttpMethod.Get, "/system/v1/controlmode", "3.1.4", ct);
        /// <summary>Reads program state.</summary>
        public Task<DashboardResponse> GetProgramStateAsync(CancellationToken ct = default)
            => SendAsync(HttpMethod.Get, "/program/v1/state", ct);
        /// <summary>Displays a popup on PolyScope X 10.14 or newer.</summary>
        public Task<DashboardResponse> PopupAsync(string message, string title = "", CancellationToken ct = default)
            => SendVersionedAsync(HttpMethod.Post, "/popup/v1", "5.0.107", ct, new { title, message });
        /// <summary>Closes the active popup.</summary>
        public Task<DashboardResponse> ClosePopupAsync(CancellationToken ct = default)
            => SendVersionedAsync(HttpMethod.Delete, "/popup/v1", "5.0.107", ct);
        /// <summary>Shuts down the controller.</summary>
        public Task<DashboardResponse> ShutdownAsync(CancellationToken ct = default)
            => SendVersionedAsync(HttpMethod.Put, "/system/v1/shutdown", "5.0.107", ct);
        /// <summary>Adds a message to the controller log.</summary>
        public Task<DashboardResponse> AddToLogAsync(string message, CancellationToken ct = default)
            => SendVersionedAsync(HttpMethod.Post, "/system/v1/log", "5.0.107", ct, new { message });
        /// <summary>Queries PolyScope marketing and baseline versions.</summary>
        public Task<DashboardResponse> GetPolyScopeVersionAsync(CancellationToken ct = default)
            => SendVersionedAsync(HttpMethod.Get, "/versions/v1", "5.0.107", ct);
        /// <summary>Queries the model and serial number.</summary>
        public Task<DashboardResponse> GetSystemInformationAsync(CancellationToken ct = default)
            => SendVersionedAsync(HttpMethod.Get, "/system/v1/information", "5.0.107", ct);
        /// <summary>Generates support files, using a 30-second request timeout.</summary>
        public Task<DashboardResponse> GenerateFlightReportAsync(CancellationToken ct = default)
            => SendVersionedAsync(HttpMethod.Post, "/supportfiles/v1", "5.0.107", ct, timeout: TimeSpan.FromSeconds(30));

        /// <summary>Lists programs, preserving nullable metadata rather than failing the entire query.</summary>
        public async Task<IReadOnlyList<ProgramInformation>> GetProgramListAsync(CancellationToken ct = default)
        {
            var response = await SendVersionedAsync(HttpMethod.Get, "/programs/v1/", "3.1.4", ct).ConfigureAwait(false);
            if (!response.Success) throw new CommunicationException(response.RawResponse ?? "Program list request failed.");
            using var document = JsonDocument.Parse(response.RawResponse!);
            var programs = new List<ProgramInformation>();
            foreach (var item in document.RootElement.GetProperty("programs").EnumerateArray())
                programs.Add(new ProgramInformation(item));
            return programs;
        }

        /// <summary>Uploads a URPX program without buffering the file in memory.</summary>
        public Task<DashboardResponse> UploadProgramAsync(string path, CancellationToken ct = default)
            => UploadAsync(path, HttpMethod.Post, ct);
        /// <summary>Replaces a URPX program.</summary>
        public Task<DashboardResponse> UpdateProgramAsync(string path, CancellationToken ct = default)
            => UploadAsync(path, HttpMethod.Put, ct);
        /// <summary>Downloads a program to a file, preserving an existing file on failure.</summary>
        public Task<DashboardResponse> DownloadProgramAsync(string name, string path, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(nameof(name));
            return DownloadAsync("/programs/v1/" + Uri.EscapeDataString(name), path, "3.1.4", ct);
        }
        /// <summary>Streams support files to disk, preserving an existing file on failure.</summary>
        public Task<DashboardResponse> DownloadSupportFilesAsync(string path, CancellationToken ct = default)
            => DownloadAsync("/supportfiles/v1", path, "5.0.107", ct);

        private async Task<DashboardResponse> UploadAsync(string path, HttpMethod method, CancellationToken ct)
        {
            await RequireVersionAsync("3.1.4", ct).ConfigureAwait(false);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(RequestTimeout);
            using var file = File.OpenRead(path);
            using var request = new HttpRequestMessage(method, ApiRoot + "/programs/v1/");
            var form = new MultipartFormDataContent();
            form.Add(new StreamContent(file), "file", Path.GetFileName(path));
            request.Content = form;
            using var response = await _http.SendAsync(request, linked.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return response.IsSuccessStatusCode ? DashboardResponse.Ok(body, ParseJsonResponse(body)) : DashboardResponse.Fail(body);
        }

        private async Task<DashboardResponse> DownloadAsync(string endpoint, string path, string minimum, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(nameof(path));
            await RequireVersionAsync(minimum, ct).ConfigureAwait(false);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(RequestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiRoot + endpoint);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return DashboardResponse.Fail($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            var destination = Path.GetFullPath(path);
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    await input.CopyToAsync(output, 81920, linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
                return DashboardResponse.Ok(destination);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}