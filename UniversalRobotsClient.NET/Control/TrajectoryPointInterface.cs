// Licensed under the Apache License, Version 2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalRobotics.Models;
using UniversalRobotics.Protocol;
using UniversalRobotics.Transport;

namespace UniversalRobotics.Control
{
    /// <summary>
    /// Forwards trajectory points to the robot for native execution using movej/movel/movec/splines.
    /// Communication uses a 21 × int32 big-endian wire format per trajectory point.
    /// The robot sends back a <see cref="TrajectoryResult"/> when execution completes.
    /// Replaces the C++ <c>TrajectoryPointInterface</c> class.
    /// </summary>
    public sealed class TrajectoryPointInterface : ReverseInterface
    {
        /// <summary>Number of int32 fields in each trajectory wire message.</summary>
        public const int MessageLength = 21;

        private const int MessageSizeBytes = MessageLength * 4;

        /// <summary>Modern spline velocity and acceleration encoding multiplier.</summary>
        public const int MultVelocityAcceleration = 100_000_000;
        private int _velocityAccelerationMultiplier = MultVelocityAcceleration;

        /// <summary>Multiplier matching the robot script; use 1,000,000 for legacy scripts.</summary>
        public int VelocityAccelerationMultiplier
        {
            get => _velocityAccelerationMultiplier;
            set => _velocityAccelerationMultiplier = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }

        private TaskCompletionSource<TrajectoryResult>? _resultTcs;

        /// <summary>Raised when the robot reports the result of a trajectory execution.</summary>
        public event EventHandler<TrajectoryResult>? TrajectoryResultReceived;

        /// <summary>
        /// Creates a trajectory point interface with the given configuration.
        /// </summary>
        public TrajectoryPointInterface(ReverseInterfaceConfig config, ILogger? logger = null)
            : base(config, logger) { }

        /// <summary>
        /// Creates a trajectory point interface on the given port.
        /// </summary>
        public TrajectoryPointInterface(int port = 50003, ILogger? logger = null)
            : this(new ReverseInterfaceConfig { Port = port }, logger) { }

        // ─── Trajectory point sending ─────────────────────────────────────────────

