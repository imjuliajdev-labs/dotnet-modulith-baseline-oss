param(
    [string]$SpecFile,

    [switch]$DryRun,

    [switch]$SkipValidation,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $true

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

$repositoryRoot = Get-RepositoryRoot
$resolvedSpecFile = if ([string]::IsNullOrWhiteSpace($SpecFile)) {
    Join-Path -Path $repositoryRoot -ChildPath 'templates/adoption/adopt-spec.example.json'
}
else {
    [System.IO.Path]::GetFullPath($SpecFile)
}

function Get-CurrentSolutionFile {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $solutionFiles = @(Get-ChildItem -Path $RepositoryRoot -Filter '*.sln' -File)
    if ($solutionFiles.Count -ne 1) {
        throw "Adoption preflight expected exactly one solution file in '$RepositoryRoot'."
    }

    return $solutionFiles[0]
}

function Get-DerivedAdoptionIdentity {
    param(
        [Parameter(Mandatory)]
        [hashtable]$Spec
    )

    $projectSlug = [string]$Spec['projectSlug']
    $solutionFileName = if ($Spec.ContainsKey('solutionFileName') -and -not [string]::IsNullOrWhiteSpace([string]$Spec['solutionFileName'])) {
        [string]$Spec['solutionFileName']
    }
    else {
        "$projectSlug.sln"
    }

    return [ordered]@{
        SolutionFileName              = $solutionFileName
        CookieName                    = "__Host-$projectSlug"
        DataProtectionApplicationName = $projectSlug
        MigrationAdvisoryLockName     = "${projectSlug}:migrations"
        TelemetryServiceName          = $projectSlug
        DockerTelemetryServiceName    = "${projectSlug}-api"
        WebPackageName                = "${projectSlug}-web"
    }
}

function Write-AdoptionSummary {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$SpecPath,

        [Parameter(Mandatory)]
        [hashtable]$Spec,

        [Parameter(Mandatory)]
        [string[]]$KeepModules,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$RemoveModules,

        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$DerivedIdentity,

        [Parameter(Mandatory)]
        [string]$ScratchRoot,

        [Parameter(Mandatory)]
        [string]$CurrentSolutionFileName,

        [string]$ValidationDescription,

        [switch]$SkipValidation
    )

    Write-Host ''
    Write-Host 'Config-driven adoption preflight' -ForegroundColor Cyan
    Write-Host '================================' -ForegroundColor Cyan
    Write-Host "Repository root : $RepositoryRoot"
    Write-Host "Spec file       : $SpecPath"
    Write-Host "Project name    : $($Spec['projectName'])"
    Write-Host "Project slug    : $($Spec['projectSlug'])"
    Write-Host "Module preset   : $($Spec['modulePreset'])"
    Write-Host "Validation      : $(if ($SkipValidation) { 'skipped by switch' } else { [string]$Spec['validation']['profile'] })"
    if (-not $SkipValidation -and -not [string]::IsNullOrWhiteSpace($ValidationDescription)) {
        Write-Host "Validation path : $ValidationDescription"
    }
    Write-Host "Scratch mode    : $($Spec['scratch']['mode'])"
    Write-Host "Scratch root    : $ScratchRoot"
    Write-Host "DB reset        : $($Spec['database']['resetLocalDatabase'])"
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
    Write-Host 'Derived identity values:' -ForegroundColor Yellow
    Write-Host "  - Current solution file         : $CurrentSolutionFileName"
    Write-Host "  - Target solution file          : $($DerivedIdentity['SolutionFileName'])"
    Write-Host "  - Auth cookie name              : $($DerivedIdentity['CookieName'])"
    Write-Host "  - Data Protection app name      : $($DerivedIdentity['DataProtectionApplicationName'])"
    Write-Host "  - Migration advisory lock name  : $($DerivedIdentity['MigrationAdvisoryLockName'])"
    Write-Host "  - Telemetry service name        : $($DerivedIdentity['TelemetryServiceName'])"
    Write-Host "  - Docker telemetry service name : $($DerivedIdentity['DockerTelemetryServiceName'])"
    Write-Host "  - Web package name              : $($DerivedIdentity['WebPackageName'])"

    Write-Host ''
    Write-Host 'Planned mutation scope:' -ForegroundColor Yellow
    Write-Host '  - replace the baseline project slug across governed text assets and runtime identity strings'
    Write-Host '  - rename the solution file if the target name differs'
    Write-Host '  - remove the teaching modules that are not in the resolved keep set'
    Write-Host '  - regenerate shared-runtime behavior tests to match the resolved keep set'
    Write-Host '  - refresh validation according to the requested profile'
    Write-Host ''
}

