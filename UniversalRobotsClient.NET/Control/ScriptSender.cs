// Licensed under the Apache License, Version 2.0

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalRobotics.Transport;

namespace UniversalRobotics.Control
{
    /// <summary>
    /// Listens on a TCP port for "request_program" from the robot's External Control URCap.
    /// When the robot connects and sends the trigger string, the pre-configured URScript is sent back.
    /// Replaces the C++ <c>ScriptSender</c> class.
    /// </summary>
    public sealed class ScriptSender : IDisposable
    {
        private static readonly byte[] RequestTrigger = Encoding.ASCII.GetBytes("request_program");

        private readonly UrTcpServer _server;
        private readonly ILogger _logger;
        private string _scriptContent = string.Empty;

        /// <summary>Gets the port this sender listens on.</summary>
        public int Port { get; }

        /// <summary>
        /// Creates a script sender on the specified port.
        /// </summary>
        /// <param name="port">Port to listen on (default 50002).</param>
        /// <param name="logger">Optional logger.</param>
        public ScriptSender(int port = 50002, ILogger? logger = null)
        {
            Port = port;
            _logger = logger ?? NullLogger.Instance;
            _server = new UrTcpServer(port, maxClients: 1, logger: logger);
            _server.MessageReceived += OnMessageReceived;
        }

        /// <summary>Sets the URScript content that will be sent to the robot on each request.</summary>
        public void SetScript(string scriptContent)
        {
            _scriptContent = scriptContent ?? string.Empty;
            _logger.LogDebug("Script configured ({Length} bytes).", _scriptContent.Length);
        }

        /// <summary>Starts the TCP server.</summary>
        public Task StartAsync(CancellationToken cancellationToken = default)
            => _server.StartAsync(cancellationToken);

        /// <summary>Stops the TCP server.</summary>
        public void Stop() => _server.Stop();

        private async void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            // Check for "request_program" trigger (may be partial; check if bytes start with it)
            bool isRequest = false;
            if (e.Length >= RequestTrigger.Length)
            {
                isRequest = true;
                for (int i = 0; i < RequestTrigger.Length; i++)
                    if (e.Data[i] != RequestTrigger[i]) { isRequest = false; break; }
            }

            if (!isRequest) return;

            _logger.LogInformation("Robot requested script program.");
            var scriptBytes = Encoding.UTF8.GetBytes(_scriptContent);
            var sent = await _server.SendAsync(scriptBytes).ConfigureAwait(false);
            if (sent) _logger.LogDebug("Script sent to robot ({Bytes} bytes).", scriptBytes.Length);
            else _logger.LogWarning("Could not send script: no client connected.");
        }

        /// <inheritdoc />
        public void Dispose() => _server.Dispose();
    }
}
