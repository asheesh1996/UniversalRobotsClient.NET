// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using System.Text;
using UniversalRobotics.Protocol;

namespace UniversalRobotics.Rtde.Packets
{
    // ─── Request Protocol Version ─────────────────────────────────────────────────

    /// <summary>
    /// Sent to the robot to negotiate the RTDE protocol version.
    /// The robot responds with the same type, with <see cref="Accepted"/> set to <see langword="true"/> or <see langword="false"/>.
    /// </summary>
    public sealed class RequestProtocolVersionPackage : RtdePackage
    {
        /// <summary>The protocol version being requested.</summary>
        public ushort Version { get; set; }

        /// <summary>Set by the robot in response: <see langword="true"/> if the requested version is accepted.</summary>
        public bool Accepted { get; private set; }

        /// <summary>Creates a request for the specified protocol version.</summary>
        public RequestProtocolVersionPackage(ushort version = 2)
            : base(RtdePackageType.RequestProtocolVersion)
        {
            Version = version;
        }

        /// <inheritdoc />
        public override bool ParseFrom(BinaryParser parser)
        {
            Accepted = parser.ReadBool();
            return true;
        }

        /// <inheritdoc />
        protected override byte[] SerializePayload()
        {
            var buf = new byte[2];
            int offset = 0;
            PackageSerializer.WriteUInt16(buf, ref offset, Version);
            return buf;
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"RequestProtocolVersion {{ Version={Version}, Accepted={Accepted} }}";
    }

    // ─── Get URControl Version ────────────────────────────────────────────────────

    /// <summary>
    /// Requests and parses the URControl software version from the robot.
    /// </summary>
    public sealed class GetUrControlVersionPackage : RtdePackage
    {
        /// <summary>Major version of URControl.</summary>
        public uint Major { get; private set; }
        /// <summary>Minor version of URControl.</summary>
        public uint Minor { get; private set; }
        /// <summary>Bugfix version of URControl.</summary>
        public uint Bugfix { get; private set; }
        /// <summary>Build number of URControl.</summary>
        public uint Build { get; private set; }

        /// <summary>Creates a new URControl version request.</summary>
        public GetUrControlVersionPackage() : base(RtdePackageType.GetUrControlVersion) { }

        /// <inheritdoc />
        public override bool ParseFrom(BinaryParser parser)
        {
            Major = parser.ReadUInt32();
            Minor = parser.ReadUInt32();
            Bugfix = parser.ReadUInt32();
            Build = parser.ReadUInt32();
            return true;
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"URControlVersion {Major}.{Minor}.{Bugfix}.{Build}";
    }

    // ─── Setup Outputs ────────────────────────────────────────────────────────────

    /// <summary>
    /// Configures the RTDE output recipe (data the robot sends to the client).
    /// </summary>
    public sealed class SetupOutputsPackage : RtdePackage
    {
        /// <summary>The desired output data fields.</summary>
        public IReadOnlyList<string> Recipe { get; }

        /// <summary>The output frequency in Hz (0 = robot's maximum).</summary>
        public double Frequency { get; }

        /// <summary>On response, the type name of each output field in recipe order.</summary>
        public IReadOnlyList<string> VariableTypes { get; private set; } = Array.Empty<string>();

        /// <summary>The recipe ID assigned by the robot.</summary>
        public byte RecipeId { get; private set; }

        /// <summary>The negotiated RTDE protocol version (1 or 2). Affects response parsing.</summary>
        public ushort ProtocolVersion { get; set; } = 1;

        /// <summary>Creates a setup-outputs request.</summary>
        public SetupOutputsPackage(IReadOnlyList<string> recipe, double frequency = 0.0, ushort protocolVersion = 1)
            : base(RtdePackageType.ControlPackageSetupOutputs)
        {
            Recipe = recipe;
            Frequency = frequency;
            ProtocolVersion = protocolVersion;
        }

        /// <inheritdoc />
        public override bool ParseFrom(BinaryParser parser)
        {
            // Protocol v2 response: recipe_id (1 byte) then comma-separated type list.
            // Protocol v1 response: comma-separated type list only.
            if (ProtocolVersion == 2)
                RecipeId = parser.ReadByte();

            var raw = parser.ReadString(parser.RemainingBytes);
            var parts = raw.Split(',');
            var types = new List<string>(parts.Length);
            foreach (var p in parts) types.Add(p.Trim());
            VariableTypes = types;
            return true;
        }

