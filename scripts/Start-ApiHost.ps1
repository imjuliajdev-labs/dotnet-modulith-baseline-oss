param(
    [string]$Configuration = 'Debug',

    [string]$ConnectionString = $env:ConnectionStrings__BaselineDatabase,

    [switch]$NoBuild,

    [switch]$SkipMigrator,

    [Parameter(ValueFromRemainingArguments)]
    [string[]]$ApiHostArgs = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

$repositoryRoot = Get-RepositoryRoot
$projectPath = Join-Path -Path $repositoryRoot -ChildPath 'src/ApiHost/ApiHost.csproj'
$previousConnectionString = $env:ConnectionStrings__BaselineDatabase
$previousCertificatePath = $env:ASPNETCORE_Kestrel__Certificates__Default__Path
$previousCertificatePassword = $env:ASPNETCORE_Kestrel__Certificates__Default__Password
$httpsCertificate = $null

try {
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        throw 'ConnectionString is required. Set ConnectionStrings__BaselineDatabase or pass -ConnectionString explicitly.'
    }

    if (-not $SkipMigrator) {
        & (Join-Path -Path $PSScriptRoot -ChildPath 'Invoke-DbMigrator.ps1') -Configuration $Configuration -ConnectionString $ConnectionString -NoBuild:$NoBuild
    }

    $env:ConnectionStrings__BaselineDatabase = $ConnectionString

    if (-not [string]::IsNullOrWhiteSpace($env:ASPNETCORE_URLS) -and (Test-UsesHttpsBinding -Urls $env:ASPNETCORE_URLS)) {
        $httpsCertificate = New-EphemeralHttpsCertificate -Purpose 'start-apihost'
        $env:ASPNETCORE_Kestrel__Certificates__Default__Path = $httpsCertificate.Path
        $env:ASPNETCORE_Kestrel__Certificates__Default__Password = $httpsCertificate.Password
    }

    $dotnetArgs = @(
        'run',
        '--no-launch-profile',
        '--project', $projectPath,
        '--configuration', $Configuration
    )

    if ($NoBuild) {
        $dotnetArgs += '--no-build'
    }

    if ($ApiHostArgs.Count -gt 0) {
        $dotnetArgs += '--'
        $dotnetArgs += $ApiHostArgs
    }

    & dotnet @dotnetArgs
    if ($LASTEXITCODE -ne 0) {
        throw "ApiHost exited with code $LASTEXITCODE."
    }
}
finally {
    $env:ConnectionStrings__BaselineDatabase = $previousConnectionString
    $env:ASPNETCORE_Kestrel__Certificates__Default__Path = $previousCertificatePath
    $env:ASPNETCORE_Kestrel__Certificates__Default__Password = $previousCertificatePassword

    if ($null -ne $httpsCertificate -and (Test-Path -Path $httpsCertificate.Path -PathType Leaf)) {
        Remove-Item -Path $httpsCertificate.Path -Force
    }
}