<#
.SYNOPSIS
Publishes a clean OSS release from this private source repo into the clean public repo clone.

.DESCRIPTION
Runs from the private source repository. The script exports a tracked-files-only
snapshot of the current HEAD via git archive, refreshes the public repo clone,
validates the public cut, creates and pushes a private source tag, then commits,
tags, and pushes the public OSS release.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [string]$PublicClonePath,

    [ValidateSet('none', 'fast', 'full')]
    [string]$ValidationProfile = 'fast',

    [string]$PublicRemoteName = 'origin',

    [string]$PrivateTagPrefix = 'private-v',

    [string]$PublicTagPrefix = 'v',

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $true

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

function Assert-CommandExists {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' is not available in PATH."
    }
}

function Assert-GitRepositoryPath {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$Description
    )

    if (-not (Test-Path -Path $RepositoryRoot -PathType Container)) {
        throw "$Description '$RepositoryRoot' does not exist."
    }

    if (-not (Test-Path -Path (Join-Path -Path $RepositoryRoot -ChildPath '.git'))) {
        throw "$Description '$RepositoryRoot' is not a git repository root."
    }
}

function Invoke-GitTextCapture {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [string]$FailureMessage = 'Git command failed.'
    )

    Push-Location $RepositoryRoot
    try {
        $previousNativePreference = $PSNativeCommandUseErrorActionPreference
        $script:PSNativeCommandUseErrorActionPreference = $false
        try {
            $output = & git @Arguments 2>&1
            if ($LASTEXITCODE -ne 0) {
                throw $FailureMessage
            }

            return @($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        }
        finally {
            $script:PSNativeCommandUseErrorActionPreference = $previousNativePreference
        }
    }
    finally {
        Pop-Location
    }
}

function Test-GitTagExistsLocally {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$TagName
    )

    $matches = @(Invoke-GitTextCapture -RepositoryRoot $RepositoryRoot -Arguments @('tag', '--list', $TagName))
    return $matches.Count -gt 0
}

function Test-GitTagExistsOnRemote {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$RemoteName,

        [Parameter(Mandatory)]
        [string]$TagName
    )

    Push-Location $RepositoryRoot
    try {
        $previousNativePreference = $PSNativeCommandUseErrorActionPreference
        $script:PSNativeCommandUseErrorActionPreference = $false
        try {
            $output = & git ls-remote --tags $RemoteName "refs/tags/$TagName" 2>&1
            if ($LASTEXITCODE -ne 0) {
                throw "Could not query remote '$RemoteName' for tag '$TagName'."
            }

            return @($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count -gt 0
        }
        finally {
            $script:PSNativeCommandUseErrorActionPreference = $previousNativePreference
        }
    }
    finally {
        Pop-Location
    }
}

function Get-GitStatusLines {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    return Invoke-GitTextCapture -RepositoryRoot $RepositoryRoot -Arguments @('status', '--short', '--untracked-files=all') -FailureMessage "Could not read git status for '$RepositoryRoot'."
}

function Assert-CleanWorkingTree {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$Description,

        [switch]$Force
    )

    $statusLines = @(Get-GitStatusLines -RepositoryRoot $RepositoryRoot)
    if ($statusLines.Count -eq 0) {
        return
    }

    if ($Force) {
        Write-Warning "$Description has local changes that will be discarded by the release script:`n$($statusLines -join [Environment]::NewLine)"
        return
    }

    throw "$Description has local changes. Commit, stash, or clean them first, or rerun with -Force when discarding the public clone state is intentional:`n$($statusLines -join [Environment]::NewLine)"
}

