// Licensed under the Apache License, Version 2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalRobotics.Models;
using UniversalRobotics.Protocol;

namespace UniversalRobotics.Control
{
    /// <summary>
    /// Encodes the motion primitive type sent over the trajectory interface.
    /// Values must stay in sync with <c>MOTION_TYPE_*</c> constants in <c>external_control.urscript</c>.
    /// </summary>
    public enum MotionType : byte
    {
        /// <summary>movej to a joint configuration.</summary>
        MoveJ = 0,
        /// <summary>movel to a Cartesian pose.</summary>
        MoveL = 1,
        /// <summary>movep to a Cartesian pose.</summary>
        MoveP = 2,
        /// <summary>movec with via and target as Cartesian poses.</summary>
        MoveC = 3,
        /// <summary>optimovej to a joint configuration (PolyScope ≥ 5.21 / 10.8).</summary>
        OptimoveJ = 4,
        /// <summary>optimovel to a Cartesian pose (PolyScope ≥ 5.21 / 10.8).</summary>
        OptimoveL = 5,
        /// <summary>movej to a Cartesian pose (IK-solved).</summary>
        MoveJPose = 6,
        /// <summary>movel to a joint configuration.</summary>
        MoveLJoint = 7,
        /// <summary>movep to a joint configuration.</summary>
        MovePJoint = 8,
        /// <summary>movec with joint via and target.</summary>
        MoveCJoint = 9,
        /// <summary>movec with Cartesian via, joint target.</summary>
        MoveCJointPose = 10,
        /// <summary>movec with joint via, Cartesian target.</summary>
        MoveCPoseJoint = 11,
        /// <summary>optimovej to a Cartesian pose (IK-solved).</summary>
        OptimoveJPose = 12,
        /// <summary>optimovel to a joint configuration.</summary>
        OptimoveLJoint = 13,
        /// <summary>Spline trajectory point.</summary>
        Spline = 51,
        /// <summary>Unknown/unset motion type.</summary>
        Unknown = 255
    }

    /// <summary>Spline interpolation type for trajectory points.</summary>
    public enum TrajectorySplineType : int
    {
        /// <summary>Cubic spline interpolation.</summary>
        Cubic = 1,
        /// <summary>Quintic spline interpolation.</summary>
        Quintic = 2
    }

    /// <summary>
    /// Abstract base for all motion primitives sent to the robot via the trajectory interface.
    /// </summary>
    public abstract class MotionPrimitive
    {
        /// <summary>Gets or sets the motion type discriminator.</summary>
        public MotionType Type { get; set; } = MotionType.Unknown;

        /// <summary>Gets or sets the segment duration in seconds (0 = use acceleration/velocity).</summary>
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;

        /// <summary>Gets or sets the joint acceleration [rad/s²] or tool acceleration [m/s²].</summary>
        public double Acceleration { get; set; } = 1.4;

        /// <summary>Gets or sets the joint speed [rad/s] or tool speed [m/s].</summary>
        public double Velocity { get; set; } = 1.04;

        /// <summary>Gets or sets the blend radius in meters.</summary>
        public double BlendRadius { get; set; } = 0.0;
    }

    /// <summary>A movej motion primitive with a joint configuration or Cartesian target.</summary>
    public sealed class MoveJPrimitive : MotionPrimitive
    {
        /// <summary>Gets or sets the motion target (joint angles or Cartesian pose for IK).</summary>
        public MotionTarget Target { get; set; }

        /// <summary>Creates a MoveJ primitive to a joint target.</summary>
        public MoveJPrimitive(JointAngles target)
        {
            Target = new MotionTarget(target);
            Type = MotionType.MoveJ;
        }

        /// <summary>Creates a MoveJ primitive to a Cartesian target (IK-solved).</summary>
        public MoveJPrimitive(CartesianPose target)
        {
            Target = new MotionTarget(target);
            Type = MotionType.MoveJPose;
        }
    }

    /// <summary>A movel motion primitive with a Cartesian or joint target.</summary>
    public sealed class MoveLPrimitive : MotionPrimitive
    {
        /// <summary>Gets or sets the motion target.</summary>
        public MotionTarget Target { get; set; }

