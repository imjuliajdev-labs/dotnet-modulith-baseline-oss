<#
.SYNOPSIS
Runs the Integration.Tests project.
#>

param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

$repositoryRoot = Get-RepositoryRoot
$projectPath = Join-Path -Path $repositoryRoot -ChildPath 'tests/Integration.Tests/Integration.Tests.csproj'

Assert-TestcontainersDockerReady -Context 'Integration tests' -RetryScript 'scripts/Invoke-IntegrationTests.ps1' -AttemptAutoStart

dotnet test $projectPath --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Integration tests failed with exit code $LASTEXITCODE."
}
