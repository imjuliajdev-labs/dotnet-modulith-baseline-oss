Set-StrictMode -Version Latest

function Get-RepositoryRoot {
    return Split-Path -Parent $PSScriptRoot
}

function Get-GovernanceScratchRoot {
    param(
        [string]$RepositoryRoot
    )

    if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
        $RepositoryRoot = Get-RepositoryRoot
    }

    $override = $env:DOTNET_MODULITH_SCRATCH_ROOT
    if (-not [string]::IsNullOrWhiteSpace($override)) {
        if (-not [System.IO.Path]::IsPathRooted($override)) {
            $override = Join-Path -Path $RepositoryRoot -ChildPath $override
        }

        return [System.IO.Path]::GetFullPath($override)
    }

    return [System.IO.Path]::GetFullPath((Join-Path -Path $RepositoryRoot -ChildPath '.tmp'))
}

function Assert-GovernanceScratchPurpose {
    param(
        [Parameter(Mandatory)]
        [string]$Purpose
    )

    if ($Purpose -notmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') {
        throw "Governance scratch purpose '$Purpose' must be lowercase kebab-case."
    }
}

function New-GovernanceScratchDirectory {
    param(
        [Parameter(Mandatory)]
        [string]$Purpose,

        [string]$RepositoryRoot
    )

    Assert-GovernanceScratchPurpose -Purpose $Purpose

    $root = Get-GovernanceScratchRoot -RepositoryRoot $RepositoryRoot
    $directory = Join-Path -Path $root -ChildPath ("{0}-{1}" -f $Purpose, [Guid]::NewGuid().ToString('N'))
    New-Item -Path $directory -ItemType Directory -Force | Out-Null
    return $directory
}

function New-GovernanceScratchFilePath {
    param(
        [Parameter(Mandatory)]
        [string]$Purpose,

        [string]$Suffix,

        [string]$Extension,

        [string]$RepositoryRoot
    )

    Assert-GovernanceScratchPurpose -Purpose $Purpose

    $root = Get-GovernanceScratchRoot -RepositoryRoot $RepositoryRoot
    $bucket = Join-Path -Path $root -ChildPath $Purpose
    if (-not (Test-Path -Path $bucket -PathType Container)) {
        New-Item -Path $bucket -ItemType Directory -Force | Out-Null
    }

    $fileName = [Guid]::NewGuid().ToString('N')
    if (-not [string]::IsNullOrWhiteSpace($Suffix)) {
        if ($Suffix -notmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') {
            throw "Governance scratch suffix '$Suffix' must be lowercase kebab-case."
        }

        $fileName = "$fileName-$Suffix"
    }

    if (-not [string]::IsNullOrWhiteSpace($Extension)) {
        if (-not $Extension.StartsWith('.')) {
            $Extension = ".$Extension"
        }

        $fileName = "$fileName$Extension"
    }

    return Join-Path -Path $bucket -ChildPath $fileName
}

function Test-UsesHttpsUrl {
    param(
        [Parameter(Mandatory)]
        [string]$Url
    )

    return $Url.Trim().StartsWith('https://', [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-UsesHttpsBinding {
    param(
        [Parameter(Mandatory)]
        [string]$Urls
    )

    return @($Urls -split ';' |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_.StartsWith('https://', [System.StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
}

if (-not ('GovernanceProcessLauncher' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.IO;

public static class GovernanceProcessLauncher
{
    public static Process Start(string fileName, string[] arguments, string workingDirectory, string stdoutPath, string stderrPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        var stdoutWriter = new StreamWriter(stdoutPath) { AutoFlush = true };
        var stderrWriter = new StreamWriter(stderrPath) { AutoFlush = true };

        var process = new Process { StartInfo = psi };
        process.EnableRaisingEvents = true;

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data == null) { return; }
            try { stdoutWriter.WriteLine(e.Data); }
            catch (ObjectDisposedException) { }
        };
        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data == null) { return; }
            try { stderrWriter.WriteLine(e.Data); }
            catch (ObjectDisposedException) { }
        };
        process.Exited += (sender, e) =>
        {
            try { stdoutWriter.Flush(); stdoutWriter.Dispose(); } catch { }
            try { stderrWriter.Flush(); stderrWriter.Dispose(); } catch { }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }
}
'@
}

function Start-GovernanceRedirectedProcess {
    param(
        [Parameter(Mandatory)]
        [string]$FileName,

        [Parameter(Mandatory)]
        [string[]]$ArgumentList,

        [Parameter(Mandatory)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory)]
        [string]$StdoutPath,

        [Parameter(Mandatory)]
        [string]$StderrPath
    )

    return [GovernanceProcessLauncher]::Start($FileName, $ArgumentList, $WorkingDirectory, $StdoutPath, $StderrPath)
}

function New-EphemeralHttpsCertificate {
    param(
        [string]$Purpose = 'local'
    )

    $certificatePath = New-GovernanceScratchFilePath -Purpose "https-dev-cert-$Purpose" -Extension 'pfx'
    $password = "DotnetModulithStarter{0}" -f [Guid]::NewGuid().ToString('N')

    & dotnet dev-certs https --export-path $certificatePath --password $password *> $null

    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -Path $certificatePath -PathType Leaf)) {
        throw "Could not export an HTTPS development certificate for '$Purpose'."
    }

    return [pscustomobject]@{
        Path = $certificatePath
        Password = $password
    }
}

function Read-JsonFileAsHashtable {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -Path $Path -PathType Leaf)) {
        throw "JSON file not found: $Path"
    }

    return Get-Content -Path $Path -Raw | ConvertFrom-Json -AsHashtable
}

