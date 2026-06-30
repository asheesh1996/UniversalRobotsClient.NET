// Copyright 2019 FZI Forschungszentrum Informatik, Created on behalf of Universal Robots A/S
// Licensed under the Apache License, Version 2.0

using System;
using UniversalRobotics.Models;

namespace UniversalRobotics.Exceptions
{
    /// <summary>
    /// Thrown when a required robot software version does not match the actual version.
    /// </summary>
    public class VersionMismatchException : UrException
    {
        /// <summary>Gets the minimum required software version.</summary>
        public VersionInformation RequiredVersion { get; }

        /// <summary>Gets the actual software version reported by the robot.</summary>
        public VersionInformation ActualVersion { get; }

        /// <summary>
        /// Initializes a new instance of <see cref="VersionMismatchException"/> with version details.
        /// </summary>
        /// <param name="message">Description of the version requirement that was not met.</param>
        /// <param name="required">The minimum required version.</param>
        /// <param name="actual">The actual version reported by the robot.</param>
        public VersionMismatchException(string message, VersionInformation required, VersionInformation actual)
            : base($"{message} (Required: {required}, Actual: {actual})")
        {
            RequiredVersion = required;
            ActualVersion = actual;
        }

        /// <summary>Initializes a new instance of <see cref="VersionMismatchException"/> with no details.</summary>
        public VersionMismatchException() : base("Version mismatch.")
        {
            RequiredVersion = new VersionInformation();
            ActualVersion = new VersionInformation();
        }
    }
}
