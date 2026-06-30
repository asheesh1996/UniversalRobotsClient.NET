// Licensed under the Apache License, Version 2.0

using System;
using System.Net;
using UniversalRobotics.Protocol;

namespace UniversalRobotics.Rtde.Packets
{
    /// <summary>
    /// Identifies the type of an RTDE packet.  Values are the ASCII character codes used by UR.
    /// </summary>
    public enum RtdePackageType : byte
    {
        /// <summary>Protocol version negotiation request (ASCII 'V' = 86).</summary>
        RequestProtocolVersion = 86,
        /// <summary>URControl version query (ASCII 'v' = 118).</summary>
        GetUrControlVersion = 118,
        /// <summary>Text message from the controller (ASCII 'M' = 77).</summary>
        TextMessage = 77,
        /// <summary>Cyclic data package (ASCII 'U' = 85).</summary>
        DataPackage = 85,
        /// <summary>Setup output recipe response (ASCII 'O' = 79).</summary>
        ControlPackageSetupOutputs = 79,
        /// <summary>Setup input recipe response (ASCII 'I' = 73).</summary>
        ControlPackageSetupInputs = 73,
        /// <summary>Start data streaming (ASCII 'S' = 83).</summary>
        ControlPackageStart = 83,
        /// <summary>Pause data streaming (ASCII 'P' = 80).</summary>
        ControlPackagePause = 80
    }

    /// <summary>
    /// Abstract base for all RTDE packages.  Carries the <see cref="PackageType"/> discriminator
    /// and implements the common RTDE header (2-byte size + 1-byte type).
    /// </summary>
    public abstract class RtdePackage : IUrPackage<RtdePackageHeader>
    {
        /// <summary>Gets the type code of this RTDE package.</summary>
        public RtdePackageType PackageType { get; }

        /// <summary>Initializes the package with its type.</summary>
        protected RtdePackage(RtdePackageType type)
        {
            PackageType = type;
        }

        /// <inheritdoc />
        public abstract bool ParseFrom(BinaryParser parser);

        /// <inheritdoc />
        public abstract string ToDebugString();

        /// <summary>
        /// Serializes this package — including the 3-byte RTDE header — into a byte array ready to
        /// send over the wire.
        /// </summary>
        public byte[] Serialize()
        {
            var payload = SerializePayload();
            int totalSize = 3 + payload.Length; // 2-byte size + 1-byte type + payload
            var buffer = new byte[totalSize];
            int offset = 0;
            PackageSerializer.WriteUInt16(buffer, ref offset, (ushort)totalSize);
            PackageSerializer.WriteByte(buffer, ref offset, (byte)PackageType);
            Buffer.BlockCopy(payload, 0, buffer, offset, payload.Length);
            return buffer;
        }

        /// <summary>
        /// Derived classes implement this to produce their payload bytes (without the 3-byte header).
        /// </summary>
        protected virtual byte[] SerializePayload() => Array.Empty<byte>();
    }

    /// <summary>
    /// Represents the 3-byte RTDE packet header (2-byte big-endian size + 1-byte type).
    /// </summary>
    public readonly struct RtdePackageHeader
    {
        /// <summary>Total packet size in bytes, including the 3-byte header.</summary>
        public ushort Size { get; }
        /// <summary>Type of the RTDE packet.</summary>
        public RtdePackageType Type { get; }

        /// <summary>Creates an RTDE header from the raw 3-byte prefix.</summary>
        public RtdePackageHeader(ushort size, RtdePackageType type)
        {
            Size = size;
            Type = type;
        }

        /// <summary>Parses a header from the first 3 bytes of a buffer.</summary>
        public static RtdePackageHeader FromBytes(byte[] buffer)
        {
            ushort size = (ushort)((buffer[0] << 8) | buffer[1]);
            var type = (RtdePackageType)buffer[2];
            return new RtdePackageHeader(size, type);
        }
    }
}
