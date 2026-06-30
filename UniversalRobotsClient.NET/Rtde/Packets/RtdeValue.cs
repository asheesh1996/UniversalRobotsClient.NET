// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalRobotics.Rtde.Packets
{
    /// <summary>
    /// Discriminator for the type of value stored in an <see cref="RtdeValue"/>.
    /// </summary>
    public enum RtdeValueKind
    {
        /// <summary>Boolean value.</summary>
        Bool,
        /// <summary>Unsigned 8-bit integer.</summary>
        UInt8,
        /// <summary>Unsigned 32-bit integer.</summary>
        UInt32,
        /// <summary>Unsigned 64-bit integer.</summary>
        UInt64,
        /// <summary>Signed 32-bit integer.</summary>
        Int32,
        /// <summary>Double-precision floating-point.</summary>
        Double,
        /// <summary>3-element double array (vector3d).</summary>
        Vector3d,
        /// <summary>6-element double array (vector6d).</summary>
        Vector6d,
        /// <summary>6-element int32 array (vector6int32).</summary>
        Vector6Int32,
        /// <summary>6-element uint32 array (vector6uint32).</summary>
        Vector6UInt32,
        /// <summary>String value.</summary>
        String,
        /// <summary>Value is not set.</summary>
        None
    }

    /// <summary>
    /// A type-discriminated value container for RTDE data fields.
    /// Replaces C++ <c>std::variant&lt;bool, uint8_t, uint32_t, uint64_t, int32_t, double,
    /// vector3d_t, vector6d_t, vector6int32_t, vector6uint32_t, std::string&gt;</c>.
    /// </summary>
    public sealed class RtdeValue
    {
        private readonly object? _value;

        /// <summary>Gets the type kind of the stored value.</summary>
        public RtdeValueKind Kind { get; }

        private RtdeValue(RtdeValueKind kind, object? value) { Kind = kind; _value = value; }

        // ─── Factory methods ──────────────────────────────────────────────────────

        /// <summary>Creates a <see cref="RtdeValueKind.Bool"/> value.</summary>
        public static RtdeValue FromBool(bool value) => new(RtdeValueKind.Bool, value);
        /// <summary>Creates a <see cref="RtdeValueKind.UInt8"/> value.</summary>
        public static RtdeValue FromUInt8(byte value) => new(RtdeValueKind.UInt8, value);
        /// <summary>Creates a <see cref="RtdeValueKind.UInt32"/> value.</summary>
        public static RtdeValue FromUInt32(uint value) => new(RtdeValueKind.UInt32, value);
        /// <summary>Creates a <see cref="RtdeValueKind.UInt64"/> value.</summary>
        public static RtdeValue FromUInt64(ulong value) => new(RtdeValueKind.UInt64, value);
        /// <summary>Creates a <see cref="RtdeValueKind.Int32"/> value.</summary>
        public static RtdeValue FromInt32(int value) => new(RtdeValueKind.Int32, value);
        /// <summary>Creates a <see cref="RtdeValueKind.Double"/> value.</summary>
        public static RtdeValue FromDouble(double value) => new(RtdeValueKind.Double, value);
        /// <summary>Creates a <see cref="RtdeValueKind.Vector3d"/> value (3-element array).</summary>
        public static RtdeValue FromVector3d(double[] value) => new(RtdeValueKind.Vector3d, (double[])value.Clone());
        /// <summary>Creates a <see cref="RtdeValueKind.Vector6d"/> value (6-element array).</summary>
        public static RtdeValue FromVector6d(double[] value) => new(RtdeValueKind.Vector6d, (double[])value.Clone());
        /// <summary>Creates a <see cref="RtdeValueKind.Vector6Int32"/> value (6-element int array).</summary>
        public static RtdeValue FromVector6Int32(int[] value) => new(RtdeValueKind.Vector6Int32, (int[])value.Clone());
        /// <summary>Creates a <see cref="RtdeValueKind.Vector6UInt32"/> value (6-element uint array).</summary>
        public static RtdeValue FromVector6UInt32(uint[] value) => new(RtdeValueKind.Vector6UInt32, (uint[])value.Clone());
        /// <summary>Creates a <see cref="RtdeValueKind.String"/> value.</summary>
        public static RtdeValue FromString(string value) => new(RtdeValueKind.String, value);

        // ─── Typed accessors ──────────────────────────────────────────────────────

        /// <summary>Attempts to get the value as <see cref="bool"/>.</summary>
        public bool TryGetBool(out bool result)
        {
            if (Kind == RtdeValueKind.Bool) { result = (bool)_value!; return true; }
            result = default; return false;
        }

        /// <summary>Attempts to get the value as <see cref="byte"/>.</summary>
        public bool TryGetUInt8(out byte result)
        {
            if (Kind == RtdeValueKind.UInt8) { result = (byte)_value!; return true; }
            result = default; return false;
        }

        /// <summary>Attempts to get the value as <see cref="uint"/>.</summary>
        public bool TryGetUInt32(out uint result)
        {
            if (Kind == RtdeValueKind.UInt32) { result = (uint)_value!; return true; }
            result = default; return false;
        }

        /// <summary>Attempts to get the value as <see cref="ulong"/>.</summary>
        public bool TryGetUInt64(out ulong result)
        {
            if (Kind == RtdeValueKind.UInt64) { result = (ulong)_value!; return true; }
            result = default; return false;
        }

        /// <summary>Attempts to get the value as <see cref="int"/>.</summary>
        public bool TryGetInt32(out int result)
        {
            if (Kind == RtdeValueKind.Int32) { result = (int)_value!; return true; }
            result = default; return false;
        }

        /// <summary>Attempts to get the value as <see cref="double"/>.</summary>
        public bool TryGetDouble(out double result)
        {
            if (Kind == RtdeValueKind.Double) { result = (double)_value!; return true; }
            result = default; return false;
        }

        /// <summary>Attempts to get the value as a 3-element <see cref="double"/> array.</summary>
        public bool TryGetVector3d(out double[]? result)
        {
            if (Kind == RtdeValueKind.Vector3d) { result = (double[])_value!; return true; }
            result = null; return false;
        }

        /// <summary>Attempts to get the value as a 6-element <see cref="double"/> array.</summary>
        public bool TryGetVector6d(out double[]? result)
        {
            if (Kind == RtdeValueKind.Vector6d) { result = (double[])_value!; return true; }
            result = null; return false;
        }

        /// <summary>Attempts to get the value as a 6-element <see cref="int"/> array.</summary>
        public bool TryGetVector6Int32(out int[]? result)
        {
            if (Kind == RtdeValueKind.Vector6Int32) { result = (int[])_value!; return true; }
            result = null; return false;
        }

        /// <summary>Attempts to get the value as a 6-element <see cref="uint"/> array.</summary>
        public bool TryGetVector6UInt32(out uint[]? result)
        {
            if (Kind == RtdeValueKind.Vector6UInt32) { result = (uint[])_value!; return true; }
            result = null; return false;
        }

        /// <summary>Attempts to get the value as a <see cref="string"/>.</summary>
        public bool TryGetString(out string? result)
        {
            if (Kind == RtdeValueKind.String) { result = (string)_value!; return true; }
            result = null; return false;
        }

        /// <inheritdoc />
        public override string ToString() => _value?.ToString() ?? "(none)";
    }

    /// <summary>Maps RTDE type name strings (as returned by the robot during recipe setup) to their <see cref="RtdeValueKind"/>.</summary>
    public static class RtdeTypeMap
    {
        private static readonly Dictionary<string, RtdeValueKind> s_map = new Dictionary<string, RtdeValueKind>(StringComparer.Ordinal)
        {
            ["BOOL"] = RtdeValueKind.Bool,
            ["UINT8"] = RtdeValueKind.UInt8,
            ["UINT32"] = RtdeValueKind.UInt32,
            ["UINT64"] = RtdeValueKind.UInt64,
            ["INT32"] = RtdeValueKind.Int32,
            ["DOUBLE"] = RtdeValueKind.Double,
            ["VECTOR3D"] = RtdeValueKind.Vector3d,
            ["VECTOR6D"] = RtdeValueKind.Vector6d,
            ["VECTOR6INT32"] = RtdeValueKind.Vector6Int32,
            ["VECTOR6UINT32"] = RtdeValueKind.Vector6UInt32,
            ["STRING"] = RtdeValueKind.String,
            ["IN_USE"] = RtdeValueKind.None,
            ["NOT_FOUND"] = RtdeValueKind.None
        };

        /// <summary>
        /// Resolves an RTDE type name string to its <see cref="RtdeValueKind"/>.
        /// Returns <see cref="RtdeValueKind.None"/> for unknown or unsupported types.
        /// </summary>
        public static RtdeValueKind Resolve(string typeName)
            => s_map.TryGetValue(typeName, out var kind) ? kind : RtdeValueKind.None;
    }
}
