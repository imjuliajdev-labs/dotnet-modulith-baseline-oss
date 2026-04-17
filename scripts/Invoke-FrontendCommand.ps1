<#
.SYNOPSIS
Runs a single pnpm command inside the web/ workspace.

.PARAMETER Command
The pnpm script name (lint, typecheck, build, test, test:e2e, ...).
#>

param(
    [Parameter(Mandatory)]
    [string]$Command
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

$repositoryRoot = Get-RepositoryRoot
$webRoot = Join-Path -Path $repositoryRoot -ChildPath 'web'

if (-not (Test-Path -Path $webRoot -PathType Container)) {
    throw "Frontend workspace '$webRoot' does not exist."
}

Push-Location -Path $webRoot
try {
    pnpm $Command
    if ($LASTEXITCODE -ne 0) {
        throw "pnpm $Command failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
