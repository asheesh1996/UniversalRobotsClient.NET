// Tests for BinaryParser and PackageSerializer (binary protocol correctness)
// Mirrors C++ test_bin_parser.cpp

using System;
using System.Net;
using UniversalRobotics.Protocol;

namespace UniversalRobotics.Tests
{
    public class BinaryParserTests
    {
        // ─── Bool ────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(new byte[] { 1 }, true)]
        [InlineData(new byte[] { 0 }, false)]
        [InlineData(new byte[] { 255 }, true)]
        public void ReadBool_ReturnsExpectedValue(byte[] buffer, bool expected)
        {
            var parser = new BinaryParser(buffer);
            Assert.Equal(expected, parser.ReadBool());
        }

        // ─── Byte ────────────────────────────────────────────────────────────────

        [Fact]
        public void ReadByte_ReturnsCorrectValue()
        {
            var parser = new BinaryParser(new byte[] { 42 });
            Assert.Equal(42, parser.ReadByte());
        }

        // ─── Int16 (big-endian) ───────────────────────────────────────────────────

        [Fact]
        public void ReadInt16_BigEndian_ReturnsCorrectValue()
        {
            // 0x0100 in big-endian = 256
            var parser = new BinaryParser(new byte[] { 0x01, 0x00 });
            Assert.Equal(256, parser.ReadInt16());
        }

        [Fact]
        public void ReadInt16_NegativeValue()
        {
            // -1 in big-endian int16 = 0xFF 0xFF
            var parser = new BinaryParser(new byte[] { 0xFF, 0xFF });
            Assert.Equal(-1, parser.ReadInt16());
        }

        // ─── UInt16 ───────────────────────────────────────────────────────────────

        [Fact]
        public void ReadUInt16_BigEndian_ReturnsCorrectValue()
        {
            // 0xFFFF = 65535
            var parser = new BinaryParser(new byte[] { 0xFF, 0xFF });
            Assert.Equal(65535, parser.ReadUInt16());
        }

        // ─── Int32 ────────────────────────────────────────────────────────────────

        [Fact]
        public void ReadInt32_BigEndian_ReturnsCorrectValue()
        {
            // 1_000_000 = 0x000F4240
            var parser = new BinaryParser(new byte[] { 0x00, 0x0F, 0x42, 0x40 });
            Assert.Equal(1_000_000, parser.ReadInt32());
        }

        [Fact]
        public void ReadInt32_NegativeValue()
        {
            // -1 in big-endian int32 = 0xFF 0xFF 0xFF 0xFF
            var parser = new BinaryParser(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
            Assert.Equal(-1, parser.ReadInt32());
        }

        // ─── Double ───────────────────────────────────────────────────────────────

        [Fact]
        public void ReadDouble_WrittenBySerializer_RoundTrips()
        {
            double originalValue = 3.14159265358979;
            var buf = new byte[8];
            int offset = 0;
            PackageSerializer.WriteDouble(buf, ref offset, originalValue);

            var parser = new BinaryParser(buf);
            double parsed = parser.ReadDouble();
            Assert.Equal(originalValue, parsed, precision: 15);
        }

        [Fact]
        public void ReadDouble_Zero_ReturnsZero()
        {
            var buf = new byte[8]; // all zeros
            var parser = new BinaryParser(buf);
            Assert.Equal(0.0, parser.ReadDouble());
        }

        // ─── Vector6d ────────────────────────────────────────────────────────────

        [Fact]
        public void ReadVector6d_RoundTrips()
        {
            double[] original = { 1.1, 2.2, 3.3, 4.4, 5.5, 6.6 };
            var buf = new byte[48];
            int offset = 0;
            PackageSerializer.WriteVector6d(buf, ref offset, original);

            var parser = new BinaryParser(buf);
            var result = parser.ReadVector6d();

            for (int i = 0; i < 6; i++)
                Assert.Equal(original[i], result[i], precision: 12);
        }

        // ─── Sub-parser ───────────────────────────────────────────────────────────

        [Fact]
        public void CreateSubParser_ReadsCorrectSlice()
        {
            var buf = new byte[] { 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x0A };
            var parser = new BinaryParser(buf);

            // Read first int32 = 5
            int first = parser.ReadInt32();
            Assert.Equal(5, first);

            // Sub-parser over next 4 bytes (containing 0x0000000A = 10)
            var sub = parser.CreateSubParser(4);
            int second = sub.ReadInt32();
            Assert.Equal(10, second);

            // Original parser should be exhausted
            Assert.Equal(0, parser.RemainingBytes);
        }

        [Fact]
        public void RemainingBytes_DecreasesAsRead()
        {
            var parser = new BinaryParser(new byte[10]);
            Assert.Equal(10, parser.RemainingBytes);
            parser.ReadInt32();
            Assert.Equal(6, parser.RemainingBytes);
        }

        // ─── Boundary conditions ─────────────────────────────────────────────────

        [Fact]
        public void ReadBeyondEnd_ThrowsInvalidOperationException()
        {
            var parser = new BinaryParser(new byte[] { 0x01, 0x02 });
            Assert.Throws<InvalidOperationException>(() => parser.ReadInt32());
        }

        [Fact]
        public void EmptyBuffer_IsEmpty()
        {
            var parser = new BinaryParser(Array.Empty<byte>());
            Assert.True(parser.IsEmpty);
        }
    }

    public class PackageSerializerTests
    {
        [Fact]
        public void WriteInt32_ThenReadBack_Matches()
        {
            int value = 123456;
            var buf = new byte[4];
            int offset = 0;
            PackageSerializer.WriteInt32(buf, ref offset, value);
            Assert.Equal(4, offset);

            var parser = new BinaryParser(buf);
            Assert.Equal(value, parser.ReadInt32());
        }

        [Fact]
        public void WriteUInt16_ThenReadBack_Matches()
        {
            ushort value = 30004;
            var buf = new byte[2];
            int offset = 0;
            PackageSerializer.WriteUInt16(buf, ref offset, value);

            var parser = new BinaryParser(buf);
            Assert.Equal(value, parser.ReadUInt16());
        }

        [Fact]
        public void WriteDouble_ThenReadBack_Matches()
        {
            double value = -1.234567890123456;
            var buf = new byte[8];
            int offset = 0;
            PackageSerializer.WriteDouble(buf, ref offset, value);

            var parser = new BinaryParser(buf);
            Assert.Equal(value, parser.ReadDouble(), precision: 15);
        }

        [Fact]
        public void Int32ToBytes_ProducesBigEndian()
        {
            // 1 in big-endian = 00 00 00 01
            var bytes = PackageSerializer.Int32ToBytes(1);
            Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x01 }, bytes);
        }

        [Fact]
        public void WriteVector6d_OffsetAdvancesBy48()
        {
            double[] vals = { 1, 2, 3, 4, 5, 6 };
            var buf = new byte[48];
            int offset = 0;
            PackageSerializer.WriteVector6d(buf, ref offset, vals);
            Assert.Equal(48, offset);
        }
    }
}
