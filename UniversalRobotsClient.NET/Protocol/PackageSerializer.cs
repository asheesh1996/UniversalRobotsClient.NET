// Licensed under the Apache License, Version 2.0

using System;
using System.Net;
using System.Text;

namespace UniversalRobotics.Protocol
{
    /// <summary>
    /// Serializes values into a byte buffer in network byte order (big-endian).
    /// Replaces the C++ <c>PackageSerializer</c> template class.
    /// </summary>
    public static class PackageSerializer
    {
        // ─── Integer serialization ────────────────────────────────────────────────

        /// <summary>Writes a big-endian <see cref="short"/> (2 bytes) at <paramref name="offset"/> and advances it by 2.</summary>
        public static void WriteInt16(byte[] buffer, ref int offset, short value)
        {
            short net = (short)IPAddress.HostToNetworkOrder(value);
            var bytes = BitConverter.GetBytes(net);
            Buffer.BlockCopy(bytes, 0, buffer, offset, 2);
            offset += 2;
        }

        /// <summary>Writes a big-endian <see cref="ushort"/> (2 bytes) at <paramref name="offset"/> and advances it by 2.</summary>
        public static void WriteUInt16(byte[] buffer, ref int offset, ushort value)
            => WriteInt16(buffer, ref offset, (short)value);

        /// <summary>Writes a big-endian <see cref="int"/> (4 bytes) at <paramref name="offset"/> and advances it by 4.</summary>
        public static void WriteInt32(byte[] buffer, ref int offset, int value)
        {
            int net = IPAddress.HostToNetworkOrder(value);
            var bytes = BitConverter.GetBytes(net);
            Buffer.BlockCopy(bytes, 0, buffer, offset, 4);
            offset += 4;
        }

        /// <summary>Writes a big-endian <see cref="uint"/> (4 bytes).</summary>
        public static void WriteUInt32(byte[] buffer, ref int offset, uint value)
            => WriteInt32(buffer, ref offset, (int)value);

        /// <summary>Writes a big-endian <see cref="long"/> (8 bytes) at <paramref name="offset"/> and advances it by 8.</summary>
        public static void WriteInt64(byte[] buffer, ref int offset, long value)
        {
            long net = IPAddress.HostToNetworkOrder(value);
            var bytes = BitConverter.GetBytes(net);
            Buffer.BlockCopy(bytes, 0, buffer, offset, 8);
            offset += 8;
        }

        /// <summary>Writes a big-endian <see cref="ulong"/> (8 bytes).</summary>
        public static void WriteUInt64(byte[] buffer, ref int offset, ulong value)
            => WriteInt64(buffer, ref offset, (long)value);

        /// <summary>Writes a single byte at <paramref name="offset"/> and advances it by 1.</summary>
        public static void WriteByte(byte[] buffer, ref int offset, byte value)
        {
            buffer[offset++] = value;
        }

        /// <summary>Writes a boolean as a single byte (1 = true, 0 = false).</summary>
        public static void WriteBool(byte[] buffer, ref int offset, bool value)
            => WriteByte(buffer, ref offset, value ? (byte)1 : (byte)0);

        // ─── Floating-point serialization ─────────────────────────────────────────

        /// <summary>
        /// Writes a big-endian IEEE 754 double (8 bytes) at <paramref name="offset"/> and advances it by 8.
        /// The double is bit-cast to a long, then written in network byte order.
        /// </summary>
        public static void WriteDouble(byte[] buffer, ref int offset, double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            WriteInt64(buffer, ref offset, bits);
        }

        /// <summary>Writes a big-endian IEEE 754 float (4 bytes) at <paramref name="offset"/> and advances it by 4.</summary>
        public static void WriteFloat(byte[] buffer, ref int offset, float value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            Buffer.BlockCopy(bytes, 0, buffer, offset, 4);
            offset += 4;
        }

        // ─── String serialization ────────────────────────────────────────────────

        /// <summary>Writes a UTF-8 string at <paramref name="offset"/> and advances it by the string's byte length.</summary>
        public static void WriteString(byte[] buffer, ref int offset, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
            offset += bytes.Length;
        }

        // ─── Vector serialization ────────────────────────────────────────────────

        /// <summary>Writes 6 big-endian doubles (6 × 8 bytes).</summary>
        public static void WriteVector6d(byte[] buffer, ref int offset, double[] values)
        {
            if (values.Length < 6) throw new ArgumentException("Expected 6 values.", nameof(values));
            for (int i = 0; i < 6; i++) WriteDouble(buffer, ref offset, values[i]);
        }

        /// <summary>Writes 3 big-endian doubles (3 × 8 bytes).</summary>
        public static void WriteVector3d(byte[] buffer, ref int offset, double[] values)
        {
            if (values.Length < 3) throw new ArgumentException("Expected 3 values.", nameof(values));
            for (int i = 0; i < 3; i++) WriteDouble(buffer, ref offset, values[i]);
        }

        // ─── Convenience: write to a fresh buffer ─────────────────────────────────

        /// <summary>Serializes a single big-endian <see cref="int"/> into a 4-byte array.</summary>
        public static byte[] Int32ToBytes(int value)
        {
            var buf = new byte[4];
            int offset = 0;
            WriteInt32(buf, ref offset, value);
            return buf;
        }

        /// <summary>Serializes a single big-endian <see cref="ushort"/> into a 2-byte array.</summary>
        public static byte[] UInt16ToBytes(ushort value)
        {
            var buf = new byte[2];
            int offset = 0;
            WriteUInt16(buf, ref offset, value);
            return buf;
        }
    }
}
