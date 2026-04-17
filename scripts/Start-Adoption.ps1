<#
.SYNOPSIS
Guides a first-time adopter through creating a target repo and running the governed adoption dry run.

.DESCRIPTION
This wrapper is a human-friendly front-end to scripts/Adopt-Baseline.ps1. It asks
for the unresolved adoption inputs, clones the current baseline into a target
folder, writes adopt-spec.json into that target repo, runs the governed dry run,
and only proceeds to apply mode after explicit confirmation.
#>

[CmdletBinding()]
param(
    [string]$TargetPath,

    [string]$TargetGitRemoteUrl,

    [string]$ProjectName,

    [string]$ProjectSlug,

    [ValidateSet('clean-base', 'recommended-first-product-base', 'full-teaching-set', 'custom')]
    [string]$ModulePreset,

    [string[]]$KeepModules,

    [ValidateSet('none', 'fast', 'full')]
    [string]$ValidationProfile = 'full',

    [switch]$ResetLocalDatabase,

    [switch]$Apply,

    [switch]$DryRunOnly,

    [switch]$Force,

    [switch]$NoPrompt
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $true

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

if ($Apply -and $DryRunOnly) {
    throw 'Specify either -Apply or -DryRunOnly, not both.'
}

function ConvertTo-AdoptionProjectSlug {
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    $slug = $Value.Trim().ToLowerInvariant()
    $slug = [System.Text.RegularExpressions.Regex]::Replace($slug, '[^a-z0-9]+', '-')
    $slug = [System.Text.RegularExpressions.Regex]::Replace($slug, '-{2,}', '-')
    return $slug.Trim('-')
}

function Test-AdoptionProjectSlugValue {
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    return $Value -match '^[a-z0-9]+(?:-[a-z0-9]+)*$'
}

function Read-ValidatedInteractiveInput {
    param(
        [Parameter(Mandatory)]
        [string]$Prompt,

        [string]$Default,

        [scriptblock]$Validator,

        [string]$ValidationMessage,

        [switch]$AllowBlank
    )

    while ($true) {
        $effectivePrompt = if ([string]::IsNullOrWhiteSpace($Default)) {
            $Prompt
        }
        else {
            "${Prompt} [$Default]"
        }

        $value = Read-Host $effectivePrompt
        if ([string]::IsNullOrWhiteSpace($value)) {
            $value = $Default
        }

        if ([string]::IsNullOrWhiteSpace($value)) {
            if ($AllowBlank) {
                return ''
            }

            Write-Warning 'A value is required.'
            continue
        }

        $value = $value.Trim()
        if ($null -eq $Validator -or (& $Validator $value)) {
            return $value
        }

        if ([string]::IsNullOrWhiteSpace($ValidationMessage)) {
            Write-Warning 'The provided value is not valid.'
        }
        else {
            Write-Warning $ValidationMessage
        }
    }
}

function Read-YesNoInteractive {
    param(
        [Parameter(Mandatory)]
        [string]$Prompt,

        [bool]$Default = $false
    )

    $defaultHint = if ($Default) { '[Y/n]' } else { '[y/N]' }
    while ($true) {
        $response = Read-Host "$Prompt $defaultHint"
        if ([string]::IsNullOrWhiteSpace($response)) {
            return $Default
        }

        switch ($response.Trim().ToLowerInvariant()) {
            'y' { return $true }
            'yes' { return $true }
            'n' { return $false }
            'no' { return $false }
            default {
                Write-Warning 'Enter y/yes or n/no.'
            }
        }
    }
}

function Read-InteractiveMenuSelection {
    param(
        [Parameter(Mandatory)]
        [string]$Title,

        [Parameter(Mandatory)]
        [object[]]$Options,

        [Parameter(Mandatory)]
        [string]$DefaultKey
    )

    while ($true) {
        Write-Host ''
        Write-Host $Title -ForegroundColor Cyan
        foreach ($option in $Options) {
            Write-Host ("  {0}) {1}" -f $option.Key, $option.Label)
        }

        $response = Read-Host "Choice [$DefaultKey]"
        if ([string]::IsNullOrWhiteSpace($response)) {
            $response = $DefaultKey
        }

        $selected = @($Options | Where-Object {
                [string]$_.Key -eq $response -or [string]$_.Value -eq $response
            })
        if ($selected.Count -eq 1) {
            return $selected[0].Value
        }

        Write-Warning 'Select one of the listed options.'
    }
}

function Get-GitRemoteUrl {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$RemoteName
    )

    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $git) {
        return $null
    }

    Push-Location $RepositoryRoot
    try {
        $previousNativePreference = $PSNativeCommandUseErrorActionPreference
        $script:PSNativeCommandUseErrorActionPreference = $false
        try {
            git rev-parse --is-inside-work-tree *> $null
            if ($LASTEXITCODE -ne 0) {
                return $null
            }

            $remoteUrl = git remote get-url $RemoteName 2>$null
            if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($remoteUrl)) {
                return $null
            }

            return ($remoteUrl | Select-Object -First 1).Trim()
        }
        finally {
            $script:PSNativeCommandUseErrorActionPreference = $previousNativePreference
        }
    }
    finally {
        Pop-Location
    }
}

