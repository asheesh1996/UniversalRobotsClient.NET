# UniversalRobotsClient.NET

Native, managed C# communication with Universal Robots controllers. Targets **.NET Standard 2.0**; examples, tests, and development tools use the .NET 10 SDK. No P/Invoke, C++/CLI, or native UR client library is required.

**Preview software, not a safety-rated controller.** Protocol implementations and offline tests do not establish hardware safety or hard real-time behavior. Validate your application in URSim, then in a risk-assessed robot installation. No test in CI moves a physical robot.

## Build and Test

From this repository root:

```powershell
dotnet restore UniversalRobotsClient.NET.slnx
dotnet build UniversalRobotsClient.NET.slnx -c Release --no-restore
dotnet test UniversalRobotsClient.NET.slnx -c Release --no-build
dotnet pack UniversalRobotsClient.NET/UniversalRobotsClient.NET.csproj -c Release --no-build -o artifacts/packages
dotnet run --project UniversalRobotsClient.NET.Examples
```

The XML solution (`.slnx`) is intentional and supported by the .NET 10 SDK. Only the library is packable. Output includes `.nupkg`, portable symbols (`.snupkg`), XML API documentation, the control script, and license notices.

## Read Joints and TCP Pose

RTDE uses controller TCP port **30004**. This example is read-only: it neither starts a motion program nor changes outputs. `actual_q` is six joint angles in radians. `actual_TCP_pose` is `[x,y,z,rx,ry,rz]`: metres in the base frame and an axis-angle rotation vector in radians, not Euler angles.

```csharp
using System;
using System.Threading;
using UniversalRobotics.Rtde;

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
using var client = new RtdeClient("127.0.0.1",
    new[] { "timestamp", "actual_q", "actual_TCP_pose" }, targetFrequency: 125);
try
{
    await client.InitAsync(maxConnectionAttempts: 1, cancellationToken: timeout.Token);
    await client.StartAsync(readInBackground: false, cancellationToken: timeout.Token);
    var packet = await client.GetNextDataPackageAsync(timeout.Token);
    if (packet != null && packet.GetData<double[]>("actual_q", out var joints)
        && packet.GetData<double[]>("actual_TCP_pose", out var tcp))
    {
        Console.WriteLine(string.Join(", ", joints));
        Console.WriteLine(string.Join(", ", tcp));
    }
}
finally
{
    await client.DisconnectAsync();
}
```

`127.0.0.1` requires URSim with port forwarding on your machine. Use the controller address for a remote simulator or robot. RTDE protocol v2 requires a positive on-wire frequency; the library resolves the default `0` to 125 Hz for CB3 or 500 Hz for newer controllers. Initialization checks streaming readiness before reporting success.

## Interfaces

| Component | Endpoint / Responsibility |
| --- | --- |
| `RtdeClient` | Binary RTDE, port 30004; inline or file recipes, direct/background reads |
| `PrimaryClient` | Binary primary interface, port 30001; configurable port, hardware/configuration/error messages |
| `DashboardClientG5` | CB3/e-Series line protocol, port 29999 |
| `DashboardClientX` | PolyScope X HTTP Robot API, port 80; API-version-gated commands |
| `ReverseInterface` | PC listener, default 50001 |
| `ScriptSender` | PC listener, default 50002 |
| `TrajectoryPointInterface` | PC listener, default 50003; finite and open-ended trajectory protocol |
| `ScriptCommandInterface` | PC listener, default 50004 |
| `UrDriver` | Composition facade for control applications |

Direct and background RTDE reads are mutually exclusive. Background reads coalesce samples, so they are not a lossless history. Motion requires a matching external-control script and robot-reachable `UrDriverConfiguration.ReverseIp`. The packaged script uses the modern `100000000` spline derivative multiplier; legacy scripts need `1000000`. The driver detects the template marker when rendering scripts. Do not change the multiplier independently of the running script.

## Compatibility and Limitations

This update reviews C++ commits `757d512..d00cd65` (70 commits). Existing names and namespaces (`UniversalRobotics.*`) are retained. Wire correctness fixes are intentional behavior changes: `RobotType` now has an `int` backing type; Robot API defaults to HTTP port 80; invalid packet lengths and unsafe includes fail explicitly. No hard-real-time/FIFO scheduler is promised on managed runtimes.

The primary and driver layers remain a partial port of the full C++ library. Automatic reconnect, complete primary safety/masterboard parsing, blocking script execution semantics, and some high-level control workflows need further work. See [compatibility notes](docs/compatibility.md) before using motion or robot management features. This preview is **not a claim of complete functional parity**.

## Documentation

```powershell
dotnet tool restore
dotnet docfx docs/docfx.json
```

DocFX extracts public APIs and XML summaries from the library project into `docs/_site`. The Pages workflow builds docs for pull requests and publishes only from `main` or a manual run on `main`. Enable **Settings > Pages > Source: GitHub Actions**. The resulting address is `https://<owner>.github.io/<repository>/`; no owner is guessed in source.

## Releases

See [CONTRIBUTING.md](CONTRIBUTING.md) for validation and [release setup](docs/releases.md) for NuGet and GitHub Pages configuration. No workflow publishes a package on a pull request or ordinary branch push. SourceLink is configured for GitHub; set the real `origin` remote before release. Local packages without a remote are usable but their source URLs cannot be validated.

## License and Attribution

Apache-2.0 for this implementation. Imported upstream data retains its separate BSD-style notice; see [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) and [UPSTREAM-DATA-LICENSE.txt](UPSTREAM-DATA-LICENSE.txt). This is an independent project, not an official Universal Robots SDK or endorsement.