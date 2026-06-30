// Licensed under the Apache License, Version 2.0

using System;
using System.Net;
using System.Text;

namespace UniversalRobotics.Protocol
{
    /// <summary>
    /// Parses a byte buffer in network byte order (big-endian), advancing an internal cursor.
    /// Replaces the C++ <c>BinParser</c> class.  All multi-byte integers are read as big-endian,
    /// matching the UR protocol wire format.
    /// </summary>
    public sealed class BinaryParser
    {
        private readonly byte[] _buffer;
        private int _position;
        private readonly int _end;

        /// <summary>Initializes a parser over the entire provided buffer.</summary>
        public BinaryParser(byte[] buffer) : this(buffer, 0, buffer.Length) { }

        /// <summary>Initializes a parser over a slice of the buffer.</summary>
        public BinaryParser(byte[] buffer, int offset, int length)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _position = offset;
            _end = offset + length;
        }

        /// <summary>Gets the number of bytes remaining to be parsed.</summary>
        public int RemainingBytes => _end - _position;

        /// <summary>Returns <see langword="true"/> when there are no more bytes to parse.</summary>
        public bool IsEmpty => _position >= _end;

        /// <summary>Creates a sub-parser that spans the next <paramref name="length"/> bytes and advances this parser.</summary>
        public BinaryParser CreateSubParser(int length)
        {
            if (length > RemainingBytes)
                throw new InvalidOperationException($"Cannot create sub-parser of {length} bytes; only {RemainingBytes} remain.");
            var sub = new BinaryParser(_buffer, _position, length);
            _position += length;
            return sub;
        }

        // ─── Primitive readers ────────────────────────────────────────────────────

        /// <summary>Reads a single <see cref="bool"/> (1 byte; non-zero = true).</summary>
        public bool ReadBool() => _buffer[Advance(1)] != 0;

        /// <summary>Reads a single unsigned byte.</summary>
        public byte ReadByte() => _buffer[Advance(1)];

        /// <summary>Reads a signed byte.</summary>
        public sbyte ReadSByte() => (sbyte)_buffer[Advance(1)];

        /// <summary>Reads a big-endian <see cref="short"/> (2 bytes).</summary>
        public short ReadInt16()
        {
            int pos = Advance(2);
            return (short)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(_buffer, pos));
        }

        /// <summary>Reads a big-endian <see cref="ushort"/> (2 bytes).</summary>
        public ushort ReadUInt16()
        {
            int pos = Advance(2);
            return (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(_buffer, pos));
        }

        /// <summary>Reads a big-endian <see cref="int"/> (4 bytes).</summary>
        public int ReadInt32()
        {
            int pos = Advance(4);
            return IPAddress.NetworkToHostOrder(BitConverter.ToInt32(_buffer, pos));
        }

        /// <summary>Reads a big-endian <see cref="uint"/> (4 bytes).</summary>
        public uint ReadUInt32() => (uint)ReadInt32();

        /// <summary>Reads a big-endian <see cref="long"/> (8 bytes).</summary>
        public long ReadInt64()
        {
            int pos = Advance(8);
            return IPAddress.NetworkToHostOrder(BitConverter.ToInt64(_buffer, pos));
        }

        /// <summary>Reads a big-endian <see cref="ulong"/> (8 bytes).</summary>
        public ulong ReadUInt64() => (ulong)ReadInt64();

        /// <summary>
        /// Reads a big-endian IEEE 754 double (8 bytes).
        /// The wire format stores the raw bits of the double in network (big-endian) byte order.
        /// </summary>
        public double ReadDouble()
        {
            long bits = ReadInt64();
            return BitConverter.Int64BitsToDouble(bits);
        }

        /// <summary>Reads a big-endian IEEE 754 single-precision float (4 bytes).</summary>
        public float ReadFloat()
        {
            int pos = Advance(4);
            if (!BitConverter.IsLittleEndian)
                return BitConverter.ToSingle(_buffer, pos);

            // Convert big-endian bytes to little-endian before interpreting as float
            var tmp = new byte[4];
            tmp[0] = _buffer[pos + 3];
            tmp[1] = _buffer[pos + 2];
            tmp[2] = _buffer[pos + 1];
            tmp[3] = _buffer[pos];
            return BitConverter.ToSingle(tmp, 0);
        }

        /// <summary>Reads a fixed-length ASCII/UTF-8 string of <paramref name="length"/> bytes.</summary>
        public string ReadString(int length)
        {
            int pos = Advance(length);
            return Encoding.UTF8.GetString(_buffer, pos, length);
        }

        /// <summary>Reads a null-terminated string from the remaining bytes.</summary>
        public string ReadNullTerminatedString()
        {
            int start = _position;
            while (_position < _end && _buffer[_position] != 0)
                _position++;
            string s = Encoding.UTF8.GetString(_buffer, start, _position - start);
            if (_position < _end) _position++; // consume null
            return s;
        }

        // ─── Composite readers ────────────────────────────────────────────────────

        /// <summary>Reads 3 big-endian doubles into a <c>double[3]</c> array.</summary>
        public double[] ReadVector3d() => new[] { ReadDouble(), ReadDouble(), ReadDouble() };

        /// <summary>Reads 6 big-endian doubles into a <c>double[6]</c> array.</summary>
        public double[] ReadVector6d() => new[]
        {
            ReadDouble(), ReadDouble(), ReadDouble(),
            ReadDouble(), ReadDouble(), ReadDouble()
        };

        /// <summary>Reads 6 big-endian int32 values into an <c>int[6]</c> array.</summary>
        public int[] ReadVector6Int32() => new[]
        {
            ReadInt32(), ReadInt32(), ReadInt32(),
            ReadInt32(), ReadInt32(), ReadInt32()
        };

        /// <summary>Reads 6 big-endian uint32 values into a <c>uint[6]</c> array.</summary>
        public uint[] ReadVector6UInt32() => new[]
        {
            ReadUInt32(), ReadUInt32(), ReadUInt32(),
            ReadUInt32(), ReadUInt32(), ReadUInt32()
        };

        /// <summary>Reads all remaining bytes into a new array.</summary>
        public byte[] ReadRemaining()
        {
            var remaining = new byte[RemainingBytes];
            Array.Copy(_buffer, _position, remaining, 0, remaining.Length);
            _position = _end;
            return remaining;
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private int Advance(int count)
        {
            if (count > RemainingBytes)
                throw new InvalidOperationException(
                    $"Cannot read {count} byte(s) from parser; only {RemainingBytes} remain.");
            int pos = _position;
            _position += count;
            return pos;
        }
    }
}
