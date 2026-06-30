// Copyright 2019 FZI Forschungszentrum Informatik, Created on behalf of Universal Robots A/S
// Licensed under the Apache License, Version 2.0

using System;

namespace UniversalRobotics.Exceptions
{
    /// <summary>
    /// Base exception type for all Universal Robots client library errors.
    /// All specialized exceptions inherit from this class.
    /// </summary>
    public class UrException : Exception
    {
        /// <summary>Initializes a new instance of <see cref="UrException"/> with no message.</summary>
        public UrException() : base(string.Empty) { }

        /// <summary>Initializes a new instance of <see cref="UrException"/> with the specified message.</summary>
        /// <param name="message">Human-readable description of the error.</param>
        public UrException(string message) : base(message) { }

        /// <summary>Initializes a new instance of <see cref="UrException"/> with a message and inner exception.</summary>
        /// <param name="message">Human-readable description of the error.</param>
        /// <param name="inner">The exception that caused this exception.</param>
        public UrException(string message, Exception inner) : base(message, inner) { }
    }
}
