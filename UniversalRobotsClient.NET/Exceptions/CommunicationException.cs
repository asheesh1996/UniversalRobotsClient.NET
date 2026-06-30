// Licensed under the Apache License, Version 2.0

using System;

namespace UniversalRobotics.Exceptions
{
    /// <summary>
    /// Thrown when a network or socket communication error occurs while communicating
    /// with the robot controller.
    /// </summary>
    public class CommunicationException : UrException
    {
        /// <summary>Initializes a new instance with a descriptive message.</summary>
        public CommunicationException(string message) : base(message) { }

        /// <summary>Initializes a new instance with a message and the underlying socket exception.</summary>
        public CommunicationException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Thrown when an operation is attempted on a robot that is not connected.
    /// </summary>
    public class NotConnectedException : UrException
    {
        /// <summary>Initializes a new instance with a default message.</summary>
        public NotConnectedException() : base("Not connected to the robot controller.") { }

        /// <summary>Initializes a new instance with a custom message.</summary>
        public NotConnectedException(string message) : base(message) { }
    }

    /// <summary>
    /// Thrown when the primary interface is read-only (robot is in a state that prevents script uploads).
    /// The caller may retry after restarting the primary interface.
    /// </summary>
    public class ReadOnlyInterfaceException : UrException
    {
        /// <summary>Initializes a new instance with a default message.</summary>
        public ReadOnlyInterfaceException()
            : base("The primary interface is read-only. The robot may be running a program.") { }

        /// <summary>Initializes a new instance with a custom message.</summary>
        public ReadOnlyInterfaceException(string message) : base(message) { }
    }

    /// <summary>
    /// Thrown when an operation times out waiting for a response from the robot.
    /// </summary>
    public class UrTimeoutException : UrException
    {
        /// <summary>Initializes a new instance with a default message.</summary>
        public UrTimeoutException() : base("The operation timed out waiting for the robot.") { }

        /// <summary>Initializes a new instance with a custom message.</summary>
        public UrTimeoutException(string message) : base(message) { }

        /// <summary>Initializes a new instance with a message and inner exception.</summary>
        public UrTimeoutException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Thrown when a URScript program fails to execute, encounters an error, or raises a runtime exception.
    /// </summary>
    public class UrScriptException : UrException
    {
        /// <summary>Gets the name of the script that failed, if available.</summary>
        public string? ScriptName { get; }

        /// <summary>Initializes a new instance with a message and optional script name.</summary>
        public UrScriptException(string message, string? scriptName = null) : base(message)
        {
            ScriptName = scriptName;
        }
    }

    /// <summary>
    /// Thrown when a robot is in an incompatible safety or operational state for the requested operation.
    /// </summary>
    public class RobotStateException : UrException
    {
        /// <summary>Initializes a new instance with a message.</summary>
        public RobotStateException(string message) : base(message) { }
    }
}
