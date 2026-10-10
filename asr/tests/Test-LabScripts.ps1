<#
.SYNOPSIS
Tests for the ASR lab scripts. Needs no Docker, no GPU and no internet.

.DESCRIPTION
Run with Windows PowerShell 5.1, the edition the relay starts the scripts with:

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File asr\tests\Test-LabScripts.ps1

Docker, the lab's health endpoint, the clock and the console prompt are
replaced by stand-ins, so every step of the start, stop, build and download
logic runs, including its exact failure messages. The pieces that do not need
Docker (running a native command, reading a health document, downloading and
verifying a file) run for real against a small loopback server.

Not covered, because it needs a real Docker engine with a GPU: that Docker
accepts the arguments, and that the container starts.
#>
$ErrorActionPreference = 'Stop'
$labRoot = Split-Path -Path $PSScriptRoot -Parent
. (Join-Path $labRoot 'LabCommon.ps1')

# --- a tiny test harness --------------------------------------------------

$script:Passed = 0
$script:Failed = New-Object System.Collections.Generic.List[string]
$script:Said = New-Object System.Collections.Generic.List[string]

function Write-Host {
    # What the scripts say is collected here instead of flooding the test output.
    param([Parameter(Position = 0)]$Object)
    $script:Said.Add("$Object")
}

function Write-Result {
    param([string]$Text)
    Microsoft.PowerShell.Utility\Write-Host $Text
}

function Test-Case {
    # The parameter names are unusual on purpose: a test body runs in a child
    # scope of this function and would otherwise see them as its own variables.
    param([string]$CaseName, [scriptblock]$CaseBody)
    $script:Said.Clear()
    try {
        & $CaseBody
        $script:Passed++
        Write-Result "ok    $CaseName"
    } catch {
        $script:Failed.Add($CaseName)
        Write-Result "FAIL  $CaseName"
        Write-Result "      $($_.Exception.Message)"
        Write-Result "      $($_.ScriptStackTrace -replace "`n", "`n      ")"
    }
}

function Assert-Equal {
    param($Expected, $Actual, [string]$What = 'value')
    $want = ConvertTo-Json -InputObject $Expected -Compress -Depth 6
    $got = ConvertTo-Json -InputObject $Actual -Compress -Depth 6
    if ($want -cne $got) { throw "$What`n        expected $want`n        actual   $got" }
}

function Assert-True {
    param($Condition, [string]$What)
    if (-not $Condition) { throw "not true: $What" }
}

function Assert-Throws {
    param([scriptblock]$Body, [string]$Message)
    $thrown = $null
    try { & $Body } catch { $thrown = $_.Exception.Message }
    if ($null -eq $thrown) { throw "expected the failure '$Message' but nothing failed" }
    if ($thrown -cne $Message) { throw "expected the failure '$Message' but got '$thrown'" }
}

function New-TestFolder {
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ('livecue-asr-test-' + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path | Out-Null
    # The same spelling the scripts will compute (short 8.3 names expanded).
    return [System.IO.Path]::GetFullPath($path)
}

# --- a loopback HTTP server for the real network pieces -------------------

function Start-TestHttpServer {
    # $Files maps a path to @{ Status = '200 OK'; Body = [byte[]]; Ranges = $true }.
    param([hashtable]$Files)
    $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $log = [System.Collections.ArrayList]::Synchronized((New-Object System.Collections.ArrayList))
    $named = [System.Collections.ArrayList]::Synchronized((New-Object System.Collections.ArrayList))
    $shell = [powershell]::Create()
    [void]$shell.AddScript({
        param($listener, $files, $log, $named)
        while ($true) {
            try { $client = $listener.AcceptTcpClient() } catch { break }
            try {
                $stream = $client.GetStream()
                $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::ASCII)
                $first = $reader.ReadLine()
                $headers = @{}
                while ($true) {
                    $line = $reader.ReadLine()
                    if ([string]::IsNullOrEmpty($line)) { break }
                    $at = $line.IndexOf(':')
                    if ($at -gt 0) { $headers[$line.Substring(0, $at).Trim().ToLowerInvariant()] = $line.Substring($at + 1).Trim() }
                }
                $path = ($first -split ' ')[1]
                [void]$log.Add("$path|$($headers['range'])")
                [void]$named.Add("$($headers['host'])|$($headers.ContainsKey('origin'))")
                $entry = $files[$path]
                $status = '404 Not Found'
                $body = New-Object byte[] 0
                $extra = ''
                if ($null -ne $entry) {
                    $status = $entry.Status
                    $body = [byte[]]$entry.Body
                    if ($entry.Ranges -and $headers['range'] -match '^bytes=(\d+)-$') {
                        $from = [int]$Matches[1]
                        $total = $body.Length
                        $rest = New-Object byte[] ($total - $from)
                        [System.Array]::Copy($body, $from, $rest, 0, $rest.Length)
                        $body = $rest
                        $status = '206 Partial Content'
                        $extra = "Content-Range: bytes $from-$($total - 1)/$total`r`n"
                    }
                }
                $head = "HTTP/1.1 $status`r`nContent-Type: application/octet-stream`r`n" +
                    "Content-Length: $($body.Length)`r`n${extra}Connection: close`r`n`r`n"
                $bytes = [System.Text.Encoding]::ASCII.GetBytes($head)
                $stream.Write($bytes, 0, $bytes.Length)
                $stream.Write($body, 0, $body.Length)
                $stream.Flush()
            } catch {
            } finally {
                $client.Close()
            }
        }
    }).AddArgument($listener).AddArgument($Files).AddArgument($log).AddArgument($named)
    [void]$shell.BeginInvoke()
    # Log: 'path|Range header'. Named: 'Host header|whether an Origin header came'.
    return @{ Port = $listener.LocalEndpoint.Port; Listener = $listener; Shell = $shell; Log = $log; Named = $named }
}

function Stop-TestHttpServer {
    param($Server)
    $Server.Listener.Stop()
    $Server.Shell.Dispose()
}

function New-TestBytes {
    param([int]$Count, [int]$Seed)
    $bytes = New-Object byte[] $Count
    (New-Object System.Random $Seed).NextBytes($bytes)
    return ,$bytes
}

# ==========================================================================
# 1. The files themselves
# ==========================================================================

$scripts = @('LabCommon.ps1', 'Start-ASR-Lab.ps1', 'Stop-ASR-Lab.ps1', 'Build-ASR-Images.ps1', 'Get-ASR-Weights.ps1')

Test-Case 'running under Windows PowerShell 5.1' {
    Assert-Equal 5 $PSVersionTable.PSVersion.Major 'PowerShell major version'
}

