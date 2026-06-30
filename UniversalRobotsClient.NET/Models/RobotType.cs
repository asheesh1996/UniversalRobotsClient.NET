// Copyright 2019 FZI Forschungszentrum Informatik, Created on behalf of Universal Robots A/S
// Licensed under the Apache License, Version 2.0

namespace UniversalRobotics.Models
{
    /// <summary>
    /// Identifies the physical model of a Universal Robots manipulator.
    /// Values correspond to the type IDs transmitted on the primary interface.
    /// </summary>
    public enum RobotType : sbyte
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
        Ur15 = 9
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
        UrSeries = 3
    }

    /// <summary>
    /// Helper methods for determining the robot series from a robot type or version.
    /// </summary>
    public static class RobotSeriesHelper
    {
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
