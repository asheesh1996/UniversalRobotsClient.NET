// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using System.Text;
using UniversalRobotics.Protocol;

namespace UniversalRobotics.Rtde.Packets
{
    /// <summary>
    /// Possible runtime states of a UR robot program, as reported in the RTDE output recipe.
    /// </summary>
    public enum RuntimeState : uint
    {
        /// <summary>Program is stopping.</summary>
        Stopping = 0,
        /// <summary>Program is stopped.</summary>
        Stopped = 1,
        /// <summary>Program is running.</summary>
        Playing = 2,
        /// <summary>Program is being paused.</summary>
        Pausing = 3,
        /// <summary>Program is paused.</summary>
        Paused = 4,
        /// <summary>Program is resuming from a paused state.</summary>
        Resuming = 5
    }

    /// <summary>
    /// Represents a cyclic RTDE data package exchanged between the client and the robot controller.
    /// <para>
    /// A <see cref="DataPackage"/> is constructed from a recipe — an ordered list of field names
    /// that were negotiated during the RTDE setup phase.  Fields are read and written in recipe order.
    /// </para>
    /// </summary>
    public sealed class DataPackage : RtdePackage
    {
        private readonly IReadOnlyList<string> _recipe;
        private readonly Dictionary<string, (int index, RtdeValueKind kind)> _fieldMeta;
        private RtdeValue[] _data;
        private ushort _protocolVersion;

        /// <summary>Recipe identifier assigned during RTDE setup.</summary>
        public byte RecipeId { get; set; }

        /// <summary>Gets the RTDE protocol version used when serializing this package.</summary>
        public ushort ProtocolVersion
        {
            get => _protocolVersion;
            set => _protocolVersion = value;
        }

        /// <summary>Gets the recipe (ordered field names) for this package.</summary>
        public IReadOnlyList<string> Recipe => _recipe;

        /// <summary>
        /// Creates a new <see cref="DataPackage"/> for the given recipe.
        /// </summary>
        /// <param name="recipe">Ordered list of RTDE field names from the setup phase.</param>
        /// <param name="fieldTypes">Ordered list of RTDE type names for each recipe field.</param>
        /// <param name="protocolVersion">RTDE protocol version (1 or 2).</param>
        public DataPackage(
            IReadOnlyList<string> recipe,
            IReadOnlyList<string>? fieldTypes = null,
            ushort protocolVersion = 2)
            : base(RtdePackageType.DataPackage)
        {
            _recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
            _protocolVersion = protocolVersion;
            _fieldMeta = new Dictionary<string, (int, RtdeValueKind)>(recipe.Count, StringComparer.Ordinal);
            _data = new RtdeValue[recipe.Count];

            for (int i = 0; i < recipe.Count; i++)
            {
                var kind = fieldTypes != null && i < fieldTypes.Count
                    ? RtdeTypeMap.Resolve(fieldTypes[i])
                    : RtdeValueKind.None;
                _fieldMeta[recipe[i]] = (i, kind);
                _data[i] = kind switch
                {
                    RtdeValueKind.Bool => RtdeValue.FromBool(false),
                    RtdeValueKind.UInt8 => RtdeValue.FromUInt8(0),
                    RtdeValueKind.UInt32 => RtdeValue.FromUInt32(0),
                    RtdeValueKind.UInt64 => RtdeValue.FromUInt64(0),
                    RtdeValueKind.Int32 => RtdeValue.FromInt32(0),
                    RtdeValueKind.Double => RtdeValue.FromDouble(0),
                    RtdeValueKind.Vector3d => RtdeValue.FromVector3d(new double[3]),
                    RtdeValueKind.Vector6d => RtdeValue.FromVector6d(new double[6]),
                    RtdeValueKind.Vector6Int32 => RtdeValue.FromVector6Int32(new int[6]),
                    RtdeValueKind.Vector6UInt32 => RtdeValue.FromVector6UInt32(new uint[6]),
                    _ => null!
                };
            }
        }

        /// <summary>Updates field type information after setup (called by RtdeClient after handshake).</summary>
        internal void SetFieldTypes(IReadOnlyList<string> fieldTypes)
        {
            for (int i = 0; i < _recipe.Count && i < fieldTypes.Count; i++)
            {
                if (_fieldMeta.TryGetValue(_recipe[i], out var meta))
                    _fieldMeta[_recipe[i]] = (meta.index, RtdeTypeMap.Resolve(fieldTypes[i]));
            }
        }

        /// <summary>
        /// Retrieves a typed field value from this package.
        /// </summary>
        /// <typeparam name="T">The expected value type.</typeparam>
        /// <param name="name">Field name from the recipe.</param>
        /// <param name="value">The parsed value on success.</param>
        /// <returns><see langword="true"/> if the field exists and the type matches.</returns>
        public bool GetData<T>(string name, out T value)
        {
            value = default!;
            if (!_fieldMeta.TryGetValue(name, out var meta)) return false;
            var rtdeVal = _data[meta.index];
            if (rtdeVal == null) return false;
            return TryConvert(rtdeVal, out value);
        }