function Invoke-RepoPowerShellScript {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$RelativeScriptPath,

        [string[]]$Arguments = @()
    )

    $scriptPath = Join-Path -Path $RepositoryRoot -ChildPath ($RelativeScriptPath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    Push-Location $RepositoryRoot
    try {
        & pwsh -NoLogo -NoProfile -File $scriptPath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Script '$RelativeScriptPath' failed in '$RepositoryRoot' with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Invoke-PublicValidation {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$Profile
    )

    switch ($Profile) {
        'none' {
            Write-Host 'Public validation profile: none (skipping local gate runner).' -ForegroundColor DarkGray
            return
        }
        'fast' {
            $gateIds = @(
                'spec-governance',
                'dependency-policy',
                'build',
                'backend-unit-tests',
                'architecture-tests',
                'frontend-lint',
                'frontend-typecheck',
                'frontend-build'
            )
            $gateArguments = @('-Only') + $gateIds

            Write-Host "Public validation profile: fast ($($gateIds -join ', '))." -ForegroundColor Cyan
            Invoke-RepoPowerShellScript -RepositoryRoot $RepositoryRoot -RelativeScriptPath 'scripts/Invoke-LocalGates.ps1' -Arguments $gateArguments
            return
        }
        'full' {
            Write-Host 'Public validation profile: full (canonical local gate wall).' -ForegroundColor Cyan
            Assert-TestcontainersDockerReady -Context 'Full OSS release validation' -RetryScript 'scripts/Release-Oss.ps1' -AttemptAutoStart
            Invoke-RepoPowerShellScript -RepositoryRoot $RepositoryRoot -RelativeScriptPath 'scripts/Invoke-LocalGates.ps1'
            return
        }
        default {
            throw "Unsupported validation profile '$Profile'."
        }
    }
}

function Remove-RepositoryWorkingTreeContents {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    Get-ChildItem -Path $RepositoryRoot -Force | Where-Object { $_.Name -ne '.git' } | Remove-Item -Recurse -Force
}

function Expand-GitArchiveSnapshot {
    param(
        [Parameter(Mandatory)]
        [string]$ArchivePath,

        [Parameter(Mandatory)]
        [string]$DestinationRoot
    )

    try {
        [System.Formats.Tar.TarFile]::ExtractToDirectory($ArchivePath, $DestinationRoot, $true)
    }
    catch {
        throw "Could not extract git archive '$ArchivePath' into '$DestinationRoot': $($_.Exception.Message)"
    }
}

$privateRepositoryRoot = Get-RepositoryRoot
$publicRepositoryRoot = if ([string]::IsNullOrWhiteSpace($PublicClonePath)) {
    Join-Path -Path (Split-Path -Parent $privateRepositoryRoot) -ChildPath 'dotnet-modulith-baseline-oss'
}
else {
    [System.IO.Path]::GetFullPath($PublicClonePath)
}

$privateTagName = "$PrivateTagPrefix$Version"
$publicTagName = "$PublicTagPrefix$Version"
$archivePath = $null

Assert-CommandExists -Name 'git'
Assert-CommandExists -Name 'pwsh'
Assert-GitRepositoryPath -RepositoryRoot $privateRepositoryRoot -Description 'Private source repo'
Assert-GitRepositoryPath -RepositoryRoot $publicRepositoryRoot -Description 'Public OSS clone'
Assert-CleanWorkingTree -RepositoryRoot $privateRepositoryRoot -Description 'Private source repo'
Assert-CleanWorkingTree -RepositoryRoot $publicRepositoryRoot -Description 'Public OSS clone' -Force:$Force

$sourceCommit = (& git -C $privateRepositoryRoot rev-parse HEAD).Trim()
$publicRemoteUrl = (& git -C $publicRepositoryRoot remote get-url $PublicRemoteName).Trim()

if ((Test-GitTagExistsLocally -RepositoryRoot $privateRepositoryRoot -TagName $privateTagName) -or
    (Test-GitTagExistsOnRemote -RepositoryRoot $privateRepositoryRoot -RemoteName 'origin' -TagName $privateTagName)) {
    throw "Private tag '$privateTagName' already exists. Choose a new version or remove the existing tag before releasing."
}

if ((Test-GitTagExistsLocally -RepositoryRoot $publicRepositoryRoot -TagName $publicTagName) -or
    (Test-GitTagExistsOnRemote -RepositoryRoot $publicRepositoryRoot -RemoteName $PublicRemoteName -TagName $publicTagName)) {
    throw "Public tag '$publicTagName' already exists. Choose a new version or remove the existing tag before releasing."
}

Write-Host ''
Write-Host 'OSS release plan' -ForegroundColor Cyan
Write-Host '================' -ForegroundColor Cyan
Write-Host "Private repo       : $privateRepositoryRoot"
Write-Host "Public clone       : $publicRepositoryRoot"
Write-Host "Public remote      : $PublicRemoteName -> $publicRemoteUrl"
Write-Host "Source commit      : $sourceCommit"
Write-Host "Private source tag : $privateTagName"
Write-Host "Public release tag : $publicTagName"
Write-Host "Validation profile : $ValidationProfile"
Write-Host ''

Write-Host 'Running private secret scan...' -ForegroundColor Cyan
Invoke-RepoPowerShellScript -RepositoryRoot $privateRepositoryRoot -RelativeScriptPath 'scripts/Invoke-CiGate.ps1' -Arguments @('-Id', 'secret-scan')

Write-Host 'Refreshing public clone from origin/main...' -ForegroundColor Cyan
& git -C $publicRepositoryRoot fetch $PublicRemoteName
& git -C $publicRepositoryRoot checkout main
& git -C $publicRepositoryRoot reset --hard "$PublicRemoteName/main"
& git -C $publicRepositoryRoot clean -fdx

$archivePath = New-GovernanceScratchFilePath -Purpose 'oss-release' -Suffix 'snapshot' -Extension 'tar' -RepositoryRoot $privateRepositoryRoot
try {
    Write-Host 'Replacing public working tree with a tracked-files-only archive snapshot...' -ForegroundColor Cyan
    Remove-RepositoryWorkingTreeContents -RepositoryRoot $publicRepositoryRoot
    & git -C $privateRepositoryRoot archive --format=tar "--output=$archivePath" $sourceCommit
    Expand-GitArchiveSnapshot -ArchivePath $archivePath -DestinationRoot $publicRepositoryRoot
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($archivePath) -and (Test-Path -Path $archivePath -PathType Leaf)) {
        Remove-Item -Path $archivePath -Force
    }
}

