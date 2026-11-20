using UniversalRobotics.Models;
using UniversalRobotics.Protocol;

namespace UniversalRobotics.Primary.Packets
{
    /// <summary>Hardware identity from primary robot message type 18.</summary>
    public sealed class HardwareInfoMessage : RobotMessagePackage
    {
        /// <summary>Manipulator model; the wire value is a signed 32-bit integer.</summary>
        public RobotType RobotType { get; private set; }
        /// <summary>Control box generation.</summary>
        public ControlBoxType ControlBoxType { get; private set; }
        /// <summary>Tool flange generation.</summary>
        public ToolFlangeType ToolFlangeType { get; private set; }

        /// <inheritdoc />
        protected override bool ParsePayload(BinaryParser parser)
        {
            if (parser.RemainingBytes < 12) return false;
            RobotType = (RobotType)parser.ReadInt32();
            parser.ReadUInt16();
            ControlBoxType = (ControlBoxType)parser.ReadUInt16();
            parser.ReadUInt16();
            ToolFlangeType = (ToolFlangeType)parser.ReadUInt16();
            return true;
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"HardwareInfo {{ Robot={RobotType}, ControlBox={ControlBoxType}, ToolFlange={ToolFlangeType} }}";
    }
}