        /// <summary>
        /// Sets a typed field value for transmission to the robot.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="name">Field name from the input recipe.</param>
        /// <param name="value">The value to set.</param>
        /// <returns><see langword="true"/> if the field exists in the recipe.</returns>
        public bool SetData<T>(string name, T value)
        {
            if (!_fieldMeta.TryGetValue(name, out var meta)) return false;
            _data[meta.index] = CreateRtdeValue(meta.kind, value);
            return true;
        }

        /// <inheritdoc />
        public override bool ParseFrom(BinaryParser parser)
        {
            // Protocol v2: every data packet starts with a 1-byte recipe_id before the field data.
            if (_protocolVersion == 2)
                RecipeId = parser.ReadByte();

            for (int i = 0; i < _recipe.Count; i++)
            {
                if (!_fieldMeta.TryGetValue(_recipe[i], out var meta) || meta.kind == RtdeValueKind.None)
                {
                    // Unknown or disabled field — skip (size unknown without type)
                    continue;
                }
                _data[i] = ReadField(parser, meta.kind);
            }
            return true;
        }

        /// <summary>Serializes this package's payload for transmission to the robot (input recipe).</summary>
        protected override byte[] SerializePayload()
        {
            // Estimate size: each field has a max size; allocate a generous buffer
            var buf = new byte[1 + _recipe.Count * 48];
            int offset = 0;
            PackageSerializer.WriteByte(buf, ref offset, RecipeId);
            for (int i = 0; i < _recipe.Count; i++)
            {
                var val = _data[i];
                if (val == null) continue;
                WriteField(buf, ref offset, val);
            }
            var result = new byte[offset];
            Array.Copy(buf, result, offset);
            return result;
        }

        /// <inheritdoc />
        public override string ToDebugString()
        {
            var sb = new StringBuilder("DataPackage { ");
            for (int i = 0; i < _recipe.Count; i++)
                sb.Append($"{_recipe[i]}={_data[i]?.ToString() ?? "?"}, ");
            sb.Append('}');
            return sb.ToString();
        }

        // ─── Private helpers ──────────────────────────────────────────────────────

        private static RtdeValue ReadField(BinaryParser parser, RtdeValueKind kind) => kind switch
        {
            RtdeValueKind.Bool => RtdeValue.FromBool(parser.ReadBool()),
            RtdeValueKind.UInt8 => RtdeValue.FromUInt8(parser.ReadByte()),
            RtdeValueKind.UInt32 => RtdeValue.FromUInt32(parser.ReadUInt32()),
            RtdeValueKind.UInt64 => RtdeValue.FromUInt64(parser.ReadUInt64()),
            RtdeValueKind.Int32 => RtdeValue.FromInt32(parser.ReadInt32()),
            RtdeValueKind.Double => RtdeValue.FromDouble(parser.ReadDouble()),
            RtdeValueKind.Vector3d => RtdeValue.FromVector3d(parser.ReadVector3d()),
            RtdeValueKind.Vector6d => RtdeValue.FromVector6d(parser.ReadVector6d()),
            RtdeValueKind.Vector6Int32 => RtdeValue.FromVector6Int32(parser.ReadVector6Int32()),
            RtdeValueKind.Vector6UInt32 => RtdeValue.FromVector6UInt32(parser.ReadVector6UInt32()),
            _ => RtdeValue.FromBool(false)
        };

        private static void WriteField(byte[] buf, ref int offset, RtdeValue val)
        {
            switch (val.Kind)
            {
                case RtdeValueKind.Bool:
                    val.TryGetBool(out bool boolVal);
                    PackageSerializer.WriteBool(buf, ref offset, boolVal);
                    break;
                case RtdeValueKind.UInt8:
                    val.TryGetUInt8(out byte u8Val);
                    PackageSerializer.WriteByte(buf, ref offset, u8Val);
                    break;
                case RtdeValueKind.UInt32:
                    val.TryGetUInt32(out uint u32Val);
                    PackageSerializer.WriteUInt32(buf, ref offset, u32Val);
                    break;
                case RtdeValueKind.UInt64:
                    val.TryGetUInt64(out ulong u64Val);
                    PackageSerializer.WriteUInt64(buf, ref offset, u64Val);
                    break;
                case RtdeValueKind.Int32:
                    val.TryGetInt32(out int i32Val);
                    PackageSerializer.WriteInt32(buf, ref offset, i32Val);
                    break;
                case RtdeValueKind.Double:
                    val.TryGetDouble(out double dblVal);
                    PackageSerializer.WriteDouble(buf, ref offset, dblVal);
                    break;
                case RtdeValueKind.Vector3d:
                    val.TryGetVector3d(out var v3d);
                    if (v3d != null) PackageSerializer.WriteVector3d(buf, ref offset, v3d);
                    break;
                case RtdeValueKind.Vector6d:
                    val.TryGetVector6d(out var v6d);
                    if (v6d != null) PackageSerializer.WriteVector6d(buf, ref offset, v6d);
                    break;
                case RtdeValueKind.Vector6Int32:
                    val.TryGetVector6Int32(out var v6i);
                    if (v6i != null) for (int k = 0; k < 6; k++) PackageSerializer.WriteInt32(buf, ref offset, v6i[k]);
                    break;
                case RtdeValueKind.Vector6UInt32:
                    val.TryGetVector6UInt32(out var v6u);
                    if (v6u != null) for (int k = 0; k < 6; k++) PackageSerializer.WriteUInt32(buf, ref offset, v6u[k]);
                    break;
            }
        }