function Get-SourceWorkingTreeStatusLines {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $git) {
        return @()
    }

    Push-Location $RepositoryRoot
    try {
        $previousNativePreference = $PSNativeCommandUseErrorActionPreference
        $script:PSNativeCommandUseErrorActionPreference = $false
        try {
            git rev-parse --is-inside-work-tree *> $null
            if ($LASTEXITCODE -ne 0) {
                return @()
            }

            $status = git status --short --untracked-files=all
            if ($LASTEXITCODE -ne 0) {
                return @()
            }

            return @($status | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        }
        finally {
            $script:PSNativeCommandUseErrorActionPreference = $previousNativePreference
        }
    }
    finally {
        Pop-Location
    }
}

function Assert-SourceRepositoryReady {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [switch]$Force
    )

    $dirtyEntries = @(Get-SourceWorkingTreeStatusLines -RepositoryRoot $RepositoryRoot)
    if ($dirtyEntries.Count -eq 0) {
        return @()
    }

    if ($Force) {
        Write-Warning "The source repository has working-tree changes. The wrapper will preserve the cloned git history and then overlay the current working tree snapshot so the target matches the live source files. Changes:`n$($dirtyEntries -join [Environment]::NewLine)"
        return @($dirtyEntries)
    }

    throw "Refusing to clone from a repository with working-tree changes. Commit, stash, or clean them first, or rerun with -Force. Changes:`n$($dirtyEntries -join [Environment]::NewLine)"
}

function Get-AdoptionSnapshotDirectoryExclusions {
    $excludedDirectoryNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($name in @('.git', '.tmp', 'node_modules', 'dist', 'bin', 'obj', 'playwright-report', 'TestResults', 'test-results', 'coverage')) {
        [void]$excludedDirectoryNames.Add($name)
    }

    return $excludedDirectoryNames
}

function Get-AdoptionPathComparer {
    if ($IsWindows) {
        return [System.StringComparer]::OrdinalIgnoreCase
    }

    return [System.StringComparer]::Ordinal
}

function Get-RepositorySnapshotRelativeDirectoryPaths {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $excludedDirectoryNames = Get-AdoptionSnapshotDirectoryExclusions
    $results = [System.Collections.Generic.List[string]]::new()
    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push($RepositoryRoot)

    while ($pending.Count -gt 0) {
        $currentDirectory = $pending.Pop()

        foreach ($directory in Get-ChildItem -Path $currentDirectory -Directory -Force) {
            if ($excludedDirectoryNames.Contains($directory.Name)) {
                continue
            }

            $relativePath = [System.IO.Path]::GetRelativePath($RepositoryRoot, $directory.FullName).Replace([System.IO.Path]::DirectorySeparatorChar, '/')
            $results.Add($relativePath)
            $pending.Push($directory.FullName)
        }
    }

    return $results.ToArray()
}

function Get-RepositorySnapshotRelativeFilePaths {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $excludedDirectoryNames = Get-AdoptionSnapshotDirectoryExclusions
    $results = [System.Collections.Generic.List[string]]::new()
    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push($RepositoryRoot)

    while ($pending.Count -gt 0) {
        $currentDirectory = $pending.Pop()

        foreach ($directory in Get-ChildItem -Path $currentDirectory -Directory -Force) {
            if ($excludedDirectoryNames.Contains($directory.Name)) {
                continue
            }

            $pending.Push($directory.FullName)
        }

        foreach ($file in Get-ChildItem -Path $currentDirectory -File -Force) {
            $relativePath = [System.IO.Path]::GetRelativePath($RepositoryRoot, $file.FullName).Replace([System.IO.Path]::DirectorySeparatorChar, '/')
            $results.Add($relativePath)
        }
    }

    return $results.ToArray()
}

