param(
    [string]$Configuration = 'Debug',

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

$repositoryRoot = Get-RepositoryRoot
$startScriptPath = Join-Path -Path $PSScriptRoot -ChildPath 'Start-ApiHost.ps1'
$previousConnectionString = $env:ConnectionStrings__BaselineDatabase

try {
    Remove-Item Env:ConnectionStrings__BaselineDatabase -ErrorAction SilentlyContinue

    $errorMessage = $null

    try {
        & $startScriptPath -Configuration $Configuration -NoBuild:$NoBuild | Out-Null
        throw 'Start-ApiHost unexpectedly succeeded without ConnectionStrings__BaselineDatabase.'
    }
    catch {
        $errorMessage = $_.Exception.Message
    }

    $expectedMessage = 'ConnectionString is required. Set ConnectionStrings__BaselineDatabase or pass -ConnectionString explicitly.'
    if (-not [string]::Equals($errorMessage, $expectedMessage, [System.StringComparison]::Ordinal)) {
        throw "Start-ApiHost failed, but the expected missing-configuration guidance was not present. Actual message: $errorMessage"
    }

    Write-Host 'ApiHost bootstrap script failed fast with the expected missing-configuration guidance.'
}
finally {
    $env:ConnectionStrings__BaselineDatabase = $previousConnectionString
}