$publicStatusAfterExport = @(Get-GitStatusLines -RepositoryRoot $publicRepositoryRoot)
if ($publicStatusAfterExport.Count -eq 0) {
    throw "The public clone already matches source commit '$sourceCommit'. No release commit was created."
}

Write-Host 'Running public secret scan...' -ForegroundColor Cyan
Invoke-RepoPowerShellScript -RepositoryRoot $publicRepositoryRoot -RelativeScriptPath 'scripts/Invoke-CiGate.ps1' -Arguments @('-Id', 'secret-scan')
Invoke-PublicValidation -RepositoryRoot $publicRepositoryRoot -Profile $ValidationProfile

Write-Host 'Creating and pushing the private source tag...' -ForegroundColor Cyan
& git -C $privateRepositoryRoot tag -a $privateTagName -m "Source commit for public OSS $publicTagName"
& git -C $privateRepositoryRoot push origin $privateTagName

Write-Host 'Committing and tagging the public release...' -ForegroundColor Cyan
& git -C $publicRepositoryRoot add -A
& git -C $publicRepositoryRoot commit -m "Release $publicTagName"
& git -C $publicRepositoryRoot tag -a $publicTagName -m "Public OSS $publicTagName from $privateTagName ($sourceCommit)"

Write-Host 'Pushing the public release...' -ForegroundColor Cyan
& git -C $publicRepositoryRoot push $PublicRemoteName main
& git -C $publicRepositoryRoot push $PublicRemoteName $publicTagName

$publicReleaseCommit = (& git -C $publicRepositoryRoot rev-parse HEAD).Trim()

Write-Host ''
Write-Host 'OSS release completed.' -ForegroundColor Green
Write-Host "Private source tag : $privateTagName"
Write-Host "Public release tag : $publicTagName"
Write-Host "Public release SHA : $publicReleaseCommit"
Write-Host "Public clone path  : $publicRepositoryRoot"
Write-Host "Public repo URL    : $publicRemoteUrl"
