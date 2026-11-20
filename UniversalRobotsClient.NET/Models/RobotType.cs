// Copyright 2019 FZI Forschungszentrum Informatik, Created on behalf of Universal Robots A/S
// Licensed under the Apache License, Version 2.0

namespace UniversalRobotics.Models
{
    /// <summary>
    /// Identifies the physical model of a Universal Robots manipulator.
    /// Values correspond to the type IDs transmitted on the primary interface.
    /// </summary>
    public enum RobotType : int
    {
        /// <summary>Robot type is unknown or not yet determined.</summary>
        Undefined = -128,
        /// <summary>UR5 — 5 kg payload, 850 mm reach.</summary>
        Ur5 = 1,
        /// <summary>UR10 — 10 kg payload, 1300 mm reach.</summary>
        Ur10 = 2,
        /// <summary>UR3 — 3 kg payload, 500 mm reach.</summary>
        Ur3 = 3,
        /// <summary>UR16e — 16 kg payload, 900 mm reach.</summary>
        Ur16 = 4,
        /// <summary>UR18e — 18 kg payload, 900 mm reach.</summary>
        Ur18 = 5,
        /// <summary>UR8 Long — 8 kg payload, extended reach variant.</summary>
        Ur8Long = 6,
        /// <summary>UR20 — 20 kg payload, 1750 mm reach.</summary>
        Ur20 = 7,
        /// <summary>UR30 — 30 kg payload, 1300 mm reach.</summary>
        Ur30 = 8,
        /// <summary>UR15 — 15 kg payload.</summary>
        Ur15 = 9,
        /// <summary>UR10g with 1750 mm reach.</summary>
        Ur10G1750 = 12,
        /// <summary>UR17g with 1300 mm reach.</summary>
        Ur17G1300 = 13,
        /// <summary>UR18g with 950 mm reach.</summary>
        Ur18G950 = 14
    }

    /// <summary>
    /// Identifies the product series (controller generation) of a Universal Robots arm.
    /// </summary>
    public enum RobotSeries
    {
        /// <summary>Series is unknown or not yet determined.</summary>
        Undefined = -128,
        /// <summary>CB3 series — older generation, PolyScope 3.x software.</summary>
        Cb3 = 1,
        /// <summary>e-Series — current generation, PolyScope 5.x software.</summary>
        ESeries = 2,
        /// <summary>UR Series — next-generation controllers with PolyScope X (10.x).</summary>
        UrSeries = 3,
        /// <summary>G-series, requiring PolyScope X 10.15 or newer.</summary>
        GSeries = 4
    }

    /// <summary>Control box hardware generation reported by the primary interface.</summary>
    public enum ControlBoxType : ushort
    {
        /// <summary>Unspecified hardware.</summary>
        Unknown = 0,
        /// <summary>CB5 control box.</summary>
        Cb5 = 1,
        /// <summary>CB7 control box.</summary>
        Cb7 = 2
    }

    /// <summary>Tool flange hardware revision.</summary>
    public enum ToolFlangeType : ushort
    {
        /// <summary>Unspecified hardware.</summary>
        Unknown = 0,
        /// <summary>First revision.</summary>
        V1 = 1,
        /// <summary>Second revision.</summary>
        V2 = 2
    }

    /// <summary>
    /// Helper methods for determining the robot series from a robot type or version.
    /// </summary>
    public static class RobotSeriesHelper
    {
        /// <summary>Classifies a model using the controller version, matching upstream compatibility rules.</summary>
        public static RobotSeries GetSeries(RobotType robotType, VersionInformation version)
        {
            if (version == null) throw new System.ArgumentNullException(nameof(version));
            switch (robotType)
            {
                case RobotType.Ur3:
                case RobotType.Ur5:
                case RobotType.Ur10:
                    return version.Major >= 5 ? RobotSeries.ESeries : RobotSeries.Cb3;
                case RobotType.Ur16:
                    return version.Major >= 5 ? RobotSeries.ESeries : RobotSeries.Undefined;
                case RobotType.Ur15:
                case RobotType.Ur18:
                case RobotType.Ur20:
                case RobotType.Ur30:
                case RobotType.Ur8Long:
                    return version.Major >= 5 ? RobotSeries.UrSeries : RobotSeries.Undefined;
                case RobotType.Ur10G1750:
                case RobotType.Ur17G1300:
                case RobotType.Ur18G950:
                    return version.Major > 10 || (version.Major == 10 && version.Minor >= 15) ? RobotSeries.GSeries : RobotSeries.Undefined;
                default:
                    return RobotSeries.Undefined;
            }
        }

