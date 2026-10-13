<#
.SYNOPSIS
    Manual check of PC speech on a real GPU, through a relay of its own (protocol P2-M01 to P2-M03).

.DESCRIPTION
    powershell -File scripts/verify-pc-asr.ps1 <model> <wav> [repeats] [-RelayHost <exe>] [-LabFolder <folder>] [-KeepModel] [-CheckOnly]

    <model>     nemotron or qwen3
    <wav>       a RIFF WAV file: PCM, 1 channel, 16,000 Hz, 16 bits, with audio in it. Synthetic speech
                only: never a recording of a real conversation.
    [repeats]   how often the file is sent, one copy after the other. A whole number; values below 1
                count as 1 and values above 5 as 5. Default 1.

    -RelayHost  LiveCueRelayHost.exe of a published build. Default: artifacts\publish\LiveCueRelayHost.exe
                in this repository (scripts\publish-windows.ps1 -Output artifacts/publish).
    -LabFolder  The speech lab folder, which holds Start-ASR-Lab.ps1 and Stop-ASR-Lab.ps1. Default: asr
                in this repository.
    -KeepModel  Leave the model loaded afterwards. Without it, a model that this run loaded is
                unloaded again; one that was already loaded is always left alone.
    -CheckOnly  Check the arguments, say what would be started, and start nothing.

    What it does, when the arguments are valid (P2-M01):

      1. Starts LiveCueRelayHost.exe --endpoint https://pc.example.test/ --port 0 --data-dir <temp>
         --lab <folder> --events json, and reads the port from its output. The relay has a pairing of its
         own in a temporary folder: LiveCue's real data folder and its port are never touched.
      2. Reads that pairing's token by running LiveCueRelayHost.exe --show-pairing once for the same
         temporary folder. That starts no second relay. The token is never printed.
      3. Opens the speech socket on 127.0.0.1, waits for "ready", sends the audio in 2,560-byte frames at
         real time, then "endStream" (P2-M02).
      4. Checks what came back (P2-M03) and prints one JSON line that ends in "result":"PASS" or
         "result":"FAIL".
      5. Stops the host and deletes the temporary folder.

    It needs Docker Desktop running, the lab images and the model weights, several GB of free GPU memory,
    and Codex installed and signed in (the relay does not start without it; no Codex request is made).
    A run takes up to four minutes. Nothing is written into the repository.

    Exit codes: 0 PASS (or valid arguments with -CheckOnly), 1 FAIL, 2 the arguments were refused.

    It is run by hand. No test and no CI job runs it.

.EXAMPLE
    powershell -File scripts/verify-pc-asr.ps1 nemotron C:\samples\synthetic-speech.wav
    powershell -File scripts/verify-pc-asr.ps1 qwen3 C:\samples\synthetic-speech.wav 3
    powershell -File scripts/verify-pc-asr.ps1 nemotron C:\samples\synthetic-speech.wav -CheckOnly
