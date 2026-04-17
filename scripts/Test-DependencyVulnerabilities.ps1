<#
.SYNOPSIS
Fails if the backend solution depends on any package with a known vulnerability.

.DESCRIPTION
Runs `dotnet list package --vulnerable --include-transitive` over the
restored solution and inspects the output. Any line matching a vulnerable
package row aborts the run. The CI workflow and the local gate runner both
dispatch this script through scripts/Invoke-CiGate.ps1.
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

dotnet restore | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

$output = dotnet list package --vulnerable --include-transitive 2>&1 | Out-String
Write-Host $output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet list package --vulnerable failed with exit code $LASTEXITCODE."
}

if ($output -match '>\s*\S+\s+\S+\s+\S+\s+\S+') {
    throw "Vulnerable packages detected. See output above."
}
