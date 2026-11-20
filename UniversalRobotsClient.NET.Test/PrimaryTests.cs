using System;
using System.Buffers.Binary;
using UniversalRobotics.Models;
using UniversalRobotics.Primary;
using UniversalRobotics.Primary.Packets;
using UniversalRobotics.Protocol;

namespace UniversalRobotics.Tests
{
    public class PrimaryTests
    {
        [Fact]
        public void ErrorCodeTexts_ResolvesExactFallbackUnknownAndRobotModeMessages()
        {
            Assert.Equal("Outbuffer overflow: Buffer of stored warnings overflowed", ErrorCodeTexts.GetText(1, 1));
            Assert.Equal("No error", ErrorCodeTexts.GetText(0, 123));
            Assert.Null(ErrorCodeTexts.GetText(999999, 1));
            Assert.Equal("Robot mode changed to: Idle", ErrorCodeTexts.GetText(100, 5));
        }

        [Fact]
        public void TryParse_VersionMessage_UsesPrimaryHeaderAndWireLayout()
        {
            var packet = new byte[]
            {
                0x00, 0x00, 0x00, 0x37, 0x14,
                0, 0, 0, 0, 0, 0, 0, 0, 0xfe, 0x03,
                0x09, (byte)'U', (byte)'R', (byte)'C', (byte)'o', (byte)'n', (byte)'t', (byte)'r', (byte)'o', (byte)'l',
                0x05, 0x18, 0, 0, 0, 0, 0, 0, 0, 0,
                (byte)'0', (byte)'4', (byte)'-', (byte)'1', (byte)'0', (byte)'-', (byte)'2', (byte)'0', (byte)'2', (byte)'5',
                (byte)',', (byte)' ', (byte)'0', (byte)'0', (byte)':', (byte)'2', (byte)'5', (byte)':', (byte)'3', (byte)'3'
            };

            var parsed = new PrimaryParser().TryParse(packet, packet.Length, out var package);

            Assert.True(parsed);
            var version = Assert.IsType<VersionMessage>(package);
            Assert.Equal(5u, version.Version.Major);
            Assert.Equal(24u, version.Version.Minor);
            Assert.Equal(0u, version.Version.Bugfix);
            Assert.Equal(0u, version.Version.Build);
        }

        [Fact]
        public void TryParse_HardwareInfoType18_ReadsSignedModelAndHardwareFields()
        {
            var packet = new byte[]
            {
                0, 0, 0, 0x1b, 0x14,
                0, 0, 0, 0, 0, 0, 0, 0x2a, 0xfe, 0x12,
                0, 0, 0, 7, 0, 0, 0, 2, 0, 0, 0, 1
            };

            Assert.True(new PrimaryParser().TryParse(packet, packet.Length, out var package));
            var hardware = Assert.IsType<HardwareInfoMessage>(package);
            Assert.Equal(RobotType.Ur20, hardware.RobotType);
            Assert.Equal(ControlBoxType.Cb7, hardware.ControlBoxType);
            Assert.Equal(ToolFlangeType.V1, hardware.ToolFlangeType);
        }

        [Fact]
        public void ConfigurationDataPackage_ParsesMandatory440BytesAndOptionalHardwareTypes()
        {
            var bytes = new byte[444];
            var offset = 0;
            for (var value = 1.0; offset < 424; value += 1.0)
            {
                WriteDouble(bytes, ref offset, value);
            }

            WriteInt32(bytes, ref offset, 1234);
            WriteInt32(bytes, ref offset, 11);
            WriteInt32(bytes, ref offset, 7);
            WriteInt32(bytes, ref offset, 9);
            WriteUInt16(bytes, ref offset, (ushort)ControlBoxType.Cb7);
            WriteUInt16(bytes, ref offset, (ushort)ToolFlangeType.V2);

            var package = new ConfigurationDataPackage();
            Assert.True(package.ParseFrom(new BinaryParser(bytes)));

            Assert.Equal(1.0, package.JointMinLimits[0]);
            Assert.Equal(2.0, package.JointMaxLimits[0]);
            Assert.Equal(1234, package.MasterboardVersion);
            Assert.Equal(11, package.LegacyControllerBoxType);
            Assert.Equal(RobotType.Ur20, package.RobotType);
            Assert.Equal(9, package.RobotSubType);
            Assert.Equal(ControlBoxType.Cb7, package.ControlBoxType);
            Assert.Equal(ToolFlangeType.V2, package.ToolFlangeType);
        }

        [Fact]
        public void ConfigurationDataPackage_RejectsShortMandatoryPayload()
        {
            var package = new ConfigurationDataPackage();

            Assert.False(package.ParseFrom(new BinaryParser(new byte[439])));
        }

        private static void WriteDouble(byte[] target, ref int offset, double value)
            => WriteUInt64(target, ref offset, (ulong)BitConverter.DoubleToInt64Bits(value));

        private static void WriteInt32(byte[] target, ref int offset, int value)
            => WriteUInt32(target, ref offset, unchecked((uint)value));

        private static void WriteUInt16(byte[] target, ref int offset, ushort value)
        {
            BinaryPrimitives.WriteUInt16BigEndian(target.AsSpan(offset, 2), value);
            offset += 2;
        }

        private static void WriteUInt32(byte[] target, ref int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32BigEndian(target.AsSpan(offset, 4), value);
            offset += 4;
        }

        private static void WriteUInt64(byte[] target, ref int offset, ulong value)
        {
            BinaryPrimitives.WriteUInt64BigEndian(target.AsSpan(offset, 8), value);
            offset += 8;
        }
    }
}