foreach ($scriptName in $scripts) {
    Test-Case "$scriptName parses and is plain ASCII" {
        $tokens = $null
        $errors = $null
        $path = Join-Path $labRoot $scriptName
        [void][System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
        Assert-Equal 0 $errors.Count 'parse errors'
        $high = @([System.IO.File]::ReadAllBytes($path) | Where-Object { $_ -gt 127 })
        Assert-Equal 0 $high.Count 'non-ASCII bytes (PowerShell 5.1 would misread them)'
    }
}

Test-Case 'the launchers declare the documented parameters and defaults' {
    $start = (Get-Command (Join-Path $labRoot 'Start-ASR-Lab.ps1')).Parameters
    Assert-True ($start.ContainsKey('Model') -and $start.ContainsKey('Port')) 'Start has -Model and -Port'
    Assert-True ($start['NoBrowser'].SwitchParameter -and $start['EnsureDocker'].SwitchParameter) 'Start has both switches'
    Assert-True ($start.ContainsKey('LabFolder') -and $start['LabFolder'].ParameterType -eq [string]) 'Start has -LabFolder'
    $mandatory = @($start['LabFolder'].Attributes | Where-Object { $_ -is [System.Management.Automation.ParameterAttribute] -and $_.Mandatory })
    Assert-Equal 0 $mandatory.Count '-LabFolder is optional'
    $stop = (Get-Command (Join-Path $labRoot 'Stop-ASR-Lab.ps1')).Parameters
    Assert-True ($stop.ContainsKey('Port') -and -not $stop.ContainsKey('Model')) 'Stop has -Port only'
    Assert-True (-not $stop.ContainsKey('LabFolder')) 'Stop takes no lab folder: the relay passes it -Port alone'
    $weights = (Get-Command (Join-Path $labRoot 'Get-ASR-Weights.ps1')).Parameters
    Assert-True ($weights.ContainsKey('Model') -and $weights.ContainsKey('PlanOnly')) 'Get-ASR-Weights has -Model and -PlanOnly'
    $build = (Get-Command (Join-Path $labRoot 'Build-ASR-Images.ps1')).Parameters
    Assert-True ($build.ContainsKey('Model') -and $build.ContainsKey('PlanOnly')) 'Build-ASR-Images has -Model and -PlanOnly'
    Assert-Equal 47842 $LabDefaultPort 'default lab port'
}

Test-Case 'fixed names' {
    Assert-Equal 'livecue-remake-asr' $LabContainerName
    Assert-Equal 'com.livecueremake.component' $LabLabelName
    Assert-Equal 'asr-lab' $LabLabelValue
    Assert-Equal 'livecue-remake-asr-nemotron:1.0' (Get-LabImageName 'nemotron')
    Assert-Equal 'livecue-remake-asr-qwen3:1.0' (Get-LabImageName 'qwen3')
}

# ==========================================================================
# 2. Pure functions
# ==========================================================================

Test-Case 'ownership label must be exactly ours' {
    Assert-True (Test-LabOwnershipLabels '{"com.livecueremake.component":"asr-lab"}') 'our label'
    Assert-True (Test-LabOwnershipLabels '{"other":"x","com.livecueremake.component":"asr-lab"}') 'our label among others'
    foreach ($json in @(
            '', ' ', 'null', '{}', '[]', '"asr-lab"', 'not json',
            '{"com.livecueremake.component":"ASR-LAB"}',
            '{"com.livecueremake.component":"asr-lab "}',
            '{"com.livecueremake.component":true}',
            '{"com.livecue.component":"asr-lab"}')) {
        Assert-True (-not (Test-LabOwnershipLabels $json)) "refuses $json"
    }
}

Test-Case 'health: ready needs ready true, the requested model and relay protocol 1' {
    $good = ConvertFrom-Json '{"model":"nemotron","ready":true,"relayProtocol":1,"busy":false}'
    Assert-True (Test-LabHealthReady $good 'nemotron') 'ready'
    Assert-True (-not (Test-LabHealthReady $good 'qwen3')) 'another model is not ready'
    foreach ($json in @(
            '{"model":"nemotron","ready":true,"busy":false}',
            '{"model":"nemotron","ready":true,"relayProtocol":2}',
            '{"model":"nemotron","ready":true,"relayProtocol":"1"}',
            '{"model":"nemotron","ready":true,"relayProtocol":1.5}',
            '{"model":"nemotron","ready":false,"relayProtocol":1}',
            '{"model":"nemotron","ready":"true","relayProtocol":1}',
            '{"model":"NEMOTRON","ready":true,"relayProtocol":1}',
            '{"ready":true,"relayProtocol":1}',
            '{}')) {
        Assert-True (-not (Test-LabHealthReady (ConvertFrom-Json $json) 'nemotron')) "not ready: $json"
    }
    Assert-True (-not (Test-LabHealthReady $null 'nemotron')) 'no answer is not ready'
}

Test-Case 'health: busy errs on the side of busy' {
    Assert-True (Test-LabHealthBusy (ConvertFrom-Json '{"busy":true}')) 'busy true'
    Assert-True (Test-LabHealthBusy (ConvertFrom-Json '{"busy":"yes"}')) 'odd value counts as busy'
    Assert-True (-not (Test-LabHealthBusy (ConvertFrom-Json '{"busy":false}'))) 'busy false'
    Assert-True (-not (Test-LabHealthBusy (ConvertFrom-Json '{}'))) 'absent'
    Assert-True (-not (Test-LabHealthBusy $null)) 'no answer'
}

Test-Case 'Docker Desktop publisher check' {
    $real = "OID.1.3.6.1.4.1.311.60.2.1.3=US`r`nSERIALNUMBER=4817464`r`nC=US`r`nS=California`r`nO=Docker Inc`r`nCN=Docker Inc`r`n"
    Assert-True (Test-LabDockerPublisher 'Valid' $real) 'the genuine subject'
    Assert-True (-not (Test-LabDockerPublisher 'NotSigned' $real)) 'unsigned'
    Assert-True (-not (Test-LabDockerPublisher 'HashMismatch' $real)) 'tampered'
    Assert-True (-not (Test-LabDockerPublisher 'UnknownError' $real)) 'untrusted'
    Assert-True (-not (Test-LabDockerPublisher 'Valid' "O=Docker Incorporated`r`nCN=Docker Inc")) 'a longer organisation name'
    Assert-True (-not (Test-LabDockerPublisher 'Valid' "O=Evil Corp`r`nCN=O=Docker Inc")) 'the name hidden in another field'
    Assert-True (-not (Test-LabDockerPublisher 'Valid' "O=docker inc`r`nCN=x")) 'different case'
    Assert-True (-not (Test-LabDockerPublisher 'Valid' '')) 'no subject'
}

Test-Case 'Docker Desktop is found two folders above docker.exe' {
    Assert-Equal 'C:\Program Files\Docker\Docker\Docker Desktop.exe' `
        (Get-LabDockerDesktopPath 'C:\Program Files\Docker\Docker\resources\bin\docker.exe')
}

Test-Case 'published port is read from docker port output' {
    Assert-Equal 47842 (ConvertTo-LabPublishedPort @('127.0.0.1:47842'))
    Assert-Equal 50123 (ConvertTo-LabPublishedPort @('', '127.0.0.1:50123'))
    Assert-Equal 0 (ConvertTo-LabPublishedPort @('0.0.0.0:47842'))
    Assert-Equal 0 (ConvertTo-LabPublishedPort @())
}

Test-Case 'docker run arguments: names, hardening, loopback-only port, read-only mounts' {
    $arguments = Get-LabRunArguments -Model 'qwen3' -Port 50123 -LabRoot 'D:\Apps\LiveCue Remake\asr'
    Assert-Equal @(
        'run', '-d',
        '--name', 'livecue-remake-asr',
        '--label', 'com.livecueremake.component=asr-lab',
        '--gpus', 'all',
        '--shm-size', '1g',
        '--cap-drop', 'ALL',
        '--security-opt', 'no-new-privileges',
        '--pids-limit', '512',
        '-e', 'VLLM_NO_USAGE_STATS=1',
        '-e', 'DO_NOT_TRACK=1',
        '-e', 'GRADIO_ANALYTICS_ENABLED=False',
        '-e', 'ASR_HOST_PORT=50123',
        '-p', '127.0.0.1:50123:8765',
        '--mount', 'type=bind,source=D:\Apps\LiveCue Remake\asr\private\models\qwen3,target=/models,readonly',
        '--mount', 'type=bind,source=D:\Apps\LiveCue Remake\asr,target=/app,readonly',
        'livecue-remake-asr-qwen3:1.0'
    ) $arguments 'docker run arguments'
    Assert-Equal 1 @($arguments | Where-Object { $_ -ceq '-p' }).Count 'exactly one published port'
    Assert-True (-not ($arguments -contains '--privileged')) 'never privileged'
}

Test-Case 'the lab folder is made one absolute path, whatever the current folder' {
    Assert-Equal 'D:\Apps\LiveCue Remake\asr' (Resolve-LabFolder 'D:\Apps\LiveCue Remake\asr') 'an absolute path is kept'
    Assert-Equal 'D:\Apps\asr' (Resolve-LabFolder 'D:\Apps\asr\') 'without a trailing separator'
    Assert-Equal 'D:\Apps\asr' (Resolve-LabFolder 'D:/Apps/asr/') 'forward slashes'
    Assert-Equal 'D:\Apps\asr' (Resolve-LabFolder 'D:\Apps\other\..\asr') 'no parent steps left in it'
    Assert-Equal 'D:\' (Resolve-LabFolder 'D:\') 'a drive root keeps its separator'
    Assert-Equal 'D:\a[1]\asr' (Resolve-LabFolder 'D:\a[1]\asr') 'brackets are not a pattern'
    $here = New-TestFolder
    try {
        Push-Location -LiteralPath $here
        try {
            Assert-Equal (Join-Path $here 'lab') (Resolve-LabFolder 'lab') 'a relative path starts at the current location'
            Assert-Equal (Join-Path $here 'lab') (Resolve-LabFolder '.\lab\') 'with a leading dot'
            Assert-Equal $here (Resolve-LabFolder '.') 'the current location itself'
        } finally {
            Pop-Location
        }
        # It names a folder; it does not look for it.
        Assert-Equal (Join-Path $here 'not there') (Resolve-LabFolder (Join-Path $here 'not there')) 'a folder that does not exist'
    } finally {
        Remove-Item -LiteralPath $here -Recurse -Force
    }
}

Test-Case 'a lab folder that would break the mount option is refused' {
    Assert-Throws { Get-LabRunArguments -Model 'nemotron' -Port 47842 -LabRoot 'D:\a,b\asr' } `
        'The lab folder path must not contain a comma or a double quote.'
    Assert-Throws { Get-LabRunArguments -Model 'nemotron' -Port 47842 -LabRoot 'D:\a"b\asr' } `
        'The lab folder path must not contain a comma or a double quote.'
}

Test-Case 'sizes are written the same way in every region' {
    Assert-Equal '2.55 GB' (Format-LabSize 2552819964)
    Assert-Equal '4.70 GB' (Format-LabSize 4703055333)
    Assert-Equal '1.7 MB' (Format-LabSize 1671853)
    Assert-Equal '1 KB' (Format-LabSize 1376)
    Assert-Equal '193 bytes' (Format-LabSize 193)
}

Test-Case 'git blob id' {
    $folder = New-TestFolder
    try {
        $hello = Join-Path $folder 'hello.txt'
        [System.IO.File]::WriteAllBytes($hello, [System.Text.Encoding]::ASCII.GetBytes("hello`n"))
        Assert-Equal 'ce013625030ba8dba906f756967f9e9ca394464a' (Get-LabGitBlobSha1 $hello)
        $empty = Join-Path $folder 'empty'
        [System.IO.File]::WriteAllBytes($empty, (New-Object byte[] 0))
        Assert-Equal 'e69de29bb2d1d6434b8b29ae775ad8c2e48c5391' (Get-LabGitBlobSha1 $empty)
    } finally {
        Remove-Item -LiteralPath $folder -Recurse -Force
    }
}

