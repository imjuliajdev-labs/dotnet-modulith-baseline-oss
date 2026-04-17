<#
.SYNOPSIS
Fails if the working tree contains content that matches a secret-detection rule.

.DESCRIPTION
Runs gitleaks over the current working tree and fails the gate on any finding.
The gitleaks version is pinned and verified against a committed SHA-256 hash
for each supported platform so the rule set is reproducible across CI and
local runs. On a fresh runner the pinned binary is downloaded from the
gitleaks release page, verified, and cached under the governance scratch root.
On a developer machine the script reuses the cached binary on subsequent runs.

The CI workflow and the local gate runner both dispatch this script through
scripts/Invoke-CiGate.ps1 -Id secret-scan.

.PARAMETER Configuration
Accepted for dispatcher parity with other gate wrappers; unused.
#>

param(
    [string]$Configuration
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

# Pinned gitleaks release. Bumping the version requires refreshing every
# SHA-256 entry below from the official checksums file at
# https://github.com/gitleaks/gitleaks/releases/download/v<version>/gitleaks_<version>_checksums.txt
$script:GitleaksVersion = '8.21.2'

# Pins taken from the v8.21.2 gitleaks_8.21.2_checksums.txt release asset.
# Keys use the $os/$arch tuple produced by Resolve-PlatformAsset.
$script:GitleaksAssetPins = @{
    'linux/x64'    = @{ Asset = 'gitleaks_{0}_linux_x64.tar.gz'; Sha256 = '5bc41815076e6ed6ef8fbecc9d9b75bcae31f39029ceb55da08086315316e3ba' }
    'linux/arm64'  = @{ Asset = 'gitleaks_{0}_linux_arm64.tar.gz'; Sha256 = '654c935542c89f565aabe7bf7c6c500830f116c114f0aeb509d2460c1ac2e6da' }
    'darwin/x64'   = @{ Asset = 'gitleaks_{0}_darwin_x64.tar.gz'; Sha256 = '5b42c6e4b1fd693eaeb2b5b7faa5f17a1434299d4deb2de63d4b2efd7c753128' }
    'darwin/arm64' = @{ Asset = 'gitleaks_{0}_darwin_arm64.tar.gz'; Sha256 = 'cad3de5dc9a4d5447d967a70a4d49499c557f04db028274cc324f9ff983f6502' }
    'windows/x64'  = @{ Asset = 'gitleaks_{0}_windows_x64.zip'; Sha256 = 'f238c85e5f47e18fac779ce71ee11091cf70a0a8fb4415f165efba2800eef133' }
}

function Resolve-PlatformAsset {
    $archRaw = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $arch = switch ($archRaw) {
        'x64'   { 'x64' }
        'x86'   { throw "Unsupported 32-bit architecture '$archRaw'. Gitleaks x32 binaries are not supported by this gate." }
        'arm64' { 'arm64' }
        default { throw "Unsupported architecture '$archRaw' for gitleaks gate." }
    }

    if ($IsWindows) {
        return "windows/$arch"
    }
    if ($IsMacOS) {
        return "darwin/$arch"
    }
    if ($IsLinux) {
        return "linux/$arch"
    }

    throw 'Unsupported operating system for gitleaks gate.'
}

function Get-GitleaksCacheRoot {
    $repositoryRoot = Get-RepositoryRoot
    $cacheRoot = Join-Path -Path $repositoryRoot -ChildPath '.tmp/tools/gitleaks'
    if (-not (Test-Path -Path $cacheRoot -PathType Container)) {
        New-Item -Path $cacheRoot -ItemType Directory -Force | Out-Null
    }
    return $cacheRoot
}

function Get-ExpectedExecutableName {
    if ($IsWindows) { return 'gitleaks.exe' }
    return 'gitleaks'
}

function Test-PinnedGitleaksAvailable {
    param(
        [Parameter(Mandatory)]
        [string]$ExecutablePath
    )

    if (-not (Test-Path -Path $ExecutablePath -PathType Leaf)) {
        return $false
    }

    $versionOutput = & $ExecutablePath version 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        return $false
    }

    # gitleaks prints e.g. "v8.21.2" or "8.21.2" depending on build; accept both.
    $versionOutput = $versionOutput.Trim()
    return ($versionOutput -eq $script:GitleaksVersion) -or ($versionOutput -eq "v$script:GitleaksVersion")
}

function Assert-FileSha256 {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$ExpectedSha256
    )

    $actual = (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    $expected = $ExpectedSha256.ToLowerInvariant()
    if ($actual -ne $expected) {
        throw "Gitleaks download hash mismatch for '$Path'. Expected $expected, got $actual. Do not run the gate until the pinned hash is corrected in scripts/Test-SecretScan.ps1."
    }
}

