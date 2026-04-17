param(
    [string]$Configuration = 'Debug',

    [string]$ConnectionString = $env:ConnectionStrings__BaselineDatabase,

    [string]$BaseUrl = 'https://localhost:5079',

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
$previousConnectionString = $env:ConnectionStrings__BaselineDatabase
$previousAspNetCoreUrls = $env:ASPNETCORE_URLS
$previousCertificatePath = $env:ASPNETCORE_Kestrel__Certificates__Default__Path
$previousCertificatePassword = $env:ASPNETCORE_Kestrel__Certificates__Default__Password
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

function Invoke-EndpointSmokeChecks {
    param(
        [Parameter(Mandatory)]
        [string]$BaseUrl
    )

    $trimmedBaseUrl = $BaseUrl.TrimEnd('/')
    $skipCertificateCheck = Test-UsesHttpsUrl -Url $trimmedBaseUrl

    $hostStatusParameters = @{
        Uri = "$trimmedBaseUrl/_host/status"
        Method = 'Get'
        TimeoutSec = 5
    }

    if ($skipCertificateCheck) {
        $hostStatusParameters['SkipCertificateCheck'] = $true
    }

    $hostStatus = Invoke-RestMethod @hostStatusParameters
    if ($hostStatus.status -ne 'bootstrap') {
        throw "Host status endpoint returned an unexpected status payload. Expected 'bootstrap', received '$($hostStatus.status)'."
    }

    $manifestParameters = @{
        Uri = "$trimmedBaseUrl/api/v1/platform/bootstrap"
        Method = 'Get'
        TimeoutSec = 5
    }

    if ($skipCertificateCheck) {
        $manifestParameters['SkipCertificateCheck'] = $true
    }

    $manifest = Invoke-RestMethod @manifestParameters
    if ($null -eq $manifest.modules -or $manifest.modules.Count -lt 4) {
        throw 'Platform bootstrap endpoint did not return the expected module manifest.'
    }

    $moduleKeys = @($manifest.modules | ForEach-Object { $_.key })
    foreach ($requiredModuleKey in @('platform', 'identity', 'admin', 'sample-feature')) {
        if ($moduleKeys -notcontains $requiredModuleKey) {
            throw "Platform bootstrap endpoint is missing module '$requiredModuleKey'."
        }
    }

    $antiforgeryParameters = @{
        Uri = "$trimmedBaseUrl/api/v1/identity/antiforgery"
        Method = 'Get'
        TimeoutSec = 5
    }

    if ($skipCertificateCheck) {
        $antiforgeryParameters['SkipCertificateCheck'] = $true
    }

    $antiforgery = Invoke-RestMethod @antiforgeryParameters
    if ([string]::IsNullOrWhiteSpace($antiforgery.headerName)) {
        throw 'Identity antiforgery endpoint did not return a header name.'
    }

    if ([string]::IsNullOrWhiteSpace($antiforgery.requestToken)) {
        throw 'Identity antiforgery endpoint did not return a request token.'
    }

    Write-Host 'Verified representative ApiHost endpoints: /_host/status, /api/v1/platform/bootstrap, /api/v1/identity/antiforgery.'
}

try {
    & $invokeMigratorScriptPath -Configuration $Configuration -ConnectionString $ConnectionString -NoBuild:$NoBuild

    $env:ConnectionStrings__BaselineDatabase = $ConnectionString
    $env:ASPNETCORE_URLS = $BaseUrl

    if (Test-UsesHttpsUrl -Url $BaseUrl) {
        $httpsCertificate = New-EphemeralHttpsCertificate -Purpose 'bootstrap-smoke'
        $env:ASPNETCORE_Kestrel__Certificates__Default__Path = $httpsCertificate.Path
        $env:ASPNETCORE_Kestrel__Certificates__Default__Password = $httpsCertificate.Password
    }

    $stdoutPath = New-GovernanceScratchFilePath -Purpose 'apihost-bootstrap' -Suffix 'stdout' -Extension 'log' -RepositoryRoot $repositoryRoot
    $stderrPath = New-GovernanceScratchFilePath -Purpose 'apihost-bootstrap' -Suffix 'stderr' -Extension 'log' -RepositoryRoot $repositoryRoot

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
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($StartupTimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($process.HasExited) {
            $stdout = Get-LogTail -Path $stdoutPath
            $stderr = Get-LogTail -Path $stderrPath
            throw "ApiHost exited before reporting healthy status. Stdout:`n$stdout`nStderr:`n$stderr"
        }

        $response = $null

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
        }
        catch {
        }

        if ($null -ne $response -and $response.StatusCode -eq 200) {
            Write-Host "ApiHost reported healthy status at $healthEndpoint."
            Invoke-EndpointSmokeChecks -BaseUrl $BaseUrl
            return
        }

        [void]$process.WaitForExit(250)
    }

    $stdout = Get-LogTail -Path $stdoutPath
    $stderr = Get-LogTail -Path $stderrPath
    throw "ApiHost did not report healthy status within $StartupTimeoutSeconds seconds. Stdout:`n$stdout`nStderr:`n$stderr"
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

    $cleanupPaths = @($stdoutPath, $stderrPath)
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