# ==========================================================================
# 3. The weights manifest that ships with the lab
# ==========================================================================

Test-Case 'weights.json names the two repositories, pinned revisions and checksums' {
    $expected = @{
        nemotron = @{ Repository = 'nvidia/nemotron-3.5-asr-streaming-0.6b'; Files = 6; Total = 2552819964 }
        qwen3    = @{ Repository = 'Qwen/Qwen3-ASR-1.7B'; Files = 10; Total = 4703055333 }
    }
    foreach ($model in $LabModels) {
        $manifest = Get-LabWeightsManifest -Model $model -LabRoot $labRoot
        Assert-Equal $expected[$model].Repository $manifest.repository "$model repository"
        Assert-True ($manifest.revision -cmatch '^[0-9a-f]{40}$') "$model revision is a full commit id"
        Assert-True ($manifest.license -and $manifest.licenseUrl -cmatch '^https://') "$model licence is named"
        Assert-Equal $expected[$model].Files @($manifest.files).Count "$model file count"
        $sum = [long]0
        foreach ($file in $manifest.files) {
            $sum += [long]$file.size
            Assert-True ($file.path -cmatch '^[A-Za-z0-9._-]+$') "$model $($file.path) is a plain file name"
            $sha256 = Get-LabProperty $file 'sha256'
            $gitSha1 = Get-LabProperty $file 'gitSha1'
            Assert-True (($sha256 -and $sha256 -cmatch '^[0-9a-f]{64}$') -or ($gitSha1 -and $gitSha1 -cmatch '^[0-9a-f]{40}$')) `
                "$model $($file.path) has a checksum"
        }
        Assert-Equal $expected[$model].Total $sum "$model total bytes"
        Assert-Equal $expected[$model].Total ([long]$manifest.totalBytes) "$model totalBytes member"
        Assert-True (@($manifest.files | Where-Object { $_.path -ceq 'config.json' }).Count -eq 1) "$model has config.json"
        Assert-Equal "https://huggingface.co/$($manifest.repository)/resolve/$($manifest.revision)/config.json" `
            (Get-LabWeightUrl $manifest 'config.json') "$model download address"
    }
    Assert-Throws { Get-LabWeightsManifest -Model 'whisper' -LabRoot $labRoot } 'weights.json has no entry for whisper.'
}

Test-Case 'the weights plan says what, from where, how big and where to' {
    $manifest = Get-LabWeightsManifest -Model 'qwen3' -LabRoot $labRoot
    $plan = (Get-LabWeightsPlan -Model 'qwen3' -Manifest $manifest -Destination 'D:\lab\private\models\qwen3') -join "`n"
    foreach ($needle in @(
            'https://huggingface.co/Qwen/Qwen3-ASR-1.7B', $manifest.revision, 'Apache-2.0',
            'D:\lab\private\models\qwen3', 'model-00001-of-00002.safetensors', '4.22 GB', '4.70 GB', 'Nothing is uploaded')) {
        Assert-True ($plan.Contains($needle)) "plan mentions $needle"
    }
}

Test-Case 'the build plan names the images and the download sources' {
    $plan = (Get-LabBuildPlan -Targets @('nemotron', 'qwen3') -LabRoot 'D:\lab') -join "`n"
    foreach ($needle in @(
            'livecue-remake-asr-nemotron:1.0', 'livecue-remake-asr-qwen3:1.0', 'Docker Hub',
            'download.pytorch.org/whl/cu128', 'pypi.org', 'estimates')) {
        Assert-True ($plan.Contains($needle)) "plan mentions $needle"
    }
}

# ==========================================================================
# 4. Real pieces that need no Docker
# ==========================================================================

Test-Case 'a native command that fails and writes to stderr is an answer, not an exception' {
    $cmd = Join-Path $env:SystemRoot 'System32\cmd.exe'
    $result = Invoke-LabDocker $cmd @('/c', 'echo out& echo problem 1>&2& exit 3')
    Assert-Equal 3 $result.ExitCode 'exit code'
    Assert-Equal @('out') $result.Output 'standard output only'
    $ok = Invoke-LabDocker $cmd @('/c', 'echo one& echo two')
    Assert-Equal 0 $ok.ExitCode
    Assert-Equal @('one', 'two') $ok.Output
}

Test-Case 'docker.exe missing from PATH' {
    $saved = $env:Path
    try {
        $env:Path = ''
        Assert-Throws { Get-LabDockerPath } 'Docker Desktop is required.'
    } finally {
        $env:Path = $saved
    }
}

Test-Case 'health is read from the given loopback port, and anything odd is no answer' {
    $json = '{"model":"qwen3","ready":true,"loadSeconds":41.7,"sampleRate":16000,"maxSeconds":120,"chunkMs":1000,"relayProtocol":1,"busy":false}'
    $server = Start-TestHttpServer @{ '/health' = @{ Status = '200 OK'; Body = [System.Text.Encoding]::UTF8.GetBytes($json) } }
    try {
        $health = Get-LabHealth $server.Port
        Assert-True (Test-LabHealthReady $health 'qwen3') 'a real health document is ready'
        Assert-True (-not (Test-LabHealthBusy $health)) 'and not busy'
        Assert-Equal @('/health|') @($server.Log) 'one request to /health'
        # The lab answers 403 to any other Host, and to a foreign Origin.
        Assert-Equal @("127.0.0.1:$($server.Port)|False") @($server.Named) 'Host is the loopback port, and no Origin is sent'
    } finally {
        Stop-TestHttpServer $server
    }
    Assert-Equal $null (Get-LabHealth $server.Port) 'nothing listening'

    foreach ($case in @(
            @{ Status = '200 OK'; Body = 'not json' },
            @{ Status = '200 OK'; Body = '[1,2]' },
            @{ Status = '200 OK'; Body = '"text"' },
            @{ Status = '500 Internal Server Error'; Body = '{"ready":true}' },
            @{ Status = '302 Found'; Body = '' })) {
        $odd = Start-TestHttpServer @{ '/health' = @{ Status = $case.Status; Body = [System.Text.Encoding]::UTF8.GetBytes($case.Body) } }
        try {
            Assert-Equal $null (Get-LabHealth $odd.Port) "$($case.Status) $($case.Body)"
        } finally {
            Stop-TestHttpServer $odd
        }
    }
}

Test-Case 'download: whole file, resume, server without resume, oversized leftover, failure' {
    $folder = New-TestFolder
    $content = New-TestBytes -Count 2500000 -Seed 7
    $server = Start-TestHttpServer @{
        '/resumable.bin' = @{ Status = '200 OK'; Body = $content; Ranges = $true }
        '/plain.bin'     = @{ Status = '200 OK'; Body = $content; Ranges = $false }
    }
    try {
        $base = "http://127.0.0.1:$($server.Port)"
        $sha = [System.Security.Cryptography.SHA256]::Create()
        $want = [System.BitConverter]::ToString($sha.ComputeHash($content))
        $check = {
            param($path)
            Assert-Equal $want ([System.BitConverter]::ToString($sha.ComputeHash([System.IO.File]::ReadAllBytes($path)))) "content of $path"
        }

        $a = Join-Path $folder 'a.bin'
        Save-LabDownload -Url "$base/resumable.bin" -Destination $a -ExpectedSize $content.Length
        & $check "$a.partial"
        Assert-True (-not (Test-Path -LiteralPath $a)) 'the file is left as .partial until it is verified'

        $b = Join-Path $folder 'b.bin'
        $firstPart = New-Object byte[] 1000000
        [System.Array]::Copy($content, 0, $firstPart, 0, 1000000)
        [System.IO.File]::WriteAllBytes("$b.partial", $firstPart)
        Save-LabDownload -Url "$base/resumable.bin" -Destination $b -ExpectedSize $content.Length
        & $check "$b.partial"
        Assert-Equal '/resumable.bin|bytes=1000000-' $server.Log[$server.Log.Count - 1] 'resumed from the first missing byte'

        $c = Join-Path $folder 'c.bin'
        [System.IO.File]::WriteAllBytes("$c.partial", $firstPart)
        Save-LabDownload -Url "$base/plain.bin" -Destination $c -ExpectedSize $content.Length
        & $check "$c.partial"

        $d = Join-Path $folder 'd.bin'
        [System.IO.File]::WriteAllBytes("$d.partial", (New-Object byte[] 3000000))
        Save-LabDownload -Url "$base/resumable.bin" -Destination $d -ExpectedSize $content.Length
        & $check "$d.partial"
        Assert-Equal '/resumable.bin|' $server.Log[$server.Log.Count - 1] 'an oversized leftover is thrown away'

        $requests = $server.Log.Count
        Save-LabDownload -Url "$base/resumable.bin" -Destination $d -ExpectedSize $content.Length
        Assert-Equal $requests $server.Log.Count 'a complete .partial is not fetched again'

        Assert-Throws { Save-LabDownload -Url "$base/missing.bin" -Destination (Join-Path $folder 'e.bin') -ExpectedSize 10 } `
            'The download failed (HTTP 404).'
    } finally {
        Stop-TestHttpServer $server
        Remove-Item -LiteralPath $folder -Recurse -Force
    }
}

# ==========================================================================
# 4a. The two launchers, started the way the relay starts them
# ==========================================================================
#
# The relay keeps its own copies of Start-ASR-Lab.ps1, Stop-ASR-Lab.ps1 and
# LabCommon.ps1 in its data folder and runs those, never the files in the lab
# folder (protocol 11.5). Here the two real launchers are copied beside a
# stand-in LabCommon.ps1 that only reports what it was asked to do, and are
# run in a child PowerShell with the relay's own command line. The lab folder
# holds trap copies of all three scripts: loading any of them leaves a mark.

function New-RelayScene {
    $root = New-TestFolder
    $copies = Join-Path $root 'data folder\lab'
    $lab = Join-Path $root 'the lab\asr'
    New-Item -ItemType Directory -Path $copies -Force | Out-Null
    New-Item -ItemType Directory -Path $lab -Force | Out-Null
    foreach ($name in @('Start-ASR-Lab.ps1', 'Stop-ASR-Lab.ps1')) {
        Copy-Item -LiteralPath (Join-Path $labRoot $name) -Destination (Join-Path $copies $name)
    }
    $standIn = @(
        'function Resolve-LabFolder { param([string]$Path) return "resolved:$Path" }',
        'function Invoke-LabStart {',
        '    param([string]$Model, [int]$Port, [switch]$NoBrowser, [switch]$EnsureDocker, [string]$LabRoot)',
        '    if ($Port -eq 50999) { throw ''Model weights have not been downloaded.'' }',
        '    "start|$Model|$Port|$([bool]$NoBrowser)|$([bool]$EnsureDocker)|$LabRoot|$PSScriptRoot"',
        '}',
        'function Invoke-LabStop {',
        '    param([int]$Port)',
        '    if ($Port -eq 50999) { throw ''Could not stop the local model.'' }',
        '    "stop|$Port|$PSScriptRoot"',
        '}'
    )
    Set-Content -LiteralPath (Join-Path $copies 'LabCommon.ps1') -Value $standIn -Encoding Ascii
    $trap = 'Set-Content -LiteralPath (Join-Path $PSScriptRoot ''LOADED'') -Value $MyInvocation.MyCommand.Name -Encoding Ascii'
    foreach ($name in @('Start-ASR-Lab.ps1', 'Stop-ASR-Lab.ps1', 'LabCommon.ps1')) {
        Set-Content -LiteralPath (Join-Path $lab $name) -Value $trap -Encoding Ascii
    }
    return @{ Root = $root; Copies = $copies; Lab = $lab }
}

function Invoke-AsRelay {
    # protocol 11.5: <powershell> -NoProfile -ExecutionPolicy Bypass -File <copy> <arguments>.
    # The child starts in $WorkingFolder, so a script that looked beside the
    # current folder instead of beside itself would be caught.
    param([string]$Script, [string[]]$Arguments, [string]$WorkingFolder)
    $shell = (Get-Process -Id $PID).Path
    $all = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $Script) + $Arguments
    Push-Location -LiteralPath $WorkingFolder
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $shell @all 2>$null)
        $code = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    return [pscustomobject]@{ ExitCode = $code; Output = @($output | ForEach-Object { "$_" }) }
}

Test-Case 'as the relay runs it: Start takes the lab folder from -LabFolder and loads nothing from it' {
    $scene = New-RelayScene
    try {
        $start = Join-Path $scene.Copies 'Start-ASR-Lab.ps1'
        $run = Invoke-AsRelay $start @('-LabFolder', $scene.Lab, '-Model', 'qwen3', '-Port', '50123', '-NoBrowser', '-EnsureDocker') $scene.Lab
        Assert-Equal 0 $run.ExitCode 'exit code'
        Assert-Equal @("start|qwen3|50123|True|True|resolved:$($scene.Lab)|$($scene.Copies)") $run.Output `
            'the lab folder is the one named; the shared script is the one beside the copy'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $scene.Lab 'LOADED'))) 'no script of the lab folder was loaded'
    } finally {
        Remove-Item -LiteralPath $scene.Root -Recurse -Force
    }
}