function Get-WorkingTreeSnapshotRelativeFilePaths {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $git) {
        return @(Get-RepositorySnapshotRelativeFilePaths -RepositoryRoot $RepositoryRoot)
    }

    Push-Location $RepositoryRoot
    try {
        $previousNativePreference = $PSNativeCommandUseErrorActionPreference
        $script:PSNativeCommandUseErrorActionPreference = $false
        try {
            git rev-parse --is-inside-work-tree *> $null
            if ($LASTEXITCODE -ne 0) {
                return @(Get-RepositorySnapshotRelativeFilePaths -RepositoryRoot $RepositoryRoot)
            }

            $paths = @(git ls-files --cached --others --exclude-standard)
            if ($LASTEXITCODE -ne 0) {
                return @(Get-RepositorySnapshotRelativeFilePaths -RepositoryRoot $RepositoryRoot)
            }

            return @($paths |
                    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                    ForEach-Object { $_.Trim().Replace([System.IO.Path]::DirectorySeparatorChar, '/') } |
                    Sort-Object -Unique)
        }
        finally {
            $script:PSNativeCommandUseErrorActionPreference = $previousNativePreference
        }
    }
    finally {
        Pop-Location
    }
}

function Sync-RepositoryWorkingTreeSnapshot {
    param(
        [Parameter(Mandatory)]
        [string]$SourceRepositoryRoot,

        [Parameter(Mandatory)]
        [string]$TargetRepositoryRoot
    )

    $pathComparer = Get-AdoptionPathComparer
    $sourceDirectories = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
    $sourceFiles = [System.Collections.Generic.Dictionary[string, string]]::new($pathComparer)
    foreach ($relativePath in Get-WorkingTreeSnapshotRelativeFilePaths -RepositoryRoot $SourceRepositoryRoot) {
        $normalizedRelativePath = $relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $sourcePath = Join-Path -Path $SourceRepositoryRoot -ChildPath $normalizedRelativePath
        if (-not (Test-Path -Path $sourcePath -PathType Leaf)) {
            continue
        }

        $relativeDirectory = (Split-Path -Path $relativePath -Parent).Replace([System.IO.Path]::DirectorySeparatorChar, '/')
        if (-not [string]::IsNullOrWhiteSpace($relativeDirectory)) {
            $segments = @($relativeDirectory.Split('/', [System.StringSplitOptions]::RemoveEmptyEntries))
            $prefix = ''
            foreach ($segment in $segments) {
                $prefix = if ([string]::IsNullOrWhiteSpace($prefix)) { $segment } else { "$prefix/$segment" }
                [void]$sourceDirectories.Add($prefix)
            }
        }

        $targetPath = Join-Path -Path $TargetRepositoryRoot -ChildPath $normalizedRelativePath
        $targetParent = Split-Path -Parent $targetPath
        if (-not [string]::IsNullOrWhiteSpace($targetParent) -and -not (Test-Path -Path $targetParent -PathType Container)) {
            New-Item -Path $targetParent -ItemType Directory -Force | Out-Null
        }

        Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
        $sourceFiles[$relativePath] = $sourcePath
    }

    foreach ($relativePath in Get-RepositorySnapshotRelativeFilePaths -RepositoryRoot $TargetRepositoryRoot) {
        if ($sourceFiles.ContainsKey($relativePath)) {
            continue
        }

        $targetPath = Join-Path -Path $TargetRepositoryRoot -ChildPath ($relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        Remove-Item -Path $targetPath -Force
    }

    $targetDirectories = @(Get-RepositorySnapshotRelativeDirectoryPaths -RepositoryRoot $TargetRepositoryRoot | Sort-Object { $_.Length } -Descending)
    foreach ($relativePath in $targetDirectories) {
        if ($sourceDirectories.Contains($relativePath)) {
            continue
        }

        $targetPath = Join-Path -Path $TargetRepositoryRoot -ChildPath ($relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        Remove-Item -Path $targetPath -Recurse -Force
    }
}

function Get-OptionalTeachingModuleDescriptions {
    return [ordered]@{
        'SampleFeature' = 'smallest scaffold-first EF reference slice; keep it when you want the simplest end-to-end teaching module'
        'Blog' = 'first production-grade EF teaching slice; keep it when you want the default business-module reference'
        'KnowledgeBase' = 'richer content and runtime-settings reference; keep it when you want settings and editorial-flow examples'
        'Admin' = 'advanced projection, recovery, machine-endpoint, and realtime reference; keep it when you want the most advanced integration patterns'
    }
}

function Write-OptionalTeachingModuleGuidance {
    $descriptions = Get-OptionalTeachingModuleDescriptions

    Write-Host ''
    Write-Host 'Optional teaching modules:' -ForegroundColor Yellow
    foreach ($moduleName in $descriptions.Keys) {
        Write-Host ("  - {0}: {1}" -f $moduleName, $descriptions[$moduleName])
    }

    Write-Host ''
    Write-Host 'For more detail before choosing, read docs/MODULE_GUIDE.md and docs/ADOPT.md.' -ForegroundColor DarkYellow
}

function Assert-TargetPathIsSupported {
    param(
        [Parameter(Mandatory)]
        [string]$SourceRepositoryRoot,

        [Parameter(Mandatory)]
        [string]$CandidatePath,

        [switch]$Force
    )

    $resolvedCandidatePath = [System.IO.Path]::GetFullPath($CandidatePath)
    if (Test-IsSameOrDescendantPath -BasePath $SourceRepositoryRoot -CandidatePath $resolvedCandidatePath) {
        throw "Target path '$resolvedCandidatePath' must live outside the source repository root '$SourceRepositoryRoot'. Use Adopt-Baseline.ps1 directly if you need an in-place adoption."
    }

    if (Test-Path -Path $resolvedCandidatePath -PathType Leaf) {
        throw "Target path '$resolvedCandidatePath' points to a file. Provide a directory path instead."
    }

    if (-not (Test-Path -Path $resolvedCandidatePath -PathType Container)) {
        return
    }

    $entries = @(Get-ChildItem -Path $resolvedCandidatePath -Force)
    if ($entries.Count -eq 0) {
        return
    }

    if (-not $Force) {
        throw "Target path '$resolvedCandidatePath' already exists and is not empty. Choose a new folder or rerun with -Force."
    }

    Remove-Item -Path $resolvedCandidatePath -Recurse -Force
}

function Resolve-DesiredKeepModules {
    param(
        [string]$ModulePreset,

        [string[]]$KeepModules,

        [switch]$NoPrompt
    )

    $supportedModules = Get-SupportedAdoptionModules
    $presets = Get-AdoptionModulePresets

    if ($KeepModules -and $KeepModules.Count -gt 0) {
        $orderedKeepModules = @($supportedModules | Where-Object { $KeepModules -contains $_ })
        $resolvedPreset = if ([string]::IsNullOrWhiteSpace($ModulePreset)) {
            Resolve-AdoptionModulePresetForKeepModules -KeepModules $orderedKeepModules
        }
        else {
            $ModulePreset
        }

        $probeSpec = [ordered]@{
            '$schema' = 'https://dotnet-modulith-baseline.local/schemas/adopt-spec.v1.schema.json'
            'schemaVersion' = '1.0'
            'projectName' = 'Interactive Adoption Probe'
            'projectSlug' = 'interactive-adoption-probe'
            'modulePreset' = $resolvedPreset
            'keepModules' = @($orderedKeepModules)
            'scratch' = @{
                'mode' = 'repo-local'
                'root' = '.tmp'
            }
            'database' = @{
                'resetLocalDatabase' = $false
            }
            'validation' = @{
                'profile' = 'none'
            }
        }

        Test-AdoptionSpec -Spec $probeSpec
        $validatedKeepModules = @(Resolve-AdoptionKeepModules -Spec $probeSpec)
        return [ordered]@{
            ModulePreset = $resolvedPreset
            KeepModules = @($validatedKeepModules)
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ModulePreset)) {
        if ($ModulePreset -eq 'custom') {
            if ($NoPrompt) {
                throw "-ModulePreset custom requires -KeepModules when -NoPrompt is supplied."
            }

            Write-OptionalTeachingModuleGuidance

            $moduleDescriptions = Get-OptionalTeachingModuleDescriptions
            $selectedModules = @('Platform', 'Identity')
            foreach ($module in @('SampleFeature', 'Blog', 'KnowledgeBase', 'Admin')) {
                $description = [string]$moduleDescriptions[$module]
                if (Read-YesNoInteractive -Prompt "Keep optional module '$module' ($description)?" -Default $false) {
                    $selectedModules += $module
                }
            }

            return [ordered]@{
                ModulePreset = (Resolve-AdoptionModulePresetForKeepModules -KeepModules $selectedModules)
                KeepModules = @($selectedModules)
            }
        }

        return [ordered]@{
            ModulePreset = $ModulePreset
            KeepModules = @($presets[$ModulePreset])
        }
    }

    if ($NoPrompt) {
        throw 'Provide -ModulePreset or -KeepModules when -NoPrompt is supplied.'
    }

    Write-OptionalTeachingModuleGuidance

    $selection = Read-InteractiveMenuSelection -Title 'Select the teaching-module posture for the adopted fork.' -DefaultKey '2' -Options @(
        [pscustomobject]@{ Key = '1'; Label = 'clean-base — smallest governed starting point; keep only Platform and Identity'; Value = 'clean-base' },
        [pscustomobject]@{ Key = '2'; Label = 'recommended-first-product-base — keep SampleFeature and Blog as the small + realistic EF reference set'; Value = 'recommended-first-product-base' },
        [pscustomobject]@{ Key = '3'; Label = 'full-teaching-set — keep all optional teaching modules as live reference material'; Value = 'full-teaching-set' },
        [pscustomobject]@{ Key = '4'; Label = 'custom — choose each optional teaching module explicitly'; Value = 'custom' }
    )

    return Resolve-DesiredKeepModules -ModulePreset $selection
}

function New-AdoptionSpec {
    param(
        [Parameter(Mandatory)]
        [string]$ProjectName,

        [Parameter(Mandatory)]
        [string]$ProjectSlug,

        [Parameter(Mandatory)]
        [string]$ModulePreset,

        [Parameter(Mandatory)]
        [string[]]$KeepModules,

        [Parameter(Mandatory)]
        [string]$ValidationProfile,

        [Parameter(Mandatory)]
        [bool]$ResetLocalDatabase
    )

    $spec = [ordered]@{
        '$schema' = 'https://dotnet-modulith-baseline.local/schemas/adopt-spec.v1.schema.json'
        'schemaVersion' = '1.0'
        'projectName' = $ProjectName
        'projectSlug' = $ProjectSlug
        'solutionFileName' = "$ProjectSlug.sln"
        'modulePreset' = $ModulePreset
        'scratch' = @{
            'mode' = 'repo-local'
            'root' = '.tmp'
        }
        'database' = @{
            'resetLocalDatabase' = $ResetLocalDatabase
        }
        'validation' = @{
            'profile' = $ValidationProfile
        }
    }

    if ($ModulePreset -eq 'custom') {
        $spec['keepModules'] = @($KeepModules)
    }

    Test-AdoptionSpec -Spec $spec
    return $spec
}

function Write-AdoptionWrapperSummary {
    param(
        [Parameter(Mandatory)]
        [string]$SourceRepositoryRoot,

        [Parameter(Mandatory)]
        [string]$TargetRepositoryRoot,

        [string]$TargetGitRemoteUrl,

        [string]$SourceGitRemoteUrl,

        [Parameter(Mandatory)]
        [hashtable]$Spec,

        [Parameter(Mandatory)]
        [string[]]$KeepModules,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$RemoveModules,

        [Parameter(Mandatory)]
        [string]$SpecPath,

        [Parameter(Mandatory)]
        [bool]$ApplyRequested,

        [Parameter(Mandatory)]
        [bool]$IncludesWorkingTreeOverlay
    )

    Write-Host ''
    Write-Host 'Interactive adoption plan' -ForegroundColor Cyan
    Write-Host '=========================' -ForegroundColor Cyan
    Write-Host "Source repo     : $SourceRepositoryRoot"
    Write-Host "Target repo     : $TargetRepositoryRoot"
    Write-Host "Spec path       : $SpecPath"
    Write-Host "Application name: $($Spec['projectName'])"
    Write-Host "Technical slug  : $($Spec['projectSlug'])"
    Write-Host "Module preset   : $($Spec['modulePreset'])"
    Write-Host "Validation      : $($Spec['validation']['profile'])"
    Write-Host "DB reset        : $($Spec['database']['resetLocalDatabase'])"
    Write-Host "Apply requested : $ApplyRequested"
    Write-Host ''
    Write-Host 'Modules to keep:' -ForegroundColor Yellow
    foreach ($module in $KeepModules) {
        Write-Host "  - $module"
    }

    Write-Host ''
    Write-Host 'Modules to remove:' -ForegroundColor Yellow
    if ($RemoveModules.Count -eq 0) {
        Write-Host '  - none'
    }
    else {
        foreach ($module in $RemoveModules) {
            Write-Host "  - $module"
        }
    }

    Write-Host ''
    Write-Host 'Git remotes after clone:' -ForegroundColor Yellow
    if (-not [string]::IsNullOrWhiteSpace($TargetGitRemoteUrl)) {
        Write-Host "  - origin   -> $TargetGitRemoteUrl"
        if (-not [string]::IsNullOrWhiteSpace($SourceGitRemoteUrl) -and $SourceGitRemoteUrl -ne $TargetGitRemoteUrl) {
            Write-Host "  - upstream -> $SourceGitRemoteUrl"
        }
    }
    elseif (-not [string]::IsNullOrWhiteSpace($SourceGitRemoteUrl)) {
        Write-Host "  - upstream -> $SourceGitRemoteUrl"
        Write-Host '  - origin   -> not configured by the wrapper (safest default when no target remote was supplied)'
    }
    else {
        Write-Host '  - no remotes will be configured automatically'
    }

    Write-Host ''
    Write-Host 'Planned actions:' -ForegroundColor Yellow
    if ($IncludesWorkingTreeOverlay) {
        Write-Host '  - clone this baseline into the target folder and align it with the current working tree snapshot'
    }
    else {
        Write-Host '  - clone this baseline into the target folder'
    }
    Write-Host '  - write adopt-spec.json into the target repo'
    Write-Host '  - run Adopt-Baseline.ps1 -DryRun inside the target repo'
    if ($ApplyRequested) {
        Write-Host '  - if the dry run succeeds, run Adopt-Baseline.ps1 in apply mode'
    }
    else {
        Write-Host '  - stop after the dry run unless you explicitly confirm apply later'
    }
}

function Invoke-GitCloneForAdoption {
    param(
        [Parameter(Mandatory)]
        [string]$SourceRepositoryRoot,

        [Parameter(Mandatory)]
        [string]$TargetRepositoryRoot
    )

    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $git) {
        throw 'Git is required for the guided adoption wrapper.'
    }

    $targetParent = Split-Path -Parent $TargetRepositoryRoot
    if (-not [string]::IsNullOrWhiteSpace($targetParent) -and -not (Test-Path -Path $targetParent -PathType Container)) {
        New-Item -Path $targetParent -ItemType Directory -Force | Out-Null
    }

    & git clone --no-local $SourceRepositoryRoot $TargetRepositoryRoot
}

function Configure-AdoptionTargetRemotes {
    param(
        [Parameter(Mandatory)]
        [string]$TargetRepositoryRoot,

        [string]$TargetGitRemoteUrl,

        [string]$SourceGitRemoteUrl
    )

    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $git) {
        return
    }

    Push-Location $TargetRepositoryRoot
    try {
        $previousNativePreference = $PSNativeCommandUseErrorActionPreference
        $script:PSNativeCommandUseErrorActionPreference = $false
        try {
            git remote remove origin *> $null
            git remote remove upstream *> $null
        }
        finally {
            $script:PSNativeCommandUseErrorActionPreference = $previousNativePreference
        }

        if (-not [string]::IsNullOrWhiteSpace($TargetGitRemoteUrl)) {
            & git remote add origin $TargetGitRemoteUrl
            if (-not [string]::IsNullOrWhiteSpace($SourceGitRemoteUrl) -and $SourceGitRemoteUrl -ne $TargetGitRemoteUrl) {
                & git remote add upstream $SourceGitRemoteUrl
            }

            return
        }

        if (-not [string]::IsNullOrWhiteSpace($SourceGitRemoteUrl)) {
            & git remote add upstream $SourceGitRemoteUrl
        }
    }
    finally {
        Pop-Location
    }
}

