# Shared by the four ASR lab scripts. Dot-source it; it only defines things.
#
# Runs under Windows PowerShell 5.1, which reads a script without a byte order
# mark as ANSI: keep this file and its siblings plain ASCII.
#
# Everything that touches the outside world (Docker, HTTP, the clock, the
# console prompt) is a small function of its own, so the test script can
# replace it and run every path without Docker.

Set-StrictMode -Version 2.0

$LabContainerName = 'livecue-remake-asr'
$LabLabelName = 'com.livecueremake.component'
$LabLabelValue = 'asr-lab'
$LabImageTag = '1.0'
$LabContainerPort = 8765
$LabDefaultPort = 47842
$LabModels = @('nemotron', 'qwen3')

function Get-LabImageName {
    param([string]$Model)
    return "livecue-remake-asr-${Model}:$LabImageTag"
}

# --- seams --------------------------------------------------------------

function Get-LabNow { return Get-Date }

function Start-LabSleep {
    param([int]$Seconds)
    Start-Sleep -Seconds $Seconds
}

function Open-LabPage {
    param([int]$Port)
    Start-Process "http://127.0.0.1:$Port"
}

function Start-LabProcess {
    param([string]$Path)
    Start-Process -FilePath $Path -WindowStyle Hidden
}

function Read-LabAnswer {
    param([string]$Prompt)
    return (Read-Host -Prompt $Prompt)
}

# --- Docker -------------------------------------------------------------

function Get-LabDockerPath {
    $command = Get-Command -Name 'docker.exe' -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $command) { throw 'Docker Desktop is required.' }
    return $command.Source
}

function Invoke-LabDocker {
    # Runs docker quietly and returns its exit code and standard output.
    param([string]$DockerPath, [string[]]$Arguments)
    # Windows PowerShell 5.1 turns the redirected stderr of a native command
    # into error records, and 'Stop' would make the first one fatal. An engine
    # that is down is an expected answer here, so judge by the exit code.
    $ErrorActionPreference = 'Continue'
    $output = @(& $DockerPath @Arguments 2>$null)
    $code = $LASTEXITCODE
    return [pscustomobject]@{ ExitCode = $code; Output = @($output | ForEach-Object { "$_" }) }
}

function Invoke-LabDockerVisible {
    # Runs docker with its output on the console (for a long build). Returns the exit code.
    param([string]$DockerPath, [string[]]$Arguments)
    $ErrorActionPreference = 'Continue'
    & $DockerPath @Arguments | Out-Host
    return $LASTEXITCODE
}

function Test-LabDockerEngine {
    param([string]$DockerPath)
    $result = Invoke-LabDocker $DockerPath @('info', '--format', '{{.ServerVersion}}')
    return ($result.ExitCode -eq 0)
}

function Test-LabContainerExists {
    param([string]$DockerPath)
    $result = Invoke-LabDocker $DockerPath @(
        'ps', '-a', '--filter', "name=^/$LabContainerName`$", '--format', '{{.Names}}')
    if ($result.ExitCode -ne 0) { throw 'Could not inspect the local model.' }
    return ($result.Output -ccontains $LabContainerName)
}

function Test-LabOwnershipLabels {
    # True only when the labels (JSON text from docker inspect) carry our label.
    param([string]$LabelsJson)
    if ([string]::IsNullOrWhiteSpace($LabelsJson)) { return $false }
    try { $labels = ConvertFrom-Json -InputObject $LabelsJson } catch { return $false }
    if ($labels -isnot [System.Management.Automation.PSCustomObject]) { return $false }
    $property = $labels.PSObject.Properties[$LabLabelName]
    if ($null -eq $property) { return $false }
    return ($property.Value -is [string] -and $property.Value -ceq $LabLabelValue)
}

function Test-LabContainerOwned {
    param([string]$DockerPath)
    $result = Invoke-LabDocker $DockerPath @('inspect', '--format', '{{json .Config.Labels}}', $LabContainerName)
    if ($result.ExitCode -ne 0) { return $false }
    return (Test-LabOwnershipLabels -LabelsJson ($result.Output -join ''))
}

function ConvertTo-LabPublishedPort {
    # 'docker port' prints lines such as 127.0.0.1:47842. Returns 0 when there is none.
    param([string[]]$Lines)
    foreach ($line in $Lines) {
        if ($line -match '^127\.0\.0\.1:(\d{1,5})$') { return [int]$Matches[1] }
    }
    return 0
}

