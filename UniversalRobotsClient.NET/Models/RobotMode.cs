// Copyright 2019 FZI Forschungszentrum Informatik, Created on behalf of Universal Robots A/S
// Licensed under the Apache License, Version 2.0

namespace UniversalRobotics.Models
{
    /// <summary>
    /// Represents the current operational mode of a UR robot.
    /// Values correspond exactly to the UR controller's internal mode IDs.
    /// </summary>
    public enum RobotMode : sbyte
    {
        /// <summary>Mode is unknown. Not defined by UR; used internally to represent an unset state.</summary>
        Unknown = -128,
        /// <summary>No controller software is running on the robot.</summary>
        NoController = -1,
        /// <summary>The robot has no network connection to a controller.</summary>
        Disconnected = 0,
        /// <summary>The robot is waiting for safety configuration confirmation.</summary>
        ConfirmSafety = 1,
        /// <summary>The robot is booting up.</summary>
        Booting = 2,
        /// <summary>Robot power is off.</summary>
        PowerOff = 3,
        /// <summary>Robot power is on but brakes are engaged.</summary>
        PowerOn = 4,
        /// <summary>Robot is idle; brakes released, ready for program execution.</summary>
        Idle = 5,
        /// <summary>Robot is in back-drive mode (force-guided movement).</summary>
        Backdrive = 6,
        /// <summary>Robot is actively executing a program.</summary>
        Running = 7,
        /// <summary>The robot is in the process of updating its firmware.</summary>
        UpdatingFirmware = 8
    }

    /// <summary>
    /// Represents the safety mode of a UR robot. Values correspond to the UR safety system's mode IDs.
    /// </summary>
    public enum SafetyMode : byte
    {
        /// <summary>Normal operation; no safety violations.</summary>
        Normal = 1,
        /// <summary>Robot is operating in reduced (speed-limited) mode.</summary>
        Reduced = 2,
        /// <summary>Robot is in a protective stop state.</summary>
        ProtectiveStop = 3,
        /// <summary>Robot is in recovery mode after a protective stop.</summary>
        Recovery = 4,
        /// <summary>Robot is safeguard-stopped (external guard signal active).</summary>
        SafeguardStop = 5,
        /// <summary>System-level emergency stop is active.</summary>
        SystemEmergencyStop = 6,
        /// <summary>Robot-level emergency stop button is pressed.</summary>
        RobotEmergencyStop = 7,
        /// <summary>A safety violation has been detected.</summary>
        Violation = 8,
        /// <summary>A safety fault condition exists.</summary>
        Fault = 9,
        /// <summary>Validating joint ID configuration.</summary>
        ValidateJointId = 10,
        /// <summary>Safety mode is undefined.</summary>
        UndefinedSafetyMode = 11,
        /// <summary>Automatic mode safeguard stop is active.</summary>
        AutomaticModeSafeguardStop = 12,
        /// <summary>Three-position enabling device stop (system).</summary>
        SystemThreePositionEnablingStop = 13,
        /// <summary>Three-position enabling device stop (teach pendant).</summary>
        TpThreePositionEnablingStop = 14,
        /// <summary>IMMI emergency stop.</summary>
        ImmiEmergencyStop = 15,
        /// <summary>IMMI safeguard stop.</summary>
        ImmiSafeguardStop = 16,
        /// <summary>PROFIsafe waiting for parameters.</summary>
        ProfisafeWaitingForParameters = 17,
        /// <summary>PROFIsafe automatic mode safeguard stop.</summary>
        ProfisafeAutomaticModeSafeguardStop = 18,
        /// <summary>PROFIsafe safeguard stop.</summary>
        ProfisafeSafeguardStop = 19,
        /// <summary>PROFIsafe emergency stop.</summary>
        ProfisafeEmergencyStop = 20,
        /// <summary>Safety API safeguard stop.</summary>
        SafetyApiSafeguardStop = 22
    }

    /// <summary>
    /// Represents the safety status reported on PolyScope 3.10 / 5.4 and later.
    /// </summary>
    public enum SafetyStatus : sbyte
    {
        /// <summary>Normal operation.</summary>
        Normal = 1,
        /// <summary>Reduced mode.</summary>
        Reduced = 2,
        /// <summary>Protective stop.</summary>
        ProtectiveStop = 3,
        /// <summary>Recovery mode.</summary>
        Recovery = 4,
        /// <summary>Safeguard stop.</summary>
        SafeguardStop = 5,
        /// <summary>System emergency stop.</summary>
        SystemEmergencyStop = 6,
        /// <summary>Robot emergency stop.</summary>
        RobotEmergencyStop = 7,
        /// <summary>Safety violation.</summary>
        Violation = 8,
        /// <summary>Safety fault.</summary>
        Fault = 9,
        /// <summary>Validating joint ID.</summary>
        ValidateJointId = 10,
        /// <summary>Undefined safety mode.</summary>
        UndefinedSafetyMode = 11,
        /// <summary>Automatic mode safeguard stop.</summary>
        AutomaticModeSafeguardStop = 12,
        /// <summary>System three-position enabling stop.</summary>
        SystemThreePositionEnablingStop = 13
    }

    /// <summary>
    /// Specifies the output type for an analog output channel on the robot controller.
    /// </summary>
    public enum AnalogOutputType : sbyte
    {
        /// <summary>Type is determined by the Teach Pendant configuration.</summary>
        SetOnTeachPendant = -1,
        /// <summary>Output produces a current signal (milliamperes).</summary>
        Current = 0,
        /// <summary>Output produces a voltage signal (volts).</summary>
        Voltage = 1
    }
}