function Write-AdoptionSpecFile {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [hashtable]$Spec
    )

    $json = $Spec | ConvertTo-Json -Depth 8
    Set-Content -Path $Path -Value $json
}

function Invoke-AdoptionScript {
    param(
        [Parameter(Mandatory)]
        [string]$TargetRepositoryRoot,

        [Parameter(Mandatory)]
        [string]$SpecPath,

        [switch]$DryRun,

        [switch]$Force
    )

    $adoptionScript = Join-Path -Path $TargetRepositoryRoot -ChildPath 'scripts/Adopt-Baseline.ps1'
    if ($DryRun) {
        & pwsh -NoLogo -NoProfile -File $adoptionScript -SpecFile $SpecPath -DryRun
        return
    }

    & pwsh -NoLogo -NoProfile -File $adoptionScript -SpecFile $SpecPath -Force:$Force
}

$sourceRepositoryRoot = Get-RepositoryRoot
$sourceGitRemoteUrl = Get-GitRemoteUrl -RepositoryRoot $sourceRepositoryRoot -RemoteName 'origin'
$sourceDirtyEntries = @(Assert-SourceRepositoryReady -RepositoryRoot $sourceRepositoryRoot -Force:$Force)

if ([string]::IsNullOrWhiteSpace($ProjectName)) {
    if ($NoPrompt) {
        throw 'Provide -ProjectName when -NoPrompt is supplied.'
    }

    $ProjectName = Read-ValidatedInteractiveInput -Prompt 'Application name shown to users (human-facing app name)' -Validator { param($value) -not [string]::IsNullOrWhiteSpace($value) } -ValidationMessage 'Application name is required.'
}

