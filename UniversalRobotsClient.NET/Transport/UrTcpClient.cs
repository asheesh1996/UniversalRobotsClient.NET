// Licensed under the Apache License, Version 2.0

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UniversalRobotics.Exceptions;

namespace UniversalRobotics.Transport
{
    /// <summary>
    /// Managed TCP client that wraps <see cref="TcpClient"/> with retry logic, read-exact semantics,
    /// and async-first I/O.  Replaces the C++ <c>TCPSocket</c> class.
    /// </summary>
    public class UrTcpClient : IDisposable
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private volatile SocketState _state = SocketState.Invalid;
        private readonly object _lock = new object();
        private bool _disposed;
        private CancellationTokenSource? _connectionCancellation;

        /// <summary>Default time to wait between connection attempts (10 seconds, matching C++ default).</summary>
        public static readonly TimeSpan DefaultReconnectionTime = TimeSpan.FromSeconds(10);

        /// <summary>Gets the current state of the underlying socket.</summary>
        public SocketState State => _state;

        /// <summary>Returns <see langword="true"/> when the socket is in the <see cref="SocketState.Connected"/> state.</summary>
        public bool IsConnected => _state == SocketState.Connected;

        /// <summary>
        /// Attempts to connect to the specified host and port, retrying up to <paramref name="maxAttempts"/> times.
        /// </summary>
        /// <param name="host">Hostname or IP address of the robot controller.</param>
        /// <param name="port">TCP port number.</param>
        /// <param name="maxAttempts">Maximum number of connection attempts. 0 means retry indefinitely.</param>
        /// <param name="reconnectionDelay">Delay between connection attempts.</param>
        /// <param name="cancellationToken">Token to cancel the connect loop.</param>
        /// <returns><see langword="true"/> on success.</returns>
        /// <exception cref="CommunicationException">Thrown when maxAttempts &gt; 0 and all attempts fail.</exception>
        public virtual async Task<bool> ConnectAsync(
            string host,
            int port,
            int maxAttempts = 0,
            TimeSpan? reconnectionDelay = null,
            CancellationToken cancellationToken = default)
        {
            var delay = reconnectionDelay ?? DefaultReconnectionTime;
            CancellationTokenSource connectionCancellation;
            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(UrTcpClient));
                _connectionCancellation?.Cancel();
                _connectionCancellation = connectionCancellation = new CancellationTokenSource();
            }
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, connectionCancellation.Token);
            var token = linkedCancellation.Token;
            int attempt = 0;

            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    attempt++;
                    var client = new TcpClient();
                    bool connected = false;
                    try
                    {
                        client.NoDelay = true;
                        using (token.Register(client.Close))
                            await client.ConnectAsync(host, port).ConfigureAwait(false);

                        lock (_lock)
                        {
                            token.ThrowIfCancellationRequested();
                            _tcpClient?.Close();
                            _tcpClient = client;
                            _stream = client.GetStream();
                            _state = SocketState.Connected;
                            connected = true;
                        }
                        return true;
                    }
                    catch (SocketException) when (token.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(token);
                    }
                    catch (ObjectDisposedException) when (token.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(token);
                    }
                    catch (SocketException) when (maxAttempts == 0 || attempt < maxAttempts)
                    {
                        await Task.Delay(delay, token).ConfigureAwait(false);
                    }
                    catch (SocketException ex)
                    {
                        _state = SocketState.Invalid;
                        throw new CommunicationException(
                            $"Failed to connect to {host}:{port} after {attempt} attempt(s).", ex);
                    }
                    finally
                    {
                        if (!connected) client.Dispose();
                    }
                }
            }
            finally
            {
                lock (_lock)
                {
                    if (ReferenceEquals(_connectionCancellation, connectionCancellation))
                        _connectionCancellation = null;
                    connectionCancellation.Dispose();
                }
            }
        }

        /// <summary>Closes the socket and transitions to the <see cref="SocketState.Disconnected"/> state.</summary>
        public virtual void Disconnect()
        {
            lock (_lock)
            {
                _connectionCancellation?.Cancel();
                _stream?.Close();
                _tcpClient?.Close();
                _stream = null;
                _tcpClient = null;
                _state = SocketState.Disconnected;
            }
        }

        /// <summary>
        /// Reads exactly <paramref name="count"/> bytes from the stream, blocking until all bytes arrive.
        /// </summary>
        /// <param name="buffer">Target buffer. Must have capacity for at least <paramref name="offset"/> + <paramref name="count"/> bytes.</param>
        /// <param name="offset">Starting offset in <paramref name="buffer"/>.</param>
        /// <param name="count">Number of bytes to read.</param>
        /// <param name="cancellationToken">Token to cancel the read.</param>
        /// <exception cref="CommunicationException">Thrown when the connection is closed before all bytes are received.</exception>
        public virtual async Task ReadExactlyAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken = default)
        {
            var stream = GetStream();
            int remaining = count;
            int position = offset;

            while (remaining > 0)
            {
                int read = await stream.ReadAsync(buffer, position, remaining, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    _state = SocketState.Closed;
                    throw new CommunicationException("Connection closed by remote host before all data was received.");
                }
                position += read;
                remaining -= read;
            }
        }

        /// <summary>Reads a single byte from the stream.</summary>
        public virtual async Task<byte> ReadByteAsync(CancellationToken cancellationToken = default)
        {
            var buf = new byte[1];
            await ReadExactlyAsync(buf, 0, 1, cancellationToken).ConfigureAwait(false);
            return buf[0];
        }

        /// <summary>Writes all bytes in <paramref name="buffer"/> to the stream.</summary>
        public virtual async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            var stream = GetStream();
            await stream.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Writes an entire byte array to the stream.</summary>
        public virtual Task WriteAsync(byte[] buffer, CancellationToken cancellationToken = default)
            => WriteAsync(buffer, 0, buffer.Length, cancellationToken);

        /// <summary>Sets the receive timeout on the underlying socket.</summary>
        public void SetReceiveTimeout(TimeSpan timeout)
        {
            lock (_lock)
            {
                if (_tcpClient?.Client != null)
                    _tcpClient.ReceiveTimeout = (int)timeout.TotalMilliseconds;
            }
        }

        /// <summary>Gets the local IP address of the active connection, or null if not connected.</summary>
        public string? LocalIpAddress
        {
            get
            {
                try
                {
                    return (_tcpClient?.Client?.LocalEndPoint as IPEndPoint)?.Address.ToString();
                }
                catch
                {
                    return null;
                }
            }
        }

        private NetworkStream GetStream()
        {
            lock (_lock)
            {
                if (_stream == null || !IsConnected)
                    throw new NotConnectedException();
                return _stream;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disconnect();
            _stream?.Dispose();
            _tcpClient?.Dispose();
        }


    }
}