function Convert-ToRepoRelativePath {
    param(
        [Parameter(Mandatory)]
        [string]$Root,

        [Parameter(Mandatory)]
        [string]$Path
    )

    return [System.IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
}

function Get-CrossModulePublicContractsReferenceCap {
    return 3
}

function Get-RepositoryModuleNames {
    param(
        [string]$RepositoryRoot = (Get-RepositoryRoot)
    )

    $modulesRoot = Join-Path -Path $RepositoryRoot -ChildPath 'src/Modules'
    if (-not (Test-Path -Path $modulesRoot -PathType Container)) {
        return @()
    }

    return @(Get-ChildItem -Path $modulesRoot -Directory | Sort-Object -Property Name | ForEach-Object { $_.Name })
}

function Resolve-ProjectReferenceRelativePath {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$ProjectDirectory,

        [Parameter(Mandatory)]
        [string]$IncludeValue
    )

    $combined = Join-Path -Path $ProjectDirectory -ChildPath $IncludeValue
    $fullPath = [System.IO.Path]::GetFullPath($combined)
    return Convert-ToRepoRelativePath -Root $RepositoryRoot -Path $fullPath
}

function Get-ModuleNameFromRepoRelativePath {
    param(
        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    $parts = $RelativePath.Replace('\', '/').Split('/')
    if ($parts.Count -lt 3) {
        throw "Relative path '$RelativePath' is not rooted under src/Modules/<module>/..."
    }

    return $parts[2]
}

function Get-ModuleDependencyReportRows {
    param(
        [string]$RepositoryRoot = (Get-RepositoryRoot)
    )

    $modulesRoot = Join-Path -Path $RepositoryRoot -ChildPath 'src/Modules'
    if (-not (Test-Path -Path $modulesRoot -PathType Container)) {
        throw "Modules directory is missing: $modulesRoot"
    }

    $moduleDirectories = Get-ChildItem -Path $modulesRoot -Directory | Sort-Object -Property Name
    $rows = [System.Collections.Generic.List[object]]::new()
    $referenceCap = Get-CrossModulePublicContractsReferenceCap

    foreach ($moduleDirectory in $moduleDirectories) {
        $moduleName = $moduleDirectory.Name
        $projectFiles = Get-ChildItem -Path $moduleDirectory.FullName -Recurse -Filter '*.csproj' -File
        $referencedModules = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::Ordinal)

        foreach ($projectFile in $projectFiles) {
            [xml]$xml = Get-Content -Path $projectFile.FullName -Raw
            $projectDirectory = Split-Path -Parent $projectFile.FullName

            $projectReferenceNodes = @($xml.SelectNodes('//ProjectReference'))
            $projectReferences = @($projectReferenceNodes |
                    Where-Object { $_ -and $_.Attributes -and $_.Attributes['Include'] } |
                    ForEach-Object { [string]$_.Attributes['Include'].Value })

            foreach ($include in $projectReferences) {
                if ([string]::IsNullOrWhiteSpace($include)) {
                    continue
                }

                $relativeReference = Resolve-ProjectReferenceRelativePath -RepositoryRoot $RepositoryRoot -ProjectDirectory $projectDirectory -IncludeValue $include

                if (-not $relativeReference.StartsWith('src/Modules/', [System.StringComparison]::Ordinal)) {
                    continue
                }

                if (-not $relativeReference.EndsWith('.PublicContracts.csproj', [System.StringComparison]::Ordinal)) {
                    continue
                }

                $referencedModule = Get-ModuleNameFromRepoRelativePath -RelativePath $relativeReference
                if ($referencedModule -eq $moduleName) {
                    continue
                }

                [void]$referencedModules.Add($referencedModule)
            }
        }

        $refsList = @($referencedModules)
        $refsCount = $refsList.Count

        $rows.Add([pscustomobject]@{
                Module     = $moduleName
                Refs       = $refsCount
                Headroom   = $referenceCap - $refsCount
                References = if ($refsCount -eq 0) { '' } else { ($refsList -join ', ') }
            })
    }

    return $rows
}

function Test-SequenceEqual {
    param(
        [Parameter(Mandatory)]
        [object[]]$Left,

        [Parameter(Mandatory)]
        [object[]]$Right
    )

    if ($Left.Count -ne $Right.Count) {
        return $false
    }

    return $null -eq (Compare-Object -ReferenceObject $Left -DifferenceObject $Right)
}

function Get-RuleCatalogEntries {
    param(
        [Parameter(Mandatory)]
        [string]$CatalogPath
    )

    $entries = [System.Collections.Generic.List[object]]::new()

    foreach ($line in Get-Content -Path $CatalogPath) {
        if ($line -notmatch '^\|\s*BP-\d{3}\s*\|') {
            continue
        }

        $cells = $line.Trim().Trim('|').Split('|').ForEach({ $_.Trim() })
        if ($cells.Count -ne 5) {
            throw "Catalog row has an unexpected number of columns: $line"
        }

        $entries.Add([pscustomobject]@{
                RuleId             = $cells[0]
                Rule               = $cells[1]
                PrimaryEnforcement = $cells[2]
                TypicalArtifact    = $cells[3]
                WaiverScope        = $cells[4]
            })
    }

    return $entries
}

function Get-BlueprintNonNegotiables {
    param(
        [Parameter(Mandatory)]
        [string]$BlueprintPath
    )

    $items = [System.Collections.Generic.List[string]]::new()
    $insideSection = $false

    foreach ($line in Get-Content -Path $BlueprintPath) {
        if (-not $insideSection) {
            if ($line.Trim() -eq '## Non-Negotiables') {
                $insideSection = $true
            }

            continue
        }

        if ($line.StartsWith('## ')) {
            break
        }

        if ($line -match '^-\s+') {
            $items.Add(($line -replace '^-\s+', '').Trim())
        }
    }

    return $items
}

function Read-RuleEnforcementMap {
    param(
        [Parameter(Mandatory)]
        [string]$MapPath
    )

    $map = Read-JsonFileAsHashtable -Path $MapPath
    $requiredKeys = @('$schema', 'schemaVersion', 'rules') | Sort-Object
    $actualKeys = $map.Keys | Sort-Object

    if (-not (Test-SequenceEqual -Left @($actualKeys) -Right @($requiredKeys))) {
        throw "Rule enforcement map '$MapPath' does not match the canonical root fields."
    }

    if ($map['schemaVersion'] -ne '1.0') {
        throw "Rule enforcement map '$MapPath' must use schemaVersion 1.0."
    }

    if ($map['rules'] -isnot [hashtable]) {
        throw "Rule enforcement map '$MapPath' must contain a rules object."
    }

    return $map
}

function Test-RuleEnforcementMap {
    param(
        [Parameter(Mandatory)]
        [hashtable]$Map,

        [Parameter(Mandatory)]
        [string[]]$KnownRuleIds,

        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    function Test-IsGovernedRuleEnforcementArtifact {
        param(
            [Parameter(Mandatory)]
            [string]$RelativePath
        )

        $normalizedPath = $RelativePath.Replace('\\', '/')
        return $normalizedPath.Equals('web/eslint.config.js', [System.StringComparison]::Ordinal) `
            -or $normalizedPath.StartsWith('scripts/', [System.StringComparison]::Ordinal) `
            -or $normalizedPath.StartsWith('tests/Architecture.Tests/', [System.StringComparison]::Ordinal) `
            -or $normalizedPath.StartsWith('tests/Integration.Tests/', [System.StringComparison]::Ordinal) `
            -or $normalizedPath.StartsWith('web/tests/unit/', [System.StringComparison]::Ordinal) `
            -or $normalizedPath.StartsWith('web/tests/e2e/', [System.StringComparison]::Ordinal)
    }

    $ruleKeys = @($Map['rules'].Keys)
    if (-not (Test-SequenceEqual -Left @($ruleKeys | Sort-Object) -Right @($KnownRuleIds | Sort-Object))) {
        throw 'Rule enforcement map keys must match the rule catalog ids exactly.'
    }

    foreach ($ruleId in $KnownRuleIds) {
        if (-not $Map['rules'].ContainsKey($ruleId)) {
            throw "Rule enforcement map is missing rule '$ruleId'."
        }

        $artifacts = @($Map['rules'][$ruleId])
        if ($artifacts.Count -eq 0) {
            throw "Rule '$ruleId' must declare at least one enforcing artifact."
        }

        foreach ($artifact in $artifacts) {
            if ([string]::IsNullOrWhiteSpace([string]$artifact)) {
                throw "Rule '$ruleId' includes an empty enforcing artifact path."
            }

            if (-not (Test-IsGovernedRuleEnforcementArtifact -RelativePath ([string]$artifact))) {
                throw "Rule '$ruleId' references '$artifact', which is not a governed executable, script, or lint artifact."
            }

            $fullPath = Join-Path -Path $RepositoryRoot -ChildPath ([string]$artifact).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
            if (-not (Test-Path -Path $fullPath -PathType Leaf)) {
                throw "Rule '$ruleId' references a missing enforcing artifact '$artifact'."
            }
        }
    }
}

function Get-RequiredModuleSpecFields {
    return @(
        'moduleName',
        'moduleKey',
        'moduleOwner',
        'displayName',
        'routePrefix',
        'schemaName',
        'isCore',
        'defaultEnabled',
        'moduleNamespace',
        'publishesIntegrationEvents',
        'consumesIntegrationEvents',
        'exposesSynchronousReadContracts',
        'needsProcessManager',
        'hasOptedInIdempotentCommands',
        'hasBackgroundWorkers',
        'introducesExternallyConsumedHttpContracts',
        'exposesMachineConsumableEndpoints',
        'emitsBrowserRealtimeNotifications',
        'crossModulePublicContractDependencies',
        'configurationSectionName',
        'operatorManagedSettings',
        'settingsFanoutTags',
        'hasOperatorManagedSettings',
        'requiresRecentAuthStepUp',
        'hasFrontendSurface'
    )
}

function Get-SupportedAdoptionModules {
    return @(
        'Platform',
        'Identity',
        'SampleFeature',
        'Blog',
        'KnowledgeBase',
        'Admin'
    )
}

function Get-AdoptionModulePresets {
    $allModules = Get-SupportedAdoptionModules

    return [ordered]@{
        'clean-base'                     = @('Platform', 'Identity')
        'recommended-first-product-base' = @('Platform', 'Identity', 'SampleFeature', 'Blog')
        'full-teaching-set'             = @($allModules)
    }
}

function Resolve-AdoptionModulePresetForKeepModules {
    param(
        [Parameter(Mandatory)]
        [string[]]$KeepModules
    )

    $normalizedKeepModules = @(Get-SupportedAdoptionModules | Where-Object { $KeepModules -contains $_ })
    foreach ($entry in (Get-AdoptionModulePresets).GetEnumerator()) {
        if (Test-SequenceEqual -Left $normalizedKeepModules -Right @($entry.Value)) {
            return [string]$entry.Key
        }
    }

    return 'custom'
}

function Resolve-AdoptionKeepModules {
    param(
        [Parameter(Mandatory)]
        [hashtable]$Spec
    )

    $supportedModules = Get-SupportedAdoptionModules
    $presets = Get-AdoptionModulePresets
    $modulePreset = [string]$Spec['modulePreset']

    if ($modulePreset -eq 'custom') {
        if (-not $Spec.ContainsKey('keepModules') -or @($Spec['keepModules']).Count -eq 0) {
            throw "modulePreset 'custom' requires a non-empty keepModules array."
        }

        $requestedModules = @($Spec['keepModules'] | ForEach-Object { [string]$_ })
    }
    else {
        if (-not $presets.Contains($modulePreset)) {
            throw "Unknown modulePreset '$modulePreset'."
        }

        $presetModules = @($presets[$modulePreset])
        $requestedModules = if ($Spec.ContainsKey('keepModules') -and $null -ne $Spec['keepModules']) {
            @($Spec['keepModules'] | ForEach-Object { [string]$_ })
        }
        else {
            @($presetModules)
        }

        $normalizedPresetModules = @($presetModules | Sort-Object -Unique)
        $normalizedRequestedModules = @($requestedModules | Sort-Object -Unique)
        if (-not (Test-SequenceEqual -Left $normalizedRequestedModules -Right $normalizedPresetModules)) {
            throw "keepModules must match the selected modulePreset '$modulePreset' exactly, or be omitted."
        }
    }

    $unknownModules = @($requestedModules | Where-Object { $supportedModules -notcontains $_ } | Sort-Object -Unique)
    if ($unknownModules.Count -gt 0) {
        throw "keepModules contains unsupported modules: $($unknownModules -join ', ')."
    }

    $distinctRequestedModules = @($requestedModules | Sort-Object -Unique)
    if ($distinctRequestedModules.Count -ne $requestedModules.Count) {
        throw 'keepModules must not contain duplicates.'
    }

    foreach ($requiredModule in @('Platform', 'Identity')) {
        if ($requestedModules -notcontains $requiredModule) {
            throw "keepModules must always include '$requiredModule'."
        }
    }

    return @($supportedModules | Where-Object { $requestedModules -contains $_ })
}

function Get-PathComparisonMode {
    if ($IsWindows) {
        return [System.StringComparison]::OrdinalIgnoreCase
    }

    return [System.StringComparison]::Ordinal
}

function Test-IsSameOrDescendantPath {
    param(
        [Parameter(Mandatory)]
        [string]$BasePath,

        [Parameter(Mandatory)]
        [string]$CandidatePath
    )

    $comparison = Get-PathComparisonMode
    $normalizedBasePath = [System.IO.Path]::GetFullPath($BasePath).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $normalizedCandidatePath = [System.IO.Path]::GetFullPath($CandidatePath).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)

    if ($normalizedCandidatePath.Equals($normalizedBasePath, $comparison)) {
        return $true
    }

    $normalizedBaseWithSeparator = $normalizedBasePath + [System.IO.Path]::DirectorySeparatorChar
    return $normalizedCandidatePath.StartsWith($normalizedBaseWithSeparator, $comparison)
}

function Resolve-AdoptionScratchRoot {
    param(
        [Parameter(Mandatory)]
        [string]$Mode,

        [Parameter(Mandatory)]
        [string]$Root,

        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $comparison = Get-PathComparisonMode

    switch ($Mode) {
        'repo-local' {
            $repositoryBasePath = [System.IO.Path]::GetFullPath($RepositoryRoot)
            $resolvedPath = [System.IO.Path]::GetFullPath((Join-Path -Path $RepositoryRoot -ChildPath $Root))

            if (-not (Test-IsSameOrDescendantPath -BasePath $repositoryBasePath -CandidatePath $resolvedPath)) {
                throw "repo-local scratch.root must stay under the repository root '$repositoryBasePath'."
            }

            if ($resolvedPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar).Equals($repositoryBasePath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar), $comparison)) {
                throw 'repo-local scratch.root must resolve to a dedicated child directory under the repository root.'
            }

            return $resolvedPath
        }
        'os-temp' {
            $tempBasePath = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
            $resolvedPath = if ([System.IO.Path]::IsPathRooted($Root)) {
                [System.IO.Path]::GetFullPath($Root)
            }
            else {
                [System.IO.Path]::GetFullPath((Join-Path -Path $tempBasePath -ChildPath $Root))
            }

            if (-not (Test-IsSameOrDescendantPath -BasePath $tempBasePath -CandidatePath $resolvedPath)) {
                throw "os-temp scratch.root must stay under the operating system temp directory '$tempBasePath'."
            }

            if ($resolvedPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar).Equals($tempBasePath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar), $comparison)) {
                throw 'os-temp scratch.root must resolve to a dedicated child directory under the operating system temp directory.'
            }

            return $resolvedPath
        }
        default {
            throw "Unsupported scratch mode '$Mode'."
        }
    }
}

function Get-AdoptionValidationProfilePlan {
    param(
        [string]$RepositoryRoot = (Get-RepositoryRoot),

        [Parameter(Mandatory)]
        [string]$Profile
    )

    $manifestPath = Join-Path -Path $RepositoryRoot -ChildPath 'governance/ci-gates.json'
    $manifest = Read-JsonFileAsHashtable -Path $manifestPath
    $allGateIds = @(@($manifest['gates']) | ForEach-Object { [string]$_['id'] })

    $plan = switch ($Profile) {
        'none' {
            [ordered]@{
                Profile     = 'none'
                RunnerMode  = 'none'
                GateIds     = @()
                Description = 'skip validation entirely'
            }
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

            $unknownGateIds = @($gateIds | Where-Object { $allGateIds -notcontains $_ })
            if ($unknownGateIds.Count -gt 0) {
                throw "Adoption validation profile '$Profile' references gate ids that do not exist in governance/ci-gates.json: $($unknownGateIds -join ', ')."
            }

            [ordered]@{
                Profile     = 'fast'
                RunnerMode  = 'subset'
                GateIds     = @($gateIds)
                Description = "manifest-backed subset via Invoke-LocalGates.ps1: $($gateIds -join ', ')"
            }
        }
        'full' {
            [ordered]@{
                Profile     = 'full'
                RunnerMode  = 'all'
                GateIds     = @($allGateIds)
                Description = 'all local manifest gates in CI order via Invoke-LocalGates.ps1 (honors skipLocally defaults)'
            }
        }
        default {
            throw "Unknown adoption validation profile '$Profile'."
        }
    }

    return $plan
}

function Test-AdoptionSpec {
    param(
        [Parameter(Mandatory)]
        [hashtable]$Spec
    )

    $requiredRootFields = @(
        '$schema',
        'schemaVersion',
        'projectName',
        'projectSlug',
        'modulePreset',
        'scratch',
        'database',
        'validation'
    )

    foreach ($field in $requiredRootFields) {
        if (-not $Spec.ContainsKey($field)) {
            throw "Adoption spec is missing required field '$field'."
        }
    }

    $allowedRootFields = @(
        '$schema',
        'schemaVersion',
        'projectName',
        'projectSlug',
        'solutionFileName',
        'modulePreset',
        'keepModules',
        'scratch',
        'database',
        'validation'
    )

    $unknownRootFields = @($Spec.Keys | Where-Object { $allowedRootFields -notcontains $_ } | Sort-Object)
    if ($unknownRootFields.Count -gt 0) {
        throw "Adoption spec contains unsupported root fields: $($unknownRootFields -join ', ')."
    }

    if ([string]$Spec['$schema'] -ne 'https://dotnet-modulith-baseline.local/schemas/adopt-spec.v1.schema.json') {
        throw "Adoption spec must use the canonical schema id."
    }

    if ([string]$Spec['schemaVersion'] -ne '1.0') {
        throw "Adoption spec must use schemaVersion 1.0."
    }

    if ([string]::IsNullOrWhiteSpace([string]$Spec['projectName'])) {
        throw 'projectName must be a non-empty string.'
    }

    if ([string]$Spec['projectSlug'] -notmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') {
        throw 'projectSlug must be lowercase kebab-case.'
    }

    if ($Spec.ContainsKey('solutionFileName') -and -not [string]::IsNullOrWhiteSpace([string]$Spec['solutionFileName'])) {
        if ([string]$Spec['solutionFileName'] -notmatch '^[A-Za-z0-9_.-]+\.sln$') {
            throw 'solutionFileName must be a file name ending in .sln.'
        }
    }

    $modulePreset = [string]$Spec['modulePreset']
    if ($modulePreset -notin @('clean-base', 'recommended-first-product-base', 'full-teaching-set', 'custom')) {
        throw "modulePreset must be one of: clean-base, recommended-first-product-base, full-teaching-set, custom."
    }

    if ($Spec.ContainsKey('keepModules') -and $null -ne $Spec['keepModules']) {
        if ($Spec['keepModules'] -is [string] -or $Spec['keepModules'] -isnot [System.Collections.IEnumerable]) {
            throw 'keepModules must be an array when supplied.'
        }
    }

    if ($Spec['scratch'] -isnot [hashtable]) {
        throw 'scratch must be an object.'
    }

    foreach ($field in @('mode', 'root')) {
        if (-not $Spec['scratch'].ContainsKey($field)) {
            throw "scratch must include '$field'."
        }
    }

    $scratchMode = [string]$Spec['scratch']['mode']
    if ($scratchMode -notin @('repo-local', 'os-temp')) {
        throw "scratch.mode must be one of: repo-local, os-temp."
    }

    $scratchRoot = [string]$Spec['scratch']['root']
    if ([string]::IsNullOrWhiteSpace($scratchRoot)) {
        throw 'scratch.root must be a non-empty string.'
    }

    if ($scratchMode -eq 'repo-local' -and [System.IO.Path]::IsPathRooted($scratchRoot)) {
        throw 'scratch.root must be a repo-relative path when scratch.mode is repo-local.'
    }

    if ($Spec['database'] -isnot [hashtable]) {
        throw 'database must be an object.'
    }

    if (-not $Spec['database'].ContainsKey('resetLocalDatabase')) {
        throw "database must include 'resetLocalDatabase'."
    }

    if ($Spec['database']['resetLocalDatabase'] -isnot [bool]) {
        throw 'database.resetLocalDatabase must be a boolean.'
    }

    if ($Spec['validation'] -isnot [hashtable]) {
        throw 'validation must be an object.'
    }

    if (-not $Spec['validation'].ContainsKey('profile')) {
        throw "validation must include 'profile'."
    }

    $validationProfile = [string]$Spec['validation']['profile']
    if ($validationProfile -notin @('none', 'fast', 'full')) {
        throw "validation.profile must be one of: none, fast, full."
    }

    [void](Resolve-AdoptionScratchRoot -Mode $scratchMode -Root $scratchRoot -RepositoryRoot (Get-RepositoryRoot))
    [void](Get-AdoptionValidationProfilePlan -RepositoryRoot (Get-RepositoryRoot) -Profile $validationProfile)
    [void](Resolve-AdoptionKeepModules -Spec $Spec)
}

function Test-ModuleSpec {
    param(
        [Parameter(Mandatory)]
        [hashtable]$Spec
    )

    foreach ($field in Get-RequiredModuleSpecFields) {
        if (-not $Spec.ContainsKey($field)) {
            throw "Module spec is missing required field '$field'."
        }
    }

    $booleanFields = @(
        'isCore',
        'defaultEnabled',
        'publishesIntegrationEvents',
        'consumesIntegrationEvents',
        'exposesSynchronousReadContracts',
        'needsProcessManager',
        'hasOptedInIdempotentCommands',
        'hasBackgroundWorkers',
        'introducesExternallyConsumedHttpContracts',
        'exposesMachineConsumableEndpoints',
        'emitsBrowserRealtimeNotifications',
        'hasOperatorManagedSettings',
        'requiresRecentAuthStepUp',
        'hasFrontendSurface'
    )

    foreach ($field in $booleanFields) {
        if ($Spec[$field] -isnot [bool]) {
            throw "Module spec field '$field' must be a boolean."
        }
    }

    if ($Spec['moduleName'] -notmatch '^[A-Z][A-Za-z0-9]+$') {
        throw "moduleName must be PascalCase alphanumeric."
    }

    if ($Spec['moduleKey'] -notmatch '^[a-z0-9-]+$') {
        throw "moduleKey must be lowercase kebab-case."
    }

    if ($Spec['routePrefix'] -notmatch '^/[a-z0-9-]+$') {
        throw "routePrefix must start with '/' and use lowercase kebab-case."
    }

    if ($Spec['schemaName'] -notmatch '^[a-z0-9_]+$') {
        throw "schemaName must be lowercase snake_case."
    }

    if ($Spec['configurationSectionName'] -notmatch '^Modules:[A-Za-z0-9]+$') {
        throw "configurationSectionName must start with 'Modules:' and end with a module section name."
    }

    if ($Spec['crossModulePublicContractDependencies'] -isnot [System.Collections.IEnumerable]) {
        throw "crossModulePublicContractDependencies must be an array."
    }

    if ($Spec['operatorManagedSettings'] -isnot [System.Collections.IEnumerable]) {
        throw "operatorManagedSettings must be an array."
    }

    if ($Spec['settingsFanoutTags'] -isnot [System.Collections.IEnumerable]) {
        throw "settingsFanoutTags must be an array."
    }

    $declaredCrossModuleDependencies = [System.Collections.Generic.List[string]]::new()
    foreach ($dependency in @($Spec['crossModulePublicContractDependencies'])) {
        if ([string]::IsNullOrWhiteSpace([string]$dependency)) {
            throw "crossModulePublicContractDependencies must contain only non-empty module names."
        }

        $dependencyName = [string]$dependency
        if ($dependencyName -notmatch '^[A-Z][A-Za-z0-9]+$') {
            throw "crossModulePublicContractDependencies entries must be PascalCase module names."
        }

        if ($dependencyName -eq [string]$Spec['moduleName']) {
            throw "crossModulePublicContractDependencies must not include the scaffolding module itself."
        }

        $declaredCrossModuleDependencies.Add($dependencyName)
    }

    $distinctCrossModuleDependencies = @($declaredCrossModuleDependencies | Sort-Object -Unique)
    if ($distinctCrossModuleDependencies.Count -ne $declaredCrossModuleDependencies.Count) {
        throw "crossModulePublicContractDependencies must not contain duplicates."
    }

    $knownModules = @(Get-RepositoryModuleNames)
    if ($knownModules.Count -gt 0) {
        $unknownDependencies = @($declaredCrossModuleDependencies | Where-Object { $knownModules -notcontains $_ })
        if ($unknownDependencies.Count -gt 0) {
            throw "crossModulePublicContractDependencies must reference existing modules in src/Modules. Unknown modules: $($unknownDependencies -join ', ')."
        }
    }

    $crossModuleReferenceCap = Get-CrossModulePublicContractsReferenceCap
    if ($declaredCrossModuleDependencies.Count -gt $crossModuleReferenceCap) {
        throw "crossModulePublicContractDependencies declares $($declaredCrossModuleDependencies.Count) modules, which exceeds the BP-033 cap of $crossModuleReferenceCap."
    }

    $managedSettingNames = [System.Collections.Generic.List[string]]::new()
    foreach ($setting in @($Spec['operatorManagedSettings'])) {
        if ($setting -isnot [hashtable]) {
            throw "operatorManagedSettings must contain only objects."
        }

        foreach ($requiredField in @('name', 'displayName', 'type', 'defaultValue', 'reloadSafe')) {
            if (-not $setting.ContainsKey($requiredField)) {
                throw "Each operatorManagedSettings entry must include '$requiredField'."
            }
        }

        $name = [string]$setting['name']
        if ($name -notmatch '^[A-Z][A-Za-z0-9]+$') {
            throw "operatorManagedSettings names must be PascalCase alphanumeric."
        }

        $managedSettingNames.Add($name)

        if ([string]::IsNullOrWhiteSpace([string]$setting['displayName'])) {
            throw "operatorManagedSettings displayName values must be non-empty strings."
        }

        $type = [string]$setting['type']
        if ($type -notin @('string', 'int', 'bool')) {
            throw "operatorManagedSettings types must be one of: string, int, bool."
        }

        if ($setting['reloadSafe'] -isnot [bool]) {
            throw "operatorManagedSettings reloadSafe values must be booleans."
        }

        switch ($type) {
            'string' {
                if ($setting['defaultValue'] -isnot [string]) {
                    throw "operatorManagedSettings string defaultValue values must be strings."
                }
            }
            'int' {
                $value = $setting['defaultValue']
                $isWholeNumber = $value -is [byte] -or $value -is [int16] -or $value -is [int32] -or $value -is [int64]
                if (-not $isWholeNumber) {
                    throw "operatorManagedSettings int defaultValue values must be whole numbers."
                }
            }
            'bool' {
                if ($setting['defaultValue'] -isnot [bool]) {
                    throw "operatorManagedSettings bool defaultValue values must be booleans."
                }
            }
        }
    }

    $distinctManagedSettingNames = @($managedSettingNames | Sort-Object -Unique)
    if ($distinctManagedSettingNames.Count -ne $managedSettingNames.Count) {
        throw "operatorManagedSettings must not contain duplicate names."
    }

    if ($managedSettingNames.Count -gt 0 -and -not [bool]$Spec['hasOperatorManagedSettings']) {
        throw "operatorManagedSettings entries require hasOperatorManagedSettings to be true."
    }

    foreach ($tag in @($Spec['settingsFanoutTags'])) {
        if ([string]::IsNullOrWhiteSpace([string]$tag)) {
            throw "settingsFanoutTags must contain only non-empty strings."
        }

        if ([string]$tag -notmatch '^[A-Z][A-Za-z0-9]+$') {
            throw "settingsFanoutTags entries must be PascalCase alphanumeric names."
        }
    }

    $distinctFanoutTags = @($Spec['settingsFanoutTags'] | Sort-Object -Unique)
    if ($distinctFanoutTags.Count -ne @($Spec['settingsFanoutTags']).Count) {
        throw "settingsFanoutTags must not contain duplicates."
    }

    if (@($Spec['settingsFanoutTags']).Count -gt 0 -and -not [bool]$Spec['hasOperatorManagedSettings']) {
        throw "settingsFanoutTags require hasOperatorManagedSettings to be true."
    }

    if (@($Spec['settingsFanoutTags']).Count -gt 0 -and -not [bool]$Spec['hasFrontendSurface']) {
        throw "settingsFanoutTags require hasFrontendSurface to be true."
    }
}

function Resolve-TokenizedPath {
    param(
        [Parameter(Mandatory)]
        [string]$Template,

        [Parameter(Mandatory)]
        [hashtable]$Tokens
    )

    $resolved = $Template
    foreach ($token in $Tokens.GetEnumerator()) {
        $resolved = $resolved.Replace("{$($token.Key)}", [string]$token.Value)
    }

    return $resolved
}

function Get-ExpectedScaffoldPaths {
    param(
        [Parameter(Mandatory)]
        [hashtable]$Contract,

        [Parameter(Mandatory)]
        [hashtable]$Spec
    )

    $tokens = [ordered]@{
        ModuleName = [string]$Spec['moduleName']
        ModuleKey  = [string]$Spec['moduleKey']
    }

    $paths = [System.Collections.Generic.List[string]]::new()

    foreach ($path in $Contract['requiredPaths']) {
        $paths.Add((Resolve-TokenizedPath -Template $path -Tokens $tokens))
    }

    foreach ($capability in $Contract['optionalPathsByCapability'].Keys | Sort-Object) {
        if (-not [bool]$Spec[$capability]) {
            continue
        }

        foreach ($path in $Contract['optionalPathsByCapability'][$capability]) {
            $paths.Add((Resolve-TokenizedPath -Template $path -Tokens $tokens))
        }
    }

    foreach ($conditionalGroup in @($Contract['conditionalPaths'])) {
        if ($null -eq $conditionalGroup) {
            continue
        }

        $requiredCapabilities = @($conditionalGroup['allOf'])
        if ($requiredCapabilities.Count -eq 0) {
            continue
        }

        $matches = $true
        foreach ($capability in $requiredCapabilities) {
            if (-not [bool]$Spec[[string]$capability]) {
                $matches = $false
                break
            }
        }

        if (-not $matches) {
            continue
        }

        foreach ($path in @($conditionalGroup['paths'])) {
            $paths.Add((Resolve-TokenizedPath -Template ([string]$path) -Tokens $tokens))
        }
    }

    return $paths | Sort-Object -Unique
}

function Test-WaiverFile {
    param(
        [Parameter(Mandatory)]
        [string]$WaiverPath,

        [Parameter(Mandatory)]
        [string[]]$KnownRuleIds
    )

    $waiverFile = Read-JsonFileAsHashtable -Path $WaiverPath
    $requiredRootKeys = @('$schema', 'schemaVersion', 'waivers')
    $actualRootKeys = $waiverFile.Keys | Sort-Object
    $expectedRootKeys = $requiredRootKeys | Sort-Object

    if (-not (Test-SequenceEqual -Left @($actualRootKeys) -Right @($expectedRootKeys))) {
        throw "Waiver file '$WaiverPath' does not match the canonical root fields."
    }

    if ($waiverFile['$schema'] -ne './schema/waiver-file.v1.schema.json') {
        throw "Waiver file '$WaiverPath' must use the canonical file schema."
    }

    if ($waiverFile['schemaVersion'] -ne '1.0') {
        throw "Waiver file '$WaiverPath' must use schemaVersion 1.0."
    }

    if ($waiverFile['waivers'] -isnot [System.Collections.IEnumerable]) {
        throw "Waiver file '$WaiverPath' must contain a waivers array."
    }

    $requiredEntryKeys = @(
        'waiver_id',
        'rule_id',
        'scope',
        'owner',
        'created_utc',
        'expires_utc',
        'reason',
        'linked_follow_up',
        'removal_condition'
    )

    $seenWaiverIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($waiver in $waiverFile['waivers']) {
        if ($waiver -isnot [hashtable]) {
            throw "Waiver entries in '$WaiverPath' must deserialize as objects."
        }

        $entryKeys = $waiver.Keys | Sort-Object
        if (-not (Test-SequenceEqual -Left @($entryKeys) -Right @($requiredEntryKeys | Sort-Object))) {
            throw "Waiver entry in '$WaiverPath' does not match the canonical entry fields."
        }

        if ($waiver['waiver_id'] -notmatch '^WVR-[0-9]{4}$') {
            throw "Waiver id '$($waiver['waiver_id'])' in '$WaiverPath' is invalid."
        }

        if (-not $seenWaiverIds.Add([string]$waiver['waiver_id'])) {
            throw "Waiver id '$($waiver['waiver_id'])' appears more than once in '$WaiverPath'."
        }

        if ($KnownRuleIds -notcontains [string]$waiver['rule_id']) {
            throw "Waiver '$($waiver['waiver_id'])' references unknown rule id '$($waiver['rule_id'])'."
        }

        foreach ($field in @('scope', 'owner', 'reason', 'linked_follow_up', 'removal_condition')) {
            if ([string]::IsNullOrWhiteSpace([string]$waiver[$field])) {
                throw "Waiver '$($waiver['waiver_id'])' has an empty '$field'."
            }
        }

        $createdUtc = [DateTimeOffset]::Parse([string]$waiver['created_utc'])
        $expiresUtc = [DateTimeOffset]::Parse([string]$waiver['expires_utc'])
        if ($expiresUtc -le $createdUtc) {
            throw "Waiver '$($waiver['waiver_id'])' expires before or at its creation time."
        }

        if ($expiresUtc -le [DateTimeOffset]::UtcNow) {
            throw "Waiver '$($waiver['waiver_id'])' is expired."
        }
    }
}