function Assert-WorkingTreeReady {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [switch]$Force
    )

    if ($Force) {
        return
    }

    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $git) {
        Write-Warning 'Git is not available. Skipping tracked-working-tree cleanliness check.'
        return
    }

    Push-Location $RepositoryRoot
    try {
        $previousNativePreference = $PSNativeCommandUseErrorActionPreference
        $script:PSNativeCommandUseErrorActionPreference = $false
        try {
            git rev-parse --is-inside-work-tree *> $null
            if ($LASTEXITCODE -ne 0) {
                Write-Warning 'Git repository metadata is not available. Skipping tracked-working-tree cleanliness check.'
                return
            }

            $output = git status --short --untracked-files=no
        }
        finally {
            $script:PSNativeCommandUseErrorActionPreference = $previousNativePreference
        }

        $dirtyEntries = @($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($dirtyEntries.Count -gt 0) {
            throw "Refusing to mutate a repository with tracked changes. Commit or stash them first, or rerun with -Force. Dirty entries:`n$($dirtyEntries -join [Environment]::NewLine)"
        }
    }
    finally {
        Pop-Location
    }
}

function Get-RepositoryTextFiles {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $includedExtensions = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($extension in @('.cs', '.csproj', '.json', '.md', '.ps1', '.props', '.targets', '.sln', '.ts', '.tsx', '.txt', '.xml', '.yml', '.yaml', '.html', '.toml', '.svg')) {
        [void]$includedExtensions.Add($extension)
    }

    $includedFileNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($fileName in @('Dockerfile', 'Dockerfile.migrator')) {
        [void]$includedFileNames.Add($fileName)
    }

    $excludedDirectoryNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($name in @('.git', '.tmp', 'node_modules', 'dist', 'bin', 'obj', 'playwright-report', 'TestResults', 'test-results', 'coverage')) {
        [void]$excludedDirectoryNames.Add($name)
    }

    $results = [System.Collections.Generic.List[string]]::new()
    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push($RepositoryRoot)

    while ($pending.Count -gt 0) {
        $currentDirectory = $pending.Pop()

        foreach ($directory in Get-ChildItem -Path $currentDirectory -Directory) {
            if ($excludedDirectoryNames.Contains($directory.Name)) {
                continue
            }

            $pending.Push($directory.FullName)
        }

        foreach ($file in Get-ChildItem -Path $currentDirectory -File -Force) {
            if ($includedExtensions.Contains($file.Extension) -or $includedFileNames.Contains($file.Name)) {
                $results.Add($file.FullName)
            }
        }
    }

    return $results.ToArray()
}

function Invoke-RecursiveLiteralReplacement {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$OldValue,

        [Parameter(Mandatory)]
        [string]$NewValue
    )

    $mutatedFileCount = 0

    foreach ($filePath in Get-RepositoryTextFiles -RepositoryRoot $RepositoryRoot) {
        $content = [System.IO.File]::ReadAllText($filePath)
        if (-not $content.Contains($OldValue, [System.StringComparison]::Ordinal)) {
            continue
        }

        [System.IO.File]::WriteAllText($filePath, $content.Replace($OldValue, $NewValue, [System.StringComparison]::Ordinal))
        $mutatedFileCount++
    }

    if ($mutatedFileCount -eq 0) {
        throw "Recursive rename found zero occurrences of '$OldValue' under '$RepositoryRoot'. The working tree appears to already be renamed (or the slug is wrong). Aborting to avoid silently skipping the rename phase on a re-run."
    }
}

