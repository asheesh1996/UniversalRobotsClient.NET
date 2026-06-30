// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using System.Text;
using UniversalRobotics.Models;
using UniversalRobotics.Protocol;

namespace UniversalRobotics.Primary.Packets
{
    /// <summary>Identifies the type of a UR primary interface message package.</summary>
    public enum RobotPackageType : sbyte
    {
        /// <summary>Disconnect signal.</summary>
        Disconnect = -1,
        /// <summary>Robot state data (joint positions, mode, I/O, etc.).</summary>
        RobotState = 16,
        /// <summary>Robot message (error codes, status text, version info, etc.).</summary>
        RobotMessage = 20,
        /// <summary>HMC message (rare).</summary>
        HmcMessage = 22,
        /// <summary>Modbus info message.</summary>
        ModbusInfoMessage = 5,
        /// <summary>Safety setup broadcast.</summary>
        SafetySetupBroadcastMessage = 23,
        /// <summary>Safety compliance tolerances message.</summary>
        SafetyComplianceTolerancesMessage = 24,
        /// <summary>Program state message.</summary>
        ProgramStateMessage = 25
    }

    /// <summary>
    /// Base class for all packages received on the primary interface.
    /// The primary interface header is: 4-byte big-endian total size + 1-byte type.
    /// </summary>
    public abstract class PrimaryPackage : IUrPackage<PrimaryPackageHeader>
    {
        /// <summary>Gets the package type discriminator.</summary>
        public RobotPackageType PackageType { get; }

        /// <summary>Initializes a primary package with its type.</summary>
        protected PrimaryPackage(RobotPackageType type) => PackageType = type;

        /// <inheritdoc />
        public abstract bool ParseFrom(BinaryParser parser);

        /// <inheritdoc />
        public abstract string ToDebugString();
    }

    /// <summary>
    /// Represents the 5-byte primary interface packet header (4-byte size + 1-byte type).
    /// </summary>
    public readonly struct PrimaryPackageHeader
    {
        /// <summary>Total packet size in bytes (including the 4-byte size field + 1-byte type).</summary>
        public int Size { get; }
        /// <summary>Package type identifier.</summary>
        public RobotPackageType PackageType { get; }

        /// <summary>Creates a header from its components.</summary>
        public PrimaryPackageHeader(int size, RobotPackageType packageType)
        {
            Size = size;
            PackageType = packageType;
        }
    }

    // ─── Robot Message Types ──────────────────────────────────────────────────────

    /// <summary>Identifies the sub-type of a robot message packet.</summary>
    public enum RobotMessageType : int
    {
        /// <summary>Text message displayed on the Teach Pendant.</summary>
        TextMessage = 0,
        /// <summary>Robot program source or tab message.</summary>
        ProgramLabel = 1,
        /// <summary>Joint configuration variable key.</summary>
        JointConfigurationKey = 2,
        /// <summary>Configuration data message.</summary>
        Configuration = 3,
        /// <summary>Safety mode change message.</summary>
        SafetyMode = 5,
        /// <summary>Error code message.</summary>
        ErrorCode = 6,
        /// <summary>Key message (robot model / version).</summary>
        Key = 7,
        /// <summary>Runtime exception message from URScript execution.</summary>
        RuntimeException = 10,
        /// <summary>Software version message (first message on connection).</summary>
        Version = 3  // NOTE: same value as Configuration; context distinguishes them
    }

    /// <summary>
    /// Base for all robot message sub-packets.
    /// Robot messages share a common header: int64 timestamp + int8 source + int8 robot_message_type.
    /// </summary>
    public abstract class RobotMessagePackage : PrimaryPackage
    {
        /// <summary>Message timestamp from the robot's internal clock (µs since epoch).</summary>
        public long Timestamp { get; private set; }
        /// <summary>Source identifier (0 = robot, 1 = joint firmware, etc.).</summary>
        public sbyte Source { get; private set; }
        /// <summary>Robot message sub-type.</summary>
        public int RobotMessageType { get; private set; }

        /// <summary>Initializes the base robot message package.</summary>
        protected RobotMessagePackage() : base(RobotPackageType.RobotMessage) { }

