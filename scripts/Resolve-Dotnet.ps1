function Resolve-NiumaDotnet {
    $privateSdk = Join-Path $PSScriptRoot '..\.private\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $privateSdk) { return [System.IO.Path]::GetFullPath($privateSdk) }
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $command) { throw 'Install the .NET SDK specified by global.json first.' }
    return $command.Source
}

function Invoke-NiumaDotnet {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}

function Get-NiumaVersion {
    $repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    [xml]$properties = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props') -Raw
    $version = [string]$properties.Project.PropertyGroup.Version
    $webVersion = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'package.json') -Raw | ConvertFrom-Json).version
    if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$' -or $webVersion -ne $version) {
        throw 'Directory.Build.props and package.json must contain the same release version.'
    }
    return $version
}
