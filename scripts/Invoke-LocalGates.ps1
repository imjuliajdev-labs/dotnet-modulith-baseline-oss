<#
.SYNOPSIS
Runs every CI gate locally in the same order as .github/workflows/ci.yml.

.DESCRIPTION
This is the authoritative local pre-push gate runner. It reads the canonical
gate manifest at governance/ci-gates.json and dispatches each gate through
scripts/Invoke-CiGate.ps1. The first failing gate stops the run unless
-ContinueOnError is supplied. Gates flagged skipLocally are skipped with a
notice unless -IncludeSkipped is supplied.

The script does no setup of its own beyond setting the working directory to
the repository root: developers are expected to have .NET 10, Node.js 22,
pnpm, and (for the frontend e2e gate) Playwright Chromium already installed.

.PARAMETER Only
Restricts the run to a comma-separated list of gate ids. Useful for re-running
a single gate after a fix.

.PARAMETER From
Starts the run at the named gate id, skipping all earlier gates.

.PARAMETER ContinueOnError
Continue running gates after the first failure. The script still exits with a
non-zero code if any gate failed.

.PARAMETER IncludeSkipped
Run gates that declare skipLocally: true. Use when the local environment can
satisfy their preconditions (Postgres on localhost, Playwright installed,
etc.).

.PARAMETER List
List the gates that would run and exit without executing anything.
#>

[CmdletBinding()]
param(
    [string[]]$Only,
    [string]$From,
    [switch]$ContinueOnError,
    [switch]$IncludeSkipped,
    [switch]$List
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

# CI-parity default for the shared-runtime connection string. Several gates
# (dbmigrator-smoke, apihost-*-smoke, contract-generation-and-compatibility,
# integration-tests) assume a live Postgres reachable via this env var, matching
# the CI workflow's service-container configuration. If the developer already
# exported ConnectionStrings__BaselineDatabase we leave it alone; otherwise we
# fill in the canonical localhost:5432 shape used by docker-compose and CI so
# the local run reaches the same gates the workflow does. Developers without a
# local Postgres listener will still see a clear connection failure; they can
# override with their own value before invoking the runner.
if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__BaselineDatabase)) {
    $env:ConnectionStrings__BaselineDatabase = 'Host=127.0.0.1;Port=5432;Database=baseline;Username=postgres;Password=postgres'
    Write-Host "ConnectionStrings__BaselineDatabase not set; defaulting to local Postgres on 127.0.0.1:5432." -ForegroundColor DarkGray
}

$repositoryRoot = Get-RepositoryRoot
$manifestPath = Join-Path -Path $repositoryRoot -ChildPath 'governance/ci-gates.json'
$manifest = Read-JsonFileAsHashtable -Path $manifestPath
$dispatcher = Join-Path -Path $repositoryRoot -ChildPath 'scripts/Invoke-CiGate.ps1'

$allGates = @($manifest['gates'])
$allGateIds = @($allGates | ForEach-Object { [string]$_['id'] })

$selectedGates = $allGates

if ($PSBoundParameters.ContainsKey('From') -and -not [string]::IsNullOrWhiteSpace($From)) {
    $startIndex = [Array]::IndexOf($allGateIds, $From)
    if ($startIndex -lt 0) {
        throw "Unknown gate id '$From'. Known ids: $($allGateIds -join ', ')."
    }

    $selectedGates = $allGates[$startIndex..($allGates.Count - 1)]
}

if ($PSBoundParameters.ContainsKey('Only') -and $Only -and $Only.Count -gt 0) {
    $unknown = @($Only | Where-Object { $allGateIds -notcontains $_ })
    if ($unknown.Count -gt 0) {
        throw "Unknown gate ids: $($unknown -join ', '). Known ids: $($allGateIds -join ', ')."
    }

    $selectedGates = $selectedGates | Where-Object { $Only -contains [string]$_['id'] }
}

if ($List) {
    Write-Host "Local gate plan:" -ForegroundColor Cyan
    foreach ($gate in $selectedGates) {
        $skipLocally = $false
        if ($gate.ContainsKey('skipLocally')) {
            $skipLocally = [bool]$gate['skipLocally']
        }

        $marker = if ($skipLocally -and -not $IncludeSkipped) { '[skip]' } else { '[run] ' }
        Write-Host ("  {0} {1,-42} {2}" -f $marker, [string]$gate['id'], [string]$gate['displayName'])
    }

    return
}

$failed = [System.Collections.Generic.List[string]]::new()
$skipped = [System.Collections.Generic.List[string]]::new()
$ran = [System.Collections.Generic.List[string]]::new()

foreach ($gate in $selectedGates) {
    $gateId = [string]$gate['id']
    $skipLocally = $false
    if ($gate.ContainsKey('skipLocally')) {
        $skipLocally = [bool]$gate['skipLocally']
    }

    if ($skipLocally -and -not $IncludeSkipped) {
        $reason = if ($gate.ContainsKey('skipReason')) { [string]$gate['skipReason'] } else { 'skipLocally is set' }
        Write-Host ("-- [{0}] skipped locally: {1}" -f $gateId, $reason) -ForegroundColor DarkYellow
        [void]$skipped.Add($gateId)
        continue
    }

    try {
        & $dispatcher -Id $gateId
        if ($LASTEXITCODE -ne 0) {
            throw "Gate exited with code $LASTEXITCODE."
        }

        [void]$ran.Add($gateId)
    }
    catch {
        Write-Host ("xx [{0}] FAILED: {1}" -f $gateId, $_.Exception.Message) -ForegroundColor Red
        [void]$failed.Add($gateId)
        if (-not $ContinueOnError) {
            break
        }
    }
}

Write-Host ""
Write-Host "Local gate run summary:" -ForegroundColor Cyan
Write-Host ("  passed:  {0}" -f $ran.Count)
Write-Host ("  skipped: {0}" -f $skipped.Count)
Write-Host ("  failed:  {0}" -f $failed.Count)

if ($failed.Count -gt 0) {
    Write-Host ("Failed gates: {0}" -f ($failed -join ', ')) -ForegroundColor Red
    exit 1
}
