$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

$repositoryRoot = Get-RepositoryRoot
$nugetAllowlistPath = Join-Path -Path $repositoryRoot -ChildPath 'policies/dependency-allowlists/nuget.allowed-packages.txt'
$pnpmAllowlistPath = Join-Path -Path $repositoryRoot -ChildPath 'policies/dependency-allowlists/pnpm.allowed-packages.txt'
$directoryPackagesPath = Join-Path -Path $repositoryRoot -ChildPath 'Directory.Packages.props'
$webPackageJsonPath = Join-Path -Path $repositoryRoot -ChildPath 'web/package.json'

function Read-Allowlist {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -Path $Path -PathType Leaf)) {
        throw "Allowlist file is missing: $Path"
    }

    return @(Get-Content -Path $Path |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and -not $_.StartsWith('#', [System.StringComparison]::Ordinal) } |
            Sort-Object -Unique)
}

function Get-NuGetPackages {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -Path $Path -PathType Leaf)) {
        throw "File is missing: $Path"
    }

    [xml]$xml = Get-Content -Path $Path -Raw
    $packages = @($xml.Project.ItemGroup.PackageVersion | ForEach-Object { $_.Include })
    return @($packages | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
}

function Get-PnpmPackages {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $packageJson = Read-JsonFileAsHashtable -Path $Path
    $packageNames = [System.Collections.Generic.List[string]]::new()

    foreach ($section in @('dependencies', 'devDependencies')) {
        if (-not $packageJson.ContainsKey($section)) {
            continue
        }

        $entries = $packageJson[$section]
        if ($entries -is [hashtable]) {
            foreach ($name in $entries.Keys) {
                $packageNames.Add([string]$name)
            }
        }
    }

    return @($packageNames | Sort-Object -Unique)
}

function Assert-Allowlisted {
    param(
        [Parameter(Mandatory)]
        [string]$Family,

        [Parameter(Mandatory)]
        [string[]]$Declared,

        [Parameter(Mandatory)]
        [string[]]$Allowlisted
    )

    $notAllowlisted = @($Declared | Where-Object { $Allowlisted -notcontains $_ })
    if ($notAllowlisted.Count -gt 0) {
        throw "$Family contains packages not present in the allowlist: $($notAllowlisted -join ', ')"
    }
}

$nugetAllowlist = Read-Allowlist -Path $nugetAllowlistPath
$pnpmAllowlist = Read-Allowlist -Path $pnpmAllowlistPath
$nugetDeclared = Get-NuGetPackages -Path $directoryPackagesPath
$pnpmDeclared = Get-PnpmPackages -Path $webPackageJsonPath

Assert-Allowlisted -Family 'NuGet package set' -Declared $nugetDeclared -Allowlisted $nugetAllowlist
Assert-Allowlisted -Family 'pnpm package set' -Declared $pnpmDeclared -Allowlisted $pnpmAllowlist

Write-Host "Dependency policy validation passed."
Write-Host "NuGet packages validated: $($nugetDeclared.Count)"
Write-Host "pnpm packages validated: $($pnpmDeclared.Count)"