        /// <summary>
        /// Sends a simple trajectory point (position, goal time, blend radius, Cartesian/joint flag).
        /// </summary>
        public async Task<bool> WriteTrajectoryPointAsync(
            double[] positions,
            float goalTime,
            float blendRadius,
            bool cartesian,
            CancellationToken ct = default)
        {
            if (positions == null || positions.Length != 6) throw new ArgumentException("Six positions are required.", nameof(positions));
            MotionPrimitive primitive = cartesian
                ? new MoveLPrimitive(new CartesianPose(positions[0], positions[1], positions[2], positions[3], positions[4], positions[5]))
                : new MoveJPrimitive(new JointAngles(positions));
            primitive.Duration = TimeSpan.FromSeconds(goalTime);
            primitive.BlendRadius = blendRadius;
            var buf = BuildMessageFromPrimitive(primitive);
            return await SendToRobotAsync(buf, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a motion primitive over the trajectory interface.
        /// </summary>
        public async Task<bool> WriteMotionPrimitiveAsync(MotionPrimitive primitive, CancellationToken ct = default)
        {
            var buf = BuildMessageFromPrimitive(primitive);
            return await SendToRobotAsync(buf, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts waiting for a trajectory result from the robot.
        /// Returns when the robot sends back a <see cref="TrajectoryResult"/>.
        /// </summary>
        public Task<TrajectoryResult> WaitForResultAsync(CancellationToken ct = default)
        {
            _resultTcs = new TaskCompletionSource<TrajectoryResult>();
            ct.Register(() => _resultTcs?.TrySetCanceled());
            return _resultTcs.Task;
        }

        // ─── Message construction ─────────────────────────────────────────────────

        private byte[] BuildMessageFromPrimitive(MotionPrimitive prim)
        {
            if (prim == null) throw new ArgumentNullException(nameof(prim));
            if (prim.Duration < TimeSpan.Zero || prim.BlendRadius < 0 || prim.Acceleration < 0 || prim.Velocity < 0)
                throw new ArgumentOutOfRangeException(nameof(prim));
            var first = new double[6];
            var second = new double[6];
            var third = new double[] { prim.Velocity, prim.Acceleration, 0, 0, 0, 0 };
            MotionTarget? target = prim switch
            {
                MoveJPrimitive move => move.Target,
                MoveLPrimitive move => move.Target,
                MovePPrimitive move => move.Target,
                MoveCPrimitive move => move.Target,
                OptimoveJPrimitive move => move.Target,
                OptimoveLPrimitive move => move.Target,
                SplinePrimitive _ => null,
                _ => throw new ArgumentException("Unsupported motion primitive.", nameof(prim))
            };
            if (target != null)
            {
                first = ExtractPositions(target);
                if (target.CartesianTarget?.QNear != null)
                {
                    second = target.CartesianTarget.QNear.ToArray();
                    third[2] = 1;
                }
            }
            if (prim is MoveCPrimitive circular)
            {
                if (circular.ArcMode < 0 || circular.ArcMode > 1) throw new ArgumentOutOfRangeException(nameof(prim));
                second = ExtractPositions(circular.ViaPoint);
                third[2] = circular.ArcMode;
            }
            if (prim is SplinePrimitive spline)
            {
                if (spline.SplineType != TrajectorySplineType.Cubic && spline.SplineType != TrajectorySplineType.Quintic)
                    throw new ArgumentOutOfRangeException(nameof(prim));
                first = spline.Positions;
                second = spline.Velocities;
                third = spline.SplineType == TrajectorySplineType.Quintic ? spline.Accelerations : new double[6];
            }
            var buf = new byte[MessageSizeBytes];
            int offset = 0;
            int derivativeMultiplier = prim is SplinePrimitive ? VelocityAccelerationMultiplier : MultJointState;
            WriteBlock(buf, ref offset, first, MultJointState);
            WriteBlock(buf, ref offset, second, derivativeMultiplier);
            WriteBlock(buf, ref offset, third, derivativeMultiplier);
            PackageSerializer.WriteInt32(buf, ref offset, Encode(prim.Duration.TotalSeconds, MultTime));
            PackageSerializer.WriteInt32(buf, ref offset, prim is SplinePrimitive point ? (int)point.SplineType : Encode(prim.BlendRadius, MultJointState));
            PackageSerializer.WriteInt32(buf, ref offset, (int)prim.Type);
            return buf;
        }

        private static void WriteBlock(byte[] buffer, ref int offset, double[] values, int multiplier)
        {
            if (values == null || values.Length != 6) throw new ArgumentException("Six values are required.", nameof(values));
            foreach (var value in values)
                PackageSerializer.WriteInt32(buffer, ref offset, Encode(value, multiplier));
        }

        private static int Encode(double value, int multiplier)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > (double)int.MaxValue / multiplier)
                throw new ArgumentOutOfRangeException(nameof(value));
            return checked((int)Math.Round(value * multiplier, MidpointRounding.AwayFromZero));
        }

        private static double[] ExtractPositions(MotionTarget target)
        {
            if (target.IsJoint)
                return target.JointTarget!.ToArray();
            var p = target.CartesianTarget!;
            return new[] { p.X, p.Y, p.Z, p.Rx, p.Ry, p.Rz };
        }

        // ─── Feedback handling ────────────────────────────────────────────────────

        protected override void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            // The robot sends back a single int32 trajectory result
            if (e.Length >= 4)
            {
                var parser = new BinaryParser(e.Data, 0, 4);
                var result = (TrajectoryResult)parser.ReadInt32();
                TrajectoryResultReceived?.Invoke(this, result);
                _resultTcs?.TrySetResult(result);
                _resultTcs = null;
            }
        }
    }
}