function Replace-InFile {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$RelativePath,

        [Parameter(Mandatory)]
        [string]$OldValue,

        [Parameter(Mandatory)]
        [string]$NewValue
    )

    $fullPath = Join-Path -Path $RepositoryRoot -ChildPath ($RelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    if (-not (Test-Path -Path $fullPath -PathType Leaf)) {
        return
    }

    $content = [System.IO.File]::ReadAllText($fullPath)
    if ($content.Contains($OldValue, [System.StringComparison]::Ordinal)) {
        [System.IO.File]::WriteAllText($fullPath, $content.Replace($OldValue, $NewValue, [System.StringComparison]::Ordinal))
        return
    }

    if ($content.Contains($NewValue, [System.StringComparison]::Ordinal)) {
        return
    }

    throw "Expected to find '$OldValue' in '$RelativePath' during adoption rename."
}

function Ensure-DirectoryExists {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -Path $Path -PathType Container)) {
        New-Item -Path $Path -ItemType Directory -Force | Out-Null
    }
}

function Remove-RelativePathIfExists {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    $fullPath = Join-Path -Path $RepositoryRoot -ChildPath ($RelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    if (Test-Path -Path $fullPath) {
        Remove-Item -Path $fullPath -Recurse -Force
    }
}

function Remove-ModuleSolutionProjects {
    param(
        [Parameter(Mandatory)]
        [string]$SolutionPath,

        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$ModuleName
    )

    $moduleDirectory = Join-Path -Path $RepositoryRoot -ChildPath ("src/Modules/{0}" -f $ModuleName)
    if (-not (Test-Path -Path $moduleDirectory -PathType Container)) {
        return
    }

    $projectPaths = @(Get-ChildItem -Path $moduleDirectory -Recurse -Filter '*.csproj' -File |
            ForEach-Object { [System.IO.Path]::GetRelativePath($RepositoryRoot, $_.FullName) })
    if ($projectPaths.Count -eq 0) {
        return
    }

    Push-Location $RepositoryRoot
    try {
        & dotnet sln $SolutionPath remove @($projectPaths)
    }
    finally {
        Pop-Location
    }
}

function Get-ModuleRemovalPaths {
    param(
        [Parameter(Mandatory)]
        [string]$ModuleName
    )

    switch ($ModuleName) {
        'Admin' {
            return @(
                'src/Modules/Admin',
                'tests/Architecture.Tests/Modules/Admin',
                'tests/Integration.Tests/Modules/Admin',
                'tests/Module.UnitTests/Admin',
                'web/src/features/admin',
                'web/tests/unit/admin-feature.test.tsx'
            )
        }
        'KnowledgeBase' {
            return @(
                'src/Modules/KnowledgeBase',
                'tests/Architecture.Tests/Modules/KnowledgeBase',
                'tests/Integration.Tests/Modules/KnowledgeBase',
                'tests/Module.UnitTests/KnowledgeBase',
                'web/src/features/knowledge-base',
                'web/tests/unit/knowledge-base-feature.test.tsx',
                'web/tests/e2e/knowledge-base-live.spec.ts'
            )
        }
        'Blog' {
            return @(
                'src/Modules/Blog',
                'tests/Architecture.Tests/Modules/Blog',
                'tests/Integration.Tests/Modules/Blog',
                'tests/Module.UnitTests/Blog',
                'web/src/features/blog',
                'web/tests/unit/blog-feature.test.tsx',
                'web/tests/unit/blog-settings-api.test.ts',
                'web/tests/unit/blog-settings-tags.test.ts',
                'web/tests/e2e/blog.spec.ts'
            )
        }
        'SampleFeature' {
            return @(
                'src/Modules/SampleFeature',
                'tests/Architecture.Tests/Modules/SampleFeature',
                'tests/Integration.Tests/Modules/SampleFeature',
                'tests/Module.UnitTests/SampleFeature',
                'web/src/features/sampleFeature',
                'web/tests/unit/sample-feature.test.tsx'
            )
        }
        default {
            return @()
        }
    }
}

function Get-SharedReadFallbackTestBlockForModule {
    param(
        [Parameter(Mandatory)]
        [string]$ModuleName
    )

    switch ($ModuleName) {
        'Blog' {
            return @'
    [Xunit.Fact]
    public async Task BlogIdentityTimeZoneReaderReturnsDeclaredFailureWhenUpstreamThrowsTransient()
    {
        var policy = SharedReadPolicyAccessor.ReadDeclaredPolicy(typeof(BlogIdentityTimeZoneReader));
        Xunit.Assert.Equal(SharedReadFallbackBehavior.Fail, policy.FallbackBehavior);

        var reader = new BlogIdentityTimeZoneReader(new ThrowingTimeZonePreferenceQueryService(new HttpRequestException("upstream down")));

        var result = await reader.GetRequiredTimeZoneIdAsync("identity:user:alpha", CancellationToken.None);

        Xunit.Assert.True(result.IsFailure);
        Xunit.Assert.Equal(BlogPostSchedulingErrors.TimeZoneReadUnavailable().Code, result.Error.Code);
    }
'@
        }
        'SampleFeature' {
            return @'
    [Xunit.Fact]
    public async Task SampleFeatureIdentityTimeZoneReaderReturnsDeclaredFailureWhenUpstreamThrowsTransient()
    {
        var policy = SharedReadPolicyAccessor.ReadDeclaredPolicy(typeof(SampleFeatureIdentityTimeZoneReader));
        Xunit.Assert.Equal(SharedReadFallbackBehavior.Fail, policy.FallbackBehavior);

        var reader = new SampleFeatureIdentityTimeZoneReader(new ThrowingTimeZonePreferenceQueryService(new TimeoutException("upstream slow")));

        var result = await reader.GetRequiredTimeZoneIdAsync("identity:user:alpha", CancellationToken.None);

        Xunit.Assert.True(result.IsFailure);
        Xunit.Assert.Equal(SampleAnnouncementSchedulingErrors.CurrentActorTimeZoneUnavailable().Code, result.Error.Code);
    }
'@
        }
        'AdminKnowledgeBase' {
            return @'
    [Xunit.Fact]
    public async Task AdminKnowledgeBaseGuidanceReaderReturnsEmptyFallbackWhenUpstreamThrowsTransient()
    {
        var policy = SharedReadPolicyAccessor.ReadDeclaredPolicy(typeof(AdminKnowledgeBaseGuidanceReader));
        Xunit.Assert.Equal(SharedReadFallbackBehavior.ReturnFallback, policy.FallbackBehavior);

        var reader = new AdminKnowledgeBaseGuidanceReader(new ThrowingKnowledgeBaseQueryService(new HttpRequestException("upstream down")));

        var result = await reader.ListAsync(5, CancellationToken.None);

        Xunit.Assert.NotNull(result);
        Xunit.Assert.Empty(result);
    }
'@
        }
        default {
            throw "Unknown shared-read fallback test block '$ModuleName'."
        }
    }
}

function Apply-SharedRuntimeBehaviorTestCleanup {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string[]]$KeepModules
    )

    $fallbackTestRelativePath = 'tests/Integration.Tests/SharedRuntime/SharedReadFallbackBehaviorTests.cs'
    $timeoutTestRelativePath = 'tests/Integration.Tests/SharedRuntime/SharedReadTimeoutBehaviorTests.cs'
    $fallbackTestPath = Join-Path -Path $RepositoryRoot -ChildPath $fallbackTestRelativePath

    $keepsBlog = $KeepModules -contains 'Blog'
    $keepsSampleFeature = $KeepModules -contains 'SampleFeature'
    $keepsAdmin = $KeepModules -contains 'Admin'
    $keepsKnowledgeBase = $KeepModules -contains 'KnowledgeBase'
    $keepsAdminKnowledgeBaseBridge = $keepsAdmin -and $keepsKnowledgeBase

    if (-not $keepsBlog) {
        Remove-RelativePathIfExists -RepositoryRoot $RepositoryRoot -RelativePath $timeoutTestRelativePath
    }

    if (-not (Test-Path -Path $fallbackTestPath -PathType Leaf)) {
        return
    }

    if ($keepsBlog -and $keepsSampleFeature -and $keepsAdminKnowledgeBaseBridge) {
        return
    }

    $activeBlocks = [System.Collections.Generic.List[string]]::new()
    $usings = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::Ordinal)
    [void]$usings.Add('BuildingBlocks.Application.SharedReads')
    [void]$usings.Add('Identity.PublicContracts.Queries')
    $needsHttpClient = $false
    $needsTimeZoneFake = $false
    $needsKnowledgeBaseFake = $false

    if ($keepsBlog) {
        $activeBlocks.Add((Get-SharedReadFallbackTestBlockForModule -ModuleName 'Blog'))
        [void]$usings.Add('Blog.Application.Scheduling')
        [void]$usings.Add('Blog.Application.SharedReads')
        $needsHttpClient = $true
        $needsTimeZoneFake = $true
    }

    if ($keepsSampleFeature) {
        $activeBlocks.Add((Get-SharedReadFallbackTestBlockForModule -ModuleName 'SampleFeature'))
        [void]$usings.Add('SampleFeature.Application.Scheduling')
        [void]$usings.Add('SampleFeature.Application.SharedReads')
        $needsTimeZoneFake = $true
    }

    if ($keepsAdminKnowledgeBaseBridge) {
        $activeBlocks.Add((Get-SharedReadFallbackTestBlockForModule -ModuleName 'AdminKnowledgeBase'))
        [void]$usings.Add('Admin.Application.SharedReads')
        [void]$usings.Add('KnowledgeBase.PublicContracts.Queries')
        $needsHttpClient = $true
        $needsKnowledgeBaseFake = $true
    }

    if ($activeBlocks.Count -eq 0) {
        Remove-RelativePathIfExists -RepositoryRoot $RepositoryRoot -RelativePath $fallbackTestRelativePath
        return
    }

    if ($needsHttpClient) {
        [void]$usings.Add('System.Net.Http')
    }

    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.AppendLine('// Shared-read behavior test: construct each adapter directly with a hand-rolled upstream that')
    [void]$builder.AppendLine('// throws a transient exception so the assertion stays focused on the declared fallback policy.')
    [void]$builder.AppendLine('// A full Postgres-backed host startup would overwhelm a pure fallback-path check that has no')
    [void]$builder.AppendLine('// database state and no DI-composition behavior to verify.')
    [void]$builder.AppendLine('')
    foreach ($usingNamespace in $usings) {
        [void]$builder.AppendLine("using $usingNamespace;")
    }
    [void]$builder.AppendLine('')
    [void]$builder.AppendLine('namespace Integration.Tests.SharedRuntime;')
    [void]$builder.AppendLine('')
    [void]$builder.AppendLine('public sealed class SharedReadFallbackBehaviorTests')
    [void]$builder.AppendLine('{')

    for ($blockIndex = 0; $blockIndex -lt $activeBlocks.Count; $blockIndex++) {
        [void]$builder.AppendLine($activeBlocks[$blockIndex])
        [void]$builder.AppendLine('')
    }

    if ($needsTimeZoneFake) {
        [void]$builder.AppendLine(@'
    private sealed class ThrowingTimeZonePreferenceQueryService : IIdentityTimeZonePreferenceQueryService
    {
        private readonly Exception _exception;

        public ThrowingTimeZonePreferenceQueryService(Exception exception)
        {
            _exception = exception;
        }

        public ValueTask<string?> GetPreferredTimeZoneIdAsync(string actorId, CancellationToken cancellationToken)
        {
            throw _exception;
        }
    }
'@)
        [void]$builder.AppendLine('')
    }

    if ($needsKnowledgeBaseFake) {
        [void]$builder.AppendLine(@'
    private sealed class ThrowingKnowledgeBaseQueryService : IKnowledgeBasePublishedEntryQueryService
    {
        private readonly Exception _exception;

        public ThrowingKnowledgeBaseQueryService(Exception exception)
        {
            _exception = exception;
        }

        public ValueTask<IReadOnlyCollection<KnowledgeBasePublishedEntryReadModel>> ListAsync(int limit, CancellationToken cancellationToken)
        {
            throw _exception;
        }
    }
'@)
        [void]$builder.AppendLine('')
    }

    [void]$builder.AppendLine('}')

    [System.IO.File]::WriteAllText($fallbackTestPath, $builder.ToString())
}

function Invoke-AdoptionRenames {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$CurrentSolutionFileName,

        [Parameter(Mandatory)]
        [hashtable]$Spec,

        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$DerivedIdentity
    )

    $projectSlug = [string]$Spec['projectSlug']
    $projectName = [string]$Spec['projectName']

    Invoke-RecursiveLiteralReplacement -RepositoryRoot $RepositoryRoot -OldValue 'dotnet-modulith-baseline' -NewValue $projectSlug

    Replace-InFile -RepositoryRoot $RepositoryRoot -RelativePath 'README.md' -OldValue "# $projectSlug" -NewValue "# $projectName"
    Replace-InFile -RepositoryRoot $RepositoryRoot -RelativePath 'web/index.html' -OldValue "<title>$projectSlug</title>" -NewValue "<title>$projectName</title>"
    Replace-InFile -RepositoryRoot $RepositoryRoot -RelativePath 'social-preview.svg' -OldValue ">$projectSlug<" -NewValue ">$projectName<"

    $currentSolutionPath = Join-Path -Path $RepositoryRoot -ChildPath $CurrentSolutionFileName
    $targetSolutionFileName = [string]$DerivedIdentity['SolutionFileName']
    $targetSolutionPath = Join-Path -Path $RepositoryRoot -ChildPath $targetSolutionFileName
    if (-not $currentSolutionPath.Equals($targetSolutionPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        if (Test-Path -Path $targetSolutionPath -PathType Leaf) {
            throw "Target solution file '$targetSolutionPath' already exists."
        }

        Move-Item -Path $currentSolutionPath -Destination $targetSolutionPath
    }

    $slugDerivedSolutionFileName = "$projectSlug.sln"
    if (-not $slugDerivedSolutionFileName.Equals($targetSolutionFileName, [System.StringComparison]::OrdinalIgnoreCase)) {
        Replace-InFile -RepositoryRoot $RepositoryRoot -RelativePath 'Dockerfile' -OldValue "COPY $slugDerivedSolutionFileName ./" -NewValue "COPY $targetSolutionFileName ./"
        Replace-InFile -RepositoryRoot $RepositoryRoot -RelativePath 'Dockerfile.migrator' -OldValue "COPY $slugDerivedSolutionFileName ./" -NewValue "COPY $targetSolutionFileName ./"
    }

    return $targetSolutionPath
}

function Invoke-ModuleRemoval {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$SolutionPath,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$RemoveModules,

        [Parameter(Mandatory)]
        [string[]]$KeepModules
    )

    foreach ($module in $RemoveModules) {
        Remove-ModuleSolutionProjects -SolutionPath $SolutionPath -RepositoryRoot $RepositoryRoot -ModuleName $module
    }

    foreach ($module in $RemoveModules) {
        foreach ($relativePath in Get-ModuleRemovalPaths -ModuleName $module) {
            Remove-RelativePathIfExists -RepositoryRoot $RepositoryRoot -RelativePath $relativePath
        }
    }

    Apply-SharedRuntimeBehaviorTestCleanup -RepositoryRoot $RepositoryRoot -KeepModules $KeepModules
}