        /// <summary>Parses the common robot message header fields.</summary>
        protected bool ParseHeader(BinaryParser parser)
        {
            Timestamp = parser.ReadInt64();
            Source = parser.ReadSByte();
            RobotMessageType = parser.ReadByte();
            return true;
        }

        /// <inheritdoc />
        public override bool ParseFrom(BinaryParser parser)
        {
            ParseHeader(parser);
            return ParsePayload(parser);
        }

        /// <summary>Parses the message-type-specific payload bytes.</summary>
        protected abstract bool ParsePayload(BinaryParser parser);
    }

    // ─── Error Code Message ───────────────────────────────────────────────────────

    /// <summary>
    /// Carries error code data from the robot controller.
    /// Error codes can be queried with PrimaryClient.GetErrorCodesAsync().
    /// </summary>
    public sealed class ErrorCodeMessage : RobotMessagePackage
    {
        /// <summary>Gets the error code reported by the robot.</summary>
        public uint ErrorCode { get; private set; }
        /// <summary>Gets the error argument (context-specific meaning).</summary>
        public uint ErrorArgument { get; private set; }

        /// <inheritdoc />
        protected override bool ParsePayload(BinaryParser parser)
        {
            if (parser.RemainingBytes < 8) return false;
            ErrorCode = parser.ReadUInt32();
            ErrorArgument = parser.ReadUInt32();
            return true;
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"ErrorCodeMessage {{ Code=0x{ErrorCode:X8}, Arg={ErrorArgument} }}";
    }

    // ─── Runtime Exception Message ────────────────────────────────────────────────

    /// <summary>Carries a URScript runtime exception message from the robot.</summary>
    public sealed class RuntimeExceptionMessage : RobotMessagePackage
    {
        /// <summary>Gets the script line number where the exception occurred.</summary>
        public int LineNumber { get; private set; }
        /// <summary>Gets the column number in the script source.</summary>
        public int ColumnNumber { get; private set; }
        /// <summary>Gets the exception message text.</summary>
        public string ExceptionMessage { get; private set; } = string.Empty;

        /// <inheritdoc />
        protected override bool ParsePayload(BinaryParser parser)
        {
            if (parser.RemainingBytes < 9) return false;
            LineNumber = parser.ReadInt32();
            ColumnNumber = parser.ReadInt32();
            byte msgLen = parser.ReadByte();
            ExceptionMessage = msgLen > 0 && parser.RemainingBytes >= msgLen
                ? parser.ReadString(msgLen)
                : string.Empty;
            return true;
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"RuntimeException {{ Line={LineNumber}, Col={ColumnNumber}, Msg={ExceptionMessage} }}";
    }

    // ─── Version Message ──────────────────────────────────────────────────────────

    /// <summary>
    /// The first message received on a new primary connection; carries the robot's software version.
    /// </summary>
    public sealed class VersionMessage : RobotMessagePackage
    {
        /// <summary>Gets the robot software (PolyScope) version.</summary>
        public VersionInformation Version { get; private set; } = new VersionInformation();

        /// <inheritdoc />
        protected override bool ParsePayload(BinaryParser parser)
        {
            if (parser.RemainingBytes < 16) return false;
            uint major = parser.ReadUInt32();
            uint minor = parser.ReadUInt32();
            uint bugfix = parser.ReadUInt32();
            uint build = parser.ReadUInt32();
            Version = new VersionInformation(major, minor, bugfix, build);
            return true;
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"VersionMessage {{ Version={Version} }}";
    }

    // ─── Text Message ─────────────────────────────────────────────────────────────

    /// <summary>A text message displayed on the robot's Teach Pendant popup.</summary>
    public sealed class PrimaryTextMessage : RobotMessagePackage
    {
        /// <summary>Gets the message text.</summary>
        public string Text { get; private set; } = string.Empty;

        /// <inheritdoc />
        protected override bool ParsePayload(BinaryParser parser)
        {
            if (parser.RemainingBytes < 1) return true;
            byte len = parser.ReadByte();
            Text = len > 0 ? parser.ReadString(len) : string.Empty;
            return true;
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"TextMessage {{ Text={Text} }}";
    }

    // ─── Safety Mode Message ──────────────────────────────────────────────────────

    /// <summary>Reports a safety mode change from the robot.</summary>
    public sealed class SafetyModeMessage : RobotMessagePackage
    {
        /// <summary>Gets the new safety mode.</summary>
        public SafetyMode SafetyMode { get; private set; }

