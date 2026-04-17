param(
    [string]$Configuration = 'Debug',

    [string]$ConnectionString = $env:ConnectionStrings__BaselineDatabase,

    [switch]$NoBuild,

    [Parameter(ValueFromRemainingArguments)]
    [string[]]$MigratorArgs = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

$repositoryRoot = Get-RepositoryRoot
$projectPath = Join-Path -Path $repositoryRoot -ChildPath 'src/Tools/DbMigrator/DbMigrator.csproj'
$previousConnectionString = $env:ConnectionStrings__BaselineDatabase

try {
    if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
        $env:ConnectionStrings__BaselineDatabase = $ConnectionString
    }

    $dotnetArgs = @(
        'run',
        '--project', $projectPath,
        '--configuration', $Configuration
    )

    if ($NoBuild) {
        $dotnetArgs += '--no-build'
    }

    if ($MigratorArgs.Count -gt 0) {
        $dotnetArgs += '--'
        $dotnetArgs += $MigratorArgs
    }

    & dotnet @dotnetArgs
    if ($LASTEXITCODE -ne 0) {
        throw "DbMigrator exited with code $LASTEXITCODE."
    }
}
finally {
    $env:ConnectionStrings__BaselineDatabase = $previousConnectionString
}