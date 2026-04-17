param(
    [string]$SpecFile
)

$ErrorActionPreference = 'Stop'

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')

$repositoryRoot = Get-RepositoryRoot
$resolvedSpecFile = if ([string]::IsNullOrWhiteSpace($SpecFile)) {
    Join-Path -Path $repositoryRoot -ChildPath 'templates/module/module-spec.example.json'
}
else {
    $SpecFile
}

$contractPath = Join-Path -Path $repositoryRoot -ChildPath 'templates/module/scaffold.contract.json'
$scaffoldScript = Join-Path -Path $repositoryRoot -ChildPath 'scripts/New-Module.ps1'

$spec = Read-JsonFileAsHashtable -Path $resolvedSpecFile
Test-ModuleSpec -Spec $spec

$contract = Read-JsonFileAsHashtable -Path $contractPath
$expectedPaths = Get-ExpectedScaffoldPaths -Contract $contract -Spec $spec

function Get-GeneratedFileMap {
    param(
        [Parameter(Mandatory)]
        [string]$Root
    )

    $map = @{}
    foreach ($file in Get-ChildItem -Path $Root -Recurse -File) {
        $relativePath = Convert-ToRepoRelativePath -Root $Root -Path $file.FullName
        $map[$relativePath] = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash
    }

    return $map
}