function Get-LabPublishedPort {
    param([string]$DockerPath)
    $result = Invoke-LabDocker $DockerPath @('port', $LabContainerName, "$LabContainerPort/tcp")
    if ($result.ExitCode -ne 0) { return 0 }
    return (ConvertTo-LabPublishedPort -Lines $result.Output)
}

function Write-LabContainerLog {
    param([string]$DockerPath)
    $ErrorActionPreference = 'Continue'
    & $DockerPath logs --tail 40 $LabContainerName 2>&1 | ForEach-Object { Write-Host "$_" }
}

# --- Docker Desktop -----------------------------------------------------

function Get-LabDockerDesktopPath {
    # Docker Desktop.exe sits two folders above docker.exe (resources\bin).
    param([string]$DockerPath)
    $folder = Split-Path -Path $DockerPath -Parent
    return [System.IO.Path]::GetFullPath((Join-Path $folder '..\..\Docker Desktop.exe'))
}

function Test-LabDockerPublisher {
    # $SubjectText is the certificate subject with one name part per line.
    # Matching a whole line means another field (such as a common name made to
    # contain 'O=Docker Inc') cannot stand in for the organisation.
    param([string]$Status, [string]$SubjectText)
    if ($Status -cne 'Valid') { return $false }
    foreach ($line in ($SubjectText -split "`r?`n")) {
        if ($line.Trim() -ceq 'O=Docker Inc') { return $true }
    }
    return $false
}

function Test-LabDockerDesktopSignature {
    param([string]$Path)
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($null -eq $signature -or $null -eq $signature.SignerCertificate) { return $false }
    $subject = $signature.SignerCertificate.SubjectName.Format($true)
    return (Test-LabDockerPublisher -Status ([string]$signature.Status) -SubjectText $subject)
}

function Start-LabDockerDesktop {
    # Starts Docker Desktop, but only the genuine one, and waits for its engine.
    param([string]$DockerPath)
    $desktop = Get-LabDockerDesktopPath $DockerPath
    if (-not (Test-Path -LiteralPath $desktop -PathType Leaf)) {
        throw 'Docker Desktop executable was not found. Open it manually.'
    }
    if (-not (Test-LabDockerDesktopSignature $desktop)) {
        throw 'Docker Desktop publisher could not be verified. Open it manually.'
    }
    Write-Host 'Starting verified Docker Desktop. Waiting for its engine...'
    Start-LabProcess $desktop
    $deadline = (Get-LabNow).AddMinutes(2)
    do {
        Start-LabSleep 2
        if (Test-LabDockerEngine $DockerPath) { return }
    } while ((Get-LabNow) -lt $deadline)
    throw 'Docker engine did not start. Check Docker Desktop on the PC.'
}

# --- the lab's health document ------------------------------------------

function Get-LabHealth {
    # GET http://127.0.0.1:<port>/health. Returns the parsed object, or $null on any failure.
    param([int]$Port)
    $response = $null
    try {
        $request = [System.Net.HttpWebRequest]::Create("http://127.0.0.1:$Port/health")
        $request.Method = 'GET'
        $request.Timeout = 2000
        $request.ReadWriteTimeout = 2000
        $request.Proxy = $null
        $request.AllowAutoRedirect = $false
        $request.KeepAlive = $false
        $response = $request.GetResponse()
        if ([int]$response.StatusCode -ne 200) { return $null }
        $reader = New-Object System.IO.StreamReader($response.GetResponseStream(), [System.Text.Encoding]::UTF8)
        $buffer = New-Object char[] 4096
        $count = $reader.ReadBlock($buffer, 0, $buffer.Length)
        $health = ConvertFrom-Json -InputObject ([System.String]::new($buffer, 0, $count))
        if ($health -is [System.Management.Automation.PSCustomObject]) { return $health }
        return $null
    } catch {
        return $null
    } finally {
        if ($null -ne $response) { $response.Close() }
    }
}