Test-Case 'run by hand: without -LabFolder the lab folder is the folder of the script' {
    $scene = New-RelayScene
    try {
        $start = Join-Path $scene.Copies 'Start-ASR-Lab.ps1'
        $run = Invoke-AsRelay $start @('-Model', 'nemotron') $scene.Lab
        Assert-Equal 0 $run.ExitCode 'exit code'
        Assert-Equal @("start|nemotron|47842|False|False|resolved:$($scene.Copies)|$($scene.Copies)") $run.Output `
            'defaults, and never the current folder'
        $byPosition = Invoke-AsRelay $start @('qwen3', '50123') $scene.Lab
        Assert-Equal @("start|qwen3|50123|False|False|resolved:$($scene.Copies)|$($scene.Copies)") $byPosition.Output `
            'the model and the port keep their positions'
        # '""' is how Windows PowerShell 5.1 hands a program an empty argument.
        $empty = Invoke-AsRelay $start @('-LabFolder', '""', '-Model', 'nemotron') $scene.Lab
        Assert-True ($empty.ExitCode -ne 0) 'an empty -LabFolder is refused'
        Assert-Equal 0 $empty.Output.Count 'and nothing is started for it'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $scene.Lab 'LOADED'))) 'no script of the current folder was loaded'
    } finally {
        Remove-Item -LiteralPath $scene.Root -Recurse -Force
    }
}

