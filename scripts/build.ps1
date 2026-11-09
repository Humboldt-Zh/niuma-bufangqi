param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Resolve-Dotnet.ps1')
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnetExecutable = Resolve-NiumaDotnet
$null = Get-NiumaVersion
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Push-Location $root
try {
    Invoke-NiumaDotnet $dotnetExecutable @('restore', 'NiumaBufangqi.sln', '--locked-mode')
    Invoke-NiumaDotnet $dotnetExecutable @('build', 'NiumaBufangqi.sln', '--configuration', $Configuration, '--no-restore')
    Invoke-NiumaDotnet $dotnetExecutable @('run', '--project', 'tests/NiumaBufangqi.Core.Tests', '--configuration', $Configuration, '--no-build')
    & node --check app.js
    if ($LASTEXITCODE -ne 0) { throw 'app.js syntax check failed' }
    & node --check desktop-bridge.js
    if ($LASTEXITCODE -ne 0) { throw 'desktop bridge syntax check failed' }
    & node --check paper-play.js
    if ($LASTEXITCODE -ne 0) { throw 'paper interaction syntax check failed' }
    $nodeTests = @(Get-ChildItem -LiteralPath (Join-Path $root 'tests') -Filter '*.test.cjs' -File | ForEach-Object FullName)
    if ($nodeTests.Count -eq 0) { throw 'No JavaScript tests were discovered.' }
    & node --test @nodeTests
    if ($LASTEXITCODE -ne 0) { throw 'JavaScript model tests failed' }
} finally { Pop-Location }
