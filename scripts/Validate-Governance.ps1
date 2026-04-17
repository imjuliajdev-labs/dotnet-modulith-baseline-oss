$ErrorActionPreference = 'Stop'

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

$repositoryRoot = Get-RepositoryRoot
$catalogPath = Join-Path -Path $repositoryRoot -ChildPath 'docs/RULE_TO_GATE_CATALOG.md'
$ruleEnforcementMapPath = Join-Path -Path $repositoryRoot -ChildPath 'docs/RULE_ENFORCEMENT_MAP.json'
$blueprintPath = Join-Path -Path $repositoryRoot -ChildPath 'docs/BLUEPRINT.md'
$qualityGatesPath = Join-Path -Path $repositoryRoot -ChildPath 'docs/QUALITY_GATES.md'
$addModulePath = Join-Path -Path $repositoryRoot -ChildPath 'docs/ADD_MODULE.md'
$readmePath = Join-Path -Path $repositoryRoot -ChildPath 'README.md'
$agentsPath = Join-Path -Path $repositoryRoot -ChildPath 'AGENTS.md'
$waiverRoot = Join-Path -Path $repositoryRoot -ChildPath 'waivers'
$ciWorkflowPath = Join-Path -Path $repositoryRoot -ChildPath '.github/workflows/ci.yml'
$ciGateManifestPath = Join-Path -Path $repositoryRoot -ChildPath 'governance/ci-gates.json'

$governedCoreDocumentPaths = @(
    $blueprintPath,
    $qualityGatesPath,
    $addModulePath,
    $catalogPath,
    $ruleEnforcementMapPath,
    $readmePath,
    $agentsPath
)

function Get-WorkflowJobBlock {
    param(
        [string[]]$WorkflowLines,

        [Parameter(Mandatory)]
        [string]$JobId
    )

    $jobHeader = "  ${JobId}:"
    $jobStartIndex = [Array]::IndexOf($WorkflowLines, $jobHeader)
    if ($jobStartIndex -lt 0) {
        throw "CI workflow is missing required job '$JobId'."
    }

    $jobBlockLines = [System.Collections.Generic.List[string]]::new()
    for ($lineIndex = $jobStartIndex + 1; $lineIndex -lt $WorkflowLines.Length; $lineIndex++) {
        $line = $WorkflowLines[$lineIndex]
        if ($line -match '^  [a-z0-9-]+:\s*$') {
            break
        }

        $jobBlockLines.Add($line)
    }

    return $jobBlockLines.ToArray()
}

function Get-WorkflowJobNeeds {
    param(
        [string[]]$WorkflowLines,

        [Parameter(Mandatory)]
        [string]$JobId
    )

    $jobBlock = Get-WorkflowJobBlock -WorkflowLines $WorkflowLines -JobId $JobId
    for ($lineIndex = 0; $lineIndex -lt $jobBlock.Length; $lineIndex++) {
        $line = $jobBlock[$lineIndex]
        if ($line -match '^    needs:\s*(?<need>[a-z0-9-]+)\s*$') {
            return @($Matches['need'])
        }

        if ($line -ne '    needs:') {
            continue
        }

        $needs = [System.Collections.Generic.List[string]]::new()
        for ($needsLineIndex = $lineIndex + 1; $needsLineIndex -lt $jobBlock.Length; $needsLineIndex++) {
            $needsLine = $jobBlock[$needsLineIndex]
            if ($needsLine -match '^      - (?<need>[a-z0-9-]+)$') {
                $needs.Add($Matches['need'])
                continue
            }

            if ($needsLine -match '^    \S') {
                break
            }
        }

        return $needs.ToArray()
    }

    return @()
}

