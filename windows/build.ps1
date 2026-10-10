<#
.SYNOPSIS
    Builds, tests and publishes the Windows side of LiveCue. CI runs the same script, one task per step.

.DESCRIPTION
    Tasks, in the order "all" runs them:

      restore   dotnet restore
      build     dotnet build -c Release
      test      dotnet test -c Release, results as .trx under artifacts/test-results
      publish   both executables, self-contained for win-x64, into ONE folder so they share one copy
                of the runtime (no trimming, no single-file bundle); then the zip and its SHA-256 file
      smoke     starts the published LiveCueRelayHost.exe on a free loopback port and checks it answers
      selftest  runs the published LiveCueRemake.exe --self-test and saves its screenshots

    Everything is written under windows/artifacts/, which git ignores.

    The .NET SDK is taken from the LIVECUE_DOTNET environment variable (full path of dotnet.exe) when
    it is set, then from DOTNET_ROOT, then from PATH. Nothing is installed.

.EXAMPLE
    ./build.ps1
    ./build.ps1 build, test
    ./build.ps1 selftest -Screenshots C:\Temp\shots
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('all', 'restore', 'build', 'test', 'publish', 'smoke', 'selftest')]
    [string[]] $Task = @('all'),

    [string] $Configuration = 'Release',

    # Where the self-test writes its PNGs and log. Default: artifacts/desktop.
    [string] $Screenshots
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = $PSScriptRoot
$Solution = Join-Path $Root 'LiveCueRemake.sln'
$Artifacts = Join-Path $Root 'artifacts'
$PublishName = 'LiveCueRemake-win-x64'
$PublishDir = Join-Path (Join-Path $Artifacts 'publish') $PublishName
$ZipPath = Join-Path $Artifacts "$PublishName.zip"
$TestResults = Join-Path $Artifacts 'test-results'
if (-not $Screenshots) { $Screenshots = Join-Path $Artifacts 'desktop' }

function Resolve-Dotnet {
    if ($env:LIVECUE_DOTNET) {
        if (-not (Test-Path -LiteralPath $env:LIVECUE_DOTNET)) { throw "LIVECUE_DOTNET points at a file that does not exist." }
        return $env:LIVECUE_DOTNET
    }
    if ($env:DOTNET_ROOT) {
        $candidate = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    throw 'The .NET SDK was not found. Set LIVECUE_DOTNET to the full path of dotnet.exe, or put dotnet on PATH.'
}

$Dotnet = Resolve-Dotnet

function Invoke-Dotnet {
    # A simple function on purpose: every argument, dashes included, goes to dotnet untouched.
    Write-Host "> dotnet $($args -join ' ')" -ForegroundColor DarkGray
    & $Dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args[0]) failed with exit code $LASTEXITCODE." }
}

function Invoke-Restore {
    Invoke-Dotnet --version
    Invoke-Dotnet restore $Solution
}

function Invoke-Build {
    Invoke-Dotnet build $Solution -c $Configuration --no-restore
}

function Invoke-Test {
    Invoke-Dotnet test $Solution -c $Configuration --no-build --logger 'trx' --results-directory $TestResults
}

function Invoke-Publish {
    if (Test-Path -LiteralPath $PublishDir) { Remove-Item -LiteralPath $PublishDir -Recurse -Force }

    # The desktop app first, then the console host into the same folder: the host needs a subset of
    # the same runtime files, so the folder ends up with one copy of the runtime and two executables.
    foreach ($project in 'src/LiveCue.Desktop/LiveCue.Desktop.csproj', 'src/LiveCue.Relay.Host/LiveCue.Relay.Host.csproj') {
        Invoke-Dotnet publish (Join-Path $Root $project) -c $Configuration -r win-x64 --self-contained true -o $PublishDir
    }

    Copy-Item -LiteralPath (Join-Path (Split-Path $Root -Parent) 'LICENSE') -Destination $PublishDir

    foreach ($executable in 'LiveCueRemake.exe', 'LiveCueRelayHost.exe') {
        if (-not (Test-Path -LiteralPath (Join-Path $PublishDir $executable))) { throw "$executable is missing from the publish folder." }
    }

    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
    Compress-Archive -Path (Join-Path $PublishDir '*') -DestinationPath $ZipPath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$ZipPath.sha256" -Value "$hash  $PublishName.zip" -Encoding ascii

    $files = @(Get-ChildItem -LiteralPath $PublishDir -Recurse -File)
    $folderMb = [math]::Round((($files | Measure-Object -Property Length -Sum).Sum) / 1MB, 1)
    $zipMb = [math]::Round((Get-Item -LiteralPath $ZipPath).Length / 1MB, 1)
    Write-Host "Published $($files.Count) files, $folderMb MB, to $PublishDir"
    Write-Host "Zip: $zipMb MB, SHA-256 $hash"
}

