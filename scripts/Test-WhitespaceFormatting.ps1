<#
.SYNOPSIS
Verifies that the backend solution has no whitespace formatting drift.
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

dotnet format whitespace --verify-no-changes --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet format whitespace --verify-no-changes failed with exit code $LASTEXITCODE."
}