function Test-CiWorkflowShape {
    param(
        [Parameter(Mandatory)]
        [string]$WorkflowPath,

        [Parameter(Mandatory)]
        [string]$ManifestPath
    )

    if (-not (Test-Path -Path $ManifestPath -PathType Leaf)) {
        throw "CI gate manifest is missing at '$ManifestPath'."
    }

    $manifest = Read-JsonFileAsHashtable -Path $ManifestPath
    if (-not $manifest.ContainsKey('jobs')) {
        throw "CI gate manifest '$ManifestPath' must declare a 'jobs' section."
    }

    $workflowLines = Get-Content -Path $WorkflowPath

    foreach ($jobEntry in @($manifest['jobs'])) {
        if ($jobEntry -isnot [hashtable]) {
            throw "CI gate manifest jobs entries must be objects."
        }

        $jobId = [string]$jobEntry['id']
        $expectedNeeds = @()
        if ($jobEntry.ContainsKey('needs') -and $null -ne $jobEntry['needs']) {
            $expectedNeeds = @($jobEntry['needs'] | ForEach-Object { [string]$_ })
        }

        $actualNeeds = @(Get-WorkflowJobNeeds -WorkflowLines $workflowLines -JobId $jobId)

        $normalizedActualNeeds = @($actualNeeds | Sort-Object)
        $normalizedExpectedNeeds = @($expectedNeeds | Sort-Object)

        if ($normalizedActualNeeds.Count -ne $normalizedExpectedNeeds.Count -or
            ($normalizedExpectedNeeds.Count -gt 0 -and
             $null -ne (Compare-Object -ReferenceObject $normalizedActualNeeds -DifferenceObject $normalizedExpectedNeeds))) {
            throw "CI workflow job '$jobId' must declare needs '$($expectedNeeds -join ', ')' to match governance/ci-gates.json."
        }
    }

    # Reject orphan dispatch invocations: every Invoke-CiGate.ps1 -Id <id> in the
    # workflow must point at a manifest gate.
    $manifestGateIds = @($manifest['gates'] | ForEach-Object { [string]$_['id'] })
    $dispatchRegex = [regex]'\./scripts/Invoke-CiGate\.ps1\s+-Id\s+(?<id>[A-Za-z0-9-]+)'
    $workflowText = $workflowLines -join "`n"
    foreach ($match in $dispatchRegex.Matches($workflowText)) {
        $dispatchedId = $match.Groups['id'].Value
        if ($manifestGateIds -notcontains $dispatchedId) {
            throw "CI workflow dispatches unknown gate id '$dispatchedId'. Add it to '$ManifestPath' or remove the dispatch."
        }
    }

    foreach ($gateId in $manifestGateIds) {
        if (-not ($workflowText -match [regex]::Escape("./scripts/Invoke-CiGate.ps1 -Id $gateId"))) {
            throw "CI workflow does not dispatch manifest gate '$gateId'. Add a step that runs './scripts/Invoke-CiGate.ps1 -Id $gateId'."
        }
    }
}

function Get-DurableMarkdownPaths {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $docsRoot = Join-Path -Path $RepositoryRoot -ChildPath 'docs'
    $markdownPaths = @(Get-ChildItem -Path $docsRoot -Recurse -File -Filter '*.md' |
        ForEach-Object { Convert-ToRepoRelativePath -Root $RepositoryRoot -Path $_.FullName } |
        Where-Object { -not $_.StartsWith('docs/_local/', [System.StringComparison]::Ordinal) })

    $markdownPaths += 'README.md'
    $markdownPaths += 'AGENTS.md'

    return @($markdownPaths | Sort-Object -Unique)
}