function Invoke-Smoke {
    # Proves that the published folder really contains a working ASP.NET Core: the console host and the
    # desktop app load the relay from the same files. The host binds a free loopback port, and with no
    # pairing yet the correct answer to anyone is the protocol's 401.
    $hostExe = Join-Path $PublishDir 'LiveCueRelayHost.exe'
    if (-not (Test-Path -LiteralPath $hostExe)) { throw 'Run the publish task first.' }

    $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("LiveCue-smoke-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch | Out-Null
    $dataDir = Join-Path $scratch 'data'
    $stdout = Join-Path $scratch 'out.txt'
    $stderr = Join-Path $scratch 'err.txt'

    try {
        $check = Start-Process -FilePath $hostExe -ArgumentList '--check-only', '--data-dir', "`"$dataDir`"" `
            -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        $checkText = Get-Content -LiteralPath $stdout -Raw
        if ($check.ExitCode -ne 0 -or $checkText -notmatch 'LiveCue Relay prerequisites passed\.') {
            throw "LiveCueRelayHost --check-only failed (exit code $($check.ExitCode))."
        }

        $relay = Start-Process -FilePath $hostExe -ArgumentList '--port', '0', '--data-dir', "`"$dataDir`"" `
            -NoNewWindow -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        try {
            $port = 0
            for ($attempt = 0; $attempt -lt 100 -and $port -eq 0; $attempt++) {
                Start-Sleep -Milliseconds 200
                if ($relay.HasExited) { throw "LiveCueRelayHost exited early with code $($relay.ExitCode)." }
                $text = Get-Content -LiteralPath $stdout -Raw -ErrorAction SilentlyContinue
                if ($text -match 'started on 127\.0\.0\.1:(\d+)') { $port = [int] $Matches[1] }
            }
            if ($port -eq 0) { throw 'LiveCueRelayHost did not report a port within 20 seconds.' }
            if ($port -eq 47831) { throw 'The relay must never use port 47831.' }

            Add-Type -AssemblyName System.Net.Http
            $client = New-Object System.Net.Http.HttpClient
            try {
                $response = $client.GetAsync("http://127.0.0.1:$port/v1/health").GetAwaiter().GetResult()
                $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            }
            finally {
                $client.Dispose()
            }

            $expected = '{"error":"Invalid pairing token.","code":"UNAUTHORIZED"}'
            if ([int] $response.StatusCode -ne 401 -or $body -ne $expected) {
                throw "Expected 401 $expected from the published relay, got $([int] $response.StatusCode) $body"
            }
            Write-Host "Published relay host answered on 127.0.0.1:$port with 401 UNAUTHORIZED, as it must without a pairing."
        }
        finally {
            if (-not $relay.HasExited) { Stop-Process -Id $relay.Id -Force }
            $relay.WaitForExit()
        }

        if (Test-Path -LiteralPath $dataDir) { throw 'The relay host created its data folder although nothing was saved.' }
    }
    finally {
        Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-SelfTest {
    $desktopExe = Join-Path $PublishDir 'LiveCueRemake.exe'
    if (-not (Test-Path -LiteralPath $desktopExe)) { throw 'Run the publish task first.' }

    if (Test-Path -LiteralPath $Screenshots) { Remove-Item -LiteralPath $Screenshots -Recurse -Force }
    New-Item -ItemType Directory -Path $Screenshots | Out-Null
    $stdout = Join-Path $Screenshots 'self-test.stdout.txt'
    $stderr = Join-Path $Screenshots 'self-test.stderr.txt'

    # LiveCueRemake.exe is a windowed program: redirecting its output is what makes the shell wait
    # for it and what lets its result line be read.
    $run = Start-Process -FilePath $desktopExe -ArgumentList '--self-test', '--screenshots', "`"$Screenshots`"" `
        -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $output = Get-Content -LiteralPath $stdout -Raw
    Write-Host $output
    if ((Get-Item -LiteralPath $stderr).Length -gt 0) { Write-Host (Get-Content -LiteralPath $stderr -Raw) }

    if ($run.ExitCode -ne 0) { throw "The desktop self-test failed with exit code $($run.ExitCode)." }
    if ($output -notmatch 'Desktop self-test passed') { throw 'The desktop self-test did not print its result line.' }

    $pngs = @(Get-ChildItem -LiteralPath $Screenshots -Filter '*.png')
    if ($pngs.Count -eq 0) { throw 'The desktop self-test wrote no screenshots.' }
    Write-Host "$($pngs.Count) screenshots in $Screenshots"
}

$order = 'restore', 'build', 'test', 'publish', 'smoke', 'selftest'
$selected = if ($Task -contains 'all') { $order } else { $order | Where-Object { $Task -contains $_ } }

foreach ($name in $selected) {
    Write-Host "== $name ==" -ForegroundColor Cyan
    switch ($name) {
        'restore'  { Invoke-Restore }
        'build'    { Invoke-Build }
        'test'     { Invoke-Test }
        'publish'  { Invoke-Publish }
        'smoke'    { Invoke-Smoke }
        'selftest' { Invoke-SelfTest }
    }
}
