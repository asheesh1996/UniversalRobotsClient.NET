// Universal Robots .NET Client Library — Example Programs
// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniversalRobotics;
using UniversalRobotics.Commands;
using UniversalRobotics.Control;
using UniversalRobotics.Models;
using UniversalRobotics.Rtde;

Console.WriteLine("=== Universal Robots .NET Client Library Examples ===");
Console.WriteLine("Select example:");
Console.WriteLine("  1. RTDE Client (connect, read timestamp)");
Console.WriteLine("  2. Instruction Executor (MoveJ demo — requires live robot)");
Console.WriteLine("  3. Dashboard Client (power on/off)");
Console.Write("Enter choice (or press Enter to skip): ");

var input = Console.ReadLine();
switch (input?.Trim())
{
    case "1": await RtdeClientExample(); break;
    case "2": await InstructionExecutorExample(); break;
    case "3": await DashboardExample(); break;
    default:
        Console.WriteLine("No example selected. Exiting.");
        break;
}

// ─────────────────────────────────────────────────────────────────────────────

static async Task RtdeClientExample()
{
    const string RobotIp = "192.168.56.101";
    Console.WriteLine($"\n--- RTDE Client Example (robot at {RobotIp}) ---");

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

    var outputRecipe = new List<string> { "timestamp", "actual_q" };

    var client = new RtdeClient(RobotIp, outputRecipe, null, logger: null);

    Console.WriteLine("Connecting to RTDE interface...");
    try
    {
        await client.InitAsync(
            maxConnectionAttempts: 3,
            reconnectionTimeout: TimeSpan.FromSeconds(2),
            cancellationToken: cts.Token);

        await client.StartAsync(readInBackground: true, cts.Token);

        Console.WriteLine("Connected. Reading 5 data packages...");
        for (int i = 0; i < 5; i++)
        {
            var pkg = await client.GetDataPackageAsync(TimeSpan.FromSeconds(2), cts.Token);
            if (pkg != null && pkg.GetData<double>("timestamp", out var ts))
                Console.WriteLine($"  Timestamp: {ts:F3} s");
            else
                Console.WriteLine("  (no data yet)");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Connection failed: {ex.Message}");
        Console.WriteLine("(Is URSim running at 192.168.56.101?)");
    }
    finally
    {
        await client.DisconnectAsync();
        Console.WriteLine("Disconnected.");
    }
}

static async Task InstructionExecutorExample()
{
    const string RobotIp = "192.168.56.101";
    const string ScriptFile = "resources/external_control.urscript";

    Console.WriteLine($"\n--- Instruction Executor Example (robot at {RobotIp}) ---");

    var config = new UrDriverConfiguration
    {
        RobotIp = RobotIp,
        ScriptFile = ScriptFile,
        OutputRecipe = new[] { "timestamp", "actual_q" },
        InputRecipe = Array.Empty<string>(),
        HeadlessMode = true
    };

    using var driver = new UrDriver(config);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

    try
    {
        Console.WriteLine("Connecting to robot...");
        await driver.ConnectAsync(cts.Token);
        Console.WriteLine($"Connected. Robot version: {driver.SoftwareVersion}");

        using var executor = new InstructionExecutor(driver);

        // Move to a safe home position
        var home = new JointAngles(0, -Math.PI / 2, 0, -Math.PI / 2, 0, 0);
        Console.WriteLine("Moving to home position...");
        await executor.MoveJAsync(home, acceleration: 0.5, velocity: 0.5, cancellationToken: cts.Token);
        Console.WriteLine("Motion complete!");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

static async Task DashboardExample()
{
    const string RobotIp = "192.168.56.101";
    Console.WriteLine($"\n--- Dashboard Client Example (robot at {RobotIp}) ---");

    using var dashboard = new UniversalRobotics.Dashboard.DashboardClientG5(RobotIp);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

    try
    {
        await dashboard.ConnectAsync(cts.Token);
        Console.WriteLine("Connected to dashboard server.");

        var mode = await dashboard.GetRobotModeAsync(cts.Token);
        Console.WriteLine($"Robot mode: {mode}");

        var program = await dashboard.GetLoadedProgramAsync(cts.Token);
        Console.WriteLine($"Loaded program: {program}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Dashboard error: {ex.Message}");
    }
    finally
    {
        dashboard.Disconnect();
    }
}