Test-Case 'as the relay runs it: Stop takes -Port alone and loads nothing from the lab folder' {
    $scene = New-RelayScene
    try {
        $stop = Join-Path $scene.Copies 'Stop-ASR-Lab.ps1'
        $run = Invoke-AsRelay $stop @('-Port', '50123') $scene.Lab
        Assert-Equal 0 $run.ExitCode 'exit code'
        Assert-Equal @("stop|50123|$($scene.Copies)") $run.Output 'the shared script is the one beside the copy'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $scene.Lab 'LOADED'))) 'no script of the lab folder was loaded'
    } finally {
        Remove-Item -LiteralPath $scene.Root -Recurse -Force
    }
}

Test-Case 'as the relay runs it: a failure is a non-zero exit code, success is 0' {
    $scene = New-RelayScene
    try {
        $start = Join-Path $scene.Copies 'Start-ASR-Lab.ps1'
        $failed = Invoke-AsRelay $start @('-LabFolder', $scene.Lab, '-Model', 'nemotron', '-Port', '50999', '-NoBrowser', '-EnsureDocker') $scene.Lab
        Assert-True ($failed.ExitCode -ne 0) 'Start fails with a non-zero exit code'
        $stopped = Invoke-AsRelay (Join-Path $scene.Copies 'Stop-ASR-Lab.ps1') @('-Port', '50999') $scene.Lab
        Assert-True ($stopped.ExitCode -ne 0) 'Stop fails with a non-zero exit code'
        $unknown = Invoke-AsRelay $start @('-LabFolder', $scene.Lab, '-Model', 'whisper', '-Port', '50123') $scene.Lab
        Assert-True ($unknown.ExitCode -ne 0) 'a model that is not one of the two is refused'
        Assert-Equal 0 $unknown.Output.Count 'and nothing is started for it'
    } finally {
        Remove-Item -LiteralPath $scene.Root -Recurse -Force
    }
}

# ==========================================================================
# 5. Stand-ins for Docker, health, the clock and the prompt
# ==========================================================================

$OurLabels = '{"com.livecueremake.component":"asr-lab"}'

function Reset-World {
    $script:World = @{
        DockerInstalled = $true
        EngineUp        = $true
        EngineUpAfter   = 0      # with EngineUp false: the engine answers from this 'docker info' call on
        InfoCalls       = 0
        Container       = $null  # @{ Labels; Running; Port }
        Health          = @{}    # port -> health object, for the time before 'docker run'
        HealthAfterRun  = $null  # what the new container reports ...
        ReadyAfterPolls = 1      # ... from this poll on
        Polls           = 0
        DiesAfterRun    = $false
        RunExit         = 0
        StopExit        = 0
        RmExit          = 0
        BuildExit       = 0
        SignatureOk     = $true
        Answer          = 'yes'
        Calls           = New-Object System.Collections.Generic.List[string]
        Prompts         = New-Object System.Collections.Generic.List[string]
        Opened          = New-Object System.Collections.Generic.List[int]
        Started         = New-Object System.Collections.Generic.List[string]
        Checked         = New-Object System.Collections.Generic.List[string]
        LogsShown       = 0
        Now             = [datetime]'2026-01-01T00:00:00'
        Start           = [datetime]'2026-01-01T00:00:00'
        Ran             = $false
        RunArguments    = $null
    }
}

function Get-LabDockerPath {
    if (-not $World.DockerInstalled) { throw 'Docker Desktop is required.' }
    return $World.DockerPath
}

function Invoke-LabDocker {
    param([string]$DockerPath, [string[]]$Arguments)
    $World.Calls.Add($Arguments -join ' ')
    $exit = 0
    $output = @()
    $verb = $Arguments[0]
    if ($verb -ceq 'info') {
        $World.InfoCalls++
        $up = $World.EngineUp -or ($World.EngineUpAfter -gt 0 -and $World.InfoCalls -ge $World.EngineUpAfter)
        if (-not $up) { $exit = 1 }
    } elseif ($verb -ceq 'ps') {
        if ($null -ne $World.Container) { $output = @('livecue-remake-asr') }
    } elseif ($verb -ceq 'inspect' -and $Arguments[2] -ceq '{{json .Config.Labels}}') {
        if ($null -eq $World.Container) { $exit = 1 } else { $output = @($World.Container.Labels) }
    } elseif ($verb -ceq 'inspect' -and $Arguments[2] -ceq '{{.State.Running}}') {
        if ($null -eq $World.Container) { $exit = 1 } else { $output = @(([string]$World.Container.Running).ToLowerInvariant()) }
    } elseif ($verb -ceq 'port') {
        if ($null -eq $World.Container -or -not $World.Container.Running) { $exit = 1 } else { $output = @("127.0.0.1:$($World.Container.Port)") }
    } elseif ($verb -ceq 'stop') {
        $exit = $World.StopExit
        if ($exit -eq 0 -and $null -ne $World.Container) { $World.Container.Running = $false }
    } elseif ($verb -ceq 'rm') {
        $exit = $World.RmExit
        if ($exit -eq 0) { $World.Container = $null }
    } elseif ($verb -ceq 'run') {
        $exit = $World.RunExit
        if ($exit -eq 0) {
            $World.Ran = $true
            $World.RunArguments = $Arguments
            $World.Container = @{ Labels = $OurLabels; Running = (-not $World.DiesAfterRun); Port = 0 }
        }
    } else {
        throw "the stand-in does not know 'docker $verb'"
    }
    return [pscustomobject]@{ ExitCode = $exit; Output = $output }
}

