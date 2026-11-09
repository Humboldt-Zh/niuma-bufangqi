param([ValidateSet('win-x64')][string]$Runtime = 'win-x64', [switch]$SkipChecks)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Resolve-Dotnet.ps1')
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnetExecutable = Resolve-NiumaDotnet
$version = Get-NiumaVersion
if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw 'package.json version must be a valid release identifier.' }
$artifactName = "NiumaBufangqi-$version-$Runtime"
$artifactRoot = Join-Path $root 'artifacts'
$output = Join-Path $artifactRoot $artifactName
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
if (-not $SkipChecks) { & (Join-Path $PSScriptRoot 'build.ps1') }
Push-Location $root
try {
    # A new staging folder prevents stale release files from entering the ZIP.
    $stage = Join-Path $artifactRoot ('.stage-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Invoke-NiumaDotnet $dotnetExecutable @('publish', 'src/NiumaBufangqi.Desktop/NiumaBufangqi.Desktop.csproj', '-c', 'Release', '-r', $Runtime, '--self-contained', 'true', '-o', $stage,
        '-p:PublishSingleFile=false', '-p:DebugType=None', '-p:DebugSymbols=false', '-p:CopyOutputSymbolsToPublishDirectory=false',
        '-p:RestoreLockedMode=true', "-p:NuGetLockFilePath=packages.$Runtime.lock.json")
    foreach ($file in @('LICENSE','THIRD_PARTY_NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $stage }
    & (Join-Path $root 'tools\Copy-RuntimeLicenses.ps1') -PublishDirectory $stage -AssetsFile (Join-Path $root 'src\NiumaBufangqi.Desktop\obj\project.assets.json')
    $sourceCommit = & git rev-parse HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') { $sourceCommit = $null }
    $sourceChanges = @(& git status --porcelain 2>$null)
    $buildInfo = [ordered]@{ version = $version; runtime = $Runtime; sourceCommit = $sourceCommit; sourceModified = ($sourceChanges.Count -gt 0) }
    $buildInfo | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'build-info.json') -Encoding UTF8
    $runText = "牛马不放器 $version`r`n`r`n请完整解压后打开 NiumaBufangqi.exe。`r`n此包自带 .NET，仍需 Microsoft Edge WebView2 Runtime。`r`n首次运行请填写自己的薪资与排班，默认不会设置开机启动。`r`n数据仅保存在本机，可通过软件设置打开数据目录。`r`n首版未数字签名；请从你信任的项目发布页获取并核对 SHA256。`r`n"
    Set-Content -LiteralPath (Join-Path $stage '打开软件.txt') -Value $runText -Encoding UTF8
    if (Test-Path -LiteralPath $output) {
        $output = Join-Path $artifactRoot ($artifactName + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    }
    $artifactBoundary = [System.IO.Path]::GetFullPath($artifactRoot) + [System.IO.Path]::DirectorySeparatorChar
    foreach ($target in @($stage, $output)) {
        if (-not [System.IO.Path]::GetFullPath($target).StartsWith($artifactBoundary, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'Release staging path escaped the artifacts directory.'
        }
    }
    Move-Item -LiteralPath $stage -Destination $output
    $archive = $output + '.zip'
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText($archive + '.sha256', "$hash  $([System.IO.Path]::GetFileName($archive))`n", [System.Text.UTF8Encoding]::new($false))
    Write-Output "Portable folder: $output"
    Write-Output "Release archive: $archive"
    Write-Output "SHA256: $hash"
} finally { Pop-Location }
