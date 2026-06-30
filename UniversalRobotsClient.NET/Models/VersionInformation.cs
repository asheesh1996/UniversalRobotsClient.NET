// Copyright 2019 FZI Forschungszentrum Informatik, Created on behalf of Universal Robots A/S
// Licensed under the Apache License, Version 2.0

using System;
using System.Text;

namespace UniversalRobotics.Models
{
    /// <summary>
    /// Encapsulates the software version information of a UR robot controller (PolyScope).
    /// Supports comparison and parsing from version strings such as "5.12.0.1101319".
    /// </summary>
    public sealed class VersionInformation : IComparable<VersionInformation>, IEquatable<VersionInformation>
    {
        /// <summary>Gets the major version component.</summary>
        public uint Major { get; }

        /// <summary>Gets the minor version component.</summary>
        public uint Minor { get; }

        /// <summary>Gets the bugfix (patch) version component.</summary>
        public uint Bugfix { get; }

        /// <summary>Gets the build number.</summary>
        public uint Build { get; }

        /// <summary>Initializes an empty (all-zero) version.</summary>
        public VersionInformation() : this(0, 0, 0, 0) { }

        /// <summary>Initializes a <see cref="VersionInformation"/> with all components.</summary>
        public VersionInformation(uint major, uint minor, uint bugfix, uint build)
        {
            Major = major;
            Minor = minor;
            Bugfix = bugfix;
            Build = build;
        }

        /// <summary>
        /// Returns <see langword="true"/> if all version components are zero.
        /// </summary>
        public bool IsEmpty => Major == 0 && Minor == 0 && Bugfix == 0 && Build == 0;

        /// <summary>
        /// Returns <see langword="true"/> if this is an e-Series controller (major version 5).
        /// </summary>
        public bool IsESeries => Major == 5;

        /// <summary>
        /// Returns <see langword="true"/> if this is a PolyScope X controller (major version 10+).
        /// </summary>
        public bool IsPolyScopeX => Major >= 10;

        /// <summary>
        /// Parses a version string in the format "Major.Minor.Bugfix" or "Major.Minor.Bugfix.Build".
        /// </summary>
        /// <param name="versionString">Version string such as "5.12.0.1101319" or "10.7.0".</param>
        /// <returns>A parsed <see cref="VersionInformation"/>.</returns>
        /// <exception cref="FormatException">Thrown if the string cannot be parsed.</exception>
        public static VersionInformation Parse(string versionString)
        {
            if (string.IsNullOrWhiteSpace(versionString))
                throw new FormatException("Version string must not be null or empty.");

            // Strip optional leading 'v' (used in ScriptReader templates)
            if (versionString.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                versionString = versionString.Substring(1);

            var parts = versionString.Split('.');
            if (parts.Length < 3)
                throw new FormatException($"Invalid version string '{versionString}'. Expected at least 3 components.");

            uint major = uint.Parse(parts[0]);
            uint minor = uint.Parse(parts[1]);
            uint bugfix = uint.Parse(parts[2]);
            uint build = parts.Length >= 4 ? uint.Parse(parts[3]) : 0;

            return new VersionInformation(major, minor, bugfix, build);
        }

        /// <summary>
        /// Tries to parse a version string, returning <see langword="false"/> on failure.
        /// </summary>
        public static bool TryParse(string versionString, out VersionInformation? result)
        {
            try
            {
                result = Parse(versionString);
                return true;
            }
            catch
            {
                result = null;
                return false;
            }
        }

        /// <inheritdoc />
        public int CompareTo(VersionInformation? other)
        {
            if (other is null) return 1;
            int cmp = Major.CompareTo(other.Major);
            if (cmp != 0) return cmp;
            cmp = Minor.CompareTo(other.Minor);
            if (cmp != 0) return cmp;
            cmp = Bugfix.CompareTo(other.Bugfix);
            if (cmp != 0) return cmp;
            return Build.CompareTo(other.Build);
        }

        /// <inheritdoc />
        public bool Equals(VersionInformation? other)
            => other is not null && Major == other.Major && Minor == other.Minor
               && Bugfix == other.Bugfix && Build == other.Build;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is VersionInformation v && Equals(v);

        /// <inheritdoc />
        public override int GetHashCode() => (Major, Minor, Bugfix, Build).GetHashCode();

        /// <summary>Returns the version as "Major.Minor.Bugfix-Build".</summary>
        public override string ToString() => $"{Major}.{Minor}.{Bugfix}-{Build}";

        /// <summary>Compares two version instances for equality.</summary>
        public static bool operator ==(VersionInformation? left, VersionInformation? right)
            => left?.Equals(right) ?? right is null;

        /// <summary>Compares two version instances for inequality.</summary>
        public static bool operator !=(VersionInformation? left, VersionInformation? right) => !(left == right);

        /// <summary>Returns <see langword="true"/> if <paramref name="left"/> is less than <paramref name="right"/>.</summary>
        public static bool operator <(VersionInformation left, VersionInformation right) => left.CompareTo(right) < 0;

        /// <summary>Returns <see langword="true"/> if <paramref name="left"/> is less than or equal to <paramref name="right"/>.</summary>
        public static bool operator <=(VersionInformation left, VersionInformation right) => left.CompareTo(right) <= 0;

        /// <summary>Returns <see langword="true"/> if <paramref name="left"/> is greater than <paramref name="right"/>.</summary>
        public static bool operator >(VersionInformation left, VersionInformation right) => left.CompareTo(right) > 0;

        /// <summary>Returns <see langword="true"/> if <paramref name="left"/> is greater than or equal to <paramref name="right"/>.</summary>
        public static bool operator >=(VersionInformation left, VersionInformation right) => left.CompareTo(right) >= 0;
    }
}