        /// <inheritdoc />
        protected override byte[] SerializePayload()
        {
            // Payload: optional 8-byte frequency (protocol v2), then comma-separated field names
            var recipeStr = string.Join(",", Recipe);
            var recipeBytes = Encoding.ASCII.GetBytes(recipeStr);
            if (ProtocolVersion == 1) return recipeBytes;
            var freqBytes = new byte[8];
            int offset = 0;
            PackageSerializer.WriteDouble(freqBytes, ref offset, Frequency);
            var payload = new byte[8 + recipeBytes.Length];
            Buffer.BlockCopy(freqBytes, 0, payload, 0, 8);
            Buffer.BlockCopy(recipeBytes, 0, payload, 8, recipeBytes.Length);
            return payload;
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"SetupOutputs {{ Recipe=[{string.Join(",", Recipe)}], Types=[{string.Join(",", VariableTypes)}] }}";
    }

    // ─── Setup Inputs ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Configures the RTDE input recipe (data the client sends to the robot).
    /// </summary>
    public sealed class SetupInputsPackage : RtdePackage
    {
        /// <summary>The desired input data fields.</summary>
        public IReadOnlyList<string> Recipe { get; }

        /// <summary>The recipe ID assigned by the robot, valid after parsing the response.</summary>
        public byte RecipeId { get; private set; }

        /// <summary>On response, the type name of each input field in recipe order.</summary>
        public IReadOnlyList<string> VariableTypes { get; private set; } = Array.Empty<string>();

        /// <summary>Creates a setup-inputs request.</summary>
        public SetupInputsPackage(IReadOnlyList<string> recipe)
            : base(RtdePackageType.ControlPackageSetupInputs)
        {
            Recipe = recipe;
        }

        /// <inheritdoc />
        public override bool ParseFrom(BinaryParser parser)
        {
            RecipeId = parser.ReadByte();
            var raw = parser.ReadString(parser.RemainingBytes);
            var parts = raw.Split(',');
            var types = new List<string>(parts.Length);
            foreach (var p in parts) types.Add(p.Trim());
            VariableTypes = types;
            return true;
        }

        /// <inheritdoc />
        protected override byte[] SerializePayload()
        {
            var recipeStr = string.Join(",", Recipe);
            return Encoding.ASCII.GetBytes(recipeStr);
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"SetupInputs {{ Recipe=[{string.Join(",", Recipe)}], RecipeId={RecipeId} }}";
    }

    // ─── Control Start / Pause ────────────────────────────────────────────────────

    /// <summary>Requests the robot to start sending RTDE data packages.</summary>
    public sealed class ControlStartPackage : RtdePackage
    {
        /// <summary><see langword="true"/> if the robot accepted the start request.</summary>
        public bool Accepted { get; private set; }

        /// <summary>Creates a new start request.</summary>
        public ControlStartPackage() : base(RtdePackageType.ControlPackageStart) { }

        /// <inheritdoc />
        public override bool ParseFrom(BinaryParser parser)
        {
            Accepted = parser.ReadBool();
            return true;
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"ControlStart {{ Accepted={Accepted} }}";
    }

    /// <summary>Requests the robot to pause RTDE data streaming.</summary>
    public sealed class ControlPausePackage : RtdePackage
    {
        /// <summary><see langword="true"/> if the robot accepted the pause request.</summary>
        public bool Accepted { get; private set; }

        /// <summary>Creates a new pause request.</summary>
        public ControlPausePackage() : base(RtdePackageType.ControlPackagePause) { }

        /// <inheritdoc />
        public override bool ParseFrom(BinaryParser parser)
        {
            Accepted = parser.ReadBool();
            return true;
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"ControlPause {{ Accepted={Accepted} }}";
    }

    // ─── Text Message ─────────────────────────────────────────────────────────────

    /// <summary>A text message sent from the robot over the RTDE channel.</summary>
    public sealed class RtdeTextMessage : RtdePackage
    {
        /// <summary>Gets the message text.</summary>
        public string Message { get; private set; } = string.Empty;
        /// <summary>Gets the source identifier for the message.</summary>
        public string Source { get; private set; } = string.Empty;

        /// <summary>Creates a new text message.</summary>
        public RtdeTextMessage() : base(RtdePackageType.TextMessage) { }

        /// <inheritdoc />
        public override bool ParseFrom(BinaryParser parser)
        {
            byte msgLen = parser.ReadByte();
            Message = msgLen > 0 ? parser.ReadString(msgLen) : string.Empty;
            byte srcLen = parser.ReadByte();
            Source = srcLen > 0 ? parser.ReadString(srcLen) : string.Empty;
            return true;
        }

        /// <inheritdoc />
        public override string ToDebugString() => $"RtdeTextMessage {{ Source={Source}, Message={Message} }}";
    }
}
