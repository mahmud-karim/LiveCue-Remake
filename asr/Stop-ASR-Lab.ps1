<#
.SYNOPSIS
Unloads the speech model to free the GPU.

.DESCRIPTION
Stops the LiveCue ASR lab container. The container is stopped, not removed;
the images and the model weights are left alone. It refuses to stop a
container that LiveCue did not create, and refuses while a transcription
session is running. If Docker is not running there is nothing to stop, and
the script succeeds without starting it.

.PARAMETER Port
The lab's port on this PC's loopback address (default 47842).
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$Port = 47842
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LabCommon.ps1')

Invoke-LabStop -Port $Port
