// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalRobotics.Control;
using UniversalRobotics.Exceptions;
using UniversalRobotics.Models;

namespace UniversalRobotics.Commands
{
    /// <summary>
    /// High-level, blocking motion API built on top of <see cref="UrDriver"/>.
    /// Wraps the trajectory interface to expose simple <c>MoveJAsync</c>, <c>MoveLAsync</c>, etc. calls
    /// that return when the robot has finished the motion.
    /// Replaces the C++ <c>InstructionExecutor</c> class.
    /// </summary>
    public sealed class InstructionExecutor : IDisposable
    {
        private readonly UrDriver _driver;
        private readonly ILogger _logger;
        private bool _disposed;

        /// <summary>
        /// Creates an instruction executor that uses the given <see cref="UrDriver"/> instance.
        /// </summary>
        public InstructionExecutor(UrDriver driver, ILogger? logger = null)
        {
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _logger = logger ?? NullLogger.Instance;
        }

        // ─── Sequence execution ───────────────────────────────────────────────────

        /// <summary>
        /// Executes a sequence of motion primitives on the robot.
        /// Returns when the last primitive completes (or fails/is canceled).
        /// </summary>
        public async Task<bool> ExecuteMotionAsync(
            IReadOnlyList<MotionPrimitive> sequence,
            CancellationToken cancellationToken = default)
        {
            if (sequence == null || sequence.Count == 0) return true;

            var traj = _driver.TrajectoryInterface;
            if (traj == null) throw new NotConnectedException("Trajectory interface is not connected.");

            // Send start signal to the robot
            await _driver.ReverseInterface!.WriteTrajectoryControlAsync(
                TrajectoryControlMessage.Start,
                sequence.Count,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Send all trajectory points
            foreach (var prim in sequence)
            {
                bool sent = await traj.WriteMotionPrimitiveAsync(prim, cancellationToken).ConfigureAwait(false);
                if (!sent) throw new CommunicationException("Failed to send trajectory point to robot.");
            }

            // Wait for the robot's execution result
            var result = await traj.WaitForResultAsync(cancellationToken).ConfigureAwait(false);

            if (result == TrajectoryResult.Canceled)
                throw new OperationCanceledException("Robot trajectory was canceled.");
            if (result == TrajectoryResult.Failure)
                throw new UrException("Robot reported a trajectory execution failure.");

            return result == TrajectoryResult.Success;
        }

        // ─── Single-motion helpers ────────────────────────────────────────────────

        /// <summary>
        /// Moves the robot to the given joint configuration using movej.
        /// </summary>
        public Task<bool> MoveJAsync(
            JointAngles target,
            double acceleration = 1.4,
            double velocity = 1.04,
            double time = 0.0,
            double blendRadius = 0.0,
            CancellationToken cancellationToken = default)
        {
            var prim = new MoveJPrimitive(target)
            {
                Acceleration = acceleration,
                Velocity = velocity,
                Duration = TimeSpan.FromSeconds(time),
                BlendRadius = blendRadius
            };
            return ExecuteMotionAsync(new[] { prim }, cancellationToken);
        }

        /// <summary>
        /// Moves the robot to the given Cartesian pose using movej (IK-solved).
        /// </summary>
        public Task<bool> MoveJAsync(
            CartesianPose target,
            double acceleration = 1.4,
            double velocity = 1.04,
            double time = 0.0,
            double blendRadius = 0.0,
            CancellationToken cancellationToken = default)
        {
            var prim = new MoveJPrimitive(target)
            {
                Acceleration = acceleration,
                Velocity = velocity,
                Duration = TimeSpan.FromSeconds(time),
                BlendRadius = blendRadius
            };
            return ExecuteMotionAsync(new[] { prim }, cancellationToken);
        }

        /// <summary>
        /// Moves the robot's TCP linearly to the given Cartesian pose using movel.
        /// </summary>
        public Task<bool> MoveLAsync(
            CartesianPose target,
            double acceleration = 1.2,
            double velocity = 0.25,
            double time = 0.0,
            double blendRadius = 0.0,
            CancellationToken cancellationToken = default)
        {
            var prim = new MoveLPrimitive(target)
            {
                Acceleration = acceleration,
                Velocity = velocity,
                Duration = TimeSpan.FromSeconds(time),
                BlendRadius = blendRadius
            };
            return ExecuteMotionAsync(new[] { prim }, cancellationToken);
        }

        /// <summary>
        /// Moves the robot's TCP linearly to the joint configuration using movel.
        /// </summary>
        public Task<bool> MoveLAsync(
            JointAngles target,
            double acceleration = 1.2,
            double velocity = 0.25,
            double time = 0.0,
            double blendRadius = 0.0,
            CancellationToken cancellationToken = default)
        {
            var prim = new MoveLPrimitive(target)
            {
                Acceleration = acceleration,
                Velocity = velocity,
                Duration = TimeSpan.FromSeconds(time),
                BlendRadius = blendRadius
            };
            return ExecuteMotionAsync(new[] { prim }, cancellationToken);
        }

        /// <summary>
        /// Moves the robot's TCP along a constant-speed circular arc using movep.
        /// </summary>
        public Task<bool> MovePAsync(
            CartesianPose target,
            double acceleration = 1.2,
            double velocity = 0.25,
            double blendRadius = 0.0,
            CancellationToken cancellationToken = default)
        {
            var prim = new MovePPrimitive(target)
            {
                Acceleration = acceleration,
                Velocity = velocity,
                BlendRadius = blendRadius
            };
            return ExecuteMotionAsync(new[] { prim }, cancellationToken);
        }

        /// <summary>
        /// Moves the robot's TCP along a circular arc defined by a via point and a target.
        /// </summary>
        public Task<bool> MoveCAsync(
            CartesianPose via,
            CartesianPose target,
            double acceleration = 1.2,
            double velocity = 0.25,
            CancellationToken cancellationToken = default)
        {
            var prim = new MoveCPrimitive(via, target)
            {
                Acceleration = acceleration,
                Velocity = velocity
            };
            return ExecuteMotionAsync(new[] { prim }, cancellationToken);
        }

        /// <summary>
        /// Moves the robot using the optimized movej (requires PolyScope ≥ 5.21 / 10.8).
        /// </summary>
        public Task<bool> OptimoveJAsync(
            JointAngles target,
            double acceleration = 1.4,
            double velocity = 1.04,
            CancellationToken cancellationToken = default)
        {
            var prim = new OptimoveJPrimitive(target) { Acceleration = acceleration, Velocity = velocity };
            return ExecuteMotionAsync(new[] { prim }, cancellationToken);
        }

        /// <summary>
        /// Moves the robot using the optimized movel (requires PolyScope ≥ 5.21 / 10.8).
        /// </summary>
        public Task<bool> OptimoveLAsync(
            CartesianPose target,
            double acceleration = 1.2,
            double velocity = 0.25,
            CancellationToken cancellationToken = default)
        {
            var prim = new OptimoveLPrimitive(target) { Acceleration = acceleration, Velocity = velocity };
            return ExecuteMotionAsync(new[] { prim }, cancellationToken);
        }

        // ─── Motion with MotionTarget ─────────────────────────────────────────────

        /// <summary>
        /// Executes a movej motion towards either a joint or Cartesian target.
        /// </summary>
        public Task<bool> MoveJAsync(
            MotionTarget target,
            double acceleration = 1.4,
            double velocity = 1.04,
            CancellationToken cancellationToken = default)
        {
            return target.IsJoint
                ? MoveJAsync(target.JointTarget!, acceleration, velocity, cancellationToken: cancellationToken)
                : MoveJAsync(target.CartesianTarget!, acceleration, velocity, cancellationToken: cancellationToken);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Resources are owned by UrDriver; we do not dispose them here.
        }
    }
}
