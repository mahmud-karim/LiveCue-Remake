<#
.SYNOPSIS
Builds the Docker images for the LiveCue ASR lab. Run by hand, once.

.DESCRIPTION
Explains what will be built and downloaded, from where and roughly how large,
then asks before doing anything. Only the typed word "yes" starts the build.
Nothing else runs this script: not the application, not a test, not CI.

Docker Desktop must already be running. This script never starts it.

.PARAMETER Model
nemotron, qwen3, or all (default).

.PARAMETER PlanOnly
Print the explanation and stop. Contacts nothing, builds nothing.
#>
[CmdletBinding()]
param(
    [ValidateSet('nemotron', 'qwen3', 'all')][string]$Model = 'all',
    [switch]$PlanOnly
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LabCommon.ps1')

Invoke-LabBuildImages -Model $Model.ToLowerInvariant() -PlanOnly:$PlanOnly -LabRoot $PSScriptRoot
