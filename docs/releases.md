# Release Setup

## One-Time Owner Actions

1. Create the public GitHub repository and set its real `origin` URL. Workflows derive `RepositoryUrl` from `GITHUB_REPOSITORY`.
2. Enable private vulnerability reporting and require CI/docs checks on `main`.
3. Enable GitHub Pages with **GitHub Actions** as the source.
4. Create a GitHub environment named `nuget`, add required reviewers, and restrict it to version tags.
5. Add a scoped NuGet push API key as the environment secret **NUGET_API_KEY**. Never add its value to source or command logs.
6. Confirm ownership/availability of NuGet ID `UniversalRobotsClient.NET`, author identity, and license notices before the first release.

## Intentional Release

Push a reviewed tag such as `v0.1.0-preview.1`. The release workflow validates its SemVer shape, derives the package version, restores, builds, tests, packs, and uploads artifacts before entering the protected `nuget` publishing job. That job downloads those exact tested artifacts and pushes the `.nupkg` and associated symbols to nuget.org. Pull requests and branch pushes cannot enter it.

Tag creation and NuGet publication are not performed by local validation. Re-running a release skips an already-published version; increment the version for changed binaries.

## Local SourceLink

Portable PDBs and SourceLink are enabled. Before validating source URLs, commit the intended changes and configure the real GitHub remote; a clean checkout is required for useful immutable source links. No fake remote is configured by setup. Local metadata can be supplied explicitly:

```powershell
dotnet pack UniversalRobotsClient.NET/UniversalRobotsClient.NET.csproj -c Release -p:RepositoryUrl=https://github.com/OWNER/REPOSITORY
```

Replace the example URL with your actual repository. GitHub-hosted execution, branch protection, Pages deployment, and NuGet credentials must be verified after repository creation.