function Test-MarkdownRelativeLinks {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string[]]$MarkdownPaths
    )

    $linkPattern = [regex]'\[[^\]]+\]\((?<target>[^)]+)\)'
    $brokenLinks = [System.Collections.Generic.List[string]]::new()

    foreach ($relativePath in $MarkdownPaths) {
        $fullPath = Join-Path -Path $RepositoryRoot -ChildPath ($relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        $sourceDirectory = Split-Path -Parent $fullPath
        $markdown = Get-Content -Path $fullPath -Raw

        foreach ($match in $linkPattern.Matches($markdown)) {
            $target = $match.Groups['target'].Value
            if ([string]::IsNullOrWhiteSpace($target) -or
                $target.StartsWith('#', [System.StringComparison]::Ordinal) -or
                $target.StartsWith('http://', [System.StringComparison]::OrdinalIgnoreCase) -or
                $target.StartsWith('https://', [System.StringComparison]::OrdinalIgnoreCase) -or
                $target.StartsWith('mailto:', [System.StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            $normalizedTarget = $target.Split('#', 2)[0]
            $resolvedPath = [System.IO.Path]::GetFullPath((Join-Path -Path $sourceDirectory -ChildPath $normalizedTarget))
            if (-not (Test-Path -Path $resolvedPath)) {
                $brokenLinks.Add("$relativePath -> $target")
            }
        }
    }

    if ($brokenLinks.Count -gt 0) {
        throw "Durable markdown docs contain broken relative links: $($brokenLinks -join ', ')"
    }
}

function Test-MarkdownRuleReferences {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string[]]$MarkdownPaths,

        [Parameter(Mandatory)]
        [string[]]$KnownRuleIds
    )

    $ruleIdPattern = [regex]'\bBP-\d{3}\b'
    $unknownReferences = [System.Collections.Generic.List[string]]::new()

    foreach ($relativePath in $MarkdownPaths) {
        $fullPath = Join-Path -Path $RepositoryRoot -ChildPath ($relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        $markdown = Get-Content -Path $fullPath -Raw

        foreach ($match in $ruleIdPattern.Matches($markdown)) {
            $ruleId = $match.Value
            if ($KnownRuleIds -notcontains $ruleId) {
                $unknownReferences.Add("$relativePath -> $ruleId")
            }
        }
    }

    if ($unknownReferences.Count -gt 0) {
        throw "Durable markdown docs reference unknown rule ids: $($unknownReferences -join ', ')"
    }
}

$requiredPaths = @(
    'docs/BLUEPRINT.md',
    'docs/QUALITY_GATES.md',
    'docs/ADD_MODULE.md',
    'docs/RULE_TO_GATE_CATALOG.md',
    'docs/RULE_ENFORCEMENT_MAP.json',
    'README.md',
    'AGENTS.md',
    'docs/schema/rule-enforcement-map.v1.schema.json',
    'docs/adr/README.md',
    'waivers/README.md',
    'waivers/active.waivers.json',
    'waivers/schema/waiver-entry.v1.schema.json',
    'waivers/schema/waiver-file.v1.schema.json',
    'docs/schema/adopt-spec.v1.schema.json',
    'templates/adoption/adopt-spec.example.json',
    'prompts/add-governed-module.md',
    'prompts/adopt-governed-baseline.md',
    'templates/module/scaffold.contract.json',
    'templates/module/module-spec.example.json',
    'scripts/Adopt-Baseline.ps1',
    'scripts/Start-Adoption.ps1',
    'scripts/New-Module.ps1',
    'scripts/Test-ModuleScaffold.ps1',
    'scripts/Test-ModuleScaffoldFrontend.ps1',
    'scripts/Test-ApiHostMissingConfiguration.ps1',
    'tests/Architecture.Tests/Approved/BuildingBlocks.PublicSurface.approved.txt',
    'scripts/Validate-OpenApiCompatibility.ps1',
    'scripts/Validate-DependencyPolicy.ps1',
    'policies/dependency-allowlists/nuget.allowed-packages.txt',
    'policies/dependency-allowlists/pnpm.allowed-packages.txt',
    'tests/Architecture.Tests/Architecture.Tests.csproj',
    '.github/workflows/ci.yml',
    'governance/ci-gates.json',
    'scripts/Invoke-CiGate.ps1',
    'scripts/Invoke-LocalGates.ps1'
)

foreach ($relativePath in $requiredPaths) {
    $fullPath = Join-Path -Path $repositoryRoot -ChildPath ($relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    if (-not (Test-Path -Path $fullPath)) {
        throw "Required governance asset is missing: $relativePath"
    }
}

Test-CiWorkflowShape -WorkflowPath $ciWorkflowPath -ManifestPath $ciGateManifestPath

$catalogEntries = Get-RuleCatalogEntries -CatalogPath $catalogPath
if ($catalogEntries.Count -eq 0) {
    throw 'Rule catalog is empty.'
}

$expectedRuleIds = 1..$catalogEntries.Count | ForEach-Object { 'BP-{0:000}' -f $_ }
$actualRuleIds = $catalogEntries.RuleId
if (-not (Test-SequenceEqual -Left @($actualRuleIds) -Right @($expectedRuleIds))) {
    throw 'Rule ids must be unique and contiguous starting at BP-001.'
}

$allowedPrimaryEnforcements = @(
    'Spec And Scaffold Gate',
    'Build Gate',
    'Architecture Gate',
    'Integration Gate',
    'Frontend Gate',
    'Contract Gate',
    'Data Gate',
    'Security And Redaction Gate'
)

foreach ($entry in $catalogEntries) {
    if ($allowedPrimaryEnforcements -notcontains $entry.PrimaryEnforcement) {
        throw "Catalog entry '$($entry.RuleId)' uses unknown primary enforcement '$($entry.PrimaryEnforcement)'."
    }
}

$nonNegotiables = Get-BlueprintNonNegotiables -BlueprintPath $blueprintPath
if ($nonNegotiables.Count -ne $catalogEntries.Count) {
    throw "Non-negotiable count ($($nonNegotiables.Count)) does not match catalog entry count ($($catalogEntries.Count))."
}

$ruleEnforcementMap = Read-RuleEnforcementMap -MapPath $ruleEnforcementMapPath
Test-RuleEnforcementMap -Map $ruleEnforcementMap -KnownRuleIds $actualRuleIds -RepositoryRoot $repositoryRoot

$durableMarkdownPaths = Get-DurableMarkdownPaths -RepositoryRoot $repositoryRoot
Test-MarkdownRelativeLinks -RepositoryRoot $repositoryRoot -MarkdownPaths $durableMarkdownPaths
Test-MarkdownRuleReferences -RepositoryRoot $repositoryRoot -MarkdownPaths $durableMarkdownPaths -KnownRuleIds $actualRuleIds

$waiverFiles = @(Get-ChildItem -Path $waiverRoot -Recurse -File -Filter '*.waivers.json')
if ($waiverFiles.Count -eq 0) {
    throw 'No versioned waiver files were found under waivers/.'
}

foreach ($waiverFile in $waiverFiles) {
    Test-WaiverFile -WaiverPath $waiverFile.FullName -KnownRuleIds $actualRuleIds
}

& (Join-Path -Path $repositoryRoot -ChildPath 'scripts/Test-ModuleScaffold.ps1')
& (Join-Path -Path $repositoryRoot -ChildPath 'scripts/Test-ModuleScaffoldFrontend.ps1')
& (Join-Path -Path $repositoryRoot -ChildPath 'scripts/Adopt-Baseline.ps1') -SpecFile (Join-Path -Path $repositoryRoot -ChildPath 'templates/adoption/adopt-spec.example.json') -DryRun
$adoptionWrapperSmokeRoot = Resolve-AdoptionScratchRoot -Mode 'os-temp' -Root 'adoption-wrapper-smoke' -RepositoryRoot $repositoryRoot
if (-not (Test-Path -Path $adoptionWrapperSmokeRoot -PathType Container)) {
    New-Item -Path $adoptionWrapperSmokeRoot -ItemType Directory -Force | Out-Null
}

$adoptionWrapperSmokeTarget = Join-Path -Path $adoptionWrapperSmokeRoot -ChildPath ([Guid]::NewGuid().ToString('N'))
& (Join-Path -Path $repositoryRoot -ChildPath 'scripts/Start-Adoption.ps1') `
    -TargetPath $adoptionWrapperSmokeTarget `
    -ProjectName 'Governance Smoke Product' `
    -ProjectSlug 'governance-smoke-product' `
    -ModulePreset 'clean-base' `
    -ValidationProfile 'none' `
    -DryRunOnly `
    -NoPrompt `
    -Force
& (Join-Path -Path $repositoryRoot -ChildPath 'scripts/Validate-DependencyPolicy.ps1')

Write-Host "Validated governance assets in '$repositoryRoot'."
Write-Host "Catalog entries: $($catalogEntries.Count)"
Write-Host "Rule enforcement entries: $($ruleEnforcementMap['rules'].Count)"
Write-Host "Waiver files: $($waiverFiles.Count)"
Write-Host "Governed core documents checked: $($governedCoreDocumentPaths.Count)"