function Update-RuleEnforcementMapForCurrentRepo {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $mapPath = Join-Path -Path $RepositoryRoot -ChildPath 'docs/RULE_ENFORCEMENT_MAP.json'
    if (-not (Test-Path -Path $mapPath -PathType Leaf)) {
        return
    }

    $map = Read-JsonFileAsHashtable -Path $mapPath
    foreach ($ruleId in @($map['rules'].Keys)) {
        $retainedArtifacts = [System.Collections.Generic.List[string]]::new()
        foreach ($artifact in @($map['rules'][$ruleId])) {
            $fullPath = Join-Path -Path $RepositoryRoot -ChildPath ([string]$artifact).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
            if (Test-Path -Path $fullPath) {
                $retainedArtifacts.Add([string]$artifact)
            }
        }

        if ($retainedArtifacts.Count -eq 0) {
            throw "Rule enforcement map entry '$ruleId' would become empty after module cleanup."
        }

        $map['rules'][$ruleId] = @($retainedArtifacts)
    }

    [System.IO.File]::WriteAllText($mapPath, ($map | ConvertTo-Json -Depth 8))
}

function Update-ReferenceModuleTeachingMap {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string[]]$KeepModules
    )

    $teachingMapPath = Join-Path -Path $RepositoryRoot -ChildPath 'docs/REFERENCE_MODULE_TEACHING_MAP.md'
    if (-not (Test-Path -Path $teachingMapPath -PathType Leaf)) {
        return
    }

    $optionalTeachingModules = @($KeepModules | Where-Object { $_ -in @('SampleFeature', 'Blog', 'KnowledgeBase', 'Admin') })
    $moduleLinks = [System.Collections.Generic.List[string]]::new()

    foreach ($module in $optionalTeachingModules) {
        $moduleLinks.Add("- [`../src/Modules/$module/README.md`](../src/Modules/$module/README.md)")
    }

    $body = if ($moduleLinks.Count -eq 0) {
@"
<!-- doc-tier: maintained-narrative -->
# Reference Module Teaching Map

This fork no longer keeps the optional teaching modules. The maintained baseline now starts from the governed platform plus your own scaffolded modules.

Recommended next step:

- read [`MODULE_GUIDE.md`](MODULE_GUIDE.md) for the baseline posture
- scaffold the first real module with [`../scripts/New-Module.ps1`](../scripts/New-Module.ps1)
- use [`EF_MODULE_PERSISTENCE_GUIDE.md`](EF_MODULE_PERSISTENCE_GUIDE.md) for the default relational persistence path
"@
    }
    else {
        $joinedLinks = $moduleLinks -join [Environment]::NewLine
@"
<!-- doc-tier: maintained-narrative -->
# Reference Module Teaching Map

This fork retains the following checked-in optional teaching modules after adoption cleanup:

$joinedLinks

Use [`MODULE_GUIDE.md`](MODULE_GUIDE.md) for the module-by-module narrative and [`EF_MODULE_PERSISTENCE_GUIDE.md`](EF_MODULE_PERSISTENCE_GUIDE.md) for the default EF-first persistence guidance.
"@
    }

    [System.IO.File]::WriteAllText($teachingMapPath, $body)
}

