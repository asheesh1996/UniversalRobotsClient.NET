# Contributing

Use the .NET 10 SDK. Keep the library compatible with .NET Standard 2.0 and retain existing public APIs where feasible. Tests use xUnit and live in `UniversalRobotsClient.NET.Test`.

Run restore, Release build, tests, and pack using the README commands before submitting a pull request. Run `dotnet tool restore` and `dotnet docfx docs/docfx.json` for API documentation changes. Include XML summaries on public APIs.

Protocol changes need tests asserting actual wire bytes, not merely serialization round trips. Use loopback listeners on ephemeral ports and cancellable waits. CI must not depend on a robot or trigger motion. Record separately any URSim/hardware validation, firmware version, and commands used.

For an upstream refresh, inspect the stored baseline and C++ commit intent. `eng/Import-UpstreamAssets.ps1 -Upstream <checkout>` imports the matching control script/error catalog and notices. Review the generated asset diff, pin the source SHA in `upstream-sync.json`, and update compatibility notes. Never claim hardware parity solely from green unit tests.

Do not commit credentials, generated packages, test results, DocFX output, or local robot IP configuration. Repository owners should require the CI and documentation build checks using GitHub branch protection.