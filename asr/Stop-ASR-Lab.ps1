<#
.SYNOPSIS
Unloads the speech model to free the GPU.

.DESCRIPTION
Stops the LiveCue ASR lab container. The container is stopped, not removed;
the images and the model weights are left alone. It refuses to stop a
container that LiveCue did not create, and refuses while a transcription
session is running. If Docker is not running there is nothing to stop, and
the script succeeds without starting it.

It needs nothing from the lab folder, so it takes no -LabFolder: the relay
runs its own copy from its data folder with -Port alone. LabCommon.ps1 is
loaded only from the folder this script is in.

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