function Get-LabProperty {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Test-LabHealthBusy {
    # Anything that is not plainly "not busy" counts as busy: refusing is the safe side.
    param($Health)
    return [bool](Get-LabProperty $Health 'busy')
}

function Test-LabHealthReady {
    # Usable means: ready, the requested model, and relay protocol 1.
    param($Health, [string]$Model)
    $ready = Get-LabProperty $Health 'ready'
    $name = Get-LabProperty $Health 'model'
    $protocol = Get-LabProperty $Health 'relayProtocol'
    if (-not ($ready -is [bool] -and $ready)) { return $false }
    if (-not ($name -is [string] -and $name -ceq $Model)) { return $false }
    return (($protocol -is [int] -or $protocol -is [long]) -and $protocol -eq 1)
}

function Test-LabSessionActive {
    # Looks at the port asked for and, when it differs, at the port the
    # container really publishes (the lab port setting may have changed
    # while a container was running).
    param([string]$DockerPath, [int]$Port, $Health)
    if (Test-LabHealthBusy $Health) { return $true }
    $published = Get-LabPublishedPort $DockerPath
    if ($published -ne 0 -and $published -ne $Port) {
        return (Test-LabHealthBusy (Get-LabHealth $published))
    }
    return $false
}

# --- start --------------------------------------------------------------

function Get-LabRunArguments {
    param([string]$Model, [int]$Port, [string]$LabRoot)
    $models = Join-Path (Join-Path $LabRoot 'private\models') $Model
    foreach ($path in @($LabRoot, $models)) {
        # --mount takes a comma-separated list, so these would change its meaning.
        if ($path -match '[,"]') { throw 'The lab folder path must not contain a comma or a double quote.' }
    }
    return @(
        'run', '-d',
        '--name', $LabContainerName,
        '--label', "$LabLabelName=$LabLabelValue",
        '--gpus', 'all',
        '--shm-size', '1g',
        '--cap-drop', 'ALL',
        '--security-opt', 'no-new-privileges',
        '--pids-limit', '512',
        '-e', 'VLLM_NO_USAGE_STATS=1',
        '-e', 'DO_NOT_TRACK=1',
        '-e', 'GRADIO_ANALYTICS_ENABLED=False',
        '-e', "ASR_HOST_PORT=$Port",
        '-p', "127.0.0.1:${Port}:$LabContainerPort",
        '--mount', "type=bind,source=$models,target=/models,readonly",
        '--mount', "type=bind,source=$LabRoot,target=/app,readonly",
        (Get-LabImageName $Model)
    )
}

function Invoke-LabStart {
    param([string]$Model, [int]$Port, [switch]$NoBrowser, [switch]$EnsureDocker, [string]$LabRoot)

    # 1. Docker must be installed.
    $docker = Get-LabDockerPath

    # 2. Its engine must be running, or be started on request.
    if (-not (Test-LabDockerEngine $docker)) {
        if (-not $EnsureDocker) { throw 'Start Docker Desktop first, then run this launcher again.' }
        Start-LabDockerDesktop $docker
    }

    # 3. The weights must be on disk.
    $config = Join-Path (Join-Path (Join-Path $LabRoot 'private\models') $Model) 'config.json'
    if (-not (Test-Path -LiteralPath $config -PathType Leaf)) { throw 'Model weights have not been downloaded.' }

    # 4. A container of ours may already exist.
    if (Test-LabContainerExists $docker) {
        $health = Get-LabHealth $Port
        if (Test-LabSessionActive $docker $Port $health) {
            throw 'An ASR session is active. Stop it before switching models.'
        }
        if (Test-LabHealthReady $health $Model) {
            if (-not $NoBrowser) { Open-LabPage $Port }
            return
        }
        if (-not (Test-LabContainerOwned $docker)) {
            throw 'Container name belongs to another workload; refusing to replace it.'
        }
        # -t is --timeout (named --time before Docker 28): 15 seconds of grace.
        if ((Invoke-LabDocker $docker @('stop', '-t', '15', $LabContainerName)).ExitCode -ne 0) {
            throw 'Could not stop the existing model container.'
        }
        if ((Invoke-LabDocker $docker @('rm', $LabContainerName)).ExitCode -ne 0) {
            throw 'Could not remove the existing model container.'
        }
    }

    # 5. Start the container.
    $run = Invoke-LabDocker $docker (Get-LabRunArguments -Model $Model -Port $Port -LabRoot $LabRoot)
    if ($run.ExitCode -ne 0) { throw 'Could not start the local model container.' }
    Write-Host "Loading $Model on the GPU. This can take a few minutes on first start."

    # 6. Wait until it reports ready for this model with relay protocol 1.
    $deadline = (Get-LabNow).AddMinutes(8)
    do {
        $state = Invoke-LabDocker $docker @('inspect', '--format', '{{.State.Running}}', $LabContainerName)
        if ($state.ExitCode -ne 0 -or -not ($state.Output -ccontains 'true')) {
            Write-LabContainerLog $docker
            throw 'Model service stopped during initialization.'
        }
        if (Test-LabHealthReady (Get-LabHealth $Port) $Model) {
            Write-Host "Ready: http://127.0.0.1:$Port ($Model)"
            if (-not $NoBrowser) { Open-LabPage $Port }
            return
        }
        Start-LabSleep 2
    } while ((Get-LabNow) -lt $deadline)
    throw "Model is still starting. Inspect: docker logs --tail 40 $LabContainerName"
}

# --- stop ---------------------------------------------------------------

function Invoke-LabStop {
    param([int]$Port)

    # 1. Docker must be installed.
    $docker = Get-LabDockerPath

    # 2. No engine or no container means nothing holds the GPU. Never start
    #    Docker just to stop a container.
    if (-not (Test-LabDockerEngine $docker)) { return }
    if (-not (Test-LabContainerExists $docker)) { return }

    # 3. Only ever touch our own container.
    if (-not (Test-LabContainerOwned $docker)) { throw 'Container is not owned by LiveCue. Refusing to stop it.' }

    # 4. Never cut off a session.
    if (Test-LabSessionActive $docker $Port (Get-LabHealth $Port)) {
        throw 'An ASR session is active. Stop it before unloading the model.'
    }

    # 5. Stop it (15 seconds of grace). It is not removed; images and weights stay.
    if ((Invoke-LabDocker $docker @('stop', '-t', '15', $LabContainerName)).ExitCode -ne 0) {
        throw 'Could not stop the local model.'
    }
    Write-Host 'PC model stopped. Model weights and Docker images are unchanged.'
}

# --- manual setup: shared pieces ----------------------------------------

function Format-LabSize {
    # Decimal units, always written the same way whatever the Windows region.
    param([long]$Bytes)
    $plain = [System.Globalization.CultureInfo]::InvariantCulture
    if ($Bytes -ge 1000000000) { return (($Bytes / 1000000000).ToString('0.00', $plain) + ' GB') }
    if ($Bytes -ge 1000000) { return (($Bytes / 1000000).ToString('0.0', $plain) + ' MB') }
    if ($Bytes -ge 1000) { return (($Bytes / 1000).ToString('0', $plain) + ' KB') }
    return "$Bytes bytes"
}

function Confirm-LabAction {
    # Only the typed word 'yes' counts. An empty answer, or no console at all, is a no.
    param([string]$Question)
    $answer = Read-LabAnswer "$Question Type yes to continue"
    return ($answer -is [string] -and $answer.Trim() -ceq 'yes')
}

# --- manual setup: images -----------------------------------------------

function Get-LabBuildPlan {
    param([string[]]$Targets, [string]$LabRoot)
    $lines = @(
        'LiveCue ASR lab: build the Docker images',
        '',
        'This builds, on this PC only, one image per speech model:'
    )
    foreach ($target in $Targets) { $lines += "  $(Get-LabImageName $target)" }
    $lines += @(
        '',
        'The images hold the software the models need, and nothing else: no model',
        'weights and none of the lab code (that is read from this folder when a model',
        'starts).',
        '',
        'What the build downloads, and from where:',
        '  python:3.12-slim base image       Docker Hub                       about 50 MB',
        '  system libraries                  Debian package mirrors           under 100 MB',
        '  PyTorch 2.9.1 for CUDA 12.8       download.pytorch.org/whl/cu128   about 4 GB',
        '  Python packages                   pypi.org                         about 0.5 GB (nemotron)',
        '                                                                     about 1 to 5 GB (qwen3, with vLLM)',
        '',
        'Disk space: allow about 10 GB for the nemotron image and about 15 GB for the',
        'qwen3 image. They share the PyTorch layer, so both together need about 17 GB.',
        'These sizes are estimates; they were not measured for this version.',
        '',
        'The build can take 10 to 30 minutes. Nothing is uploaded: only the pinned',
        'requirement files in this folder are sent to the Docker engine.',
        '',
        "Lab folder: $LabRoot"
    )
    return $lines
}

function Invoke-LabBuildImages {
    param([string]$Model, [switch]$PlanOnly, [string]$LabRoot)
    $targets = @($LabModels | Where-Object { $Model -ceq 'all' -or $_ -ceq $Model })
    Get-LabBuildPlan -Targets $targets -LabRoot $LabRoot | ForEach-Object { Write-Host $_ }
    if ($PlanOnly) { return }

    $docker = Get-LabDockerPath
    if (-not (Test-LabDockerEngine $docker)) { throw 'Start Docker Desktop first, then run this script again.' }
    Write-Host ''
    if (-not (Confirm-LabAction 'Build these images now?')) {
        Write-Host 'Nothing was built.'
        return
    }
    foreach ($target in $targets) {
        Write-Host ''
        Write-Host "Building $(Get-LabImageName $target) ..."
        $code = Invoke-LabDockerVisible $docker @(
            'build', '--target', $target, '--tag', (Get-LabImageName $target), $LabRoot)
        if ($code -ne 0) { throw "The image build failed for $target." }
    }
    Write-Host ''
    Write-Host 'Images built. Next: Get-ASR-Weights.ps1 -Model <model>, then Start-ASR-Lab.ps1.'
}

# --- manual setup: weights ----------------------------------------------

function Get-LabWeightsManifest {
    # The pinned source of one model's weights, from weights.json.
    param([string]$Model, [string]$LabRoot)
    $path = Join-Path $LabRoot 'weights.json'
    $all = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $path -Raw -Encoding UTF8)
    $entry = Get-LabProperty $all $Model
    if ($null -eq $entry) { throw "weights.json has no entry for $Model." }
    return $entry
}