        private static bool TryConvert<T>(RtdeValue val, out T result)
        {
            switch (val.Kind)
            {
                case RtdeValueKind.Bool when typeof(T) == typeof(bool):
                    val.TryGetBool(out bool boolV); result = (T)(object)boolV; return true;
                case RtdeValueKind.UInt8 when typeof(T) == typeof(byte):
                    val.TryGetUInt8(out byte u8V); result = (T)(object)u8V; return true;
                case RtdeValueKind.UInt32 when typeof(T) == typeof(uint):
                    val.TryGetUInt32(out uint u32V); result = (T)(object)u32V; return true;
                case RtdeValueKind.UInt64 when typeof(T) == typeof(ulong):
                    val.TryGetUInt64(out ulong u64V); result = (T)(object)u64V; return true;
                case RtdeValueKind.Int32 when typeof(T) == typeof(int):
                    val.TryGetInt32(out int i32V); result = (T)(object)i32V; return true;
                case RtdeValueKind.Double when typeof(T) == typeof(double):
                    val.TryGetDouble(out double dblV); result = (T)(object)dblV; return true;
                case RtdeValueKind.Vector6d when typeof(T) == typeof(double[]):
                    val.TryGetVector6d(out var v6d); result = (T)(object)v6d!; return v6d != null;
                case RtdeValueKind.Vector3d when typeof(T) == typeof(double[]):
                    val.TryGetVector3d(out var v3d); result = (T)(object)v3d!; return v3d != null;
                case RtdeValueKind.Vector6Int32 when typeof(T) == typeof(int[]):
                    val.TryGetVector6Int32(out var v6i); result = (T)(object)v6i!; return v6i != null;
                case RtdeValueKind.Vector6UInt32 when typeof(T) == typeof(uint[]):
                    val.TryGetVector6UInt32(out var v6u); result = (T)(object)v6u!; return v6u != null;
                case RtdeValueKind.String when typeof(T) == typeof(string):
                    val.TryGetString(out var str); result = (T)(object)str!; return str != null;
            }
            result = default!;
            return false;
        }

        internal void CopyFrom(DataPackage source)
        {
            if (_recipe.Count != source._recipe.Count) throw new ArgumentException(nameof(source));
            for (int index = 0; index < _recipe.Count; index++)
                if (_recipe[index] != source._recipe[index]) throw new ArgumentException(nameof(source));
            _protocolVersion = source._protocolVersion;
            RecipeId = source.RecipeId;
            foreach (var entry in source._fieldMeta) _fieldMeta[entry.Key] = entry.Value;
            _data = (RtdeValue[])source._data.Clone();
        }

        private static RtdeValue CreateRtdeValue<T>(RtdeValueKind kind, T value)
        {
            object v = value!;
            return kind switch
            {
                RtdeValueKind.Bool => RtdeValue.FromBool((bool)v),
                RtdeValueKind.UInt8 => RtdeValue.FromUInt8((byte)v),
                RtdeValueKind.UInt32 => RtdeValue.FromUInt32((uint)v),
                RtdeValueKind.UInt64 => RtdeValue.FromUInt64((ulong)v),
                RtdeValueKind.Int32 => RtdeValue.FromInt32((int)v),
                RtdeValueKind.Double => RtdeValue.FromDouble((double)v),
                RtdeValueKind.Vector3d => RtdeValue.FromVector3d((double[])v),
                RtdeValueKind.Vector6d => RtdeValue.FromVector6d((double[])v),
                RtdeValueKind.Vector6Int32 => RtdeValue.FromVector6Int32((int[])v),
                RtdeValueKind.Vector6UInt32 => RtdeValue.FromVector6UInt32((uint[])v),
                RtdeValueKind.String => RtdeValue.FromString((string)v),
                _ => RtdeValue.FromBool(false)
            };
        }
    }
}
