// Licensed under the Apache License, Version 2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using UniversalRobotics.Protocol;

namespace UniversalRobotics.Control
{
    /// <summary>
    /// Configures the read timeout for the reverse socket running in the robot's external control script.
    /// </summary>
    public sealed class RobotReceiveTimeout
    {
        /// <summary>Maximum allowed receive timeout for real-time control modes (200 ms).</summary>
        public static readonly TimeSpan MaxRealtimeTimeout = TimeSpan.FromMilliseconds(200);

        private readonly int _milliseconds;

        private RobotReceiveTimeout(int ms) => _milliseconds = ms;

        /// <summary>
        /// Creates a timeout of <paramref name="milliseconds"/> milliseconds.
        /// A value of 0 or negative makes the read blocking (no timeout).
        /// </summary>
        public static RobotReceiveTimeout Milliseconds(int milliseconds) => new RobotReceiveTimeout(milliseconds);

        /// <summary>Creates a timeout of <paramref name="seconds"/> seconds.</summary>
        public static RobotReceiveTimeout Seconds(float seconds)
            => new RobotReceiveTimeout((int)(seconds * 1000));

        /// <summary>
        /// Creates a "no timeout" configuration: the robot's socket read becomes blocking.
        /// Use only for non-real-time control modes.
        /// </summary>
        public static RobotReceiveTimeout Off() => new RobotReceiveTimeout(0);

        /// <summary>Gets the timeout value in milliseconds. 0 means blocking (no timeout).</summary>
        public int GetAsMilliseconds() => _milliseconds;

        /// <summary>
        /// Validates that this timeout is appropriate for the given control mode.
        /// Throws if a real-time control mode is used with a timeout exceeding 200 ms.
        /// </summary>
        public void ValidateForControlMode(ControlMode mode)
        {
            if (ControlModeTypes.IsRealtime(mode) && _milliseconds > (int)MaxRealtimeTimeout.TotalMilliseconds)
                throw new ArgumentException(
                    $"Receive timeout {_milliseconds}ms exceeds the maximum allowed {MaxRealtimeTimeout.TotalMilliseconds}ms for real-time mode {mode}.");
        }
    }

    /// <summary>Control messages sent via the reverse interface for trajectory forwarding.</summary>
    public enum TrajectoryControlMessage : int
    {
        /// <summary>Cancels the currently active trajectory.</summary>
        Cancel = -1,
        /// <summary>No operation (keep alive).</summary>
        Noop = 0,
        /// <summary>Starts sending trajectory points.</summary>
        Start = 1
    }

    /// <summary>Control messages sent via the reverse interface for freedrive mode.</summary>
    public enum FreedriveControlMessage : int
    {
        /// <summary>Stops freedrive mode.</summary>
        Stop = -1,
        /// <summary>Keeps freedrive mode running.</summary>
        Noop = 0,
        /// <summary>Starts freedrive mode.</summary>
        Start = 1
    }

    /// <summary>Result codes sent back from the robot when tool contact execution completes.</summary>
    public enum ToolContactResult : int
    {
        /// <summary>Tool contact was detected successfully.</summary>
        Success = 0,
        /// <summary>Tool contact was canceled before detection.</summary>
        Canceled = 1
    }

    /// <summary>Result codes returned by the robot when a trajectory finishes.</summary>
    public enum TrajectoryResult : int
    {
        /// <summary>No result received yet.</summary>
        Unknown = -1,
        /// <summary>Trajectory completed successfully.</summary>
        Success = 0,
        /// <summary>Trajectory was canceled externally.</summary>
        Canceled = 1,
        /// <summary>Trajectory failed due to a robot-side error.</summary>
        Failure = 2
    }
}
