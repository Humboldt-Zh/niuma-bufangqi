[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$AssetsFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-JsonValue {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -ne $property) { return $property.Value }
    return $null
}

function Get-ExactPackageVersion {
    param([string]$Value)
    $version = $Value.Trim()
    if ($version -match '^\[\s*([^,\]\s]+)\s*(?:,\s*([^,\]\s]+)\s*)?\]$') {
        if ($Matches[2] -and $Matches[1] -cne $Matches[2]) { throw "Runtime pack version is not exact: $Value" }
        $version = $Matches[1]
    }
    if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
        throw "Cannot resolve an exact runtime pack version from '$Value'. Publish with restore first."
    }
    return $version
}

function Test-LicenseName {
    param([string]$Name)
    return $Name -match '^(?i:licen[cs]e|third[-_ ]?party[-_ ]?notices|notice)(?:\.(?i:txt|md))?$'
}

function Read-PackageLicenses {
    param([string]$PackageDirectory, [string]$PackageId, [string]$Version)
    $files = [System.Collections.Generic.List[object]]::new()
    $archivePath = Join-Path $PackageDirectory ($PackageId.ToLowerInvariant() + '.' + $Version.ToLowerInvariant() + '.nupkg')
    if (Test-Path -LiteralPath $archivePath -PathType Leaf) {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
        try {
            foreach ($entry in $archive.Entries) {
                $name = [System.IO.Path]::GetFileName($entry.FullName)
                if (-not (Test-LicenseName $name)) { continue }
                if ($files | Where-Object { $_.Name -ieq $name }) { throw "Ambiguous license name in ${PackageId}: $name" }
                $entryStream = $entry.Open()
                $buffer = [System.IO.MemoryStream]::new()
                try { $entryStream.CopyTo($buffer); $bytes = $buffer.ToArray() }
                finally { $entryStream.Dispose(); $buffer.Dispose() }
                $files.Add([pscustomobject]@{ Name = $name; Bytes = $bytes })
            }
        } finally { $archive.Dispose() }
    }
    else {
        foreach ($file in Get-ChildItem -LiteralPath $PackageDirectory -File) {
            if (Test-LicenseName $file.Name) {
                $files.Add([pscustomobject]@{ Name = $file.Name; Bytes = [System.IO.File]::ReadAllBytes($file.FullName) })
            }
        }
    }
    if (-not ($files | Where-Object { $_.Name -match '^(?i:licen[cs]e)(?:\.(?i:txt|md))?$' })) {
        throw "The restored runtime pack has no LICENSE: $PackageId/$Version"
    }
    if ($PackageId -like 'Microsoft.NETCore.App.Runtime.*' -and
        -not ($files | Where-Object { $_.Name -match '^(?i:third[-_ ]?party[-_ ]?notices|notice)(?:\.(?i:txt|md))?$' })) {
        throw "The restored .NET runtime pack has no third-party notices: $PackageId/$Version"
    }
    if ($files | Where-Object { $_.Bytes.Length -eq 0 }) { throw "An empty runtime license was found: $PackageId/$Version" }
    return $files.ToArray()
}

$publishRoot = [System.IO.Path]::GetFullPath($PublishDirectory)
$assetsPath = [System.IO.Path]::GetFullPath($AssetsFile)
if (-not (Test-Path -LiteralPath $publishRoot -PathType Container)) { throw 'PublishDirectory must be an existing publish staging directory.' }
if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) { throw 'AssetsFile must be the project.assets.json produced by publish restore.' }
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json

$runtimeConfigs = @(Get-ChildItem -LiteralPath $publishRoot -Filter '*.runtimeconfig.json' -File)
if ($runtimeConfigs.Count -ne 1) { throw 'Expected exactly one application runtimeconfig.json in the publish directory.' }
$runtimeConfig = Get-Content -LiteralPath $runtimeConfigs[0].FullName -Raw | ConvertFrom-Json
$includedFrameworks = @(Get-JsonValue (Get-JsonValue $runtimeConfig 'runtimeOptions') 'includedFrameworks')
$depsName = $runtimeConfigs[0].Name -replace '\.runtimeconfig\.json$', '.deps.json'
$depsPath = Join-Path $publishRoot $depsName
if (-not (Test-Path -LiteralPath $depsPath -PathType Leaf)) { throw 'Published application deps.json is missing.' }
$deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json
$runtimeTarget = [string](Get-JsonValue (Get-JsonValue $deps 'runtimeTarget') 'name')
if ($runtimeTarget -notmatch '/(win-(?:x64|arm64|x86))$') { throw "Cannot resolve the published Windows runtime identifier: $runtimeTarget" }
$runtimeIdentifier = $Matches[1]