        /// <summary>Creates a MoveL primitive to a Cartesian target.</summary>
        public MoveLPrimitive(CartesianPose target)
        {
            Target = new MotionTarget(target);
            Type = MotionType.MoveL;
        }

        /// <summary>Creates a MoveL primitive to a joint target.</summary>
        public MoveLPrimitive(JointAngles target)
        {
            Target = new MotionTarget(target);
            Type = MotionType.MoveLJoint;
        }
    }

    /// <summary>A movep motion primitive.</summary>
    public sealed class MovePPrimitive : MotionPrimitive
    {
        /// <summary>Gets or sets the motion target.</summary>
        public MotionTarget Target { get; set; }

        /// <summary>Creates a MoveP primitive to a Cartesian target.</summary>
        public MovePPrimitive(CartesianPose target)
        {
            Target = new MotionTarget(target);
            Type = MotionType.MoveP;
        }

        /// <summary>Creates a MoveP primitive to a joint target.</summary>
        public MovePPrimitive(JointAngles target)
        {
            Target = new MotionTarget(target);
            Type = MotionType.MovePJoint;
        }
    }

    /// <summary>A movec (circular) motion primitive with a via point and target.</summary>
    public sealed class MoveCPrimitive : MotionPrimitive
    {
        /// <summary>Gets or sets the via point for the circular arc.</summary>
        public MotionTarget ViaPoint { get; set; }

        /// <summary>Gets or sets the final target.</summary>
        public MotionTarget Target { get; set; }

        /// <summary>Gets or sets the arc mode (0 = unconstrained, 1 = fixed).</summary>
        public int ArcMode { get; set; } = 0;

        /// <summary>Creates a MoveC primitive with Cartesian via and target.</summary>
        public MoveCPrimitive(CartesianPose via, CartesianPose target)
        {
            ViaPoint = new MotionTarget(via);
            Target = new MotionTarget(target);
            Type = MotionType.MoveC;
        }
    }

    /// <summary>An optimovej motion primitive (requires PolyScope ≥ 5.21 / 10.8).</summary>
    public sealed class OptimoveJPrimitive : MotionPrimitive
    {
        /// <summary>Gets or sets the motion target.</summary>
        public MotionTarget Target { get; set; }

        /// <summary>Creates an OptimoveJ primitive to a joint target.</summary>
        public OptimoveJPrimitive(JointAngles target)
        {
            Target = new MotionTarget(target);
            Type = MotionType.OptimoveJ;
        }

        /// <summary>Creates an OptimoveJ primitive to a Cartesian target (IK-solved).</summary>
        public OptimoveJPrimitive(CartesianPose target)
        {
            Target = new MotionTarget(target);
            Type = MotionType.OptimoveJPose;
        }
    }

    /// <summary>An optimovel motion primitive (requires PolyScope ≥ 5.21 / 10.8).</summary>
    public sealed class OptimoveLPrimitive : MotionPrimitive
    {
        /// <summary>Gets or sets the motion target.</summary>
        public MotionTarget Target { get; set; }

        /// <summary>Creates an OptimoveL primitive to a Cartesian target.</summary>
        public OptimoveLPrimitive(CartesianPose target)
        {
            Target = new MotionTarget(target);
            Type = MotionType.OptimoveL;
        }

        /// <summary>Creates an OptimoveL primitive to a joint target.</summary>
        public OptimoveLPrimitive(JointAngles target)
        {
            Target = new MotionTarget(target);
            Type = MotionType.OptimoveLJoint;
        }
    }

    /// <summary>A spline trajectory point primitive.</summary>
    public sealed class SplinePrimitive : MotionPrimitive
    {
        /// <summary>Gets or sets the 6-element position array (joint angles or Cartesian).</summary>
        public double[] Positions { get; set; } = new double[6];

        /// <summary>Gets or sets the 6-element velocity array.</summary>
        public double[] Velocities { get; set; } = new double[6];

        /// <summary>Gets or sets the 6-element acceleration array.</summary>
        public double[] Accelerations { get; set; } = new double[6];

        /// <summary>Gets or sets the spline type (cubic or quintic).</summary>
        public TrajectorySplineType SplineType { get; set; } = TrajectorySplineType.Cubic;

        /// <summary>Creates a spline primitive.</summary>
        public SplinePrimitive() => Type = MotionType.Spline;
    }
}
