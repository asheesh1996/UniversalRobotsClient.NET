// Copyright 2026 Universal Robots A/S
// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;

namespace UniversalRobotics.Models
{
    /// <summary>
    /// Represents a joint configuration — a 6-element array of joint angles in radians.
    /// This type unambiguously expresses "these six values are joint positions", as opposed
    /// to a Cartesian <see cref="CartesianPose"/>, and is used throughout the motion API.
    /// </summary>
    public sealed class JointAngles : IEquatable<JointAngles>
    {
        private readonly double[] _values;

        /// <summary>
        /// Initializes a new <see cref="JointAngles"/> from six individual joint positions.
        /// </summary>
        /// <param name="q1">Joint 1 angle in radians.</param>
        /// <param name="q2">Joint 2 angle in radians.</param>
        /// <param name="q3">Joint 3 angle in radians.</param>
        /// <param name="q4">Joint 4 angle in radians.</param>
        /// <param name="q5">Joint 5 angle in radians.</param>
        /// <param name="q6">Joint 6 angle in radians.</param>
        public JointAngles(double q1, double q2, double q3, double q4, double q5, double q6)
        {
            _values = new[] { q1, q2, q3, q4, q5, q6 };
        }

        /// <summary>
        /// Initializes a new <see cref="JointAngles"/> from an array.
        /// </summary>
        /// <param name="values">Array of exactly 6 joint angles in radians.</param>
        /// <exception cref="ArgumentException">Thrown if <paramref name="values"/> does not contain exactly 6 elements.</exception>
        public JointAngles(double[] values)
        {
            if (values == null || values.Length != 6)
                throw new ArgumentException("JointAngles requires exactly 6 joint values.", nameof(values));
            _values = (double[])values.Clone();
        }

        /// <summary>Gets the joint angle at the specified zero-based joint index (0–5).</summary>
        public double this[int index] => _values[index];

        /// <summary>Gets Joint 1 angle in radians.</summary>
        public double Q1 => _values[0];
        /// <summary>Gets Joint 2 angle in radians.</summary>
        public double Q2 => _values[1];
        /// <summary>Gets Joint 3 angle in radians.</summary>
        public double Q3 => _values[2];
        /// <summary>Gets Joint 4 angle in radians.</summary>
        public double Q4 => _values[3];
        /// <summary>Gets Joint 5 angle in radians.</summary>
        public double Q5 => _values[4];
        /// <summary>Gets Joint 6 angle in radians.</summary>
        public double Q6 => _values[5];

        /// <summary>Returns a copy of the internal values as an array.</summary>
        public double[] ToArray() => (double[])_values.Clone();

        /// <summary>Returns the internal array directly (do not modify).</summary>
        internal double[] GetValues() => _values;

        /// <inheritdoc />
        public bool Equals(JointAngles? other)
        {
            if (other is null) return false;
            for (int i = 0; i < 6; i++)
                if (_values[i] != other._values[i]) return false;
            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is JointAngles q && Equals(q);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                foreach (var v in _values) hash = hash * 31 + v.GetHashCode();
                return hash;
            }
        }

        /// <inheritdoc />
        public override string ToString()
            => $"[{_values[0]:F4}, {_values[1]:F4}, {_values[2]:F4}, {_values[3]:F4}, {_values[4]:F4}, {_values[5]:F4}]";

        /// <summary>Compares two <see cref="JointAngles"/> for equality.</summary>
        public static bool operator ==(JointAngles? left, JointAngles? right)
            => left?.Equals(right) ?? right is null;

        /// <summary>Compares two <see cref="JointAngles"/> for inequality.</summary>
        public static bool operator !=(JointAngles? left, JointAngles? right) => !(left == right);
    }
}
