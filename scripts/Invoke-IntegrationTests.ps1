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

function Assert-DockerAvailableForIntegrationTests {
    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if ($null -eq $docker) {
        throw "Integration tests require Docker because the suite boots PostgreSQL through Testcontainers. Install Docker Desktop/Engine and ensure 'docker info' succeeds before rerunning scripts/Invoke-IntegrationTests.ps1."
    }

    & docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Integration tests require a running Docker daemon because the suite boots PostgreSQL through Testcontainers. Start Docker and ensure 'docker info' succeeds before rerunning scripts/Invoke-IntegrationTests.ps1."
    }
}

$repositoryRoot = Get-RepositoryRoot
$projectPath = Join-Path -Path $repositoryRoot -ChildPath 'tests/Integration.Tests/Integration.Tests.csproj'

Assert-DockerAvailableForIntegrationTests

dotnet test $projectPath --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Integration tests failed with exit code $LASTEXITCODE."
}