function Invoke-LabDockerVisible {
    param([string]$DockerPath, [string[]]$Arguments)
    $World.Calls.Add($Arguments -join ' ')
    return $World.BuildExit
}

function Get-LabHealth {
    param([int]$Port)
    if ($World.Ran) {
        $World.Polls++
        if ($World.Polls -ge $World.ReadyAfterPolls) { return $World.HealthAfterRun }
        return $null
    }
    return $World.Health[$Port]
}

function Get-LabNow { return $World.Now }
function Start-LabSleep { param([int]$Seconds) $World.Now = $World.Now.AddSeconds($Seconds) }
function Open-LabPage { param([int]$Port) $World.Opened.Add($Port) }
function Start-LabProcess { param([string]$Path) $World.Started.Add($Path) }
function Test-LabDockerDesktopSignature { param([string]$Path) $World.Checked.Add($Path); return $World.SignatureOk }
function Write-LabContainerLog { param([string]$DockerPath) $World.LogsShown++ }
function Read-LabAnswer { param([string]$Prompt) $World.Prompts.Add($Prompt); return $World.Answer }

function Get-Health {
    param([string]$Model, [bool]$Busy = $false, $Protocol = 1, [bool]$Ready = $true)
    return [pscustomobject]@{ model = $Model; ready = $Ready; relayProtocol = $Protocol; busy = $Busy }
}

function Get-DockerVerbs {
    # The comma keeps a one-item list a list.
    return ,@($World.Calls | ForEach-Object { ($_ -split ' ')[0] })
}

# A lab folder with weights for nemotron only, and a fake Docker install tree.
$fixture = New-TestFolder
New-Item -ItemType Directory -Path (Join-Path $fixture 'lab\private\models\nemotron') -Force | Out-Null
Set-Content -LiteralPath (Join-Path $fixture 'lab\private\models\nemotron\config.json') -Value '{}' -Encoding Ascii
New-Item -ItemType Directory -Path (Join-Path $fixture 'Docker\resources\bin') -Force | Out-Null
Set-Content -LiteralPath (Join-Path $fixture 'Docker\Docker Desktop.exe') -Value 'not really' -Encoding Ascii
$fixtureLab = Join-Path $fixture 'lab'
$fixtureDocker = Join-Path $fixture 'Docker\resources\bin\docker.exe'
$fixtureDesktop = Join-Path $fixture 'Docker\Docker Desktop.exe'

function Reset-Scene {
    Reset-World
    $World.DockerPath = $fixtureDocker
}

function Start-Lab {
    param([string]$Model = 'nemotron', [int]$Port = 47842, [switch]$Browser, [switch]$EnsureDocker)
    Invoke-LabStart -Model $Model -Port $Port -NoBrowser:(-not $Browser) -EnsureDocker:$EnsureDocker -LabRoot $fixtureLab
}

# ==========================================================================
# 6. Start-ASR-Lab, step by step
# ==========================================================================

Test-Case 'start 1: Docker must be installed' {
    Reset-Scene
    $World.DockerInstalled = $false
    Assert-Throws { Start-Lab } 'Docker Desktop is required.'
    Assert-Equal 0 $World.Calls.Count 'docker calls'
}

Test-Case 'start 2: engine down without -EnsureDocker fails and starts nothing' {
    Reset-Scene
    $World.EngineUp = $false
    Assert-Throws { Start-Lab } 'Start Docker Desktop first, then run this launcher again.'
    Assert-Equal @('info') (Get-DockerVerbs) 'docker calls'
    Assert-Equal 0 $World.Started.Count 'programs started'
}

Test-Case 'start 2: -EnsureDocker starts the verified Docker Desktop and waits for the engine' {
    Reset-Scene
    $World.EngineUp = $false
    $World.EngineUpAfter = 4   # the first check plus three polls
    $World.HealthAfterRun = Get-Health 'nemotron'
    Start-Lab -EnsureDocker
    Assert-Equal @($fixtureDesktop) @($World.Checked) 'signature checked on Docker Desktop.exe beside docker.exe'
    Assert-Equal @($fixtureDesktop) @($World.Started) 'started exactly that program, once'
    Assert-True $World.Ran 'went on to start the container'
    Assert-True (($World.Now - $World.Start).TotalSeconds -ge 6) 'polled every 2 seconds'
}

Test-Case 'start 2: Docker Desktop that is not where expected is not started' {
    Reset-Scene
    $World.EngineUp = $false
    $World.DockerPath = Join-Path $fixture 'elsewhere\resources\bin\docker.exe'
    Assert-Throws { Start-Lab -EnsureDocker } 'Docker Desktop executable was not found. Open it manually.'
    Assert-Equal 0 $World.Started.Count 'programs started'
}

Test-Case 'start 2: Docker Desktop with a bad signature is never started' {
    Reset-Scene
    $World.EngineUp = $false
    $World.SignatureOk = $false
    Assert-Throws { Start-Lab -EnsureDocker } 'Docker Desktop publisher could not be verified. Open it manually.'
    Assert-Equal 1 $World.Checked.Count 'signature checks'
    Assert-Equal 0 $World.Started.Count 'programs started'
}

Test-Case 'start 2: an engine that never comes up fails after two minutes' {
    Reset-Scene
    $World.EngineUp = $false
    Assert-Throws { Start-Lab -EnsureDocker } 'Docker engine did not start. Check Docker Desktop on the PC.'
    Assert-Equal 120 ([int]($World.Now - $World.Start).TotalSeconds) 'seconds waited'
    Assert-Equal 61 $World.InfoCalls 'engine checks: one before, then every 2 seconds'
    Assert-True (-not $World.Ran) 'no container was started'
}

Test-Case 'start 3: weights must be on disk' {
    Reset-Scene
    Assert-Throws { Start-Lab -Model 'qwen3' } 'Model weights have not been downloaded.'
    Assert-Equal @('info') (Get-DockerVerbs) 'docker calls'
}

Test-Case 'start 4: an active session is never interrupted' {
    Reset-Scene
    $World.Container = @{ Labels = $OurLabels; Running = $true; Port = 47842 }
    $World.Health[47842] = Get-Health 'qwen3' -Busy $true
    Assert-Throws { Start-Lab } 'An ASR session is active. Stop it before switching models.'
    Assert-Equal @('info', 'ps') (Get-DockerVerbs) 'docker calls'
}

Test-Case 'start 4: a session on a container published on another port is seen too' {
    Reset-Scene
    $World.Container = @{ Labels = $OurLabels; Running = $true; Port = 50123 }
    $World.Health[50123] = Get-Health 'qwen3' -Busy $true
    Assert-Throws { Start-Lab -Port 47842 } 'An ASR session is active. Stop it before switching models.'
    Assert-True (-not ((Get-DockerVerbs) -contains 'stop')) 'nothing was stopped'
}

Test-Case 'start 4: the requested model already ready with protocol 1 is left alone' {
    Reset-Scene
    $World.Container = @{ Labels = $OurLabels; Running = $true; Port = 47842 }
    $World.Health[47842] = Get-Health 'nemotron'
    Start-Lab
    Assert-Equal @('info', 'ps', 'port') (Get-DockerVerbs) 'docker calls'
    Assert-Equal 0 $World.Opened.Count 'no page with -NoBrowser'
    Reset-Scene
    $World.Container = @{ Labels = $OurLabels; Running = $true; Port = 47842 }
    $World.Health[47842] = Get-Health 'nemotron'
    Start-Lab -Browser
    Assert-Equal @(47842) @($World.Opened) 'the lab page is opened otherwise'
}

