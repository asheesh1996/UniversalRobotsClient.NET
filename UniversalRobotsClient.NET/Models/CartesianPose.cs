// Copyright 2026 Universal Robots A/S
// Licensed under the Apache License, Version 2.0

using System;

namespace UniversalRobotics.Models
{
    /// <summary>
    /// Represents a Cartesian pose (position + orientation) for a robot TCP or tool frame.
    /// Position is expressed in meters (x, y, z) and orientation as an Axis-Angle vector
    /// (rx, ry, rz) in radians, following the URScript convention.
    /// </summary>
    public sealed class CartesianPose : IEquatable<CartesianPose>
    {
        /// <summary>Gets or sets the X position in meters.</summary>
        public double X { get; set; }
        /// <summary>Gets or sets the Y position in meters.</summary>
        public double Y { get; set; }
        /// <summary>Gets or sets the Z position in meters.</summary>
        public double Z { get; set; }
        /// <summary>Gets or sets the X component of the axis-angle orientation vector in radians.</summary>
        public double Rx { get; set; }
        /// <summary>Gets or sets the Y component of the axis-angle orientation vector in radians.</summary>
        public double Ry { get; set; }
        /// <summary>Gets or sets the Z component of the axis-angle orientation vector in radians.</summary>
        public double Rz { get; set; }

        /// <summary>
        /// Gets or sets an optional joint configuration hint for the inverse kinematics solver.
        /// When set, the trajectory interface passes this as the <c>q_near</c> seed to the IK solver
        /// on the robot, allowing the desired joint angle configuration to be selected.
        /// </summary>
        public JointAngles? QNear { get; set; }

        /// <summary>Initializes a default (zero) Cartesian pose.</summary>
        public CartesianPose() { }

        /// <summary>Initializes a Cartesian pose without an IK hint.</summary>
        public CartesianPose(double x, double y, double z, double rx, double ry, double rz)
        {
            X = x; Y = y; Z = z; Rx = rx; Ry = ry; Rz = rz;
        }

        /// <summary>Initializes a Cartesian pose with an IK seed hint.</summary>
        public CartesianPose(double x, double y, double z, double rx, double ry, double rz, JointAngles qNear)
            : this(x, y, z, rx, ry, rz)
        {
            QNear = qNear;
        }

        /// <summary>
        /// Returns the pose values as a 6-element array [x, y, z, rx, ry, rz].
        /// </summary>
        public double[] ToArray() => new[] { X, Y, Z, Rx, Ry, Rz };

        /// <inheritdoc />
        public bool Equals(CartesianPose? other)
            => other is not null
               && X == other.X && Y == other.Y && Z == other.Z
               && Rx == other.Rx && Ry == other.Ry && Rz == other.Rz;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is CartesianPose p && Equals(p);

        /// <inheritdoc />
        public override int GetHashCode() => (X, Y, Z, Rx, Ry, Rz).GetHashCode();

        /// <inheritdoc />
        public override string ToString()
            => $"[{X:F4}, {Y:F4}, {Z:F4}, {Rx:F4}, {Ry:F4}, {Rz:F4}]";

        /// <summary>Compares two poses for equality.</summary>
        public static bool operator ==(CartesianPose? left, CartesianPose? right)
            => left?.Equals(right) ?? right is null;

        /// <summary>Compares two poses for inequality.</summary>
        public static bool operator !=(CartesianPose? left, CartesianPose? right) => !(left == right);
    }

    /// <summary>
    /// A discriminated union type that can hold either a <see cref="JointAngles"/> (joint-space)
    /// target or a <see cref="CartesianPose"/> (Cartesian-space) target.
    /// Used in motion functions that accept either target type.
    /// </summary>
    public sealed class MotionTarget
    {
        /// <summary>Gets the joint-space target, if this instance holds one.</summary>
        public JointAngles? JointTarget { get; }

        /// <summary>Gets the Cartesian target, if this instance holds one.</summary>
        public CartesianPose? CartesianTarget { get; }

        /// <summary>Returns <see langword="true"/> if this target is a joint configuration.</summary>
        public bool IsJoint => JointTarget is not null;

        /// <summary>Returns <see langword="true"/> if this target is a Cartesian pose.</summary>
        public bool IsCartesian => CartesianTarget is not null;

        /// <summary>Creates a joint-space motion target.</summary>
        public MotionTarget(JointAngles jointTarget) => JointTarget = jointTarget;

        /// <summary>Creates a Cartesian-space motion target.</summary>
        public MotionTarget(CartesianPose cartesianTarget) => CartesianTarget = cartesianTarget;

        /// <summary>Implicitly converts <see cref="JointAngles"/> to a <see cref="MotionTarget"/>.</summary>
        public static implicit operator MotionTarget(JointAngles q) => new(q);

        /// <summary>Implicitly converts <see cref="CartesianPose"/> to a <see cref="MotionTarget"/>.</summary>
        public static implicit operator MotionTarget(CartesianPose p) => new(p);
    }
}
