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
            var buf = BuildTrajectoryMessage(positions, null, null, goalTime, blendRadius, 0f,
                cartesian ? MotionType.MoveL : MotionType.MoveJ);
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

        private static byte[] BuildMessageFromPrimitive(MotionPrimitive prim)
        {
            double[] positions = new double[6];
            double[] via = new double[6];
            bool hasVia = false;

            switch (prim)
            {
                case MoveJPrimitive mj:
                    positions = ExtractPositions(mj.Target);
                    break;
                case MoveLPrimitive ml:
                    positions = ExtractPositions(ml.Target);
                    break;
                case MovePPrimitive mp:
                    positions = ExtractPositions(mp.Target);
                    break;
                case MoveCPrimitive mc:
                    positions = ExtractPositions(mc.Target);
                    via = ExtractPositions(mc.ViaPoint);
                    hasVia = true;
                    break;
                case OptimoveJPrimitive oj:
                    positions = ExtractPositions(oj.Target);
                    break;
                case OptimoveLPrimitive ol:
                    positions = ExtractPositions(ol.Target);
                    break;
                case SplinePrimitive sp:
                    positions = sp.Positions;
                    break;
            }

            return BuildTrajectoryMessage(
                positions,
                hasVia ? via : null,
                prim is SplinePrimitive spline ? spline.Velocities : null,
                (float)prim.Duration.TotalSeconds,
                (float)prim.BlendRadius,
                (float)prim.Acceleration,
                prim.Type);
        }

        private static byte[] BuildTrajectoryMessage(
            double[] positions,
            double[]? via,
            double[]? velocities,
            float goalTime,
            float blendRadius,
            float acceleration,
            MotionType motionType)
        {
            var buf = new byte[MessageSizeBytes];
            int offset = 0;

            // [0..5] positions (× MULT_JOINTSTATE)
            for (int i = 0; i < 6; i++)
                PackageSerializer.WriteInt32(buf, ref offset, (int)(positions[i] * MultJointState));

            // [6..11] via or velocities
            if (via != null)
                for (int i = 0; i < 6; i++)
                    PackageSerializer.WriteInt32(buf, ref offset, (int)(via[i] * MultJointState));
            else if (velocities != null)
                for (int i = 0; i < 6; i++)
                    PackageSerializer.WriteInt32(buf, ref offset, (int)(velocities[i] * MultJointState));
            else
                for (int i = 0; i < 6; i++) PackageSerializer.WriteInt32(buf, ref offset, 0);

            // [12] acceleration / or accelerations[0]
            PackageSerializer.WriteInt32(buf, ref offset, (int)(acceleration * MultJointState));
            // [13] velocity
            PackageSerializer.WriteInt32(buf, ref offset, 0);
            // [14] arc mode / reserved
            PackageSerializer.WriteInt32(buf, ref offset, 0);
            // [15..17] reserved
            PackageSerializer.WriteInt32(buf, ref offset, 0);
            PackageSerializer.WriteInt32(buf, ref offset, 0);
            PackageSerializer.WriteInt32(buf, ref offset, 0);

            // [18] duration (μs)
            PackageSerializer.WriteInt32(buf, ref offset, (int)(goalTime * MultJointState));

            // [19] blend radius (× MULT_JOINTSTATE)
            PackageSerializer.WriteInt32(buf, ref offset, (int)(blendRadius * MultJointState));

            // [20] motion type
            PackageSerializer.WriteInt32(buf, ref offset, (int)motionType);

            return buf;
        }

        private static readonly int MultJointState = ReverseInterface.MultJointState;

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