if ([string]::IsNullOrWhiteSpace($ProjectSlug)) {
    $derivedProjectSlug = ConvertTo-AdoptionProjectSlug -Value $ProjectName
    if ($NoPrompt) {
        if (-not (Test-AdoptionProjectSlugValue -Value $derivedProjectSlug)) {
            throw "Could not derive a valid project slug from '$ProjectName'. Provide -ProjectSlug explicitly."
        }

        $ProjectSlug = $derivedProjectSlug
    }
    else {
        $ProjectSlug = Read-ValidatedInteractiveInput -Prompt 'Technical slug (used for cookie, Data Protection, telemetry, default solution file, and npm package names)' -Default $derivedProjectSlug -Validator { param($value) Test-AdoptionProjectSlugValue -Value $value } -ValidationMessage 'Project slug must be lowercase kebab-case (letters, numbers, hyphens).'
    }
}
elseif (-not (Test-AdoptionProjectSlugValue -Value $ProjectSlug)) {
    throw "Project slug '$ProjectSlug' must be lowercase kebab-case (letters, numbers, hyphens)."
}

if ([string]::IsNullOrWhiteSpace($TargetPath)) {
    $defaultTargetPath = Join-Path -Path (Split-Path -Parent $sourceRepositoryRoot) -ChildPath $ProjectSlug
    if ($NoPrompt) {
        $TargetPath = $defaultTargetPath
    }
    else {
        while ($true) {
            $candidateTargetPath = Read-ValidatedInteractiveInput -Prompt 'Target folder' -Default $defaultTargetPath -Validator { param($value) -not [string]::IsNullOrWhiteSpace($value) } -ValidationMessage 'Target folder is required.'
            try {
                Assert-TargetPathIsSupported -SourceRepositoryRoot $sourceRepositoryRoot -CandidatePath $candidateTargetPath -Force:$Force
                $TargetPath = $candidateTargetPath
                break
            }
            catch {
                Write-Warning $_.Exception.Message
            }
        }
    }
}