Test-Case 'start 4: a container that is not ours is never replaced' {
    foreach ($labels in @('{}', 'null', '{"com.livecueremake.component":"something-else"}', '{"com.livecue.component":"asr-lab"}')) {
        Reset-Scene
        $World.Container = @{ Labels = $labels; Running = $true; Port = 47842 }
        Assert-Throws { Start-Lab } 'Container name belongs to another workload; refusing to replace it.'
        $verbs = Get-DockerVerbs
        Assert-True (-not ($verbs -contains 'stop') -and -not ($verbs -contains 'rm') -and -not ($verbs -contains 'run')) "untouched with labels $labels"
    }
}

Test-Case 'start 4 to 6: another model is replaced: stop with 15 s grace, remove, run, wait' {
    Reset-Scene
    $World.Container = @{ Labels = $OurLabels; Running = $true; Port = 47842 }
    $World.Health[47842] = Get-Health 'qwen3'
    $World.HealthAfterRun = Get-Health 'nemotron'
    $World.ReadyAfterPolls = 5
    Start-Lab
    Assert-Equal 'stop -t 15 livecue-remake-asr' $World.Calls[4] 'stop'
    Assert-Equal 'rm livecue-remake-asr' $World.Calls[5] 'remove'
    Assert-Equal (Get-LabRunArguments -Model 'nemotron' -Port 47842 -LabRoot $fixtureLab) $World.RunArguments 'run arguments'
    Assert-Equal 8 ([int]($World.Now - $World.Start).TotalSeconds) 'polled every 2 seconds until ready'
}

Test-Case 'start 4: the right model with another relay protocol is replaced, not trusted' {
    Reset-Scene
    $World.Container = @{ Labels = $OurLabels; Running = $true; Port = 47842 }
    $World.Health[47842] = Get-Health 'nemotron' -Protocol 2
    $World.HealthAfterRun = Get-Health 'nemotron'
    Start-Lab
    Assert-True $World.Ran 'a new container was started'
}

Test-Case 'start 4: a failed stop or remove ends the start' {
    Reset-Scene
    $World.Container = @{ Labels = $OurLabels; Running = $true; Port = 47842 }
    $World.StopExit = 1
    Assert-Throws { Start-Lab } 'Could not stop the existing model container.'
    Reset-Scene
    $World.Container = @{ Labels = $OurLabels; Running = $false; Port = 47842 }
    $World.RmExit = 1
    Assert-Throws { Start-Lab } 'Could not remove the existing model container.'
    Assert-True (-not $World.Ran) 'nothing was started'
}

Test-Case 'start 5: docker run failing is reported' {
    Reset-Scene
    $World.RunExit = 125
    Assert-Throws { Start-Lab } 'Could not start the local model container.'
}

Test-Case 'start 6: a container that dies while loading shows its log and fails' {
    Reset-Scene
    $World.DiesAfterRun = $true
    Assert-Throws { Start-Lab } 'Model service stopped during initialization.'
    Assert-Equal 1 $World.LogsShown 'log tails shown'
}

Test-Case 'start 6: ready without relay protocol 1 is not ready (old defect B20)' {
    foreach ($health in @((Get-Health 'nemotron' -Protocol 2), (Get-Health 'nemotron' -Protocol $null), (Get-Health 'qwen3'), (Get-Health 'nemotron' -Ready $false))) {
        Reset-Scene
        $World.HealthAfterRun = $health
        Assert-Throws { Start-Lab } 'Model is still starting. Inspect: docker logs --tail 40 livecue-remake-asr'
        Assert-Equal 480 ([int]($World.Now - $World.Start).TotalSeconds) 'gave up after 8 minutes'
        Assert-Equal 0 $World.Opened.Count 'no page opened'
    }
}

Test-Case 'start 6: ready opens the lab page on the chosen port unless -NoBrowser' {
    Reset-Scene
    $World.HealthAfterRun = Get-Health 'nemotron'
    Start-Lab -Port 50123 -Browser
    Assert-Equal @(50123) @($World.Opened) 'page opened'
    Assert-True ($script:Said -contains 'Ready: http://127.0.0.1:50123 (nemotron)') 'says where the lab is'
    Assert-True ($World.RunArguments -contains '127.0.0.1:50123:8765') 'published on the chosen loopback port'
    Assert-True ($World.RunArguments -contains 'ASR_HOST_PORT=50123') 'the container is told its host port'
}

# ==========================================================================
# 7. Stop-ASR-Lab, step by step
# ==========================================================================

Test-Case 'stop 1: Docker must be installed' {
    Reset-Scene
    $World.DockerInstalled = $false
    Assert-Throws { Invoke-LabStop -Port 47842 } 'Docker Desktop is required.'
}

Test-Case 'stop 2: engine down means nothing to do, and Docker is not started' {
    Reset-Scene
    $World.EngineUp = $false
    Invoke-LabStop -Port 47842
    Assert-Equal @('info') (Get-DockerVerbs) 'docker calls'
    Assert-Equal 0 $World.Started.Count 'programs started'
}

Test-Case 'stop 2: no container means nothing to do' {
    Reset-Scene
    Invoke-LabStop -Port 47842
    Assert-Equal @('info', 'ps') (Get-DockerVerbs) 'docker calls'
}

Test-Case 'stop 3: a container that is not ours is never stopped' {
    Reset-Scene
    $World.Container = @{ Labels = '{"com.docker.compose.project":"other"}'; Running = $true; Port = 47842 }
    Assert-Throws { Invoke-LabStop -Port 47842 } 'Container is not owned by LiveCue. Refusing to stop it.'
    Assert-True (-not ((Get-DockerVerbs) -contains 'stop')) 'not stopped'
}

Test-Case 'stop 4: an active session is never cut off' {
    Reset-Scene
    $World.Container = @{ Labels = $OurLabels; Running = $true; Port = 47842 }
    $World.Health[47842] = Get-Health 'nemotron' -Busy $true
    Assert-Throws { Invoke-LabStop -Port 47842 } 'An ASR session is active. Stop it before unloading the model.'
    Assert-True (-not ((Get-DockerVerbs) -contains 'stop')) 'not stopped'
    Reset-Scene
    $World.Container = @{ Labels = $OurLabels; Running = $true; Port = 50123 }
    $World.Health[50123] = Get-Health 'nemotron' -Busy $true
    Assert-Throws { Invoke-LabStop -Port 47842 } 'An ASR session is active. Stop it before unloading the model.'
}

Test-Case 'stop 5: stops with 15 s grace and removes nothing' {
    Reset-Scene
    $World.Container = @{ Labels = $OurLabels; Running = $true; Port = 47842 }
    $World.Health[47842] = Get-Health 'nemotron'
    Invoke-LabStop -Port 47842
    Assert-Equal 'stop -t 15 livecue-remake-asr' $World.Calls[$World.Calls.Count - 1] 'last docker call'
    Assert-True (-not ((Get-DockerVerbs) -contains 'rm')) 'the container is kept'
    Assert-True ($null -ne $World.Container -and -not $World.Container.Running) 'stopped, still there'
}

Test-Case 'stop 5: a failed stop is reported' {
    Reset-Scene
    $World.Container = @{ Labels = $OurLabels; Running = $true; Port = 47842 }
    $World.StopExit = 1
    Assert-Throws { Invoke-LabStop -Port 47842 } 'Could not stop the local model.'
}

# ==========================================================================
# 8. Build-ASR-Images
# ==========================================================================

Test-Case 'build: -PlanOnly touches nothing' {
    Reset-Scene
    $World.DockerInstalled = $false
    Invoke-LabBuildImages -Model 'all' -PlanOnly -LabRoot $fixtureLab
    Assert-Equal 0 $World.Calls.Count 'docker calls'
    Assert-Equal 0 $World.Prompts.Count 'questions asked'
    Assert-True ($script:Said -contains '  livecue-remake-asr-qwen3:1.0') 'the plan was shown'
}