        /// <inheritdoc />
        protected override bool ParsePayload(BinaryParser parser)
        {
            if (parser.RemainingBytes < 1) return false;
            SafetyMode = (SafetyMode)parser.ReadByte();
            return true;
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"SafetyModeMessage {{ SafetyMode={SafetyMode} }}";
    }

    // ─── Unknown / Raw Package ────────────────────────────────────────────────────

    /// <summary>Wraps unrecognized primary interface packages as raw bytes for logging.</summary>
    public sealed class UnknownPrimaryPackage : PrimaryPackage
    {
        private readonly byte[]? _raw;

        /// <summary>Creates an unknown package with the raw payload.</summary>
        public UnknownPrimaryPackage(RobotPackageType type, byte[]? raw = null) : base(type) { _raw = raw; }

        /// <inheritdoc />
        public override bool ParseFrom(BinaryParser parser) => true;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            var sb = new StringBuilder($"UnknownPackage({PackageType}) [");
            if (_raw != null)
                foreach (var b in _raw) sb.Append($"{b:X2} ");
            sb.Append(']');
            return sb.ToString();
        }
    }

    // ─── Robot State Packets ──────────────────────────────────────────────────────

    /// <summary>Carries a snapshot of the robot's state (joint positions, mode data, etc.).</summary>
    public sealed class RobotStatePackage : PrimaryPackage
    {
        /// <summary>Gets all sub-packages parsed from this robot state packet.</summary>
        public List<PrimaryPackage> SubPackages { get; } = new List<PrimaryPackage>();

        /// <summary>Creates a new robot state package.</summary>
        public RobotStatePackage() : base(RobotPackageType.RobotState) { }

        /// <inheritdoc />
        public override bool ParseFrom(BinaryParser parser)
        {
            while (!parser.IsEmpty)
            {
                if (parser.RemainingBytes < 5) break;
                int subSize = parser.ReadInt32();
                byte subType = parser.ReadByte();
                int payloadSize = subSize - 5;

                if (payloadSize < 0 || payloadSize > parser.RemainingBytes) break;

                var subParser = parser.CreateSubParser(payloadSize);
                var subPkg = CreateSubPackage(subType, subParser);
                SubPackages.Add(subPkg);
            }
            return true;
        }

        private PrimaryPackage CreateSubPackage(byte type, BinaryParser parser)
        {
            PrimaryPackage pkg = type switch
            {
                0 => new RobotModeDataPackage(),
                1 => new JointDataPackage(),
                4 => new MasterboardDataPackage(),
                _ => new UnknownPrimaryPackage((RobotPackageType)type)
            };
            pkg.ParseFrom(parser);
            return pkg;
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"RobotState {{ SubPackages={SubPackages.Count} }}";
    }

    /// <summary>Robot mode data sub-packet from the robot state message.</summary>
    public sealed class RobotModeDataPackage : PrimaryPackage
    {
        /// <summary>Gets the timestamp from this packet.</summary>
        public ulong Timestamp { get; private set; }
        /// <summary>Gets whether the robot is connected to the controller.</summary>
        public bool IsRobotConnected { get; private set; }
        /// <summary>Gets whether the robot is real (false = simulation).</summary>
        public bool IsRealRobot { get; private set; }
        /// <summary>Gets whether the power button is on.</summary>
        public bool IsPowerOnRobot { get; private set; }
        /// <summary>Gets whether emergency stop is pressed.</summary>
        public bool IsEmergencyStoppedByProtectiveStop { get; private set; }
        /// <summary>Gets whether protective stop is active.</summary>
        public bool IsProtectiveStopped { get; private set; }
        /// <summary>Gets whether a program is currently running.</summary>
        public bool IsProgramRunning { get; private set; }
        /// <summary>Gets whether a program is paused.</summary>
        public bool IsProgramPaused { get; private set; }
        /// <summary>Gets the current robot mode.</summary>
        public RobotMode RobotMode { get; private set; }
        /// <summary>Gets the current control mode.</summary>
        public byte ControlMode { get; private set; }
        /// <summary>Gets the target speed fraction.</summary>
        public double TargetSpeedFraction { get; private set; }
        /// <summary>Gets the actual speed fraction.</summary>
        public double SpeedScaling { get; private set; }

