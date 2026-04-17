<#
.SYNOPSIS
Runs a single CI gate by its id from governance/ci-gates.json.

.DESCRIPTION
Reads the canonical gate manifest at governance/ci-gates.json, locates the
entry whose id matches -Id, and executes its command relative to the
repository root. This is the only sanctioned way for both the GitHub Actions
workflow and the local pre-push script to invoke a gate, ensuring there is
exactly one source of truth for what each gate does.

The architecture test LocalGateParityTests asserts that every substantive
run-step in .github/workflows/ci.yml dispatches through this script.

.PARAMETER Id
The gate id to invoke. Must match a gate.id value in
governance/ci-gates.json.
#>

param(
    [Parameter(Mandatory)]
    [string]$Id
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

$repositoryRoot = Get-RepositoryRoot
$manifestPath = Join-Path -Path $repositoryRoot -ChildPath 'governance/ci-gates.json'
$manifest = Read-JsonFileAsHashtable -Path $manifestPath

$gates = @($manifest['gates'])
$gate = $gates | Where-Object { [string]$_['id'] -eq $Id } | Select-Object -First 1

if ($null -eq $gate) {
    $availableIds = ($gates | ForEach-Object { [string]$_['id'] }) -join ', '
    throw "Unknown CI gate id '$Id'. Known ids: $availableIds."
}

$displayName = [string]$gate['displayName']
$command = [string]$gate['command']
if ([string]::IsNullOrWhiteSpace($command)) {
    throw "Gate '$Id' in '$manifestPath' has no command."
}

Write-Host "==> [$Id] $displayName" -ForegroundColor Cyan

Push-Location -Path $repositoryRoot
try {
    $invocation = "./$command"
    $global:LASTEXITCODE = 0
    Invoke-Expression -Command $invocation
    if ($LASTEXITCODE -ne 0) {
        throw "Gate '$Id' failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

Write-Host "<== [$Id] passed" -ForegroundColor Green