function Get-LabWeightUrl {
    param($Manifest, [string]$Path)
    return "https://huggingface.co/$($Manifest.repository)/resolve/$($Manifest.revision)/$Path"
}

function Get-LabGitBlobSha1 {
    # The id git gives a file: SHA-1 over 'blob <length>' + NUL + content.
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $header = [System.Text.Encoding]::ASCII.GetBytes("blob $($bytes.Length)`0")
    $sha1 = [System.Security.Cryptography.SHA1]::Create()
    try {
        [void]$sha1.TransformBlock($header, 0, $header.Length, $null, 0)
        [void]$sha1.TransformFinalBlock($bytes, 0, $bytes.Length)
        return (-join ($sha1.Hash | ForEach-Object { $_.ToString('x2') }))
    } finally {
        $sha1.Dispose()
    }
}

function Test-LabWeightFile {
    # True when the file exists with the published size and checksum.
    param([string]$Path, $File)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    if ((Get-Item -LiteralPath $Path).Length -ne [long]$File.size) { return $false }
    $sha256 = Get-LabProperty $File 'sha256'
    if ($sha256) {
        $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
        return ($actual -ceq $sha256)
    }
    $gitSha1 = Get-LabProperty $File 'gitSha1'
    if ($gitSha1) { return ((Get-LabGitBlobSha1 $Path) -ceq $gitSha1) }
    return $false
}