$TargetPath = [System.IO.Path]::GetFullPath($TargetPath)
Assert-TargetPathIsSupported -SourceRepositoryRoot $sourceRepositoryRoot -CandidatePath $TargetPath -Force:$Force

if ([string]::IsNullOrWhiteSpace($TargetGitRemoteUrl) -and -not $NoPrompt) {
    $TargetGitRemoteUrl = Read-ValidatedInteractiveInput -Prompt 'Target git remote URL (optional)' -Validator { param($value) $true } -AllowBlank
}

$moduleDecision = Resolve-DesiredKeepModules -ModulePreset $ModulePreset -KeepModules $KeepModules -NoPrompt:$NoPrompt
$resolvedModulePreset = [string]$moduleDecision['ModulePreset']
$resolvedKeepModules = @($moduleDecision['KeepModules'])

$spec = New-AdoptionSpec -ProjectName $ProjectName -ProjectSlug $ProjectSlug -ModulePreset $resolvedModulePreset -KeepModules $resolvedKeepModules -ValidationProfile $ValidationProfile -ResetLocalDatabase:$ResetLocalDatabase
$resolvedKeepModules = @(Resolve-AdoptionKeepModules -Spec $spec)
$removeModules = @(Get-SupportedAdoptionModules | Where-Object { $resolvedKeepModules -notcontains $_ })
$targetSpecPath = Join-Path -Path $TargetPath -ChildPath 'adopt-spec.json'
$applyRequested = [bool]$Apply

