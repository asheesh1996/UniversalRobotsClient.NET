using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalRobotics.Exceptions
{
    /// <summary>Reports all RTDE output fields unavailable on the connected controller.</summary>
    public sealed class RtdeInvalidKeyException : UrException
    {
        /// <summary>Unavailable field names in recipe order.</summary>
        public IReadOnlyList<string> MissingKeys { get; }

        /// <summary>Creates an exception carrying a defensive copy of the unavailable keys.</summary>
        public RtdeInvalidKeyException(IEnumerable<string> missingKeys)
            : this((missingKeys ?? throw new ArgumentNullException(nameof(missingKeys))).ToArray()) { }

        private RtdeInvalidKeyException(string[] missingKeys)
            : base("Unavailable RTDE fields: " + string.Join(", ", missingKeys))
        {
            MissingKeys = Array.AsReadOnly(missingKeys);
        }
    }
}