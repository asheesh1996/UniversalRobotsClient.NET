// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using UniversalRobotics.Models;

namespace UniversalRobotics
{
    /// <summary>
    /// Configuration options for creating a <see cref="UrDriver"/> instance.
    /// </summary>
    public sealed class UrDriverConfiguration
    {
        /// <summary>Gets or sets the robot's IP address.</summary>
        public string RobotIp { get; set; } = string.Empty;

        /// <summary>Gets or sets the path to the URScript file (used with the script sender).</summary>
        public string? ScriptFile { get; set; }

        /// <summary>Gets or sets the path to the RTDE output recipe file.</summary>
        public string? OutputRecipeFile { get; set; }

        /// <summary>Gets or sets the path to the RTDE input recipe file.</summary>
        public string? InputRecipeFile { get; set; }

        /// <summary>Gets or sets the RTDE output recipe fields inline (alternative to a file).</summary>
        public IReadOnlyList<string>? OutputRecipe { get; set; }

        /// <summary>Gets or sets the RTDE input recipe fields inline (alternative to a file).</summary>
        public IReadOnlyList<string>? InputRecipe { get; set; }

        /// <summary>
        /// Gets or sets the target RTDE frequency in Hz.
        /// Use 0 (default) to run at the robot's maximum frequency.
        /// </summary>
        public double TargetFrequency { get; set; } = 0.0;

        /// <summary>
        /// Gets or sets the callback invoked when the external control program starts or stops.
        /// </summary>
        public Action<bool>? HandleProgramState { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the driver should run in headless mode.
        /// In headless mode, the script is sent directly without requiring the External Control URCap.
        /// </summary>
        public bool HeadlessMode { get; set; } = false;

        /// <summary>
        /// Gets or sets the port for the <c>ReverseInterface</c> (default 50001).
        /// </summary>
        public int ReversePort { get; set; } = 50001;

        /// <summary>
        /// Gets or sets the port for the <c>ScriptSender</c> (default 50002).
        /// </summary>
        public int ScriptSenderPort { get; set; } = 50002;

        /// <summary>
        /// Gets or sets the port for the <c>TrajectoryPointInterface</c> (default 50003).
        /// </summary>
        public int TrajectoryPort { get; set; } = 50003;

        /// <summary>
        /// Gets or sets the port for the <c>ScriptCommandInterface</c> (default 50004).
        /// </summary>
        public int ScriptCommandPort { get; set; } = 50004;

        /// <summary>
        /// Gets or sets the maximum number of RTDE initialization attempts (default 3).
        /// </summary>
        public int RtdeInitializationAttempts { get; set; } = 3;

        /// <summary>
        /// Gets or sets the timeout between RTDE initialization attempts (default 1 second).
        /// </summary>
        public TimeSpan RtdeInitializationTimeout { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Gets or sets the keepalive count for the reverse interface
        /// (number of allowed consecutive read timeouts before the robot stops).
        /// </summary>
        public int KeepaliveCount { get; set; } = 0;

        /// <summary>
        /// Gets or sets the robot software version (used to enable version-specific features).
        /// Leave null to auto-detect from the primary interface.
        /// </summary>
        public VersionInformation? RobotSoftwareVersion { get; set; }

        /// <summary>
        /// Gets or sets whether unavailable RTDE output fields should be silently ignored.
        /// If false (default), an exception is thrown for unavailable fields.
        /// </summary>
        public bool IgnoreUnavailableOutputs { get; set; } = false;
    }
}
