$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.IO.Compression.FileSystem
$package = Get-ChildItem (Join-Path $root 'artifacts/packages/*.nupkg') | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $package) { throw 'Run dotnet pack first.' }
$archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $names = @($archive.Entries.FullName)
    foreach ($required in @('lib/netstandard2.0/UniversalRobotsClient.NET.dll', 'lib/netstandard2.0/UniversalRobotsClient.NET.xml', 'README.md', 'LICENSE', 'UPSTREAM-DATA-LICENSE.txt', 'THIRD-PARTY-NOTICES.txt', 'contentFiles/any/any/resources/external_control.urscript')) {
        if ($names -notcontains $required) { throw "Package missing: $required" }
    }
    $nuspec = $archive.Entries | Where-Object FullName -Like '*.nuspec' | Select-Object -First 1
    $reader = [IO.StreamReader]::new($nuspec.Open())
    try { [xml]$metadata = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($metadata.package.metadata.id -ne 'UniversalRobotsClient.NET') { throw 'Unexpected package ID.' }
    if ($metadata.package.metadata.license.InnerText -ne 'Apache-2.0') { throw 'Missing license metadata.' }
} finally { $archive.Dispose() }
$symbols = [IO.Compression.ZipFile]::OpenRead([IO.Path]::ChangeExtension($package.FullName, 'snupkg'))
try {
    if (-not ($symbols.Entries.FullName -like '*.pdb')) { throw 'Symbol package has no portable PDB.' }
} finally { $symbols.Dispose() }

foreach ($file in @('.config/dotnet-tools.json', 'docs/docfx.json', 'upstream-sync.json')) {
    $null = Get-Content (Join-Path $root $file) -Raw | ConvertFrom-Json
}
$sourceFiles = @(git -C $root ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate repository files.' }
$credentialPattern = '(gh[pousr]_[A-Za-z0-9]{36}|github_pat_[A-Za-z0-9_]{40,}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----|"(?:password|api[_-]?key|access[_-]?token)"\s*:\s*"[A-Za-z0-9+/=_-]{24,}")'
foreach ($relative in $sourceFiles) {
    $path = Join-Path $root $relative
    if (-not (Test-Path $path -PathType Leaf)) { continue }
    if ([IO.Path]::GetExtension($path) -notin @('.cs', '.json', '.yml', '.yaml', '.xml', '.csproj', '.md', '.txt', '.ps1')) { continue }
    if ((Get-Content $path -Raw) -match $credentialPattern) { throw "Possible credential detected in $relative (value withheld)." }
}

$documents = @(Get-ChildItem $root -File -Filter '*.md') + @(Get-ChildItem (Join-Path $root 'docs') -File -Filter '*.md')
$workspaceDocs = Join-Path $root '../docs/codebase'
if (Test-Path $workspaceDocs) { $documents += @(Get-ChildItem $workspaceDocs -File -Filter '*.md') }
foreach ($document in $documents) {
    foreach ($link in [regex]::Matches((Get-Content $document.FullName -Raw), '\]\(([^)]+)\)')) {
        $target = $link.Groups[1].Value
        if ($target -match '^(https?://|mailto:|#)') { continue }
        $target = [Uri]::UnescapeDataString(($target -split '#')[0])
        if (-not (Test-Path (Join-Path $document.DirectoryName $target))) { throw "Broken link in $($document.Name): $target" }
    }
}
Write-Output "Package payload, symbols, JSON configuration, local documentation links and credential-pattern scan passed."