        /// <summary>Parses a model name, including UR7e/UR12e aliases and G-series names.</summary>
        public static RobotType ParseRobotType(string name)
        {
            if (name == null) throw new System.ArgumentNullException(nameof(name));
            return name.ToLowerInvariant() switch
            {
                "ur3" or "ur3e" => RobotType.Ur3,
                "ur5" or "ur5e" or "ur7e" => RobotType.Ur5,
                "ur10" or "ur10e" or "ur12e" => RobotType.Ur10,
                "ur16e" => RobotType.Ur16,
                "ur15" => RobotType.Ur15,
                "ur18" => RobotType.Ur18,
                "ur20" => RobotType.Ur20,
                "ur30" => RobotType.Ur30,
                "ur8long" => RobotType.Ur8Long,
                "ur10g-1750" => RobotType.Ur10G1750,
                "ur17g-1300" => RobotType.Ur17G1300,
                "ur18g-950" => RobotType.Ur18G950,
                _ => throw new System.ArgumentException("Unknown robot model.", nameof(name))
            };
        }

        /// <summary>
        /// Returns the <see cref="RobotSeries"/> that the given <see cref="RobotType"/> belongs to.
        /// </summary>
        /// <param name="robotType">The robot model type.</param>
        /// <returns>The corresponding <see cref="RobotSeries"/>.</returns>
        public static RobotSeries GetSeries(RobotType robotType)
        {
            return robotType switch
            {
                RobotType.Ur5 => RobotSeries.ESeries,
                RobotType.Ur10 => RobotSeries.ESeries,
                RobotType.Ur3 => RobotSeries.ESeries,
                RobotType.Ur16 => RobotSeries.ESeries,
                RobotType.Ur18 => RobotSeries.ESeries,
                RobotType.Ur8Long => RobotSeries.ESeries,
                RobotType.Ur20 => RobotSeries.UrSeries,
                RobotType.Ur30 => RobotSeries.UrSeries,
                RobotType.Ur15 => RobotSeries.UrSeries,
                RobotType.Ur10G1750 => RobotSeries.GSeries,
                RobotType.Ur17G1300 => RobotSeries.GSeries,
                RobotType.Ur18G950 => RobotSeries.GSeries,
                _ => RobotSeries.Undefined
            };
        }

        /// <summary>
        /// Returns a human-readable string for the given <see cref="RobotSeries"/>.
        /// </summary>
        public static string GetSeriesString(RobotSeries series) => series switch
        {
            RobotSeries.Cb3 => "CB3",
            RobotSeries.ESeries => "e-Series",
            RobotSeries.UrSeries => "UR Series",
            RobotSeries.GSeries => "G_SERIES",
            _ => "Unknown"
        };

        /// <summary>
        /// Determines the <see cref="RobotSeries"/> from a <see cref="VersionInformation"/> object.
        /// Major version 3 → CB3, 5 → e-Series, 10+ → UR Series.
        /// </summary>
        public static RobotSeries GetSeriesFromVersion(VersionInformation version)
        {
            if (version.Major == 3) return RobotSeries.Cb3;
            if (version.Major == 5) return RobotSeries.ESeries;
            if (version.Major >= 10) return RobotSeries.UrSeries;
            return RobotSeries.Undefined;
        }
    }
}