Test-Case 'build: needs a running engine and never starts Docker' {
    Reset-Scene
    $World.EngineUp = $false
    Assert-Throws { Invoke-LabBuildImages -Model 'all' -LabRoot $fixtureLab } 'Start Docker Desktop first, then run this script again.'
    Assert-Equal 0 $World.Started.Count 'programs started'
    Assert-Equal 0 $World.Prompts.Count 'questions asked'
}

Test-Case 'build: anything but the typed word yes builds nothing' {
    foreach ($answer in @('', 'y', 'Yes', 'YES', 'no', 'yes please', $null)) {
        Reset-Scene
        $World.Answer = $answer
        Invoke-LabBuildImages -Model 'all' -LabRoot $fixtureLab
        Assert-Equal @('Build these images now? Type yes to continue') @($World.Prompts) 'asked once, plainly'
        Assert-True ($script:Said -contains 'Nothing was built.') 'says that nothing happened'
        Assert-Equal @('info') (Get-DockerVerbs) "no build after answering '$answer'"
    }
}

Test-Case 'build: yes builds each target with its tag from the lab folder' {
    Reset-Scene
    Invoke-LabBuildImages -Model 'all' -LabRoot $fixtureLab
    Assert-Equal @(
        'info --format {{.ServerVersion}}',
        "build --target nemotron --tag livecue-remake-asr-nemotron:1.0 $fixtureLab",
        "build --target qwen3 --tag livecue-remake-asr-qwen3:1.0 $fixtureLab"
    ) @($World.Calls) 'docker calls'
    Assert-True ($script:Said -contains 'Images built. Next: Get-ASR-Weights.ps1 -Model <model>, then Start-ASR-Lab.ps1.') 'says what is next'
    Reset-Scene
    $World.Answer = ' yes '
    Invoke-LabBuildImages -Model 'qwen3' -LabRoot $fixtureLab
    Assert-Equal "build --target qwen3 --tag livecue-remake-asr-qwen3:1.0 $fixtureLab" $World.Calls[1] 'one target'
    Assert-Equal 2 $World.Calls.Count 'docker calls for one target'
    Reset-Scene
    $World.BuildExit = 1
    Assert-Throws { Invoke-LabBuildImages -Model 'all' -LabRoot $fixtureLab } 'The image build failed for nemotron.'
}

# ==========================================================================
# 9. Get-ASR-Weights, end to end against the loopback server
# ==========================================================================

Test-Case 'weights: plan, confirm, download, verify, skip what is present, reject what is wrong' {
    $lab = New-TestFolder
    $small = [System.Text.Encoding]::ASCII.GetBytes('{"model_type":"test"}')
    $big = New-TestBytes -Count 1500000 -Seed 11
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $bigHash = ([System.BitConverter]::ToString($sha.ComputeHash($big)) -replace '-', '').ToLowerInvariant()
    $smallPath = Join-Path $lab 'small.tmp'
    [System.IO.File]::WriteAllBytes($smallPath, $small)
    $smallId = Get-LabGitBlobSha1 $smallPath
    Remove-Item -LiteralPath $smallPath
    $manifest = @{
        nemotron = @{
            name = 'Test model'; repository = 'example/test'; revision = ('0' * 40)
            license = 'Test'; licenseUrl = 'https://example.test/licence'; totalBytes = ($small.Length + $big.Length)
            files = @(
                @{ path = 'config.json'; size = $small.Length; gitSha1 = $smallId },
                @{ path = 'model.safetensors'; size = $big.Length; sha256 = $bigHash })
        }
    }
    Set-Content -LiteralPath (Join-Path $lab 'weights.json') -Value (ConvertTo-Json $manifest -Depth 6) -Encoding UTF8
    $files = @{
        '/config.json'       = @{ Status = '200 OK'; Body = $small; Ranges = $true }
        '/model.safetensors' = @{ Status = '200 OK'; Body = $big; Ranges = $true }
    }
    $server = Start-TestHttpServer $files
    $script:WeightBase = "http://127.0.0.1:$($server.Port)"
    function Get-LabWeightUrl { param($Manifest, [string]$Path) return "$script:WeightBase/$Path" }
    $target = Join-Path $lab 'private\models\nemotron'
    try {
        Reset-Scene
        Invoke-LabGetWeights -Model 'nemotron' -PlanOnly -LabRoot $lab
        Assert-Equal 0 $server.Log.Count 'requests after -PlanOnly'
        Assert-Equal 0 $World.Prompts.Count 'questions after -PlanOnly'

        Reset-Scene
        $World.Answer = 'no'
        Invoke-LabGetWeights -Model 'nemotron' -LabRoot $lab
        Assert-Equal @('Download these files now? Type yes to continue') @($World.Prompts) 'asked once, plainly'
        Assert-True ($script:Said -contains 'Source:      https://huggingface.co/example/test') 'the source was shown first'
        Assert-True ($script:Said -contains 'Nothing was downloaded.') 'says that nothing happened'
        Assert-Equal 0 $server.Log.Count 'requests after answering no'
        Assert-True (-not (Test-Path -LiteralPath $target)) 'nothing created after answering no'

        Reset-Scene
        Invoke-LabGetWeights -Model 'nemotron' -LabRoot $lab
        Assert-Equal 1 $World.Prompts.Count 'asked once'
        Assert-Equal @('/config.json|', '/model.safetensors|') @($server.Log) 'both files requested'
        Assert-Equal $bigHash (Get-FileHash -LiteralPath (Join-Path $target 'model.safetensors') -Algorithm SHA256).Hash.ToLowerInvariant() 'weights content'
        Assert-Equal @('config.json', 'model.safetensors') @(Get-ChildItem -LiteralPath $target | ForEach-Object { $_.Name } | Sort-Object) 'no leftovers'

        Reset-Scene
        Invoke-LabGetWeights -Model 'nemotron' -LabRoot $lab
        Assert-Equal 0 $World.Prompts.Count 'nothing to ask when everything is present'
        Assert-Equal 2 $server.Log.Count 'no further requests'

        # A damaged file is noticed and only that file is fetched again.
        [System.IO.File]::WriteAllBytes((Join-Path $target 'config.json'), [System.Text.Encoding]::ASCII.GetBytes('{"model_type":"evil"}'))
        Reset-Scene
        Invoke-LabGetWeights -Model 'nemotron' -LabRoot $lab
        Assert-Equal '/config.json|' $server.Log[2] 'the damaged file is requested'
        Assert-Equal 3 $server.Log.Count 'only that file'
        Assert-Equal $smallId (Get-LabGitBlobSha1 (Join-Path $target 'config.json')) 'repaired'

        # A server that sends the wrong bytes: the file is discarded, never put in place.
        Remove-Item -LiteralPath (Join-Path $target 'model.safetensors')
        $files['/model.safetensors'].Body = (New-TestBytes -Count 1500000 -Seed 12)
        Reset-Scene
        Assert-Throws { Invoke-LabGetWeights -Model 'nemotron' -LabRoot $lab } `
            'model.safetensors did not match its published size and checksum. It was discarded; run the script again.'
        Assert-Equal @('config.json') @(Get-ChildItem -LiteralPath $target | ForEach-Object { $_.Name }) 'the bad download is gone'
    } finally {
        Stop-TestHttpServer $server
        Remove-Item -LiteralPath $lab -Recurse -Force
    }
}

# ==========================================================================

Remove-Item -LiteralPath $fixture -Recurse -Force
Write-Result ''
Write-Result "$($script:Passed) passed, $($script:Failed.Count) failed"
if ($script:Failed.Count -gt 0) {
    $script:Failed | ForEach-Object { Write-Result "  failed: $_" }
    exit 1
}
exit 0
