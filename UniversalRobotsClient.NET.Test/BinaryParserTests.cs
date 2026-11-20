// Tests for BinaryParser and PackageSerializer (binary protocol correctness)
// Mirrors C++ test_bin_parser.cpp

using System;
using System.Net;
using UniversalRobotics.Protocol;

namespace UniversalRobotics.Tests
{
    public class BinaryParserTests
    {
        [Fact]
        public void Constructor_NullBuffer_ThrowsArgumentNullException()
        {
            Assert.Equal("buffer", Assert.Throws<ArgumentNullException>(() => new BinaryParser(null!)).ParamName);
            Assert.Equal("buffer", Assert.Throws<ArgumentNullException>(() => new BinaryParser(null!, 0, 0)).ParamName);
        }

        [Theory]
        [InlineData(int.MinValue, 0, "offset")]
        [InlineData(int.MaxValue, 0, "offset")]
        [InlineData(4, 1, "length")]
        [InlineData(1, int.MaxValue, "length")]
        [InlineData(0, int.MinValue, "length")]
        public void Constructor_InvalidSlice_ReportsParameter(int offset, int length, string parameter)
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new BinaryParser(new byte[4], offset, length));

            Assert.Equal(parameter, exception.ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(4)]
        public void Constructor_EmptySlice_IsExhausted(int offset)
        {
            var parser = new BinaryParser(new byte[] { 10, 20, 30, 40 }, offset, 0);

            Assert.Equal(0, parser.RemainingBytes);
            Assert.True(parser.IsEmpty);
            Assert.Empty(parser.ReadRemaining());
            Assert.Throws<InvalidOperationException>(() => parser.ReadByte());
            Assert.Equal(0, parser.RemainingBytes);
        }

        [Theory]
        [InlineData(-1, typeof(ArgumentOutOfRangeException))]
        [InlineData(int.MinValue, typeof(ArgumentOutOfRangeException))]
        [InlineData(4, typeof(InvalidOperationException))]
        [InlineData(int.MaxValue, typeof(InvalidOperationException))]
        public void ReadString_InvalidLength_PreservesCursor(int length, Type exceptionType)
        {
            var parser = new BinaryParser(new byte[] { 99, 1, 65, 66, 67, 88 }, 1, 4);
            Assert.Equal(1, parser.ReadByte());

            var exception = Assert.Throws(exceptionType, () => parser.ReadString(length));

            if (exception is ArgumentOutOfRangeException argumentException)
                Assert.Equal("count", argumentException.ParamName);
            Assert.Equal(3, parser.RemainingBytes);
            Assert.False(parser.IsEmpty);
            Assert.Equal("ABC", parser.ReadString(3));
            Assert.True(parser.IsEmpty);
        }

        [Fact]
        public void ReadString_ZeroAndExactLength_RespectSlice()
        {
            var parser = new BinaryParser(new byte[] { 88, 65, 66, 67, 89 }, 1, 3);

            Assert.Equal(string.Empty, parser.ReadString(0));
            Assert.Equal(3, parser.RemainingBytes);
            Assert.Equal("ABC", parser.ReadString(3));
            Assert.Equal(0, parser.RemainingBytes);
            Assert.True(parser.IsEmpty);
            Assert.Equal(string.Empty, parser.ReadString(0));
            Assert.Throws<InvalidOperationException>(() => parser.ReadByte());
        }

        [Fact]
        public void ReadInt32_TruncatedSlice_PreservesCursor()
        {
            var parser = new BinaryParser(new byte[] { 99, 1, 2, 3, 4, 5 }, 1, 3);

            Assert.Throws<InvalidOperationException>(() => parser.ReadInt32());

            Assert.Equal(3, parser.RemainingBytes);
            Assert.False(parser.IsEmpty);
            Assert.Equal(new byte[] { 1, 2, 3 }, parser.ReadRemaining());
            Assert.True(parser.IsEmpty);
        }