function Invoke-LocalDatabaseReset {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if ($null -eq $docker) {
        throw 'database.resetLocalDatabase is true, but docker is not available. Use the documented manual reset path or install Docker.'
    }

    Push-Location $RepositoryRoot
    try {
        & docker compose down -v
        & docker compose up -d postgres
    }
    finally {
        Pop-Location
    }
}

function Invoke-AdoptionValidation {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$ValidationPlan,

        [Parameter(Mandatory)]
        [string]$ScratchRoot
    )

    $localGateRunner = Join-Path -Path $RepositoryRoot -ChildPath 'scripts/Invoke-LocalGates.ps1'
    $runnerMode = [string]$ValidationPlan['RunnerMode']

    if ($runnerMode -eq 'none') {
        return
    }

    $hadScratchOverride = Test-Path -Path Env:DOTNET_MODULITH_SCRATCH_ROOT
    $previousScratchOverride = $env:DOTNET_MODULITH_SCRATCH_ROOT
    $env:DOTNET_MODULITH_SCRATCH_ROOT = $ScratchRoot

    Push-Location $RepositoryRoot
    try {
        switch ($runnerMode) {
            'all' {
                & pwsh -NoLogo -NoProfile -File $localGateRunner
            }
            'subset' {
                & pwsh -NoLogo -NoProfile -File $localGateRunner -Only @($ValidationPlan['GateIds'])
            }
            default {
                throw "Unsupported adoption validation runner mode '$runnerMode'."
            }
        }

        if ($LASTEXITCODE -ne 0) {
            throw "Adoption validation profile '$($ValidationPlan['Profile'])' failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
        if ($hadScratchOverride) {
            $env:DOTNET_MODULITH_SCRATCH_ROOT = $previousScratchOverride
        }
        else {
            Remove-Item -Path Env:DOTNET_MODULITH_SCRATCH_ROOT -ErrorAction SilentlyContinue
        }
    }
}