$downloads = [System.Collections.Generic.List[object]]::new()
$frameworkMap = Get-JsonValue (Get-JsonValue $assets 'project') 'frameworks'
if ($null -eq $frameworkMap) { throw 'Assets file has no restored project frameworks.' }
foreach ($framework in $frameworkMap.PSObject.Properties) {
    foreach ($download in @(Get-JsonValue $framework.Value 'downloadDependencies')) {
        if ($null -eq $download) { continue }
        $id = [string](Get-JsonValue $download 'name')
        if ($id -match '^Microsoft\.(?:NETCore|WindowsDesktop)\.App\.Runtime\.win-(?:x64|arm64|x86)$') {
            $downloads.Add([pscustomobject]@{ Id = $id; Version = Get-ExactPackageVersion ([string](Get-JsonValue $download 'version')) })
        }
    }
}

$roots = [System.Collections.Generic.List[string]]::new()
$rootSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$packageFolders = Get-JsonValue $assets 'packageFolders'
if ($null -ne $packageFolders) { foreach ($folder in $packageFolders.PSObject.Properties) { if ($rootSet.Add($folder.Name)) { $roots.Add($folder.Name) } } }
if ($env:NUGET_PACKAGES -and $rootSet.Add($env:NUGET_PACKAGES)) { $roots.Add($env:NUGET_PACKAGES) }
$userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
if ($userProfile) { $standardRoot = Join-Path $userProfile '.nuget/packages'; if ($rootSet.Add($standardRoot)) { $roots.Add($standardRoot) } }

$packages = [System.Collections.Generic.List[object]]::new()
foreach ($frameworkName in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
    $published = @($includedFrameworks | Where-Object { $_ -and (Get-JsonValue $_ 'name') -ceq $frameworkName })
    if ($published.Count -ne 1) { throw "Self-contained published framework is missing or ambiguous: $frameworkName" }
    $version = Get-ExactPackageVersion ([string](Get-JsonValue $published[0] 'version'))
    $packageId = "$frameworkName.Runtime.$runtimeIdentifier"
    if (-not ($downloads | Where-Object { $_.Id -ieq $packageId -and $_.Version -ceq $version })) {
        throw "Publish output and restore assets do not agree on $packageId/$version. Run publish restore before copying licenses."
    }
    $packageDirectory = $null
    foreach ($root in $roots) {
        $candidate = Join-Path (Join-Path $root $packageId.ToLowerInvariant()) $version.ToLowerInvariant()
        if (Test-Path -LiteralPath $candidate -PathType Container) { $packageDirectory = $candidate; break }
    }
    if ($null -eq $packageDirectory) { throw "The exact restored runtime pack is not available locally: $packageId/$version" }
    $files = @(Read-PackageLicenses $packageDirectory $packageId $version)
    $packages.Add([pscustomobject]@{ Id = $packageId; Version = $version; Files = $files })
}

# Resolve every required source before writing. No network or whole-package extraction is used.
$licenseRoot = Join-Path $publishRoot 'licenses'
$inventory = [System.Collections.Generic.List[object]]::new()
foreach ($package in $packages) {
    $relativeDirectory = $package.Id.ToLowerInvariant() + '/' + $package.Version
    $destination = Join-Path $licenseRoot $relativeDirectory
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $copied = [System.Collections.Generic.List[object]]::new()
    foreach ($file in $package.Files) {
        [System.IO.File]::WriteAllBytes((Join-Path $destination $file.Name), $file.Bytes)
        $hash = [System.Security.Cryptography.SHA256]::Create()
        try { $digest = [BitConverter]::ToString($hash.ComputeHash($file.Bytes)).Replace('-', '').ToLowerInvariant() }
        finally { $hash.Dispose() }
        $copied.Add([pscustomobject]@{ file = $relativeDirectory + '/' + $file.Name; sha256 = $digest })
    }
    $inventory.Add([pscustomobject]@{ id = $package.Id; version = $package.Version; files = $copied.ToArray() })
    Write-Output "Runtime licenses: $($package.Id)/$($package.Version) ($($package.Files.Count) files)"
}
$manifest = [pscustomobject]@{ schemaVersion = 1; runtimeIdentifier = $runtimeIdentifier; packages = $inventory.ToArray() }
$manifestJson = $manifest | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText((Join-Path $licenseRoot 'runtime-packages.json'), $manifestJson + "`n", [System.Text.UTF8Encoding]::new($false))
