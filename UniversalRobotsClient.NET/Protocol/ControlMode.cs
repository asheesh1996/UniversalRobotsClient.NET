// Copyright 2021 FZI Forschungszentrum Informatik, Created on behalf of Universal Robots A/S
// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalRobotics.Protocol
{
    /// <summary>
    /// Represents the control mode used by the reverse interface when communicating
    /// motion targets to the robot.  The integer values match the UR controller protocol exactly.
    /// </summary>
    public enum ControlMode : int
    {
        /// <summary>Program has stopped; the external control script is expected to exit.</summary>
        Stopped = -2,
        /// <summary>Startup default until a concrete mode is transmitted.</summary>
        Uninitialized = -1,
        /// <summary>No motion controller is active; the robot holds its position.</summary>
        Idle = 0,
        /// <summary>High-frequency joint position streaming (servoj).</summary>
        ServoJ = 1,
        /// <summary>High-frequency joint velocity streaming (speedj).</summary>
        SpeedJ = 2,
        /// <summary>Trajectory forwarding — full trajectories sent via TrajectoryPointInterface.</summary>
        Forward = 3,
        /// <summary>High-frequency Cartesian velocity control (speedl).</summary>
        SpeedL = 4,
        /// <summary>High-frequency Cartesian pose control (servoj with IK).</summary>
        Pose = 5,
        /// <summary>Freedrive (gravity-compensated, force-guided movement).</summary>
        Freedrive = 6,
        /// <summary>Tool-in-contact status — used only internally by the URScript; not sent by the driver.</summary>
        ToolInContact = 7,
        /// <summary>Direct joint torque control (requires PolyScope ≥ 5.23.0 / 10.10.0).</summary>
        Torque = 8
    }

    /// <summary>
    /// Provides helper methods to classify <see cref="ControlMode"/> values into
    /// real-time, non-real-time, and stationary categories.
    /// </summary>
    public static class ControlModeTypes
    {
        private static readonly HashSet<ControlMode> s_realtime = new HashSet<ControlMode>
        {
            ControlMode.ServoJ, ControlMode.SpeedJ, ControlMode.SpeedL,
            ControlMode.Pose, ControlMode.Torque
        };

        private static readonly HashSet<ControlMode> s_nonRealtime = new HashSet<ControlMode>
        {
            ControlMode.Idle, ControlMode.Forward, ControlMode.Freedrive
        };

        private static readonly HashSet<ControlMode> s_stationary = new HashSet<ControlMode>
        {
            ControlMode.Stopped, ControlMode.Uninitialized, ControlMode.ToolInContact
        };

        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="mode"/> requires real-time scheduling.
        /// Real-time modes send a new control signal every control cycle (~8 ms).
        /// </summary>
        public static bool IsRealtime(ControlMode mode) => s_realtime.Contains(mode);

        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="mode"/> does not require real-time scheduling.
        /// </summary>
        public static bool IsNonRealtime(ControlMode mode) => s_nonRealtime.Contains(mode);

        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="mode"/> does not move the robot at all.
        /// </summary>
        public static bool IsStationary(ControlMode mode) => s_stationary.Contains(mode);
    }
}
