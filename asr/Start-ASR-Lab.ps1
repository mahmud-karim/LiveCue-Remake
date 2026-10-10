<#
.SYNOPSIS
Loads one speech model on the PC's GPU, replacing whichever model is loaded.

.DESCRIPTION
Starts the LiveCue ASR lab container for the chosen model and waits until the
service reports that it is ready. The relay runs this script when a paired
iPhone asks for a PC speech model; it can also be run by hand.

It never downloads anything and never builds an image: run
Build-ASR-Images.ps1 and Get-ASR-Weights.ps1 first.

The relay does not run this file where it lies. It runs its own copy from its
data folder and names the lab folder with -LabFolder. So this script takes
nothing from the lab folder but data (the weights and the service code, which
the container reads), and loads LabCommon.ps1 only from the folder it is in.

.PARAMETER Model
nemotron (default) or qwen3.

.PARAMETER Port
The lab's port on this PC's loopback address (default 47842).

.PARAMETER NoBrowser
Do not open the lab test page when the model is ready.

.PARAMETER EnsureDocker
If Docker's engine is not running, start Docker Desktop (after checking that
it is the genuine, signed Docker Desktop) and wait for it.

.PARAMETER LabFolder
The lab folder: the one that holds private\models\<model> and the service
code. Default: the folder this script is in.
#>
[CmdletBinding()]
param(
    [ValidateSet('nemotron', 'qwen3')][string]$Model = 'nemotron',
    [ValidateRange(1024, 65535)][int]$Port = 47842,
    [switch]$NoBrowser,
    [switch]$EnsureDocker,
    [ValidateNotNullOrEmpty()][string]$LabFolder
)
$ErrorActionPreference = 'Stop'
# Always from beside this script, never from -LabFolder.
. (Join-Path $PSScriptRoot 'LabCommon.ps1')
# Set here and not as the parameter's default: started with -File, Windows
# PowerShell 5.1 can work out the defaults before it knows $PSScriptRoot.
if (-not $PSBoundParameters.ContainsKey('LabFolder')) { $LabFolder = $PSScriptRoot }

Invoke-LabStart -Model $Model.ToLowerInvariant() -Port $Port -NoBrowser:$NoBrowser `
    -EnsureDocker:$EnsureDocker -LabRoot (Resolve-LabFolder $LabFolder)