        [Theory]
        [InlineData(-1, typeof(ArgumentOutOfRangeException))]
        [InlineData(int.MinValue, typeof(ArgumentOutOfRangeException))]
        [InlineData(4, typeof(InvalidOperationException))]
        [InlineData(int.MaxValue, typeof(InvalidOperationException))]
        public void CreateSubParser_InvalidLength_PreservesCursor(int length, Type exceptionType)
        {
            var parser = new BinaryParser(new byte[] { 99, 1, 2, 3, 4, 88 }, 1, 4);
            Assert.Equal(1, parser.ReadByte());

            var exception = Assert.Throws(exceptionType, () => parser.CreateSubParser(length));

            if (exception is ArgumentOutOfRangeException argumentException)
                Assert.Equal("length", argumentException.ParamName);
            Assert.Equal(3, parser.RemainingBytes);
            Assert.False(parser.IsEmpty);
            Assert.Equal(new byte[] { 2, 3, 4 }, parser.ReadRemaining());
        }

        [Fact]
        public void CreateSubParser_ZeroLength_DoesNotAdvanceParent()
        {
            var parser = new BinaryParser(new byte[] { 99, 42, 88 }, 1, 1);

            var sub = parser.CreateSubParser(0);

            Assert.Equal(0, sub.RemainingBytes);
            Assert.True(sub.IsEmpty);
            Assert.Throws<InvalidOperationException>(() => sub.ReadByte());
            Assert.Equal(1, parser.RemainingBytes);
            Assert.Equal(42, parser.ReadByte());
            Assert.True(parser.IsEmpty);
        }

        [Fact]
        public void CreateSubParser_ExactLength_AdvancesParentAndBoundsChild()
        {
            var parser = new BinaryParser(new byte[] { 99, 1, 2, 3, 88 }, 1, 3);
            Assert.Equal(1, parser.ReadByte());

            var sub = parser.CreateSubParser(2);

            Assert.Equal(0, parser.RemainingBytes);
            Assert.True(parser.IsEmpty);
            Assert.Equal(2, sub.RemainingBytes);
            Assert.Equal(2, sub.ReadByte());
            Assert.Throws<InvalidOperationException>(() => sub.ReadUInt16());
            Assert.Equal(1, sub.RemainingBytes);
            Assert.Equal(3, sub.ReadByte());
            Assert.True(sub.IsEmpty);
            Assert.Throws<InvalidOperationException>(() => sub.ReadByte());
            Assert.Empty(parser.ReadRemaining());
        }

        [Theory]
        [InlineData(new byte[] { 88, 65, 66, 0, 89 }, 2, "AB", 0)]
        [InlineData(new byte[] { 88, 65, 0, 66, 89 }, 3, "A", 1)]
        public void ReadNullTerminatedString_RespectsSlice(byte[] buffer, int length, string expected, int remaining)
        {
            var parser = new BinaryParser(buffer, 1, length);

            Assert.Equal(expected, parser.ReadNullTerminatedString());
            Assert.Equal(remaining, parser.RemainingBytes);
            Assert.Equal(remaining == 0, parser.IsEmpty);
            Assert.Equal(remaining == 0 ? Array.Empty<byte>() : new byte[] { 66 }, parser.ReadRemaining());
            Assert.True(parser.IsEmpty);
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(5, 0)]
        [InlineData(0, -1)]
        [InlineData(3, 2)]
        [InlineData(1, int.MaxValue)]
        public void Constructor_InvalidSlice_Throws(int offset, int length)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BinaryParser(new byte[4], offset, length));
        }

        [Fact]
        public void ReadString_NegativeLength_Throws()
        {
            var parser = new BinaryParser(new byte[4]);
            Assert.Throws<ArgumentOutOfRangeException>(() => parser.ReadString(-1));
        }

        [Fact]
        public void CreateSubParser_NegativeLength_Throws()
        {
            var parser = new BinaryParser(new byte[4]);
            Assert.Throws<ArgumentOutOfRangeException>(() => parser.CreateSubParser(-1));
        }

        [Fact]
        public void ReadUInt32_UnalignedOffset_ReadsNetworkOrder()
        {
            var parser = new BinaryParser(new byte[] { 255, 0, 0, 1, 0 }, 1, 4);
            Assert.Equal(256u, parser.ReadUInt32());
        }

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
