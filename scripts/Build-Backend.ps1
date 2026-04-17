<#
.SYNOPSIS
Restores and builds the backend solution.

.DESCRIPTION
Wraps `dotnet restore` followed by `dotnet build` so the operation can be
dispatched as a single CI gate via governance/ci-gates.json. Failing on either
restore or build halts the gate.
#>

param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Write-Host "Restoring backend packages..."
dotnet restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

Write-Host "Building backend in '$Configuration' configuration..."
dotnet build --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}