function Get-LabWeightsPlan {
    param([string]$Model, $Manifest, [string]$Destination)
    $lines = @(
        "LiveCue ASR lab: download the weights for $Model",
        '',
        "Model:       $($Manifest.name)",
        "Source:      https://huggingface.co/$($Manifest.repository)",
        "Revision:    $($Manifest.revision)",
        "Licence:     $($Manifest.license) ($($Manifest.licenseUrl))",
        "Destination: $Destination",
        '',
        'Files:'
    )
    foreach ($file in $Manifest.files) {
        $lines += ('  {0,-36} {1,12}' -f $file.path, (Format-LabSize ([long]$file.size)))
    }
    $lines += @(
        ('  {0,-36} {1,12}' -f 'Total', (Format-LabSize ([long]$Manifest.totalBytes))),
        '',
        'The files come straight from the publisher''s repository at the revision above,',
        'over HTTPS, with no account or token. Each file is checked against its published',
        'size and checksum before it is put in place. An interrupted download resumes.',
        'By downloading you accept the model''s licence. Nothing is uploaded.'
    )
    return $lines
}

function Save-LabDownload {
    # Downloads $Url to "$Destination.partial", resuming a partial file.
    param([string]$Url, [string]$Destination, [long]$ExpectedSize)
    Add-Type -AssemblyName System.Net.Http
    [System.Net.ServicePointManager]::SecurityProtocol =
        [System.Net.ServicePointManager]::SecurityProtocol -bor [System.Net.SecurityProtocolType]::Tls12

    $partial = "$Destination.partial"
    $have = 0L
    if (Test-Path -LiteralPath $partial -PathType Leaf) { $have = (Get-Item -LiteralPath $partial).Length }
    if ($have -gt $ExpectedSize) {
        Remove-Item -LiteralPath $partial -Force
        $have = 0L
    }
    if ($have -eq $ExpectedSize -and $ExpectedSize -gt 0) { return }

    $client = New-Object System.Net.Http.HttpClient
    $client.Timeout = [System.Threading.Timeout]::InfiniteTimeSpan
    $response = $null
    $source = $null
    $target = $null
    try {
        $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, $Url)
        $request.Headers.UserAgent.ParseAdd('LiveCue-ASR-Lab-Setup/1.0')
        if ($have -gt 0) {
            $request.Headers.Range = [System.Net.Http.Headers.RangeHeaderValue]::new($have, $null)
        }
        $completion = [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead
        $response = $client.SendAsync($request, $completion).GetAwaiter().GetResult()
        $status = [int]$response.StatusCode
        if ($status -eq 200) {
            $have = 0L  # the server sent the whole file
            $mode = [System.IO.FileMode]::Create
        } elseif ($status -eq 206 -and $have -gt 0) {
            $mode = [System.IO.FileMode]::Append
        } else {
            throw "The download failed (HTTP $status)."
        }
        $source = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $target = New-Object System.IO.FileStream(
            $partial, $mode, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        $buffer = New-Object byte[] 1048576
        $shown = Get-LabNow
        while ($true) {
            $pending = $source.ReadAsync($buffer, 0, $buffer.Length)
            if (-not $pending.Wait(60000)) { throw 'The download stalled. Run the script again to resume.' }
            $read = $pending.Result
            if ($read -le 0) { break }
            $target.Write($buffer, 0, $read)
            $have += $read
            if (((Get-LabNow) - $shown).TotalSeconds -ge 5) {
                Write-Host ('    {0} of {1}' -f (Format-LabSize $have), (Format-LabSize $ExpectedSize))
                $shown = Get-LabNow
            }
        }
    } finally {
        if ($null -ne $target) { $target.Dispose() }
        if ($null -ne $source) { $source.Dispose() }
        if ($null -ne $response) { $response.Dispose() }
        $client.Dispose()
    }
}

function Invoke-LabGetWeights {
    param([string]$Model, [switch]$PlanOnly, [string]$LabRoot)
    $manifest = Get-LabWeightsManifest -Model $Model -LabRoot $LabRoot
    $destination = Join-Path (Join-Path $LabRoot 'private\models') $Model
    Get-LabWeightsPlan -Model $Model -Manifest $manifest -Destination $destination |
        ForEach-Object { Write-Host $_ }
    if ($PlanOnly) { return }

    Write-Host ''
    Write-Host 'Checking what is already here ...'
    $missing = @($manifest.files | Where-Object { -not (Test-LabWeightFile (Join-Path $destination $_.path) $_) })
    if ($missing.Count -eq 0) {
        Write-Host 'All files are present and match their checksums. Nothing to download.'
        return
    }
    $bytes = [long]0
    foreach ($file in $missing) { $bytes += [long]$file.size }
    Write-Host ("To download: {0} file(s), {1}." -f $missing.Count, (Format-LabSize $bytes))
    try {
        $drive = New-Object System.IO.DriveInfo ([System.IO.Path]::GetPathRoot($LabRoot))
        Write-Host ("Free space on {0} {1}." -f $drive.Name, (Format-LabSize $drive.AvailableFreeSpace))
        if ($drive.AvailableFreeSpace -lt $bytes) { throw 'There is not enough free disk space for this download.' }
    } catch [System.ArgumentException] {
        Write-Host 'Free space could not be checked for this location.'
    }
    if (-not (Confirm-LabAction 'Download these files now?')) {
        Write-Host 'Nothing was downloaded.'
        return
    }

    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($file in $missing) {
        $path = Join-Path $destination $file.path
        Write-Host ("  {0} ({1})" -f $file.path, (Format-LabSize ([long]$file.size)))
        Save-LabDownload -Url (Get-LabWeightUrl $manifest $file.path) -Destination $path -ExpectedSize ([long]$file.size)
        if (-not (Test-LabWeightFile "$path.partial" $file)) {
            Remove-Item -LiteralPath "$path.partial" -Force -ErrorAction SilentlyContinue
            throw "$($file.path) did not match its published size and checksum. It was discarded; run the script again."
        }
        Move-Item -LiteralPath "$path.partial" -Destination $path -Force
    }
    Write-Host ''
    Write-Host "Weights for $Model are in place. Next: Start-ASR-Lab.ps1 -Model $Model"
}