Write-AdoptionWrapperSummary -SourceRepositoryRoot $sourceRepositoryRoot -TargetRepositoryRoot $TargetPath -TargetGitRemoteUrl $TargetGitRemoteUrl -SourceGitRemoteUrl $sourceGitRemoteUrl -Spec $spec -KeepModules $resolvedKeepModules -RemoveModules $removeModules -SpecPath $targetSpecPath -ApplyRequested $applyRequested -IncludesWorkingTreeOverlay:($sourceDirtyEntries.Count -gt 0)

if (-not $NoPrompt) {
    if (-not (Read-YesNoInteractive -Prompt 'Proceed with clone, spec generation, and dry run?' -Default $true)) {
        Write-Host 'Interactive adoption cancelled before mutation.' -ForegroundColor Yellow
        return
    }
}

Invoke-GitCloneForAdoption -SourceRepositoryRoot $sourceRepositoryRoot -TargetRepositoryRoot $TargetPath
if ($sourceDirtyEntries.Count -gt 0) {
    Sync-RepositoryWorkingTreeSnapshot -SourceRepositoryRoot $sourceRepositoryRoot -TargetRepositoryRoot $TargetPath
}
Configure-AdoptionTargetRemotes -TargetRepositoryRoot $TargetPath -TargetGitRemoteUrl $TargetGitRemoteUrl -SourceGitRemoteUrl $sourceGitRemoteUrl
Write-AdoptionSpecFile -Path $targetSpecPath -Spec $spec

