using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UniversalRobotics.Dashboard;

namespace UniversalRobotics.Tests
{
    public class DashboardClientXTests
    {
        [Fact]
        public async Task ConnectAsync_UsesRobotApiRootAndReadsOpenApiVersion()
        {
            using var handler = new RecordingHandler((request, _) =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/universal-robots/robot-api/openapi.json", request.RequestUri!.AbsolutePath);
                return Task.FromResult(JsonResponse("{\"info\":{\"version\":\"3.1.4\"}}"));
            });
            using var client = CreateClient(handler);

            await client.ConnectAsync();

            Assert.Equal(3u, client.ApiVersion!.Major);
            Assert.Equal(1u, client.ApiVersion.Minor);
            Assert.Equal(4u, client.ApiVersion.Bugfix);
        }

        [Fact]
        public async Task PowerOnAsync_Accepts204_AndSendsExactJsonRequest()
        {
            HttpRequestMessage? captured = null;
            string? body = null;
            using var handler = new RecordingHandler((request, _) =>
            {
                captured = request;
                body = request.Content == null ? null : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            });
            using var client = CreateClient(handler);

            var response = await client.PowerOnAsync();

            Assert.True(response.Success);
            Assert.Equal(HttpMethod.Put, captured!.Method);
            Assert.Equal("/universal-robots/robot-api/robotstate/v1/state", captured.RequestUri!.AbsolutePath);
            Assert.Equal("{\"action\":\"POWER_ON\"}", body);
        }

        [Fact]
        public async Task VersionGatedCommands_RejectOldApiBeforeCommandRequest()
        {
            var paths = new List<string>();
            using var handler = new RecordingHandler((request, _) =>
            {
                paths.Add(request.RequestUri!.AbsolutePath);
                return request.RequestUri.AbsolutePath.EndsWith("openapi.json", StringComparison.Ordinal)
                    ? Task.FromResult(JsonResponse("{\"info\":{\"version\":\"3.1.3\"}}"))
                    : Task.FromResult(JsonResponse("{}", HttpStatusCode.NoContent));
            });
            using var client = CreateClient(handler);

            await Assert.ThrowsAsync<NotSupportedException>(() => client.GetLoadedProgramAsync());

            Assert.Equal(new[] { "/universal-robots/robot-api/openapi.json" }, paths);
        }

        [Fact]
        public async Task FivePointVersionGatedCommands_RejectApiBelow50107()
        {
            using var handler = new RecordingHandler((request, _) =>
                Task.FromResult(JsonResponse(request.RequestUri!.AbsolutePath.EndsWith("openapi.json", StringComparison.Ordinal)
                    ? "{\"info\":{\"version\":\"5.0.106\"}}"
                    : "{}", HttpStatusCode.NoContent)));
            using var client = CreateClient(handler);

            await Assert.ThrowsAsync<NotSupportedException>(() => client.PopupAsync("message"));
        }

        [Fact]
        public async Task GetProgramListAsync_PreservesNullMetadata()
        {
            using var handler = new RecordingHandler((request, _) => request.RequestUri!.AbsolutePath.EndsWith("openapi.json", StringComparison.Ordinal)
                ? Task.FromResult(JsonResponse("{\"info\":{\"version\":\"3.1.4\"}}"))
                : Task.FromResult(JsonResponse("{\"programs\":[{\"name\":\"only_name\",\"description\":null,\"createdDate\":null,\"lastModifiedDate\":null,\"lastSavedDate\":null,\"programState\":null}]}")));
            using var client = CreateClient(handler);

            var programs = await client.GetProgramListAsync();

            var program = Assert.Single(programs);
            Assert.Equal("only_name", program.Name);
            Assert.Null(program.Description);
            Assert.Null(program.CreatedDate);
            Assert.Null(program.LastModifiedDate);
            Assert.Null(program.LastSavedDate);
            Assert.Null(program.ProgramState);
        }