$spec = Read-JsonFileAsHashtable -Path $resolvedSpecFile
Test-AdoptionSpec -Spec $spec

$currentSolutionFile = Get-CurrentSolutionFile -RepositoryRoot $repositoryRoot
$keepModules = @(Resolve-AdoptionKeepModules -Spec $spec)
$removeModules = @(Get-SupportedAdoptionModules | Where-Object { $keepModules -notcontains $_ })
$derivedIdentity = Get-DerivedAdoptionIdentity -Spec $spec
$scratchRoot = Resolve-AdoptionScratchRoot -Mode ([string]$spec['scratch']['mode']) -Root ([string]$spec['scratch']['root']) -RepositoryRoot $repositoryRoot
$validationPlan = Get-AdoptionValidationProfilePlan -RepositoryRoot $repositoryRoot -Profile ([string]$spec['validation']['profile'])
$validationDescription = if ($SkipValidation) {
    ''
}
else {
    [string]$validationPlan['Description']
}

Write-AdoptionSummary `
    -RepositoryRoot $repositoryRoot `
    -SpecPath $resolvedSpecFile `
    -Spec $spec `
    -KeepModules $keepModules `
    -RemoveModules $removeModules `
    -DerivedIdentity $derivedIdentity `
    -ScratchRoot $scratchRoot `
    -CurrentSolutionFileName $currentSolutionFile.Name `
    -ValidationDescription $validationDescription `
    -SkipValidation:$SkipValidation

