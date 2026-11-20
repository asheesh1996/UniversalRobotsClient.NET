// Universal Robots .NET Client Library — Example Programs
// Licensed under the Apache License, Version 2.0

#nullable enable

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UniversalRobotics;
using UniversalRobotics.Commands;
using UniversalRobotics.Control;
using UniversalRobotics.Models;
using UniversalRobotics.Rtde;
using UniversalRobotics.Rtde.Packets;

Console.WriteLine("=== Universal Robots .NET Client Library Examples ===");
Console.WriteLine("Select example:");
Console.WriteLine("  1. RTDE Client (connect, read timestamp)");
Console.WriteLine("  2. Instruction Executor (MoveJ demo — requires live robot)");
Console.WriteLine("  3. Dashboard Client (power on/off)");
Console.WriteLine("  4. RTDE Joints + TCP endpoint pose  [diagnostics + retry loop]");
Console.Write("Enter choice (or press Enter to skip): ");

var input = Console.ReadLine();
switch (input?.Trim())
{
    case "1": await RtdeClientExample(); break;
    case "2": await InstructionExecutorExample(); break;
    case "3": await DashboardExample(); break;
    case "4": await RtdeJointsAndTcpExample(); break;
    default:
        Console.WriteLine("No example selected. Exiting.");
        break;
}

// ─────────────────────────────────────────────────────────────────────────────

static async Task RtdeClientExample()
{
    const string RobotIp = "127.0.0.1";
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
        Console.WriteLine("(Is URSim running at 127.0.0.1?)");
    }
    finally
    {
        await client.DisconnectAsync();
        Console.WriteLine("Disconnected.");
    }
}

static async Task RtdeJointsAndTcpExample()
{
    const string RobotIp = "127.0.0.1";
    const int RtdePort = 30004;

    Console.WriteLine($"\n--- RTDE Joints + TCP Endpoint Example ---");
    Console.WriteLine($"  Target : {RobotIp}:{RtdePort}");

    // ── Step 1: Verify TCP reachability before attempting RTDE ───────────────
    Console.Write($"  Checking port {RtdePort} ... ");
    bool reachable = await CheckTcpReachableAsync(RobotIp, RtdePort, TimeSpan.FromSeconds(3));
    Console.WriteLine(reachable ? "reachable." : "UNREACHABLE.");
    if (!reachable)
    {
        Console.Error.WriteLine(
            "  Cannot reach the RTDE port.\n" +
            "  → Ensure URSim / robot is running and port 30004 is accessible.");
        return;
    }

    // ── Step 2: RTDE retry loop ───────────────────────────────────────────────
    // readInBackground:false  →  we call GetNextDataPackageAsync() directly.
    // This surfaces every exception immediately (no silent background-thread swallowing).
    var outputRecipe = new[] { "timestamp", "actual_q", "actual_TCP_pose" };

    // Pass a real ILogger so all library warnings / errors appear in the console.
    var logger = new InlineConsoleLogger("RTDE");

    using var globalCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    int attempt = 0;

    while (!globalCts.IsCancellationRequested)
    {
        attempt++;
        Console.WriteLine($"\n[Attempt {attempt}] Connecting ...");

        using var client = new RtdeClient(RobotIp, outputRecipe, logger: logger);
        client.ConnectionLost += (_, _) =>
            Console.Error.WriteLine("  [event] ConnectionLost fired.");

        try
        {
            await client.InitAsync(
                maxConnectionAttempts: 1,
                maxInitializationAttempts: 1,
                cancellationToken: globalCts.Token);

            Console.WriteLine($"  Initialized — state={client.State}");

            await client.StartAsync(readInBackground: false, cancellationToken: globalCts.Token);
            Console.WriteLine($"  RTDE started  — state={client.State}");
            Console.WriteLine($"  Reading packets directly (no background thread) ...");

            using var readCts  = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linkedCt = CancellationTokenSource.CreateLinkedTokenSource(
                globalCts.Token, readCts.Token);

            int received = 0;
            while (received < 5 && !linkedCt.Token.IsCancellationRequested)
            {
                DataPackage? pkg;
                try
                {
                    pkg = await client.GetNextDataPackageAsync(linkedCt.Token);
                }
                catch (OperationCanceledException)
                {
                    Console.Error.WriteLine("  Read timed out (10 s with no packets).");
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"  Read error — {ex.GetType().Name}: {ex.Message}");
                    break;
                }

                if (pkg == null) continue;
                received++;

                if (!pkg.GetData<double>("timestamp", out var ts))
                {
                    Console.WriteLine($"  [{received}] (timestamp field missing in packet)");
                    continue;
                }

                Console.WriteLine($"  [{received}] timestamp = {ts:F3} s");

                if (pkg.GetData<double[]>("actual_q", out var joints))
                    Console.WriteLine($"       Joints  : {FormatVector(joints)} rad");
                else
                    Console.Error.WriteLine("       Joints  : field not found");

                if (pkg.GetData<double[]>("actual_TCP_pose", out var tcp))
                    Console.WriteLine($"       TCP pose: {FormatVector(tcp)} m/rad");
                else
                    Console.Error.WriteLine("       TCP pose: field not found");
            }

            if (received > 0)
            {
                Console.WriteLine($"\n  ✓ Received {received} data packages. Done.");
                return; // success
            }

            Console.Error.WriteLine(
                $"  No data received. State={client.State}. Retrying in 3 s ...");
        }
        catch (OperationCanceledException) when (globalCts.IsCancellationRequested)
        {
            Console.WriteLine("Cancelled (global timeout).");
            break;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Error — {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            await client.DisconnectAsync();
            Console.WriteLine("  Disconnected.");
        }

        try { await Task.Delay(TimeSpan.FromSeconds(3), globalCts.Token); }
        catch (OperationCanceledException) { break; }
    }
}

static async Task InstructionExecutorExample()
{
    const string RobotIp = "127.0.0.1";
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
    const string RobotIp = "127.0.0.1";
    Console.WriteLine($"\n--- Dashboard Client Example (robot at {RobotIp}) ---");

    using var dashboard = new UniversalRobotics.Dashboard.DashboardClientG5(RobotIp);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

    try
    {
        await dashboard.ConnectAsync(cts.Token);
        Console.WriteLine("Connected to dashboard server.");

        string mode = await dashboard.GetRobotModeAsync(cts.Token);
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

static string FormatVector(IReadOnlyList<double> values)
{
    var parts = new string[values.Count];
    for (int i = 0; i < values.Count; i++)
        parts[i] = values[i].ToString("F4");
    return "[" + string.Join(", ", parts) + "]";
}

static async Task<bool> CheckTcpReachableAsync(string host, int port, TimeSpan timeout)
{
    try
    {
        using var tcp = new TcpClient();
        var connectTask = tcp.ConnectAsync(host, port);
        var winner = await Task.WhenAny(connectTask, Task.Delay(timeout));
        if (winner != connectTask) return false;
        await connectTask; // surface any socket exception
        return tcp.Connected;
    }
    catch
    {
        return false;
    }
}

// ─── Simple console logger used for diagnostics ───────────────────────────────

sealed class InlineConsoleLogger : ILogger
{
    private readonly string _category;
    public InlineConsoleLogger(string category) => _category = category;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Console.Error.WriteLine($"  [{_category}/{logLevel}] {formatter(state, exception)}");
        if (exception != null)
            Console.Error.WriteLine(
                $"    {exception.GetType().Name}: {exception.Message}");
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

