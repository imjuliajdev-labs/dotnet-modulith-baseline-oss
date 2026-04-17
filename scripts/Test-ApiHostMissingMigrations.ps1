param(
    [string]$Configuration = 'Debug',

    [string]$ConnectionString = $env:ConnectionStrings__BaselineDatabase,

    [string]$BaseUrl = 'http://127.0.0.1:5080',

    [int]$StartupTimeoutSeconds = 60,

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw 'ConnectionString is required. Set ConnectionStrings__BaselineDatabase or pass -ConnectionString explicitly.'
}

$repositoryRoot = Get-RepositoryRoot
$projectPath = Join-Path -Path $repositoryRoot -ChildPath 'src/ApiHost/ApiHost.csproj'
$previousConnectionString = $env:ConnectionStrings__BaselineDatabase
$previousAspNetCoreUrls = $env:ASPNETCORE_URLS
$stdoutPath = $null
$stderrPath = $null
$process = $null

function Get-LogTail {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -Path $Path -PathType Leaf)) {
        return ''
    }

    return (Get-Content -Path $Path -Tail 100 | Out-String).TrimEnd()
}

try {
    $env:ConnectionStrings__BaselineDatabase = $ConnectionString
    $env:ASPNETCORE_URLS = $BaseUrl

    $stdoutPath = New-GovernanceScratchFilePath -Purpose 'apihost-missing-migrations' -Suffix 'stdout' -Extension 'log' -RepositoryRoot $repositoryRoot
    $stderrPath = New-GovernanceScratchFilePath -Purpose 'apihost-missing-migrations' -Suffix 'stderr' -Extension 'log' -RepositoryRoot $repositoryRoot

    $dotnetArgs = @(
        'run',
        '--no-launch-profile',
        '--project', $projectPath,
        '--configuration', $Configuration
    )

    if ($NoBuild) {
        $dotnetArgs += '--no-build'
    }

    $process = Start-GovernanceRedirectedProcess -FileName 'dotnet' -ArgumentList $dotnetArgs -WorkingDirectory $repositoryRoot -StdoutPath $stdoutPath -StderrPath $stderrPath

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($StartupTimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($process.HasExited) {
            break
        }

        [void]$process.WaitForExit(250)
    }

    if (-not $process.HasExited) {
        $stdout = Get-LogTail -Path $stdoutPath
        $stderr = Get-LogTail -Path $stderrPath
        throw "ApiHost did not fail fast within $StartupTimeoutSeconds seconds when migrations were missing. Stdout:`n$stdout`nStderr:`n$stderr"
    }

    $stdout = Get-LogTail -Path $stdoutPath
    $stderr = Get-LogTail -Path $stderrPath
    $combinedOutput = @($stdout, $stderr) -join [Environment]::NewLine

    if ($process.ExitCode -eq 0) {
        throw "ApiHost exited successfully even though migrations were missing. Stdout:`n$stdout`nStderr:`n$stderr"
    }

    if ($combinedOutput -notmatch 'Run DbMigrator before starting the host\.') {
        throw "ApiHost failed, but the expected missing-migration guidance was not present. Stdout:`n$stdout`nStderr:`n$stderr"
    }

    Write-Host 'ApiHost failed fast with the expected missing-migration guidance.'
}
finally {
    if ($null -ne $process) {
        if (-not $process.HasExited) {
            try { $process.Kill($true) } catch { }
            try { $process.WaitForExit() } catch { }
        }

        try { $process.Dispose() } catch { }
    }

    $env:ConnectionStrings__BaselineDatabase = $previousConnectionString
    $env:ASPNETCORE_URLS = $previousAspNetCoreUrls

    foreach ($path in @($stdoutPath, $stderrPath)) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -Path $path -PathType Leaf)) {
            Remove-Item -Path $path -Force
        }
    }
}