param([string]$Upstream = (Join-Path $PSScriptRoot '../../Universal_Robots_Client_Library'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$resources = Join-Path $root 'UniversalRobotsClient.NET/Resources'
[void][IO.Directory]::CreateDirectory($resources)
$header = Get-Content (Join-Path $Upstream 'include/ur_client_library/ur/error_code_texts.h') -Raw
$version = [regex]::Match($header, 'ERROR_CODE_JSON_VERSION = "([^"]+)"').Groups[1].Value
if (-not $version) { throw 'Error catalog version not found.' }
$entries = [ordered]@{}
foreach ($entry in [regex]::Matches($header, '\{ UINT64_C\(0x([0-9A-Fa-f]{16})\), ("(?:\\.|[^"\\])*") \}')) {
    $literal = [regex]::Replace($entry.Groups[2].Value, '(?:\\[0-7]{3})+', {
        param($match)
        [byte[]]$bytes = @([regex]::Matches($match.Value, '\\([0-7]{3})') | ForEach-Object { [Convert]::ToByte($_.Groups[1].Value, 8) })
        $json = ConvertTo-Json ([Text.Encoding]::UTF8.GetString($bytes)) -Compress
        return $json.Substring(1, $json.Length - 2)
    })
    $entries[$entry.Groups[1].Value.ToUpperInvariant()] = ConvertFrom-Json $literal
}
$expected = [regex]::Matches($header, '\{ UINT64_C\(').Count
if ($entries.Count -ne $expected -or $expected -eq 0) { throw 'Not all upstream error entries were parsed.' }
[ordered]@{ version = $version; entries = $entries } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $resources 'error-code-texts.json') -Encoding utf8
Copy-Item (Join-Path $Upstream 'resources/external_control.urscript') $resources
Copy-Item (Join-Path $Upstream 'LICENSE_apache_2_0') (Join-Path $root 'LICENSE')
$licenseBlock = [regex]::Match($header, '(?s)// -- BEGIN LICENSE BLOCK.*?// -- END LICENSE BLOCK[^\r\n]*').Value
($licenseBlock -replace '(?m)^// ?', '') | Set-Content (Join-Path $root 'UPSTREAM-DATA-LICENSE.txt') -Encoding utf8
Write-Output "Imported $expected error descriptions (v$version) and the matching control script."