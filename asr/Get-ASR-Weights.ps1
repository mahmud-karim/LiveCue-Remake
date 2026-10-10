<#
.SYNOPSIS
Downloads the weights of one speech model. Run by hand, once per model.

.DESCRIPTION
Shows which files will be downloaded, from which repository and revision, how
large they are and where they will go, then asks before doing anything. Only
the typed word "yes" starts the download. Nothing else runs this script: not
the application, not a test, not CI.

Files go to private\models\<model>\ in this folder, which git ignores. Each
file is checked against its published size and checksum (see weights.json).
Files that are already present and correct are not downloaded again, and an
interrupted download resumes.

.PARAMETER Model
nemotron or qwen3.

.PARAMETER PlanOnly
Print the explanation and stop. Contacts nothing, downloads nothing.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('nemotron', 'qwen3')][string]$Model,
    [switch]$PlanOnly
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LabCommon.ps1')

Invoke-LabGetWeights -Model $Model.ToLowerInvariant() -PlanOnly:$PlanOnly -LabRoot $PSScriptRoot
