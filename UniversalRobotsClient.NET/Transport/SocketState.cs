// Licensed under the Apache License, Version 2.0

namespace UniversalRobotics.Transport
{
    /// <summary>
    /// Represents the connection state of a <see cref="UrTcpClient"/> socket.
    /// </summary>
    public enum SocketState
    {
        /// <summary>Socket is initialized but not yet connected, or connection setup failed.</summary>
        Invalid,
        /// <summary>Socket is actively connected and ready for data transfer.</summary>
        Connected,
        /// <summary>Socket was intentionally disconnected.</summary>
        Disconnected,
        /// <summary>The remote peer closed the connection.</summary>
        Closed
    }
}