function Assert-FileContains {
    param(
        [Parameter(Mandatory)]
        [string]$Root,

        [Parameter(Mandatory)]
        [string]$RelativePath,

        [Parameter(Mandatory)]
        [string]$ExpectedText
    )

    $fullPath = Join-Path -Path $Root -ChildPath ($RelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    $content = Get-Content -Path $fullPath -Raw
    if ($content -notmatch [regex]::Escape($ExpectedText)) {
        throw "Generated file '$RelativePath' did not contain expected text '$ExpectedText'."
    }
}

function Assert-FileDoesNotContain {
    param(
        [Parameter(Mandatory)]
        [string]$Root,

        [Parameter(Mandatory)]
        [string]$RelativePath,

        [Parameter(Mandatory)]
        [string]$UnexpectedText
    )

    $fullPath = Join-Path -Path $Root -ChildPath ($RelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    $content = Get-Content -Path $fullPath -Raw
    if ($content -match [regex]::Escape($UnexpectedText)) {
        throw "Generated file '$RelativePath' unexpectedly contained '$UnexpectedText'."
    }
}

function Assert-SolutionContainsProject {
    param(
        [Parameter(Mandatory)]
        [string]$SolutionPath,

        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    $content = Get-Content -Path $SolutionPath -Raw
    $normalizedRelativePath = $RelativePath.Replace('/', '\')
    if ($content -notmatch [regex]::Escape($normalizedRelativePath)) {
        throw "Solution '$SolutionPath' did not include generated project '$RelativePath'."
    }
}

function Copy-ScaffoldDependencyProject {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$Root,

        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    $sourcePath = Join-Path -Path $RepositoryRoot -ChildPath ($RelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    $targetPath = Join-Path -Path $Root -ChildPath ($RelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    $targetDirectory = Split-Path -Path $targetPath -Parent

    if (Test-Path -Path $sourcePath -PathType Container) {
        New-Item -Path $targetDirectory -ItemType Directory -Force | Out-Null
        Copy-Item -Path $sourcePath -Destination $targetDirectory -Recurse -Force
        return
    }

    New-Item -Path $targetDirectory -ItemType Directory -Force | Out-Null
    Copy-Item -Path $sourcePath -Destination $targetPath -Force
}

$temporaryRoot = New-GovernanceScratchDirectory -Purpose 'module-scaffold' -RepositoryRoot $repositoryRoot

try {
    dotnet new sln --name dotnet-modulith-baseline --format sln --output $temporaryRoot | Out-Null

    foreach ($dependencyFile in @(
            'Directory.Build.props',
            'Directory.Packages.props',
            'global.json')) {
        Copy-ScaffoldDependencyProject -RepositoryRoot $repositoryRoot -Root $temporaryRoot -RelativePath $dependencyFile
    }

    foreach ($dependencyProject in @(
            'src/BuildingBlocks/Application',
            'src/BuildingBlocks/Domain',
            'src/BuildingBlocks/Infrastructure')) {
        Copy-ScaffoldDependencyProject -RepositoryRoot $repositoryRoot -Root $temporaryRoot -RelativePath $dependencyProject
    }

    $temporarySolutionPath = @(Get-ChildItem -Path $temporaryRoot -Filter '*.sln*' -File | Select-Object -ExpandProperty FullName)
    if ($temporarySolutionPath.Count -ne 1) {
        throw 'Scaffold smoke expected exactly one solution file in the temporary root.'
    }

    $temporarySolutionPath = $temporarySolutionPath[0]

    & $scaffoldScript -SpecFile $resolvedSpecFile -OutputRoot $temporaryRoot
    dotnet build $temporarySolutionPath --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw 'Scaffold smoke failed to compile the generated backend solution.'
    }

    foreach ($expectedPath in $expectedPaths) {
        $fullPath = Join-Path -Path $temporaryRoot -ChildPath ($expectedPath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -Path $fullPath -PathType Leaf)) {
            throw "Scaffold smoke failed to generate expected file '$expectedPath'."
        }
    }

    $actualFiles = Get-ChildItem -Path $temporaryRoot -Recurse -File | ForEach-Object {
        Convert-ToRepoRelativePath -Root $temporaryRoot -Path $_.FullName
    } | Where-Object {
        $_ -notlike '*.sln' -and
        $_ -notlike '*.slnx' -and
        $_ -notin @('Directory.Build.props', 'Directory.Packages.props', 'global.json') -and
        $_ -notmatch '(^|/)(bin|obj)/' -and
        -not $_.StartsWith('src/BuildingBlocks/', [System.StringComparison]::Ordinal)
    } | Sort-Object

    if (-not (Test-SequenceEqual -Left @($actualFiles) -Right @($expectedPaths))) {
        throw "Scaffold smoke generated a file set that does not match the contract."
    }

    foreach ($projectPath in $expectedPaths | Where-Object { $_.EndsWith('.csproj', [System.StringComparison]::Ordinal) }) {
        Assert-SolutionContainsProject -SolutionPath $temporarySolutionPath -RelativePath $projectPath
    }

    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Api/$($spec['moduleName']).Api.csproj" -ExpectedText "$($spec['moduleName']).Application"
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Api/$($spec['moduleName']).Api.csproj" -ExpectedText 'BuildingBlocks.Application'
    Assert-FileDoesNotContain -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Api/$($spec['moduleName']).Api.csproj" -UnexpectedText 'BuildingBlocks.Infrastructure'
    Assert-FileDoesNotContain -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Api/$($spec['moduleName']).Api.csproj" -UnexpectedText 'BuildingBlocks.Domain'
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Api/$($spec['moduleName'])Module.cs" -ExpectedText 'IApiModule'
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Api/$($spec['moduleName'])Module.cs" -ExpectedText 'ModuleNamespace'
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Api/$($spec['moduleName'])Module.cs" -ExpectedText "Add$($spec['moduleName'])Infrastructure"
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Api/$($spec['moduleName'])Module.cs" -ExpectedText 'AddDispatcher'
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/$($spec['moduleName']).Infrastructure.csproj" -ExpectedText 'BuildingBlocks.Infrastructure'
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/$($spec['moduleName']).Infrastructure.csproj" -ExpectedText 'Microsoft.EntityFrameworkCore'
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/$($spec['moduleName']).Infrastructure.csproj" -ExpectedText 'Npgsql.EntityFrameworkCore.PostgreSQL'
    Assert-FileDoesNotContain -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/$($spec['moduleName']).Infrastructure.csproj" -UnexpectedText 'BuildingBlocks.Application'
    Assert-FileDoesNotContain -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/$($spec['moduleName']).Infrastructure.csproj" -UnexpectedText 'BuildingBlocks.Domain'
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/$($spec['moduleName'])InfrastructureServiceCollectionExtensions.cs" -ExpectedText "AddOptions<$($spec['moduleName'])InfrastructureOptions>()"
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/$($spec['moduleName'])InfrastructureServiceCollectionExtensions.cs" -ExpectedText 'ValidateOnStart()'
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/$($spec['moduleName'])InfrastructureServiceCollectionExtensions.cs" -ExpectedText "AddDbContextFactory<$($spec['moduleName'])PersistenceDbContext>"
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/$($spec['moduleName'])InfrastructureServiceCollectionExtensions.cs" -ExpectedText "Use$($spec['moduleName'])Persistence(connectionString)"
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/Persistence/$($spec['moduleName'])PersistenceDefaults.cs" -ExpectedText ([string]$spec['schemaName'])
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/Persistence/$($spec['moduleName'])PersistenceDbContext.cs" -ExpectedText 'HasDefaultSchema'
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/Persistence/$($spec['moduleName'])PersistenceDbContext.cs" -ExpectedText 'ApplyConfiguration(new SomeAggregatePersistenceConfiguration())'
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/Persistence/$($spec['moduleName'])EntityTypeConfigurationExample.cs" -ExpectedText 'IEntityTypeConfiguration<'
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/Persistence/$($spec['moduleName'])EntityTypeConfigurationExample.cs" -ExpectedText "$($spec['moduleName'])PersistenceRecordExample"
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/Persistence/$($spec['moduleName'])EntityTypeConfigurationExample.cs" -ExpectedText 'IsConcurrencyToken()'
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/Persistence/$($spec['moduleName'])PersistenceOptionsExtensions.cs" -ExpectedText 'MigrationsHistoryTable'
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/Persistence/$($spec['moduleName'])PersistenceDbContextFactory.cs" -ExpectedText '--connection-string'
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/Persistence/$($spec['moduleName'])DatabaseMigration.cs" -ExpectedText 'GetPendingMigrationsAsync'
    Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/Configuration/$($spec['moduleName'])InfrastructureOptions.cs" -ExpectedText 'OperatorManagedSettings'
    Assert-FileDoesNotContain -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/Configuration/$($spec['moduleName'])InfrastructureOptions.cs" -UnexpectedText 'ReloadSafeSettings'
    foreach ($settingDefinition in @($spec['operatorManagedSettings'])) {
        Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/Configuration/$($spec['moduleName'])InfrastructureOptions.cs" -ExpectedText ([string]$settingDefinition['name'])
        Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Infrastructure/Configuration/$($spec['moduleName'])InfrastructureOptions.cs" -ExpectedText ([string]$settingDefinition['displayName'])
    }
    Assert-FileContains -Root $temporaryRoot -RelativePath "tests/Architecture.Tests/Modules/$($spec['moduleName'])/Configuration/$($spec['moduleName'])ConfigurationGovernanceTests.cs" -ExpectedText 'InfrastructureWiringBindsTypedOptionsAndValidatesOnStart'
    Assert-FileContains -Root $temporaryRoot -RelativePath "tests/Architecture.Tests/Modules/$($spec['moduleName'])/Configuration/$($spec['moduleName'])ConfigurationGovernanceTests.cs" -ExpectedText 'AddDbContextFactory'
    Assert-FileContains -Root $temporaryRoot -RelativePath "tests/Architecture.Tests/Modules/$($spec['moduleName'])/Configuration/$($spec['moduleName'])ConfigurationGovernanceTests.cs" -ExpectedText 'ApplyConfiguration(new SomeAggregatePersistenceConfiguration())'
    Assert-FileContains -Root $temporaryRoot -RelativePath "tests/Architecture.Tests/Modules/$($spec['moduleName'])/Configuration/$($spec['moduleName'])ConfigurationGovernanceTests.cs" -ExpectedText 'IEntityTypeConfiguration<'
    Assert-FileContains -Root $temporaryRoot -RelativePath "tests/Module.UnitTests/$($spec['moduleName'])/$($spec['moduleName'])ModuleDescriptorTests.cs" -ExpectedText 'DescriptorMatchesTheModuleSpec'
    Assert-FileContains -Root $temporaryRoot -RelativePath "tests/Integration.Tests/Modules/$($spec['moduleName'])/$($spec['moduleName'])BootstrapManifestTests.cs" -ExpectedText 'PlatformBootstrapManifestIncludesTheScaffoldedModule'

    if ([bool]$spec['requiresRecentAuthStepUp']) {
        Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Application/Authorization/$($spec['moduleName'])RecentAuthenticationPolicy.cs" -ExpectedText 'SensitiveMutationWindow'
        Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Application/Authorization/$($spec['moduleName'])RecentAuthenticationPolicy.cs" -ExpectedText 'Duration.FromMinutes(5)'
        Assert-FileContains -Root $temporaryRoot -RelativePath "tests/Architecture.Tests/Modules/$($spec['moduleName'])/Authorization/$($spec['moduleName'])RecentAuthenticationPolicyTests.cs" -ExpectedText 'RecentAuthenticationPolicyUsesTheGovernedSensitiveMutationWindow'
    }

    if ([bool]$spec['publishesIntegrationEvents']) {
        $outboxTestPath = "tests/Integration.Tests/Modules/$($spec['moduleName'])/Events/$($spec['moduleName'])OutboxIntegrationTests.cs"
        Assert-FileContains -Root $temporaryRoot -RelativePath $outboxTestPath -ExpectedText "namespace Integration.Tests.CapabilityCoverage.$($spec['moduleName']).Events;"
        Assert-FileContains -Root $temporaryRoot -RelativePath $outboxTestPath -ExpectedText 'OutboxCapabilityShellAnchorsGovernedRuntimeSeams'
        Assert-FileDoesNotContain -Root $temporaryRoot -RelativePath $outboxTestPath -UnexpectedText 'namespace Integration.Tests.ModuleCoverage.'
        Assert-FileDoesNotContain -Root $temporaryRoot -RelativePath $outboxTestPath -UnexpectedText 'GeneratedEventContractCarriesStableMetadata'
    }

    if ([bool]$spec['consumesIntegrationEvents']) {
        $consumerReplayTestPath = "tests/Integration.Tests/Modules/$($spec['moduleName'])/Events/$($spec['moduleName'])ConsumerReplayIntegrationTests.cs"
        Assert-FileContains -Root $temporaryRoot -RelativePath $consumerReplayTestPath -ExpectedText "namespace Integration.Tests.CapabilityCoverage.$($spec['moduleName']).Events;"
        Assert-FileContains -Root $temporaryRoot -RelativePath $consumerReplayTestPath -ExpectedText 'ConsumerReplayCapabilityShellAnchorsGovernedRuntimeSeams'
        Assert-FileDoesNotContain -Root $temporaryRoot -RelativePath $consumerReplayTestPath -UnexpectedText 'namespace Integration.Tests.ModuleCoverage.'
        Assert-FileDoesNotContain -Root $temporaryRoot -RelativePath $consumerReplayTestPath -UnexpectedText 'ConsumerShellCarriesTheOwningModuleKey'
    }

    if ([bool]$spec['needsProcessManager']) {
        $processManagerTestPath = "tests/Integration.Tests/Modules/$($spec['moduleName'])/ProcessManagers/$($spec['moduleName'])ProcessManagerIntegrationTests.cs"
        Assert-FileContains -Root $temporaryRoot -RelativePath $processManagerTestPath -ExpectedText "namespace Integration.Tests.CapabilityCoverage.$($spec['moduleName']).ProcessManagers;"
        Assert-FileContains -Root $temporaryRoot -RelativePath $processManagerTestPath -ExpectedText 'ProcessManagerCapabilityShellAnchorsGovernedRuntimeSeams'
        Assert-FileDoesNotContain -Root $temporaryRoot -RelativePath $processManagerTestPath -UnexpectedText 'namespace Integration.Tests.ModuleCoverage.'
        Assert-FileDoesNotContain -Root $temporaryRoot -RelativePath $processManagerTestPath -UnexpectedText 'ProcessManagerShellCarriesTheOwningModuleKey'
    }

    if ([bool]$spec['hasFrontendSurface']) {
        Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/index.ts" -ExpectedText 'featureDefinition'
        Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])Feature.tsx" -ExpectedText $spec['displayName']

        if ([bool]$spec['hasOperatorManagedSettings']) {
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])Feature.tsx" -ExpectedText 'Operational settings shell'
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])Feature.tsx" -ExpectedText "useGet$($spec['moduleName'])SettingsProjectionQuery"
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])Feature.tsx" -ExpectedText "useGet$($spec['moduleName'])SettingsQuery"
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])Feature.tsx" -ExpectedText 'Dependent preview:'
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])Feature.tsx" -ExpectedText 'Refresh the HTTP contracts after the module endpoint shape changes'
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/tests/unit/$($spec['moduleKey'])-settings-api.test.ts" -ExpectedText 'invalidates the dependent projection when settings change'
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/tests/unit/$($spec['moduleKey'])-settings-tags.test.ts" -ExpectedText 'keeps the generated settings tag helpers aligned with the module spec'
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/tests/unit/$($spec['moduleKey'])-feature.test.tsx" -ExpectedText 'renders the scaffolded settings flow and save action'
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/tests/e2e/$($spec['moduleKey']).spec.ts" -ExpectedText 'renders its managed settings shell'
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/tests/e2e/$($spec['moduleKey']).spec.ts" -ExpectedText 'Dependent preview: Operator summary: Updated from scaffolded Playwright anchor | Preview limit: 6'
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/tests/e2e/$($spec['moduleKey']).spec.ts" -ExpectedText 'Updated from scaffolded Playwright anchor'
        }
    }

    if ([bool]$spec['hasOperatorManagedSettings']) {
        Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Application/Settings/$($spec['moduleName'])SettingsContracts.cs" -ExpectedText "$($spec['moduleName'])ModuleSettings"
        Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Application/Settings/$($spec['moduleName'])SettingsContracts.cs" -ExpectedText 'IAuditEventWriter'
        Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Application/Settings/$($spec['moduleName'])SettingsContracts.cs" -ExpectedText '.settings.update'
        Assert-FileContains -Root $temporaryRoot -RelativePath "src/Modules/$($spec['moduleName'])/$($spec['moduleName']).Api/$($spec['moduleName'])SettingsHttpModels.cs" -ExpectedText "Update$($spec['moduleName'])SettingsRequest"
        Assert-FileContains -Root $temporaryRoot -RelativePath "tests/Integration.Tests/Modules/$($spec['moduleName'])/Configuration/$($spec['moduleName'])SettingsIntegrationTests.cs" -ExpectedText 'RuntimeSettingsShellAnchorsTheGeneratedQueryUpdateSlice'

        if ([bool]$spec['hasFrontendSurface']) {
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsTags.ts" -ExpectedText "$($spec['moduleName'])SettingsTags"
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsTags.ts" -ExpectedText "get$($spec['moduleName'])SettingsDependentReadTags"
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsTags.ts" -ExpectedText "get$($spec['moduleName'])SettingsInvalidationTags"
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsProjection.ts" -ExpectedText "$($spec['moduleName'])SettingsProjectionApi"
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsProjection.ts" -ExpectedText "to$($spec['moduleName'])SettingsProjection"
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsProjection.ts" -ExpectedText "useGet$($spec['moduleName'])SettingsProjectionQuery"
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsProjection.ts" -ExpectedText "get$($spec['moduleName'])SettingsDependentReadTags"
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsProjection.ts" -ExpectedText "./$($spec['moduleName'])SettingsApi"
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsApi.ts" -ExpectedText 'starterApi.enhanceEndpoints'
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsApi.ts" -ExpectedText 'enhanceEndpoints'
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsApi.ts" -ExpectedText "useGet$($spec['moduleName'])SettingsQuery"
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsApi.ts" -ExpectedText 'contracts.generated'
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsApi.ts" -ExpectedText 'Record<string, unknown>'
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsApi.ts" -ExpectedText "./$($spec['moduleName'])SettingsTags"
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsApi.ts" -ExpectedText "get$($spec['moduleName'])SettingsInvalidationTags"
            Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsApi.ts" -ExpectedText "get$($spec['moduleName'])SettingsTagTypes"
            Assert-FileDoesNotContain -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsApi.ts" -UnexpectedText "useGet$($spec['moduleName'])SettingsProjectionQuery"
            Assert-FileDoesNotContain -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsApi.ts" -UnexpectedText "to$($spec['moduleName'])SettingsProjection"

            foreach ($tag in @($spec['settingsFanoutTags'])) {
                Assert-FileContains -Root $temporaryRoot -RelativePath "web/src/features/$($spec['moduleKey'])/$($spec['moduleName'])SettingsTags.ts" -ExpectedText ([string]$tag)
            }
        }
    }

    $beforeMap = Get-GeneratedFileMap -Root $temporaryRoot
    & $scaffoldScript -SpecFile $resolvedSpecFile -OutputRoot $temporaryRoot
    $afterMap = Get-GeneratedFileMap -Root $temporaryRoot

    if ($beforeMap.Count -ne $afterMap.Count) {
        throw 'Scaffold rerun changed the number of generated files.'
    }

    foreach ($key in $beforeMap.Keys) {
        if (-not $afterMap.ContainsKey($key)) {
            throw "Scaffold rerun removed generated file '$key'."
        }

        if ($beforeMap[$key] -ne $afterMap[$key]) {
            throw "Scaffold rerun changed generated file '$key'."
        }
    }

    Write-Host 'Scaffold smoke succeeded and the output is deterministic on rerun.'
}
finally {
    if (Test-Path -Path $temporaryRoot) {
        Remove-Item -Path $temporaryRoot -Recurse -Force
    }
}