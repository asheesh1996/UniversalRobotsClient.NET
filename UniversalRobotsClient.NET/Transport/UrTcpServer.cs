// Licensed under the Apache License, Version 2.0

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalRobotics.Exceptions;

namespace UniversalRobotics.Transport
{
    /// <summary>
    /// Event args carrying data received from a connected TCP client.
    /// </summary>
    public sealed class MessageReceivedEventArgs : EventArgs
    {
        /// <summary>Gets the data buffer (valid only during the event handler call).</summary>
        public byte[] Data { get; }
        /// <summary>Gets the number of valid bytes in <see cref="Data"/>.</summary>
        public int Length { get; }
        /// <summary>Gets the remote endpoint that sent the data.</summary>
        public EndPoint? RemoteEndPoint { get; }

        internal MessageReceivedEventArgs(byte[] data, int length, EndPoint? remoteEndPoint)
        {
            Data = data;
            Length = length;
            RemoteEndPoint = remoteEndPoint;
        }
    }

    /// <summary>
    /// Event args raised when a client connects to or disconnects from the server.
    /// </summary>
    public sealed class ClientConnectionEventArgs : EventArgs
    {
        /// <summary>Gets the remote endpoint of the client.</summary>
        public EndPoint? RemoteEndPoint { get; }

        internal ClientConnectionEventArgs(EndPoint? remoteEndPoint)
        {
            RemoteEndPoint = remoteEndPoint;
        }
    }

    /// <summary>
    /// A TCP server that accepts incoming connections from the robot controller.
    /// Used by <c>ReverseInterface</c>, <c>TrajectoryPointInterface</c>, <c>ScriptCommandInterface</c>,
    /// and <c>ScriptSender</c> to listen for robot-initiated connections.
    /// Replaces the C++ <c>TCPServer</c> class.
    /// </summary>
    public class UrTcpServer : IDisposable
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _acceptTask;
        private TcpClient? _currentClient;
        private readonly object _clientLock = new object();
        private readonly ILogger _logger;
        private readonly int _maxClients;
        private bool _disposed;

        /// <summary>Gets the port on which this server listens.</summary>
        public int Port { get; }

        /// <summary>Raised when a new client connects to the server.</summary>
        public event EventHandler<ClientConnectionEventArgs>? ClientConnected;

        /// <summary>Raised when a connected client disconnects from the server.</summary>
        public event EventHandler<ClientConnectionEventArgs>? ClientDisconnected;

        /// <summary>Raised when data is received from a connected client.</summary>
        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

        /// <summary>
        /// Creates a new <see cref="UrTcpServer"/> bound to the given port.
        /// </summary>
        /// <param name="port">Port to listen on.</param>
        /// <param name="maxClients">Maximum simultaneous clients (default 1; robot only needs one).</param>
        /// <param name="logger">Optional logger.</param>
        public UrTcpServer(int port, int maxClients = 1, ILogger? logger = null)
        {
            Port = port;
            _maxClients = maxClients;
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>
        /// Starts the server and begins accepting client connections in the background.
        /// </summary>
        /// <param name="cancellationToken">Token used to stop the server.</param>
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();

            _logger.LogInformation("TCP server listening on port {Port}.", Port);

            _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
            return Task.CompletedTask;
        }

        /// <summary>Stops the server and closes all client connections.</summary>
        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            CloseCurrentClient();
        }

        /// <summary>
        /// Sends data to the currently connected client, if any.
        /// </summary>
        public async Task<bool> SendAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            TcpClient? client;
            lock (_clientLock) client = _currentClient;

            if (client == null || !client.Connected) return false;

            try
            {
                var stream = client.GetStream();
                await stream.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error writing to client.");
                return false;
            }
        }

        /// <summary>Returns <see langword="true"/> if at least one client is currently connected.</summary>
        public bool HasClient
        {
            get { lock (_clientLock) return _currentClient?.Connected == true; }
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener!.AcceptTcpClientAsync().ConfigureAwait(false);
                    client.NoDelay = true;

                    lock (_clientLock)
                    {
                        // Close any existing client if max is 1
                        if (_maxClients == 1) CloseCurrentClientNoLock();
                        _currentClient = client;
                    }

                    var ep = client.Client.RemoteEndPoint;
                    _logger.LogInformation("Client connected from {Endpoint}.", ep);
                    ClientConnected?.Invoke(this, new ClientConnectionEventArgs(ep));

                    _ = Task.Run(() => ReadLoopAsync(client, ep, ct), ct);
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Error in accept loop.");
                }
            }
        }

        private async Task ReadLoopAsync(TcpClient client, EndPoint? ep, CancellationToken ct)
        {
            const int bufferSize = 4096;
            var buffer = new byte[bufferSize];

            try
            {
                var stream = client.GetStream();
                while (!ct.IsCancellationRequested && client.Connected)
                {
                    int read = await stream.ReadAsync(buffer, 0, bufferSize, ct).ConfigureAwait(false);
                    if (read == 0) break;  // connection closed

                    MessageReceived?.Invoke(this, new MessageReceivedEventArgs(buffer, read, ep));
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogDebug(ex, "Read loop ended for {Endpoint}.", ep);
            }
            finally
            {
                client.Close();
                lock (_clientLock)
                    if (ReferenceEquals(_currentClient, client)) _currentClient = null;

                _logger.LogInformation("Client disconnected from {Endpoint}.", ep);
                ClientDisconnected?.Invoke(this, new ClientConnectionEventArgs(ep));
            }
        }

        private void CloseCurrentClient()
        {
            lock (_clientLock) CloseCurrentClientNoLock();
        }

        private void CloseCurrentClientNoLock()
        {
            _currentClient?.Close();
            _currentClient = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _cts?.Dispose();
        }
    }
}