Ensure-DirectoryExists -Path $scratchRoot
$summaryLogDirectory = Join-Path -Path $scratchRoot -ChildPath 'adoption'
Ensure-DirectoryExists -Path $summaryLogDirectory
$summaryLogPath = Join-Path -Path $summaryLogDirectory -ChildPath 'last-run.txt'
@(
    "projectName=$($spec['projectName'])",
    "projectSlug=$($spec['projectSlug'])",
    "modulePreset=$($spec['modulePreset'])",
    "validationProfile=$($spec['validation']['profile'])",
    "validationRunnerMode=$($validationPlan['RunnerMode'])",
    "scratchRoot=$scratchRoot",
    "keepModules=$($keepModules -join ',')",
    "removeModules=$($removeModules -join ',')",
    "solutionFileName=$($derivedIdentity['SolutionFileName'])"
) | Set-Content -Path $summaryLogPath

if ($DryRun) {
    Write-Host 'Dry run completed. No files were changed.' -ForegroundColor Green
    return
}

Assert-WorkingTreeReady -RepositoryRoot $repositoryRoot -Force:$Force

$targetSolutionPath = Invoke-AdoptionRenames `
    -RepositoryRoot $repositoryRoot `
    -CurrentSolutionFileName $currentSolutionFile.Name `
    -Spec $spec `
    -DerivedIdentity $derivedIdentity

Invoke-ModuleRemoval `
    -RepositoryRoot $repositoryRoot `
    -SolutionPath $targetSolutionPath `
    -RemoveModules $removeModules `
    -KeepModules $keepModules

if ($removeModules.Count -gt 0) {
    Update-RuleEnforcementMapForCurrentRepo -RepositoryRoot $repositoryRoot
    Update-ReferenceModuleTeachingMap -RepositoryRoot $repositoryRoot -KeepModules $keepModules
}

if ([bool]$spec['database']['resetLocalDatabase']) {
    Invoke-LocalDatabaseReset -RepositoryRoot $repositoryRoot
}

if (-not $SkipValidation) {
    Invoke-AdoptionValidation -RepositoryRoot $repositoryRoot -ValidationPlan $validationPlan -ScratchRoot $scratchRoot
}

Write-Host 'Adoption apply completed.' -ForegroundColor Green
