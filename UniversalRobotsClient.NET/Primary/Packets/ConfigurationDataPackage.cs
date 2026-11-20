using UniversalRobotics.Models;
using UniversalRobotics.Protocol;

namespace UniversalRobotics.Primary.Packets
{
    /// <summary>Primary configuration sub-packet, with optional hardware types on newer controllers.</summary>
    public sealed class ConfigurationDataPackage : PrimaryPackage
    {
        /// <summary>Joint minimum limits in radians.</summary>
        public double[] JointMinLimits { get; } = new double[6];
        /// <summary>Joint maximum limits in radians.</summary>
        public double[] JointMaxLimits { get; } = new double[6];
        /// <summary>Maximum joint speeds in radians per second.</summary>
        public double[] JointMaxSpeeds { get; } = new double[6];
        /// <summary>Maximum joint accelerations in radians per second squared.</summary>
        public double[] JointMaxAccelerations { get; } = new double[6];
        /// <summary>Default joint speed.</summary>
        public double DefaultJointVelocity { get; private set; }
        /// <summary>Default joint acceleration.</summary>
        public double DefaultJointAcceleration { get; private set; }
        /// <summary>Default tool speed.</summary>
        public double DefaultToolVelocity { get; private set; }
        /// <summary>Default tool acceleration.</summary>
        public double DefaultToolAcceleration { get; private set; }
        /// <summary>Equivalent radius.</summary>
        public double EquivalentRadius { get; private set; }
        /// <summary>DH A parameters.</summary>
        public double[] DhA { get; private set; } = new double[6];
        /// <summary>DH D parameters.</summary>
        public double[] DhD { get; private set; } = new double[6];
        /// <summary>DH alpha parameters.</summary>
        public double[] DhAlpha { get; private set; } = new double[6];
        /// <summary>DH theta parameters.</summary>
        public double[] DhTheta { get; private set; } = new double[6];
        /// <summary>Masterboard version.</summary>
        public int MasterboardVersion { get; private set; }
        /// <summary>Legacy controller box integer; prefer ControlBoxType when available.</summary>
        public int LegacyControllerBoxType { get; private set; }
        /// <summary>Manipulator model.</summary>
        public RobotType RobotType { get; private set; }
        /// <summary>Robot sub-type.</summary>
        public int RobotSubType { get; private set; }
        /// <summary>Optional control box type; Unknown for old packets.</summary>
        public ControlBoxType ControlBoxType { get; private set; }
        /// <summary>Optional tool flange type; Unknown for old packets.</summary>
        public ToolFlangeType ToolFlangeType { get; private set; }

        /// <summary>Creates a configuration packet.</summary>
        public ConfigurationDataPackage() : base(RobotPackageType.RobotState) { }

        /// <inheritdoc />
        public override bool ParseFrom(BinaryParser parser)
        {
            if (parser.RemainingBytes < 440) return false;
            for (int joint = 0; joint < 6; joint++)
            {
                JointMinLimits[joint] = parser.ReadDouble();
                JointMaxLimits[joint] = parser.ReadDouble();
            }
            for (int joint = 0; joint < 6; joint++)
            {
                JointMaxSpeeds[joint] = parser.ReadDouble();
                JointMaxAccelerations[joint] = parser.ReadDouble();
            }
            DefaultJointVelocity = parser.ReadDouble();
            DefaultJointAcceleration = parser.ReadDouble();
            DefaultToolVelocity = parser.ReadDouble();
            DefaultToolAcceleration = parser.ReadDouble();
            EquivalentRadius = parser.ReadDouble();
            DhA = parser.ReadVector6d();
            DhD = parser.ReadVector6d();
            DhAlpha = parser.ReadVector6d();
            DhTheta = parser.ReadVector6d();
            MasterboardVersion = parser.ReadInt32();
            LegacyControllerBoxType = parser.ReadInt32();
            RobotType = (RobotType)parser.ReadInt32();
            RobotSubType = parser.ReadInt32();
            ControlBoxType = parser.RemainingBytes >= 2 ? (ControlBoxType)parser.ReadUInt16() : ControlBoxType.Unknown;
            ToolFlangeType = parser.RemainingBytes >= 2 ? (ToolFlangeType)parser.ReadUInt16() : ToolFlangeType.Unknown;
            return true;
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"Configuration {{ Robot={RobotType}, ControlBox={ControlBoxType}, ToolFlange={ToolFlangeType} }}";
    }
}