Write-Host ''
Write-Host 'Running adoption dry run in the target repository...' -ForegroundColor Cyan
Invoke-AdoptionScript -TargetRepositoryRoot $TargetPath -SpecPath $targetSpecPath -DryRun

if ($DryRunOnly) {
    Write-Host ''
    Write-Host "Dry run completed. Review '$targetSpecPath' inside '$TargetPath'." -ForegroundColor Green
    return
}

if (-not $applyRequested -and -not $NoPrompt) {
    $applyRequested = Read-YesNoInteractive -Prompt 'Dry run succeeded. Apply this adoption to the target repo now?' -Default $false
}

if (-not $applyRequested) {
    Write-Host ''
    Write-Host "Dry run completed. No adoption changes were applied beyond cloning/configuring the target repo and writing '$targetSpecPath'." -ForegroundColor Green
    return
}

Write-Host ''
Write-Host 'Applying adoption in the target repository...' -ForegroundColor Cyan
Invoke-AdoptionScript -TargetRepositoryRoot $TargetPath -SpecPath $targetSpecPath -Force:($sourceDirtyEntries.Count -gt 0)

Write-Host ''
Write-Host 'Interactive adoption completed.' -ForegroundColor Green
Write-Host "Target repo: $TargetPath"
if (-not [string]::IsNullOrWhiteSpace($TargetGitRemoteUrl)) {
    Write-Host "Origin    : $TargetGitRemoteUrl"
}
elseif (-not [string]::IsNullOrWhiteSpace($sourceGitRemoteUrl)) {
    Write-Host "Upstream  : $sourceGitRemoteUrl"
    Write-Host 'Origin    : not configured by the wrapper because no target remote URL was supplied'
}