function Install-PinnedGitleaks {
    param(
        [Parameter(Mandatory)]
        [string]$InstallRoot
    )

    $platformKey = Resolve-PlatformAsset
    if (-not $script:GitleaksAssetPins.ContainsKey($platformKey)) {
        throw "No pinned gitleaks asset for platform '$platformKey'."
    }

    $pin = $script:GitleaksAssetPins[$platformKey]
    $assetName = [string]::Format($pin.Asset, $script:GitleaksVersion)
    $downloadUrl = "https://github.com/gitleaks/gitleaks/releases/download/v$script:GitleaksVersion/$assetName"
    $downloadPath = Join-Path -Path $InstallRoot -ChildPath $assetName

    Write-Host "==> Downloading gitleaks v$script:GitleaksVersion ($assetName)" -ForegroundColor DarkGray
    $previousProgressPreference = $ProgressPreference
    try {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $downloadUrl -OutFile $downloadPath -UseBasicParsing
    }
    finally {
        $ProgressPreference = $previousProgressPreference
    }

    Assert-FileSha256 -Path $downloadPath -ExpectedSha256 $pin.Sha256

    $extractRoot = Join-Path -Path $InstallRoot -ChildPath 'extracted'
    if (Test-Path -Path $extractRoot) {
        Remove-Item -Path $extractRoot -Recurse -Force
    }
    New-Item -Path $extractRoot -ItemType Directory -Force | Out-Null

    if ($assetName.EndsWith('.zip')) {
        Expand-Archive -Path $downloadPath -DestinationPath $extractRoot -Force
    }
    elseif ($assetName.EndsWith('.tar.gz')) {
        & tar -xzf $downloadPath -C $extractRoot
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to extract gitleaks archive '$downloadPath'."
        }
    }
    else {
        throw "Unsupported gitleaks archive format '$assetName'."
    }

    $executableName = Get-ExpectedExecutableName
    $executablePath = Get-ChildItem -Path $extractRoot -Recurse -File -Filter $executableName
        | Select-Object -First 1
        | ForEach-Object { $_.FullName }

    if ([string]::IsNullOrWhiteSpace($executablePath)) {
        throw "Gitleaks executable '$executableName' not found after extracting '$assetName'."
    }

    if (-not $IsWindows) {
        & chmod +x $executablePath | Out-Null
    }

    Remove-Item -Path $downloadPath -Force -ErrorAction SilentlyContinue
    return $executablePath
}

function Resolve-GitleaksExecutable {
    $versionRoot = Join-Path -Path (Get-GitleaksCacheRoot) -ChildPath "v$script:GitleaksVersion"
    if (-not (Test-Path -Path $versionRoot -PathType Container)) {
        New-Item -Path $versionRoot -ItemType Directory -Force | Out-Null
    }

    $executableName = Get-ExpectedExecutableName
    $cachedExecutable = Join-Path -Path $versionRoot -ChildPath $executableName

    if (Test-PinnedGitleaksAvailable -ExecutablePath $cachedExecutable) {
        return $cachedExecutable
    }

    # Reuse a matching-version gitleaks on PATH when available so contributors
    # who already installed the exact pinned version via a package manager do
    # not pay for a redundant download.
    $onPath = Get-Command -Name 'gitleaks' -ErrorAction SilentlyContinue
    if ($null -ne $onPath -and (Test-PinnedGitleaksAvailable -ExecutablePath $onPath.Source)) {
        return $onPath.Source
    }

    $installed = Install-PinnedGitleaks -InstallRoot $versionRoot
    if ($installed -ne $cachedExecutable) {
        Copy-Item -Path $installed -Destination $cachedExecutable -Force
        if (-not $IsWindows) {
            & chmod +x $cachedExecutable | Out-Null
        }
    }

    if (-not (Test-PinnedGitleaksAvailable -ExecutablePath $cachedExecutable)) {
        throw "Installed gitleaks does not report pinned version v$script:GitleaksVersion."
    }

    return $cachedExecutable
}

$repositoryRoot = Get-RepositoryRoot
$executable = Resolve-GitleaksExecutable

$configPath = Join-Path -Path $repositoryRoot -ChildPath '.gitleaks.toml'
$configArgs = @()
if (Test-Path -Path $configPath -PathType Leaf) {
    $configArgs = @('--config', $configPath)
}

Write-Host "==> Running gitleaks v$script:GitleaksVersion over working tree" -ForegroundColor Cyan

Push-Location -Path $repositoryRoot
try {
    & $executable detect `
        --no-banner `
        --redact `
        --source $repositoryRoot `
        --exit-code 1 `
        @configArgs

    $gitleaksExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($gitleaksExitCode -ne 0) {
    throw "gitleaks reported findings (exit code $gitleaksExitCode). Review the output above and remove or waive each leak before re-running."
}

Write-Host 'No secret-detection findings.' -ForegroundColor Green
