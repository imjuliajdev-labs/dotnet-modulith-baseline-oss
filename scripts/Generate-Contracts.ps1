param(
    [string]$Configuration = 'Debug',

    [string]$ConnectionString = $env:ConnectionStrings__BaselineDatabase,

    [string]$BaseUrl = 'https://localhost:5082',

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
$invokeMigratorScriptPath = Join-Path -Path $PSScriptRoot -ChildPath 'Invoke-DbMigrator.ps1'
$openApiOutputPath = Join-Path -Path $repositoryRoot -ChildPath 'contracts/http/openapi.v1.json'
$generatedClientPath = Join-Path -Path $repositoryRoot -ChildPath 'web/src/shared/api/generated/contracts.generated.ts'
$validateCompatibilityScriptPath = Join-Path -Path $PSScriptRoot -ChildPath 'Validate-OpenApiCompatibility.ps1'
$previousConnectionString = $env:ConnectionStrings__BaselineDatabase
$previousAspNetCoreUrls = $env:ASPNETCORE_URLS
$previousCertificatePath = $env:ASPNETCORE_Kestrel__Certificates__Default__Path
$previousCertificatePassword = $env:ASPNETCORE_Kestrel__Certificates__Default__Password
$previousOpenApiSnapshotPath = $null
$stdoutPath = $null
$stderrPath = $null
$process = $null
$httpsCertificate = $null

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

function Test-GitRepository {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $git) {
        return $false
    }

    Push-Location $repositoryRoot
    try {
        git rev-parse --is-inside-work-tree *> $null
        return $LASTEXITCODE -eq 0
    }
    finally {
        Pop-Location
    }
}

try {
    if (Test-GitRepository) {
        Push-Location $repositoryRoot
        try {
            git cat-file -e HEAD:contracts/http/openapi.v1.json 2>$null
            if ($LASTEXITCODE -eq 0) {
                $previousOpenApiSnapshotPath = New-GovernanceScratchFilePath -Purpose 'generate-contracts' -Suffix 'openapi-head' -Extension 'json' -RepositoryRoot $repositoryRoot
                git show HEAD:contracts/http/openapi.v1.json | Set-Content -Path $previousOpenApiSnapshotPath -NoNewline
            }
        }
        finally {
            Pop-Location
        }
    }

    if ([string]::IsNullOrWhiteSpace($previousOpenApiSnapshotPath) -and (Test-Path -Path $openApiOutputPath -PathType Leaf)) {
        $previousOpenApiSnapshotPath = New-GovernanceScratchFilePath -Purpose 'generate-contracts' -Suffix 'openapi-working' -Extension 'json' -RepositoryRoot $repositoryRoot
        Copy-Item -Path $openApiOutputPath -Destination $previousOpenApiSnapshotPath -Force
    }

    & $invokeMigratorScriptPath -Configuration $Configuration -ConnectionString $ConnectionString -NoBuild:$NoBuild

    $env:ConnectionStrings__BaselineDatabase = $ConnectionString
    $env:ASPNETCORE_URLS = $BaseUrl

    if (Test-UsesHttpsUrl -Url $BaseUrl) {
        $httpsCertificate = New-EphemeralHttpsCertificate -Purpose 'generate-contracts'
        $env:ASPNETCORE_Kestrel__Certificates__Default__Path = $httpsCertificate.Path
        $env:ASPNETCORE_Kestrel__Certificates__Default__Password = $httpsCertificate.Password
    }

    $stdoutPath = New-GovernanceScratchFilePath -Purpose 'generate-contracts' -Suffix 'stdout' -Extension 'log' -RepositoryRoot $repositoryRoot
    $stderrPath = New-GovernanceScratchFilePath -Purpose 'generate-contracts' -Suffix 'stderr' -Extension 'log' -RepositoryRoot $repositoryRoot

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

    $healthEndpoint = '{0}/health' -f $BaseUrl.TrimEnd('/')
    $openApiEndpoint = '{0}/openapi/v1.json' -f $BaseUrl.TrimEnd('/')
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($StartupTimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($process.HasExited) {
            $stdout = Get-LogTail -Path $stdoutPath
            $stderr = Get-LogTail -Path $stderrPath
            throw "ApiHost exited before contract export completed. Stdout:`n$stdout`nStderr:`n$stderr"
        }

        try {
            $healthRequestParameters = @{
                Uri = $healthEndpoint
                Method = 'Get'
                TimeoutSec = 5
            }

            if (Test-UsesHttpsUrl -Url $BaseUrl) {
                $healthRequestParameters['SkipCertificateCheck'] = $true
            }

            $response = Invoke-WebRequest @healthRequestParameters
            if ($response.StatusCode -eq 200) {
                $openApiRequestParameters = @{
                    Uri = $openApiEndpoint
                    Method = 'Get'
                    OutFile = $openApiOutputPath
                    TimeoutSec = 10
                }

                if (Test-UsesHttpsUrl -Url $BaseUrl) {
                    $openApiRequestParameters['SkipCertificateCheck'] = $true
                }

                Invoke-WebRequest @openApiRequestParameters
                break
            }
        }
        catch {
        }

        [void]$process.WaitForExit(250)
    }

    if (-not (Test-Path -Path $openApiOutputPath -PathType Leaf)) {
        $stdout = Get-LogTail -Path $stdoutPath
        $stderr = Get-LogTail -Path $stderrPath
        throw "OpenAPI export did not complete within $StartupTimeoutSeconds seconds. Stdout:`n$stdout`nStderr:`n$stderr"
    }

    Push-Location (Join-Path -Path $repositoryRoot -ChildPath 'web')
    try {
        corepack enable | Out-Null
        pnpm install --frozen-lockfile
        pnpm generate:contracts
    }
    finally {
        Pop-Location
    }

    if (-not (Test-Path -Path $generatedClientPath -PathType Leaf)) {
        throw 'TypeScript contract generation did not produce the expected output file.'
    }

    if (-not [string]::IsNullOrWhiteSpace($previousOpenApiSnapshotPath) -and (Test-Path -Path $previousOpenApiSnapshotPath -PathType Leaf)) {
        & $validateCompatibilityScriptPath -BaselinePath $previousOpenApiSnapshotPath -CandidatePath $openApiOutputPath
    }

    if (Test-GitRepository) {
        Push-Location $repositoryRoot
        try {
            git diff --exit-code -- contracts/http/openapi.v1.json web/src/shared/api/generated/contracts.generated.ts

            if ($LASTEXITCODE -ne 0) {
                throw 'Generated contract artifacts changed. Review and commit contracts/http/openapi.v1.json and web/src/shared/api/generated/contracts.generated.ts.'
            }
        }
        finally {
            Pop-Location
        }
    }

    Write-Host 'Generated OpenAPI snapshot and refreshed TypeScript contracts.'
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
    $env:ASPNETCORE_Kestrel__Certificates__Default__Path = $previousCertificatePath
    $env:ASPNETCORE_Kestrel__Certificates__Default__Password = $previousCertificatePassword

    $cleanupPaths = @($stdoutPath, $stderrPath, $previousOpenApiSnapshotPath)
    if ($null -ne $httpsCertificate) {
        $cleanupPaths += $httpsCertificate.Path
    }

    foreach ($path in $cleanupPaths) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -Path $path -PathType Leaf)) {
            try {
                Remove-Item -Path $path -Force
            }
            catch {
                Write-Warning "Could not remove temporary file '$path': $($_.Exception.Message)"
            }
        }
    }
}