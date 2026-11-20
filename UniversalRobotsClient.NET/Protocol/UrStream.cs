// Licensed under the Apache License, Version 2.0

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalRobotics.Exceptions;
using UniversalRobotics.Transport;

namespace UniversalRobotics.Protocol
{
    /// <summary>
    /// Abstraction over a TCP connection to a UR robot, providing length-prefixed packet read/write
    /// semantics.  Replaces the C++ <c>URStream&lt;T&gt;</c> template class.
    /// </summary>
    /// <typeparam name="TPackage">The package type read from this stream.</typeparam>
    public class UrStream<TPackage> : IDisposable where TPackage : class
    {
        private readonly UrTcpClient _client;
        private readonly IPacketFramer _framer;
        private readonly ILogger _logger;

        /// <summary>Gets the remote host name or IP address.</summary>
        public string Host { get; }
        /// <summary>Gets the remote port.</summary>
        public int Port { get; }

        /// <summary>Maximum accepted packet length, including the header. Defaults to 1 MiB.</summary>
        public int MaxPacketLength { get; set; } = 1024 * 1024;

        /// <summary>Gets the current socket state.</summary>
        public SocketState State => _client.State;

        /// <summary>Returns <see langword="true"/> when the stream is connected.</summary>
        public bool IsConnected => _client.IsConnected;

        /// <summary>Returns the local IP address of the current connection.</summary>
        public string? LocalIpAddress => _client.LocalIpAddress;

        /// <summary>
        /// Creates a new stream to the given host and port.
        /// </summary>
        /// <param name="host">Robot controller hostname or IP.</param>
        /// <param name="port">TCP port number.</param>
        /// <param name="framer">Strategy for reading packet boundaries from the wire.</param>
        /// <param name="logger">Optional logger.</param>
        public UrStream(string host, int port, IPacketFramer framer, ILogger? logger = null)
        {
            Host = host;
            Port = port;
            _framer = framer ?? throw new ArgumentNullException(nameof(framer));
            _client = new UrTcpClient();
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>Opens the connection to the robot.</summary>
        public Task<bool> ConnectAsync(
            int maxAttempts = 0,
            TimeSpan? reconnectionDelay = null,
            CancellationToken cancellationToken = default)
            => _client.ConnectAsync(Host, Port, maxAttempts, reconnectionDelay, cancellationToken);

        /// <summary>Closes the connection.</summary>
        public void Disconnect()
        {
            _logger.LogDebug("Disconnecting from {Host}:{Port}.", Host, Port);
            _client.Disconnect();
        }

        /// <summary>Returns <see langword="true"/> when the underlying socket is closed.</summary>
        public bool IsClosed => _client.State == SocketState.Closed;

        /// <summary>
        /// Reads a full UR protocol packet from the stream.  The packet header is read first to
        /// determine the total length, then the payload bytes are read.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The number of bytes read (full packet including header).</returns>
        /// <exception cref="CommunicationException">On connection errors.</exception>
        public async Task<(byte[] buffer, int bytesRead)> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            // 1. Read the header bytes to determine the packet length
            int headerLength = _framer.HeaderLength;
            var headerBuf = new byte[headerLength];
            await _client.ReadExactlyAsync(headerBuf, 0, headerLength, cancellationToken).ConfigureAwait(false);

            int totalLength = _framer.GetPacketLength(headerBuf);
            if (totalLength < headerLength || totalLength > MaxPacketLength)
                throw new CommunicationException($"Malformed packet: reported size {totalLength} is outside [{headerLength}, {MaxPacketLength}].");

            // 2. Allocate buffer and copy header bytes already read
            var packetBuf = new byte[totalLength];
            Array.Copy(headerBuf, packetBuf, headerLength);

            // 3. Read the remaining payload bytes
            int payloadLength = totalLength - headerLength;
            if (payloadLength > 0)
                await _client.ReadExactlyAsync(packetBuf, headerLength, payloadLength, cancellationToken).ConfigureAwait(false);

            return (packetBuf, totalLength);
        }

        /// <summary>Writes a raw byte buffer to the stream.</summary>
        public Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
            => _client.WriteAsync(data, cancellationToken);

        /// <summary>Sets the socket receive timeout.</summary>
        public void SetReceiveTimeout(TimeSpan timeout) => _client.SetReceiveTimeout(timeout);

        /// <inheritdoc />
        public void Dispose() => _client.Dispose();
    }

    /// <summary>
    /// Strategy for reading packet length from a header prefix.
    /// Decouples <see cref="UrStream{T}"/> from specific header formats.
    /// </summary>
    public interface IPacketFramer
    {
        /// <summary>Gets the number of bytes in the fixed-size packet header.</summary>
        int HeaderLength { get; }

        /// <summary>
        /// Reads the total packet length (header + payload) from the header bytes.
        /// </summary>
        int GetPacketLength(byte[] headerBytes);
    }

    /// <summary>
    /// Packet framer for the RTDE protocol.
    /// Header: 2-byte big-endian uint16 total size + 1-byte type = 3 bytes.
    /// The size field includes the 3-byte header itself.
    /// </summary>
    public sealed class RtdePacketFramer : IPacketFramer
    {
        /// <inheritdoc />
        public int HeaderLength => 3;

        /// <inheritdoc />
        public int GetPacketLength(byte[] headerBytes)
        {
            if (headerBytes == null) throw new ArgumentNullException(nameof(headerBytes));
            if (headerBytes.Length < HeaderLength) throw new ArgumentException(nameof(headerBytes));
            // Bytes 0-1: big-endian uint16 total packet size (includes header)
            return (headerBytes[0] << 8) | headerBytes[1];
        }
    }

    /// <summary>
    /// Packet framer for the primary/secondary interface protocol.
    /// Header: 4-byte big-endian int32 total size + 1-byte type = 5 bytes.
    /// The size field includes the 4-byte size field itself but not the type byte.
    /// </summary>
    public sealed class PrimaryPacketFramer : IPacketFramer
    {
        /// <inheritdoc />
        public int HeaderLength => 5;

        /// <inheritdoc />
        public int GetPacketLength(byte[] headerBytes)
        {
            if (headerBytes == null) throw new ArgumentNullException(nameof(headerBytes));
            if (headerBytes.Length < HeaderLength) throw new ArgumentException(nameof(headerBytes));
            // Bytes 0-3: big-endian int32 total packet size (includes the 4 size bytes + 1 type byte)
            int size = System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(headerBytes, 0));
            return size; // size already includes the header bytes
        }
    }
}