        /// <summary>Creates a robot mode data package.</summary>
        public RobotModeDataPackage() : base(RobotPackageType.RobotState) { }

        /// <inheritdoc />
        public override bool ParseFrom(BinaryParser parser)
        {
            if (parser.RemainingBytes < 29) return false;
            Timestamp = parser.ReadUInt64();
            IsRobotConnected = parser.ReadBool();
            IsRealRobot = parser.ReadBool();
            IsPowerOnRobot = parser.ReadBool();
            IsEmergencyStoppedByProtectiveStop = parser.ReadBool();
            IsProtectiveStopped = parser.ReadBool();
            IsProgramRunning = parser.ReadBool();
            IsProgramPaused = parser.ReadBool();
            RobotMode = (RobotMode)parser.ReadSByte();
            ControlMode = parser.ReadByte();
            TargetSpeedFraction = parser.ReadDouble();
            SpeedScaling = parser.ReadDouble();
            return true;
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"RobotModeData {{ Mode={RobotMode}, Running={IsProgramRunning} }}";
    }

    /// <summary>Joint-level data from the robot state packet (positions, velocities, temperatures).</summary>
    public sealed class JointDataPackage : PrimaryPackage
    {
        /// <summary>Gets the actual joint positions in radians.</summary>
        public double[] ActualPositions { get; } = new double[6];
        /// <summary>Gets the actual joint velocities in rad/s.</summary>
        public double[] ActualVelocities { get; } = new double[6];
        /// <summary>Gets the actual motor currents in amperes.</summary>
        public double[] ActualCurrents { get; } = new double[6];
        /// <summary>Gets the joint temperatures in degrees Celsius.</summary>
        public double[] Temperatures { get; } = new double[6];

        /// <summary>Creates a joint data package.</summary>
        public JointDataPackage() : base(RobotPackageType.RobotState) { }

        /// <inheritdoc />
        public override bool ParseFrom(BinaryParser parser)
        {
            // Each joint has: q_actual (8), qd_actual (8), I_actual (8), T_motor (4) = 28 bytes × 6 joints
            if (parser.RemainingBytes < 28 * 6) return false;
            for (int i = 0; i < 6; i++)
            {
                ActualPositions[i] = parser.ReadDouble();
                ActualVelocities[i] = parser.ReadDouble();
                ActualCurrents[i] = parser.ReadDouble();
                parser.ReadFloat(); // T_motor (ignored for now)
            }
            return true;
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"JointData {{ q=[{string.Join(", ", ActualPositions)}] }}";
    }

    /// <summary>Master board / I/O data from the robot state packet.</summary>
    public sealed class MasterboardDataPackage : PrimaryPackage
    {
        /// <summary>Gets the digital input bits from the controller.</summary>
        public uint DigitalInputBits { get; private set; }
        /// <summary>Gets the digital output bits from the controller.</summary>
        public uint DigitalOutputBits { get; private set; }
        /// <summary>Gets analog input channel 0 (range 0-1).</summary>
        public double AnalogInput0 { get; private set; }
        /// <summary>Gets analog input channel 1 (range 0-1).</summary>
        public double AnalogInput1 { get; private set; }
        /// <summary>Gets analog output channel 0 (range 0-1).</summary>
        public double AnalogOutput0 { get; private set; }
        /// <summary>Gets analog output channel 1 (range 0-1).</summary>
        public double AnalogOutput1 { get; private set; }

        /// <summary>Creates a masterboard data package.</summary>
        public MasterboardDataPackage() : base(RobotPackageType.RobotState) { }

        /// <inheritdoc />
        public override bool ParseFrom(BinaryParser parser)
        {
            if (parser.RemainingBytes < 48) return false;
            DigitalInputBits = parser.ReadUInt32();
            DigitalOutputBits = parser.ReadUInt32();
            AnalogInput0 = parser.ReadDouble();
            AnalogInput1 = parser.ReadDouble();
            AnalogOutput0 = parser.ReadDouble();
            AnalogOutput1 = parser.ReadDouble();
            return true;
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"MasterboardData {{ DigIn=0x{DigitalInputBits:X8}, DigOut=0x{DigitalOutputBits:X8} }}";
    }
}
