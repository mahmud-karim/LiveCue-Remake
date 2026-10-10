<#
.SYNOPSIS
    Publishes the Windows side of LiveCue into one folder, and checks the result.

.DESCRIPTION
    Both executables (LiveCueRemake.exe and LiveCueRelayHost.exe) are published self-contained for
    win-x64 into the same folder, so they share one copy of the .NET runtime: no trimming and no
    single-file bundle (docs/architecture/03-system-architecture.md, section 3.15). The folder also
    receives the speech lab (asr\, without private\, tests\ and caches), LICENSE and
    THIRD-PARTY-NOTICES.md.

    The script then fails unless both executables are there and the folder holds no pairing*.json, no
    settings.json and no asr\private.

    With -Zip it also writes LiveCueRemake-win-x64.zip and its SHA-256 file beside the output folder.

    The .NET SDK is taken from the LIVECUE_DOTNET_EXE environment variable (the full path of dotnet.exe)
    when it is set, otherwise from the search path. Nothing is installed.

    First version (Windows foundation). The CI and release work package completes this file.

.EXAMPLE
    ./scripts/publish-windows.ps1 -Output artifacts/publish
    ./scripts/publish-windows.ps1 -Output artifacts/publish -Zip
#>
[CmdletBinding()]
param(
    # The folder to publish into. It is emptied first. Relative paths are relative to the repository root.
    [Parameter(Mandatory = $true)]
    [string] $Output,

    # Also write the zip and its checksum.
    [switch] $Zip,

    [string] $Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = Split-Path -Parent $PSScriptRoot
$WindowsRoot = Join-Path $RepositoryRoot 'windows'
$PublishDir = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $RepositoryRoot $Output }
$PublishDir = [System.IO.Path]::GetFullPath($PublishDir)
$ZipName = 'LiveCueRemake-win-x64.zip'

function Resolve-Dotnet {
    if ($env:LIVECUE_DOTNET_EXE) {
        if (-not (Test-Path -LiteralPath $env:LIVECUE_DOTNET_EXE)) {
            throw 'LIVECUE_DOTNET_EXE points at a file that does not exist.'
        }
        return $env:LIVECUE_DOTNET_EXE
    }
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    throw 'The .NET SDK was not found. Set LIVECUE_DOTNET_EXE to the full path of dotnet.exe, or put dotnet on the search path.'
}

$Dotnet = Resolve-Dotnet

if (Test-Path -LiteralPath $PublishDir) { Remove-Item -LiteralPath $PublishDir -Recurse -Force }
New-Item -ItemType Directory -Path $PublishDir | Out-Null

# The desktop app first, then the console host into the same folder: the host needs a subset of the
# same runtime files, so the folder ends up with one copy of the runtime and two executables.
foreach ($project in 'src/LiveCue.Desktop/LiveCue.Desktop.csproj', 'src/LiveCue.Relay.Host/LiveCue.Relay.Host.csproj') {
    Write-Host "> dotnet publish $project"
    & $Dotnet publish (Join-Path $WindowsRoot $project) -c $Configuration -r win-x64 --self-contained true -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish $project failed with exit code $LASTEXITCODE." }
}

# The speech lab: everything except what must never ship (private weights and samples, tests, caches).
$asrSource = Join-Path $RepositoryRoot 'asr'
$asrTarget = Join-Path $PublishDir 'asr'
$excludedFolders = 'private', 'tests', '__pycache__', '.pytest_cache', '.ruff_cache', '.venv'
if (Test-Path -LiteralPath $asrSource) {
    $sourceRoot = (Resolve-Path -LiteralPath $asrSource).Path.TrimEnd('\', '/')
    foreach ($file in Get-ChildItem -LiteralPath $asrSource -Recurse -File -Force) {
        $relative = $file.FullName.Substring($sourceRoot.Length + 1)
        $parts = $relative -split '[\\/]'
        if (@($parts | Where-Object { $excludedFolders -contains $_ }).Count -gt 0) { continue }
        if ($file.Extension -in '.pyc', '.wav') { continue }
        $target = Join-Path $asrTarget $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}

Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'LICENSE') -Destination $PublishDir
Copy-Item -LiteralPath (Join-Path $WindowsRoot 'THIRD-PARTY-NOTICES.md') -Destination $PublishDir

# ---- Checks on the result ---------------------------------------------------------------------------

foreach ($required in 'LiveCueRemake.exe', 'LiveCueRelayHost.exe', 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
    if (-not (Test-Path -LiteralPath (Join-Path $PublishDir $required))) {
        throw "$required is missing from the publish folder."
    }
}

$forbidden = @(Get-ChildItem -LiteralPath $PublishDir -Recurse -File -Force |
    Where-Object { $_.Name -like 'pairing*.json' -or $_.Name -eq 'settings.json' })
if ($forbidden.Count -gt 0) {
    throw "The publish folder must not hold pairing or settings files: $($forbidden.Name -join ', ')."
}
if (Test-Path -LiteralPath (Join-Path $asrTarget 'private')) {
    throw 'The publish folder must not hold asr\private.'
}

$files = @(Get-ChildItem -LiteralPath $PublishDir -Recurse -File)
$folderMb = [math]::Round((($files | Measure-Object -Property Length -Sum).Sum) / 1MB, 1)
Write-Host "Published $($files.Count) files, $folderMb MB, to $PublishDir"

if ($Zip) {
    $zipPath = Join-Path (Split-Path -Parent $PublishDir) $ZipName
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Compress-Archive -Path (Join-Path $PublishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()

    # Lower-case hex, two spaces, the file name: the form sha256sum checks.
    Set-Content -LiteralPath "$zipPath.sha256" -Value "$hash  $ZipName" -Encoding ascii
    $zipMb = [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 1)
    Write-Host "Zip: $zipMb MB, SHA-256 $hash"
}
