<#
.SYNOPSIS
Runs every backend *.UnitTests.csproj project in the repository.
#>

param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

$repositoryRoot = Get-RepositoryRoot
$testsRoot = Join-Path -Path $repositoryRoot -ChildPath 'tests'

$projects = @(Get-ChildItem -Path $testsRoot -Recurse -Filter '*.UnitTests.csproj' -File |
    Sort-Object FullName)

if ($projects.Count -eq 0) {
    throw "No backend unit-test projects found under '$testsRoot'."
}

foreach ($project in $projects) {
    Write-Host "Running unit tests: $($project.FullName)"
    dotnet test $project.FullName --configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Unit tests failed for '$($project.FullName)' with exit code $LASTEXITCODE."
    }
}