        [Fact]
        public async Task UploadProgramAsync_UsesPostMultipartAtCorrectRoot()
        {
            var source = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(source, "program bytes");
                HttpRequestMessage? captured = null;
                string? body = null;
                using var handler = new RecordingHandler((request, _) =>
                {
                    captured = request;
                    body = request.Content == null ? null : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    return request.RequestUri!.AbsolutePath.EndsWith("openapi.json", StringComparison.Ordinal)
                        ? Task.FromResult(JsonResponse("{\"info\":{\"version\":\"3.1.4\"}}"))
                        : Task.FromResult(JsonResponse("{}", HttpStatusCode.Accepted));
                });
                using var client = CreateClient(handler);

                var response = await client.UploadProgramAsync(source);

                Assert.True(response.Success);
                Assert.Equal(HttpMethod.Post, captured!.Method);
                Assert.Equal("/universal-robots/robot-api/programs/v1/", captured.RequestUri!.AbsolutePath);
                Assert.Contains("program bytes", body);
            }
            finally
            {
                File.Delete(source);
            }
        }

        [Fact]
        public async Task DownloadProgramAsync_WritesStreamAndPreservesExistingFileOnFailure()
        {
            var destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".urpx");
            try
            {
                using var handler = new RecordingHandler((request, _) =>
                {
                    if (request.RequestUri!.AbsolutePath.EndsWith("openapi.json", StringComparison.Ordinal))
                        return Task.FromResult(JsonResponse("{\"info\":{\"version\":\"3.1.4\"}}" ));
                    if (request.RequestUri.AbsolutePath.EndsWith("good.urpx", StringComparison.Ordinal))
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("downloaded") });
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("failed") });
                });
                using var client = CreateClient(handler);

                var success = await client.DownloadProgramAsync("good.urpx", destination);
                Assert.True(success.Success);
                Assert.Equal("downloaded", await File.ReadAllTextAsync(destination));

                await File.WriteAllTextAsync(destination, "keep me");
                var failure = await client.DownloadProgramAsync("bad.urpx", destination);
                Assert.False(failure.Success);
                Assert.Equal("keep me", await File.ReadAllTextAsync(destination));
            }
            finally
            {
                File.Delete(destination);
            }
        }

        [Fact]
        public async Task DownloadSupportFilesAsync_StreamsResponseToFile()
        {
            var destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                using var handler = new RecordingHandler((request, _) =>
                    Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("openapi.json", StringComparison.Ordinal)
                        ? JsonResponse("{\"info\":{\"version\":\"5.0.107\"}}")
                        : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("support archive") }));
                using var client = CreateClient(handler);

                var response = await client.DownloadSupportFilesAsync(destination);

                Assert.True(response.Success);
                Assert.Equal("support archive", await File.ReadAllTextAsync(destination));
            }
            finally
            {
                File.Delete(destination);
            }
        }

        [Fact]
        public async Task Request_CancellationIsPropagatedToHttpHandler()
        {
            using var entered = new ManualResetEventSlim();
            using var handler = new RecordingHandler(async (_, cancellationToken) =>
            {
                entered.Set();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using var client = CreateClient(handler);
            using var cancellation = new CancellationTokenSource();
            var request = client.GetRobotStateAsync(cancellation.Token);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
            Assert.True(handler.CancellationObserved);
        }

        [Fact]
        public void Dispose_DoesNotDisposeCallerOwnedHttpClient()
        {
            using var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://robot/") };
            using (var client = new DashboardClientX(httpClient)) { }

            Assert.False(handler.Disposed);
        }

        private static DashboardClientX CreateClient(RecordingHandler handler)
            => new DashboardClientX(new HttpClient(handler) { BaseAddress = new Uri("http://robot/") });

        private static HttpResponseMessage JsonResponse(string body, HttpStatusCode status = HttpStatusCode.OK)
            => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
            public bool CancellationObserved { get; private set; }
            public bool Disposed { get; private set; }

            public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
                => _send = send;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                try
                {
                    return await _send(request, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    CancellationObserved = cancellationToken.IsCancellationRequested;
                    throw;
                }
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
        }
    }
}