#>
# A plain parameter list on purpose: an argument that is none of these lands in $args and is refused with
# the usage text and exit code 2, instead of PowerShell's own binding error.
param(
    [string] $Model,
    [string] $Wav,
    [string] $Repeats = '1',
    [string] $RelayHost,
    [string] $LabFolder,
    [switch] $KeepModel,
    [switch] $CheckOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---- Constants of the protocol (docs/architecture/02-relay-protocol.md) -----------------------------

$Models = @('nemotron', 'qwen3')            # C-04
$SampleRate = 16000                         # C-70
$FrameBytes = 2560                          # C-71: 80 ms
$FrameMilliseconds = 80
$BytesPerMillisecond = 32                   # 16,000 samples of 2 bytes per second
$LimitSeconds = 240                         # P2-M03
$LongAudioMilliseconds = 32000              # P2-M03: longer audio gives at least two final texts
$Endpoint = 'https://pc.example.test/'      # a reserved test domain: nothing is ever sent there

$ExitPass = 0
$ExitFail = 1
$ExitUsage = 2

$RepositoryRoot = Split-Path -Parent $PSScriptRoot

function Write-Usage {
    param([string] $Problem)

    if ($Problem) {
        [Console]::Error.WriteLine($Problem)
        [Console]::Error.WriteLine('')
    }
    [Console]::Error.WriteLine('Usage: powershell -File scripts/verify-pc-asr.ps1 <model> <wav> [repeats] [-RelayHost <exe>] [-LabFolder <folder>] [-KeepModel] [-CheckOnly]')
    [Console]::Error.WriteLine('  <model>     nemotron or qwen3')
    [Console]::Error.WriteLine('  <wav>       RIFF WAV, PCM, 1 channel, 16,000 Hz, 16 bits, with audio in it (synthetic speech only)')
    [Console]::Error.WriteLine('  [repeats]   a whole number; below 1 counts as 1, above 5 as 5; default 1')
    [Console]::Error.WriteLine('  -RelayHost  LiveCueRelayHost.exe of a published build (default: artifacts\publish\LiveCueRelayHost.exe)')
    [Console]::Error.WriteLine('  -LabFolder  the folder with Start-ASR-Lab.ps1 and Stop-ASR-Lab.ps1 (default: asr)')
    [Console]::Error.WriteLine('  -KeepModel  leave a model that this run loaded in GPU memory')
    [Console]::Error.WriteLine('  -CheckOnly  check the arguments and start nothing')
    [Console]::Error.WriteLine('Needs Docker Desktop, the lab images and weights, a GPU, and Codex signed in. Up to four minutes.')
}

function Stop-WithUsage {
    param([string] $Problem)

    Write-Usage -Problem $Problem
    exit $ExitUsage
}

# ---- P2-M01: the arguments --------------------------------------------------------------------------

# Reads the audio of a WAV file, or says in one sentence why the file is not accepted.
function Read-WavAudio {
    param([string] $Path)

    $result = @{ Audio = $null; Problem = $null }
    $maximumBytes = 64MB
    $file = Get-Item -LiteralPath $Path
    if ($file.Length -gt $maximumBytes) {
        $result.Problem = 'The WAV file is larger than 64 MB.'
        return $result
    }

    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $ascii = [System.Text.Encoding]::ASCII
    if ($bytes.Length -lt 12 -or $ascii.GetString($bytes, 0, 4) -cne 'RIFF' -or $ascii.GetString($bytes, 8, 4) -cne 'WAVE') {
        $result.Problem = 'The file is not a RIFF WAV file.'
        return $result
    }

    $format = $null
    $audio = $null
    $position = 12
    while ($position + 8 -le $bytes.Length) {
        $id = $ascii.GetString($bytes, $position, 4)
        $size = [System.BitConverter]::ToUInt32($bytes, $position + 4)
        $start = $position + 8
        if ($size -gt $bytes.Length - $start) {
            # A data chunk that claims more than the file holds: what is there is the audio.
            if ($id -ceq 'data') { $size = $bytes.Length - $start } else { break }
        }

        if ($id -ceq 'fmt ' -and $size -ge 16) {
            $format = @{
                Tag        = [System.BitConverter]::ToUInt16($bytes, $start)
                Channels   = [System.BitConverter]::ToUInt16($bytes, $start + 2)
                SampleRate = [System.BitConverter]::ToUInt32($bytes, $start + 4)
                Bits       = [System.BitConverter]::ToUInt16($bytes, $start + 14)
            }
        }
        elseif ($id -ceq 'data' -and $null -eq $audio) {
            $audio = New-Object byte[] ([int] $size)
            [System.Array]::Copy($bytes, $start, $audio, 0, [int] $size)
        }

        # Chunks are padded to an even length.
        $position = $start + [int] $size + ([int] $size % 2)
    }

    if ($null -eq $format) {
        $result.Problem = 'The WAV file has no format chunk.'
    }
    elseif ($format.Tag -ne 1) {
        $result.Problem = 'The WAV file is not PCM.'
    }
    elseif ($format.Channels -ne 1) {
        $result.Problem = "The WAV file has $($format.Channels) channels; it must have 1."
    }
    elseif ($format.SampleRate -ne $SampleRate) {
        $result.Problem = "The WAV file is sampled at $($format.SampleRate) Hz; it must be 16,000 Hz."
    }
    elseif ($format.Bits -ne 16) {
        $result.Problem = "The WAV file has $($format.Bits) bits per sample; it must have 16."
    }
    elseif ($null -eq $audio -or $audio.Length -lt 2) {
        $result.Problem = 'The WAV file holds no audio.'
    }
    else {
        # Whole samples only: a last odd byte is not audio.
        if ($audio.Length % 2 -eq 1) {
            $whole = New-Object byte[] ($audio.Length - 1)
            [System.Array]::Copy($audio, 0, $whole, 0, $whole.Length)
            $audio = $whole
        }
        $result.Audio = $audio
    }

    return $result
}

if ($args.Count -gt 0) {
    Stop-WithUsage -Problem "Unknown argument: $($args -join ' ')"
}
if ([string]::IsNullOrWhiteSpace($Model) -or [string]::IsNullOrWhiteSpace($Wav)) {
    Stop-WithUsage -Problem 'Give the model and the WAV file.'
}
if ($Models -cnotcontains $Model) {
    Stop-WithUsage -Problem "The model must be exactly one of: $($Models -join ', ')."
}

$repeatCount = 0
if (-not [int]::TryParse($Repeats, [System.Globalization.NumberStyles]::AllowLeadingSign, [System.Globalization.CultureInfo]::InvariantCulture, [ref] $repeatCount)) {
    Stop-WithUsage -Problem 'repeats must be a whole number.'
}
$repeatCount = [Math]::Max(1, [Math]::Min(5, $repeatCount))

if (-not (Test-Path -LiteralPath $Wav -PathType Leaf)) {
    Stop-WithUsage -Problem 'The WAV file does not exist.'
}
$wavAudio = Read-WavAudio -Path $Wav
if ($wavAudio.Problem) {
    Stop-WithUsage -Problem $wavAudio.Problem
}
$audioBytes = [long] $wavAudio.Audio.Length * $repeatCount
$audioMilliseconds = [long] ($audioBytes / $BytesPerMillisecond)
if ($audioMilliseconds -ge $LimitSeconds * 1000) {
    Stop-WithUsage -Problem "The audio, sent $repeatCount time(s), lasts $([Math]::Round($audioMilliseconds / 1000)) s. A run must finish within $LimitSeconds s."
}

if (-not $RelayHost) { $RelayHost = Join-Path $RepositoryRoot 'artifacts\publish\LiveCueRelayHost.exe' }
if (-not $LabFolder) { $LabFolder = Join-Path $RepositoryRoot 'asr' }
if (-not (Test-Path -LiteralPath $RelayHost -PathType Leaf) -or (Split-Path -Leaf $RelayHost) -ne 'LiveCueRelayHost.exe') {
    Stop-WithUsage -Problem 'LiveCueRelayHost.exe was not found. Publish the build (scripts\publish-windows.ps1 -Output artifacts/publish) or give -RelayHost.'
}
$RelayHost = (Resolve-Path -LiteralPath $RelayHost).Path
if (-not (Test-Path -LiteralPath $LabFolder -PathType Container)) {
    Stop-WithUsage -Problem 'The lab folder does not exist.'
}
$LabFolder = (Resolve-Path -LiteralPath $LabFolder).Path.TrimEnd('\')
foreach ($labScript in 'Start-ASR-Lab.ps1', 'Stop-ASR-Lab.ps1') {
    if (-not (Test-Path -LiteralPath (Join-Path $LabFolder $labScript) -PathType Leaf)) {
        Stop-WithUsage -Problem "The lab folder does not hold $labScript."
    }
}

$dataFolder = Join-Path ([System.IO.Path]::GetTempPath()) ('LiveCue-verify-pc-asr-' + [Guid]::NewGuid().ToString('N'))
$hostArguments = @('--endpoint', $Endpoint, '--port', '0', '--data-dir', "`"$dataFolder`"", '--lab', "`"$LabFolder`"", '--events', 'json')

if ($CheckOnly) {
    Write-Host "The arguments are valid: model $Model, $($wavAudio.Audio.Length) bytes of audio sent $repeatCount time(s), $([Math]::Round($audioMilliseconds / 1000, 1)) s in all."
    Write-Host 'A run would start, and afterwards stop:'
    Write-Host "  LiveCueRelayHost.exe $($hostArguments -join ' ')"
    Write-Host "  LiveCueRelayHost.exe --show-pairing --endpoint $Endpoint --data-dir `"$dataFolder`""
    Write-Host 'Nothing was started.'
    exit $ExitPass
}

# ---- The run ----------------------------------------------------------------------------------------

$failures = New-Object System.Collections.Generic.List[string]
$stats = @{
    HandshakeMs      = $null
    Transcripts      = 0
    SpeechCompletes  = 0
    Words            = 0
    AudioProcessedMs = $null
    FinalizationMs   = $null
    Errors           = New-Object System.Collections.Generic.List[string]
    Ready            = $false
    Complete         = $false
    Closed           = $false
}
$clock = [System.Diagnostics.Stopwatch]::StartNew()
$none = [System.Threading.CancellationToken]::None
$hostProcess = $null
$socket = $null
$port = 0
$token = $null
$loadedHere = $false
$bytesSent = 0L

function Get-RemainingMilliseconds {
    return [int] [Math]::Max(0, ($LimitSeconds * 1000) - $clock.ElapsedMilliseconds)
}

# One request to this run's own relay, on loopback. Returns the parsed JSON body, or $null.
function Invoke-Relay {
    param([string] $Method, [string] $Path, [string] $Body)

    try {
        $request = [System.Net.HttpWebRequest]::Create("http://127.0.0.1:$port$Path")
        $request.Method = $Method
        $request.Proxy = $null
        $request.Timeout = 10000
        $request.Headers.Add('Authorization', "Bearer $token")
        $request.Headers.Add('X-LiveCue-Protocol', '2')
        if ($Body) {
            $payload = [System.Text.Encoding]::UTF8.GetBytes($Body)
            $request.ContentType = 'application/json'
            $request.ContentLength = $payload.Length
            $stream = $request.GetRequestStream()
            $stream.Write($payload, 0, $payload.Length)
            $stream.Close()
        }
        $response = $request.GetResponse()
        try {
            $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
            return ($reader.ReadToEnd() | ConvertFrom-Json)
        }
        finally {
            $response.Close()
        }
    }
    catch {
        return $null
    }
}

# The state member of the PC model's status object ("stopped", "starting", "ready", ...), or $null.
function Get-ModelState {
    $status = Invoke-Relay -Method 'GET' -Path '/v1/local-model'
    if ($status -and @($status.PSObject.Properties.Name) -contains 'state') { return [string] $status.state }
    return $null
}

# Takes what the relay sent so far. Waits at most $WaitMilliseconds for the next frame.
$receiveBuffer = New-Object byte[] 65536
$receiveSegment = New-Object 'System.ArraySegment[byte]' -ArgumentList (, $receiveBuffer)
$receiveText = New-Object System.IO.MemoryStream
$receivePending = $null

function Read-Frames {
    param([int] $WaitMilliseconds)

    while (-not $stats.Closed) {
        if ($null -eq $script:receivePending) {
            $script:receivePending = $socket.ReceiveAsync($receiveSegment, $none)
        }
        try {
            if (-not $script:receivePending.Wait($WaitMilliseconds)) { return }
        }
        catch {
            $stats.Closed = $true
            if (-not $stats.Complete) { $stats.Errors.Add('the connection ended') }
            return
        }

        $received = $script:receivePending.Result
        $script:receivePending = $null
        $WaitMilliseconds = 0
        if ($received.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
            $stats.Closed = $true
            return
        }

        $receiveText.Write($receiveBuffer, 0, $received.Count)
        if (-not $received.EndOfMessage) { continue }
        $text = [System.Text.Encoding]::UTF8.GetString($receiveText.ToArray())
        $receiveText.SetLength(0)

        try { $frame = $text | ConvertFrom-Json } catch { continue }
        $members = @($frame.PSObject.Properties.Name)
        if ($members -notcontains 'type') { continue }
        switch ($frame.type) {
            'ready' {
                $stats.Ready = $true
                if ($members -contains 'handshakeMs') { $stats.HandshakeMs = $frame.handshakeMs }
                if ($members -notcontains 'provider' -or $frame.provider -cne $Model) { $failures.Add('ready named another model') }
                if ($members -notcontains 'sampleRate' -or $frame.sampleRate -ne $SampleRate) { $failures.Add('ready did not give 16000 Hz') }
            }
            'transcript' {
                if ($members -contains 'transcript' -and -not [string]::IsNullOrWhiteSpace($frame.transcript)) { $stats.Transcripts++ }
            }
            'speechComplete' {
                $stats.SpeechCompletes++
                if ($members -contains 'transcript' -and $frame.transcript) {
                    $stats.Words += @($frame.transcript -split '\s+' | Where-Object { $_ }).Count
                }
            }
            'streamComplete' {
                $stats.Complete = $true
                if ($members -contains 'audioProcessedMs') { $stats.AudioProcessedMs = $frame.audioProcessedMs }
                if ($members -contains 'finalizationMs') { $stats.FinalizationMs = $frame.finalizationMs }
            }
            'error' {
                if ($members -contains 'code') { $stats.Errors.Add([string] $frame.code) } else { $stats.Errors.Add('error') }
            }
        }
    }
}

try {
    New-Item -ItemType Directory -Path $dataFolder | Out-Null
    $hostOutput = Join-Path $dataFolder 'host-output.jsonl'
    $hostErrors = Join-Path $dataFolder 'host-errors.txt'

    # 1. The relay, with a pairing and a port of its own.
    $hostProcess = Start-Process -FilePath $RelayHost -ArgumentList $hostArguments -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $hostOutput -RedirectStandardError $hostErrors
    $startedBy = [DateTime]::UtcNow.AddSeconds(60)
    while ($port -eq 0 -and [DateTime]::UtcNow -lt $startedBy) {
        Start-Sleep -Milliseconds 200
        foreach ($line in @(Get-Content -LiteralPath $hostOutput -ErrorAction SilentlyContinue)) {
            try { $hostEvent = $line | ConvertFrom-Json } catch { continue }
            $names = @($hostEvent.PSObject.Properties.Name)
            if ($names -contains 'type' -and $hostEvent.type -eq 'RelayStarted' -and $names -contains 'port') {
                $port = [int] $hostEvent.port
            }
            elseif ($names -contains 'type' -and $hostEvent.type -eq 'Failure' -and $names -contains 'message' -and $failures.Count -eq 0) {
                $failures.Add("the relay did not start: $($hostEvent.message)")
            }
        }
        if ($hostProcess.HasExited -and $port -eq 0) { break }
    }
    if ($port -eq 0 -and $failures.Count -eq 0) { $failures.Add('the relay did not start') }

    if ($failures.Count -eq 0) {
        # 2. The token of that pairing. It stays in this variable: it is not printed and not written to a file.
        $pairing = & $RelayHost --show-pairing --endpoint $Endpoint --data-dir $dataFolder
        foreach ($line in @($pairing)) {
            if ($line -match '^\{"endpoint":"[^"]+","token":"([A-Za-z0-9_-]{43})"\}$') { $token = $Matches[1] }
        }
        $pairing = $null
        if (-not $token) { $failures.Add('the pairing of the temporary relay could not be read') }
    }

    if ($failures.Count -eq 0) {
        $loadedHere = (Get-ModelState) -ne 'ready'

        # 3. The speech socket, as the phone opens it: three headers, no Origin, no subprotocol.
        $socket = New-Object System.Net.WebSockets.ClientWebSocket
        $socket.Options.Proxy = $null
        $socket.Options.SetRequestHeader('Authorization', "Bearer $token")
        $socket.Options.SetRequestHeader('X-LiveCue-Protocol', '2')
        $socket.Options.SetRequestHeader('X-LiveCue-Speech-Model', $Model)
        $connect = $socket.ConnectAsync([Uri] "ws://127.0.0.1:$port/v1/speech", $none)
        $connected = $false
        try { $connected = $connect.Wait(15000) } catch { $connected = $false }
        if (-not $connected) { $failures.Add('the speech socket could not be opened') }
    }

    if ($failures.Count -eq 0) {
        # The model loads; "ready" says when audio may be sent.
        while (-not $stats.Ready -and -not $stats.Closed -and $stats.Errors.Count -eq 0 -and (Get-RemainingMilliseconds) -gt 0) {
            Read-Frames -WaitMilliseconds ([Math]::Min(500, (Get-RemainingMilliseconds)))
        }
        if (-not $stats.Ready) { $failures.Add('no ready frame arrived') }
    }

    if ($failures.Count -eq 0) {
        # The audio, one 80 ms frame every 80 ms, the copies one after the other.
        $audio = $wavAudio.Audio
        $pace = [System.Diagnostics.Stopwatch]::StartNew()
        $frameIndex = 0
        for ($copy = 0; $copy -lt $repeatCount -and -not $stats.Closed -and $stats.Errors.Count -eq 0; $copy++) {
            for ($offset = 0; $offset -lt $audio.Length -and -not $stats.Closed -and $stats.Errors.Count -eq 0; $offset += $FrameBytes) {
                $due = [long] $frameIndex * $FrameMilliseconds
                while ($pace.ElapsedMilliseconds -lt $due) {
                    Read-Frames -WaitMilliseconds ([int] [Math]::Min(20, $due - $pace.ElapsedMilliseconds))
                }
                $count = [Math]::Min($FrameBytes, $audio.Length - $offset)
                $segment = New-Object 'System.ArraySegment[byte]' -ArgumentList $audio, $offset, $count
                if (-not $socket.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Binary, $true, $none).Wait(10000)) {
                    $failures.Add('an audio frame could not be sent')
                    break
                }
                $bytesSent += $count
                $frameIndex++
                Read-Frames -WaitMilliseconds 0
            }
        }

        if (-not $stats.Closed -and $stats.Errors.Count -eq 0 -and $failures.Count -eq 0) {
            $end = [System.Text.Encoding]::UTF8.GetBytes('{"type":"endStream"}')
            $segment = New-Object 'System.ArraySegment[byte]' -ArgumentList (, $end)
            [void] $socket.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $none).Wait(10000)
            while (-not $stats.Complete -and -not $stats.Closed -and $stats.Errors.Count -eq 0 -and (Get-RemainingMilliseconds) -gt 0) {
                Read-Frames -WaitMilliseconds ([Math]::Min(500, (Get-RemainingMilliseconds)))
            }
        }

        # 4. P2-M02 and P2-M03.
        if (-not $stats.Complete) { $failures.Add('no streamComplete frame arrived') }
        elseif ($stats.AudioProcessedMs -ne ($bytesSent / $BytesPerMillisecond)) { $failures.Add('audioProcessedMs is not bytes sent / 32') }
        if ($stats.Transcripts -lt 1) { $failures.Add('no transcript frame with text arrived') }
        if ($stats.Words -lt 1) { $failures.Add('the speechComplete frames hold no word') }
        if (($bytesSent / $BytesPerMillisecond) -gt $LongAudioMilliseconds -and $stats.SpeechCompletes -lt 2) { $failures.Add('audio longer than 32 s gave fewer than 2 speechComplete frames') }
        foreach ($code in $stats.Errors) { $failures.Add("error frame: $code") }
        if ($clock.Elapsed.TotalSeconds -gt $LimitSeconds) { $failures.Add("the run took longer than $LimitSeconds s") }
    }
}
catch {
    $failures.Add("the script failed: $($_.Exception.GetType().Name)")
}
finally {
    # 5. Leave nothing behind: the socket, a model this run loaded, the host, the temporary folder.
    if ($socket) {
        try { $socket.Abort() } catch { }
        $socket.Dispose()
    }
    if ($hostProcess -and -not $hostProcess.HasExited) {
        if ($token -and $port -ne 0 -and $loadedHere -and -not $KeepModel) {
            $stop = Invoke-Relay -Method 'POST' -Path '/v1/local-model/stop' -Body "{`"model`":`"$Model`"}"
            $until = [DateTime]::UtcNow.AddSeconds(90)
            while ($stop -and [DateTime]::UtcNow -lt $until) {
                Start-Sleep -Milliseconds 500
                if ((Get-ModelState) -ne 'stopping') { break }
            }
        }
        try { $hostProcess.Kill() } catch { }
        [void] $hostProcess.WaitForExit(10000)
    }
    $token = $null
    if (Test-Path -LiteralPath $dataFolder) {
        Remove-Item -LiteralPath $dataFolder -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$result = [ordered] @{
    model            = $Model
    wav              = Split-Path -Leaf $Wav
    repeats          = $repeatCount
    bytesSent        = $bytesSent
    audioMs          = [long] ($bytesSent / $BytesPerMillisecond)
    handshakeMs      = $stats.HandshakeMs
    transcripts      = $stats.Transcripts
    speechCompletes  = $stats.SpeechCompletes
    words            = $stats.Words
    audioProcessedMs = $stats.AudioProcessedMs
    finalizationMs   = $stats.FinalizationMs
    elapsedSeconds   = [Math]::Round($clock.Elapsed.TotalSeconds, 1)
    failures         = @($failures)
    result           = $(if ($failures.Count -eq 0) { 'PASS' } else { 'FAIL' })
}
Write-Output ($result | ConvertTo-Json -Compress)
exit $(if ($failures.Count -eq 0) { $ExitPass } else { $ExitFail })
