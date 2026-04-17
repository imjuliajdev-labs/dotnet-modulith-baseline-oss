param(
    [string]$Configuration = 'Debug',

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

$repositoryRoot = Get-RepositoryRoot
$testProjectPath = Join-Path -Path $repositoryRoot -ChildPath 'tests/Architecture.Tests/Architecture.Tests.csproj'
$artifactDirectory = Join-Path -Path $repositoryRoot -ChildPath 'artifacts'
$artifactPath = Join-Path -Path $artifactDirectory -ChildPath 'handler-coverage.json'

if (-not (Test-Path -Path $testProjectPath -PathType Leaf)) {
    throw "Architecture test project not found at '$testProjectPath'."
}

if (-not (Test-Path -Path $artifactDirectory -PathType Container)) {
    New-Item -Path $artifactDirectory -ItemType Directory -Force | Out-Null
}

$dotnetArgs = @(
    'test',
    $testProjectPath,
    '--configuration', $Configuration,
    '--filter', 'FullyQualifiedName~HandlerTestCoverage',
    '--nologo'
)

if ($NoBuild) {
    $dotnetArgs += '--no-build'
}

Write-Host "Running handler coverage guardrail tests to refresh '$artifactPath'..."

& dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) {
    throw "Handler coverage guardrail tests failed (exit code $LASTEXITCODE)."
}

if (-not (Test-Path -Path $artifactPath -PathType Leaf)) {
    throw "Handler coverage tests completed but '$artifactPath' was not produced."
}

$report = Get-Content -Path $artifactPath -Raw | ConvertFrom-Json

$totalHandlers = [int]$report.totalHandlers
$testedCount = @($report.tested).Count
$exemptedCount = @($report.exempted).Count

Write-Host ""
Write-Host "Handler coverage report: $artifactPath"
Write-Host ("  generatedAt    : {0}" -f $report.generatedAt)
Write-Host ("  totalHandlers  : {0}" -f $totalHandlers)
Write-Host ("  tested         : {0}" -f $testedCount)
Write-Host ("  exempted       : {0}" -f $exemptedCount)
