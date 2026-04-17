<#
.SYNOPSIS
Runs the Architecture.Tests project.
#>

param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

$repositoryRoot = Get-RepositoryRoot
$projectPath = Join-Path -Path $repositoryRoot -ChildPath 'tests/Architecture.Tests/Architecture.Tests.csproj'

dotnet test $projectPath --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Architecture tests failed with exit code $LASTEXITCODE."
}
