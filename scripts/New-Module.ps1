param(
    [Parameter(Mandatory)]
    [string]$SpecFile,

    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'

. (Join-Path -Path $PSScriptRoot -ChildPath 'Governance.Common.ps1')
. (Join-Path -Path $PSScriptRoot -ChildPath 'modules/New-Module.Backend.Persistence.ps1')
. (Join-Path -Path $PSScriptRoot -ChildPath 'modules/New-Module.Backend.Settings.ps1')
. (Join-Path -Path $PSScriptRoot -ChildPath 'modules/New-Module.Backend.Messaging.ps1')
. (Join-Path -Path $PSScriptRoot -ChildPath 'modules/New-Module.Backend.Tests.ps1')
. (Join-Path -Path $PSScriptRoot -ChildPath 'modules/New-Module.Frontend.Settings.ps1')
. (Join-Path -Path $PSScriptRoot -ChildPath 'modules/New-Module.Frontend.Feature.ps1')

$repositoryRoot = Get-RepositoryRoot
$resolvedOutputRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $repositoryRoot
}
else {
    $OutputRoot
}

$contractPath = Join-Path -Path $repositoryRoot -ChildPath 'templates/module/scaffold.contract.json'

$spec = Read-JsonFileAsHashtable -Path $SpecFile
Test-ModuleSpec -Spec $spec

$contract = Read-JsonFileAsHashtable -Path $contractPath
$expectedPaths = Get-ExpectedScaffoldPaths -Contract $contract -Spec $spec

$moduleName = [string]$spec['moduleName']
$moduleKey = [string]$spec['moduleKey']
$moduleDisplayName = [string]$spec['displayName']
$moduleNamespace = [string]$spec['moduleNamespace']
$moduleApiProjectName = "${moduleName}.Api"
$moduleApplicationProjectName = "${moduleName}.Application"
$moduleDomainProjectName = "${moduleName}.Domain"
$moduleInfrastructureProjectName = "${moduleName}.Infrastructure"
$modulePublicContractsProjectName = "${moduleName}.PublicContracts"
$moduleEntryPointTypeName = "${moduleName}Module"
$moduleAssemblyMarkerTypeName = "${moduleName}AssemblyMarker"
$moduleApiOptionsTypeName = "${moduleName}ApiOptions"
$moduleInfrastructureOptionsTypeName = "${moduleName}InfrastructureOptions"
$modulePersistenceDefaultsTypeName = "${moduleName}PersistenceDefaults"
$modulePersistenceDbContextTypeName = "${moduleName}PersistenceDbContext"
$modulePersistenceDbContextFactoryTypeName = "${moduleName}PersistenceDbContextFactory"
$moduleDatabaseMigrationTypeName = "${moduleName}DatabaseMigration"
$moduleEntityTypeConfigurationExampleTypeName = "${moduleName}EntityTypeConfigurationExample"
$moduleScaffoldedPersistenceRecordTypeName = "${moduleName}PersistenceRecordExample"
$moduleFeatureComponentTypeName = "${moduleName}Feature"
$moduleRecentAuthenticationPolicyTypeName = "${moduleName}RecentAuthenticationPolicy"
$moduleSettingsTypeName = "${moduleName}ModuleSettings"
$moduleSettingsStoreTypeName = "I${moduleName}SettingsStore"
$moduleGetSettingsQueryTypeName = "Get${moduleName}SettingsQuery"
$moduleUpdateSettingsCommandTypeName = "Update${moduleName}SettingsCommand"
$moduleSettingsResponseTypeName = "${moduleName}SettingsResponse"
$moduleUpdateSettingsRequestTypeName = "Update${moduleName}SettingsRequest"
$moduleSettingsApiFileName = "${moduleName}SettingsApi"
$moduleSettingsTagsFileName = "${moduleName}SettingsTags"
$moduleSettingsProjectionFileName = "${moduleName}SettingsProjection"
$moduleSettingsApiUnitTestFileName = "${moduleKey}-settings-api.test"
$moduleSettingsTagsUnitTestFileName = "${moduleKey}-settings-tags.test"
$moduleFeatureUnitTestFileName = "${moduleKey}-feature.test"
$moduleFeatureE2eFileName = "${moduleKey}.spec"
$defaultEnabledLiteral = ([bool]$spec['defaultEnabled']).ToString().ToLowerInvariant()
$canBeDisabledLiteral = (-not [bool]$spec['isCore']).ToString().ToLowerInvariant()
$hasFrontendSurfaceLiteral = ([bool]$spec['hasFrontendSurface']).ToString().ToLowerInvariant()
$hasOperatorManagedSettingsLiteral = ([bool]$spec['hasOperatorManagedSettings']).ToString().ToLowerInvariant()

function Write-CrossModulePublicContractDependencyPreflight {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$ModuleName,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$DeclaredDependencies
    )

    $referenceCap = Get-CrossModulePublicContractsReferenceCap
    $projectedRefs = $DeclaredDependencies.Count
    $projectedHeadroom = $referenceCap - $projectedRefs

    if ($projectedHeadroom -lt 0) {
        throw "Module '$ModuleName' declares $projectedRefs planned cross-module PublicContracts dependencies, which exceeds the BP-033 cap of $referenceCap. Redesign the module boundaries before scaffolding."
    }

    $currentRows = @(Get-ModuleDependencyReportRows -RepositoryRoot $RepositoryRoot)
    $tightestRows = @($currentRows | Sort-Object -Property Headroom, Module | Select-Object -First 3)
    $tightestSummary = if ($tightestRows.Count -eq 0) {
        'no existing module rows'
    }
    else {
        ($tightestRows | ForEach-Object { "{0}={1} remaining" -f $_.Module, $_.Headroom }) -join '; '
    }

    Write-Host "BP-033 preflight: tightest current cross-module PublicContracts headroom -> $tightestSummary"

    if ($projectedRefs -eq 0) {
        Write-Host "BP-033 preflight: '$ModuleName' declares no planned cross-module PublicContracts dependencies; projected headroom remains $referenceCap of $referenceCap."
        Write-Host 'Run ./scripts/Report-ModuleDependencies.ps1 for the full per-module headroom report.'
        return
    }

    Write-Host "BP-033 preflight: planned cross-module PublicContracts dependencies for '$ModuleName' -> $($DeclaredDependencies -join ', ')"

    if ($projectedHeadroom -eq 0) {
        Write-Warning "BP-033 preflight: scaffolding '$ModuleName' with these planned dependencies will consume the full cap ($projectedRefs/$referenceCap). Any additional cross-module PublicContracts dependency will require redesign before implementation."
    }
    else {
        Write-Host "BP-033 preflight: '$ModuleName' would start at $projectedRefs/$referenceCap cross-module PublicContracts dependencies with $projectedHeadroom headroom remaining."
    }

    Write-Host 'Run ./scripts/Report-ModuleDependencies.ps1 for the full per-module headroom report.'
}

function Get-FullPath {
    param(
        [Parameter(Mandatory)]
        [string]$Root,

        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    $normalizedRelativePath = $RelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    return Join-Path -Path $Root -ChildPath $normalizedRelativePath
}

function New-ProjectFileContent {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Api', 'Application', 'Domain', 'Infrastructure', 'PublicContracts')]
        [string]$ProjectKind
    )

    $projectReferences = [System.Collections.Generic.List[string]]::new()
    $packageReferences = [System.Collections.Generic.List[string]]::new()

    switch ($ProjectKind) {
        'Api' {
            $projectReferences.Add("..\\$moduleApplicationProjectName\\$moduleApplicationProjectName.csproj")
            $projectReferences.Add("..\\$moduleInfrastructureProjectName\\$moduleInfrastructureProjectName.csproj")
            $projectReferences.Add("..\\$modulePublicContractsProjectName\\$modulePublicContractsProjectName.csproj")
            $projectReferences.Add("..\\..\\..\\BuildingBlocks\\Application\\BuildingBlocks.Application.csproj")
        }
        'Application' {
            $projectReferences.Add("..\\$moduleDomainProjectName\\$moduleDomainProjectName.csproj")
            $projectReferences.Add("..\\$modulePublicContractsProjectName\\$modulePublicContractsProjectName.csproj")
            $projectReferences.Add("..\\..\\..\\BuildingBlocks\\Application\\BuildingBlocks.Application.csproj")
        }
        'Domain' {
            $projectReferences.Add("..\\..\\..\\BuildingBlocks\\Domain\\BuildingBlocks.Domain.csproj")
        }
        'Infrastructure' {
            $packageReferences.Add('    <PackageReference Include="Microsoft.EntityFrameworkCore.Design">')
            $packageReferences.Add('      <PrivateAssets>all</PrivateAssets>')
            $packageReferences.Add('      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>')
            $packageReferences.Add('    </PackageReference>')
            $packageReferences.Add('    <PackageReference Include="Microsoft.EntityFrameworkCore" />')
            $packageReferences.Add('    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />')
            $projectReferences.Add("..\\$moduleApplicationProjectName\\$moduleApplicationProjectName.csproj")
            $projectReferences.Add("..\\$moduleDomainProjectName\\$moduleDomainProjectName.csproj")
            $projectReferences.Add("..\\$modulePublicContractsProjectName\\$modulePublicContractsProjectName.csproj")
            $projectReferences.Add("..\\..\\..\\BuildingBlocks\\Infrastructure\\BuildingBlocks.Infrastructure.csproj")
        }
        'PublicContracts' {
            if ([bool]$spec['publishesIntegrationEvents']) {
                $projectReferences.Add("..\\..\\..\\BuildingBlocks\\Domain\\BuildingBlocks.Domain.csproj")
            }
        }
    }

        $packageXml = if ($packageReferences.Count -eq 0) {
                ''
        }
        else {
                $joinedPackages = ($packageReferences | ForEach-Object { $_ }) -join [Environment]::NewLine
@"

    <ItemGroup>
$joinedPackages
    </ItemGroup>
"@
        }

        $referenceXml = if ($projectReferences.Count -eq 0) {
        ''
    }
    else {
        $joinedReferences = ($projectReferences | Sort-Object -Unique | ForEach-Object { '    <ProjectReference Include="{0}" />' -f $_ }) -join [Environment]::NewLine
@"

  <ItemGroup>
$joinedReferences
  </ItemGroup>
"@
    }

@"
<Project Sdk="Microsoft.NET.Sdk">$referenceXml
$packageXml
</Project>
"@
}

function New-ModuleClassContent {
    $settingsUsings = if ([bool]$spec['hasOperatorManagedSettings']) {
@"
using BuildingBlocks.Infrastructure.ProblemDetails;
using ${moduleName}.Application.Settings;
"@
    }
    else {
        ''
    }

    $settingsEndpoints = if ([bool]$spec['hasOperatorManagedSettings']) {
@"

        endpoints.MapGet(
                "/settings",
                static async (IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new ${moduleGetSettingsQueryTypeName}(), cancellationToken);
                    return mapper.Match(result, httpContext, settings => Results.Ok(${moduleSettingsResponseTypeName}.From(settings!)));
                })
            .WithName("${moduleName}_GetSettings")
            .WithSummary("Get the current scaffolded runtime settings for the $moduleDisplayName module.")
            .Produces<${moduleSettingsResponseTypeName}>(StatusCodes.Status200OK);

        endpoints.MapPut(
                "/settings",
                static async (${moduleUpdateSettingsRequestTypeName} request, IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new ${moduleUpdateSettingsCommandTypeName}(
                            request.ExpectedVersion,
                            request.OperatorSummary,
                            request.PreviewLimit),
                        cancellationToken);

                    return mapper.Match(result, httpContext, settings => Results.Ok(${moduleSettingsResponseTypeName}.From(settings!)));
                })
            .WithName("${moduleName}_UpdateSettings")
            .WithSummary("Update the scaffolded runtime settings for the $moduleDisplayName module.")
            .Accepts<${moduleUpdateSettingsRequestTypeName}>("application/json")
            .Produces<${moduleSettingsResponseTypeName}>(StatusCodes.Status200OK);
"@
    }
    else {
        ''
    }

@"
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.Modules;
using ${moduleName}.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
$settingsUsings

namespace ${moduleName}.Api;

public sealed class $moduleEntryPointTypeName : IApiModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        Key: "$moduleKey",
        DisplayName: "$moduleDisplayName",
        RoutePrefix: "$([string]$spec['routePrefix'])",
        SchemaName: "$([string]$spec['schemaName'])",
        ModuleNamespace: "$moduleNamespace",
        DefaultEnabled: $defaultEnabledLiteral,
        CanBeDisabled: $canBeDisabledLiteral,
        HasFrontendSurface: $hasFrontendSurfaceLiteral);

    public string Key => Descriptor.Key;

    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDispatcher(typeof(${moduleName}.Application.${moduleAssemblyMarkerTypeName}).Assembly);
        services.Add${moduleName}Infrastructure();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
$settingsEndpoints
    }
}
"@
}

function New-AssemblyMarkerContent {
    param(
        [Parameter(Mandatory)]
        [string]$Namespace,

        [Parameter(Mandatory)]
        [string]$TypeName
    )

@"
namespace $Namespace;

public static class $TypeName
{
}
"@
}

function New-OptionsClassContent {
    param(
        [Parameter(Mandatory)]
        [string]$Namespace,

        [Parameter(Mandatory)]
        [string]$ClassName,

        [Parameter(Mandatory)]
        [ValidateSet('Api', 'Infrastructure')]
        [string]$OptionsKind
    )

    if ($OptionsKind -eq 'Api') {
@"
namespace $Namespace;

public sealed class $ClassName
{
    public const string SectionName = "$([string]$spec['configurationSectionName'])";

    public ${moduleName}ApiHttpOptions Http { get; init; } = new();
}

public sealed class ${moduleName}ApiHttpOptions
{
    public string VersionSetName { get; init; } = "v1";

    public bool EmitProblemDetailsMetadata { get; init; } = true;
}
"@
        return
    }

    $managedSettings = @($spec['operatorManagedSettings'])
    $managedSettingsLiteral = if ($managedSettings.Count -eq 0) {
        ''
    }
    else {
        $managedSettingLines = foreach ($managedSetting in $managedSettings) {
            $defaultValueLiteral = switch ([string]$managedSetting['type']) {
                'bool' {
                    if ([bool]$managedSetting['defaultValue']) { 'true' } else { 'false' }
                }
                'int' {
                    [string]$managedSetting['defaultValue']
                }
                default {
                    '"{0}"' -f ([string]$managedSetting['defaultValue']).Replace('"', '\"')
                }
            }

            ('        new("{0}", "{1}", "{2}", {3}, {4})' -f
                [string]$managedSetting['name'],
                ([string]$managedSetting['displayName']).Replace('"', '\"'),
                [string]$managedSetting['type'],
                $defaultValueLiteral,
                $(if ([bool]$managedSetting['reloadSafe']) { 'true' } else { 'false' }))
        }

        $managedSettingLines -join (",`r`n")
    }

@"
namespace $Namespace;

public sealed class $ClassName
{
    public const string SectionName = "$([string]$spec['configurationSectionName'])";

    public ${moduleName}ExperienceOptions Experience { get; init; } = new();

    public ${moduleName}OperationalOptions Operations { get; init; } = new();
}

public sealed class ${moduleName}ExperienceOptions
{
    public string Title { get; init; } = "$moduleDisplayName";

    public string Summary { get; init; } = "Scaffolded configuration surface for the $moduleDisplayName module.";
}

public sealed class ${moduleName}OperationalOptions
{
    public int PreviewLimit { get; init; } = 10;

    public IReadOnlyList<${moduleName}ManagedSettingDefinition> OperatorManagedSettings { get; init; } =
    [
$managedSettingsLiteral
    ];
}

public sealed record ${moduleName}ManagedSettingDefinition(
    string Name,
    string DisplayName,
    string Type,
    object DefaultValue,
    bool ReloadSafe);
"@
}

function New-InfrastructureServiceCollectionExtensionsContent {
    $settingsUsings = if ([bool]$spec['hasOperatorManagedSettings']) {
@"
using BuildingBlocks.Application.Results;
using ${moduleName}.Application.Settings;
using NodaTime;
"@
    }
    else {
        ''
    }

    $settingsRegistration = if ([bool]$spec['hasOperatorManagedSettings']) {
@"

        services.AddSingleton<${moduleSettingsStoreTypeName}>(static provider =>
            new InMemory${moduleName}SettingsStore(
                provider.GetRequiredService<BuildingBlocks.Domain.Time.IClock>(),
                provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<${moduleInfrastructureOptionsTypeName}>>().Value));
"@
    }
    else {
        ''
    }

    $settingsStoreType = if ([bool]$spec['hasOperatorManagedSettings']) {
@"

internal sealed class InMemory${moduleName}SettingsStore : ${moduleSettingsStoreTypeName}
{
    private readonly object _gate = new();
    private ${moduleSettingsTypeName} _settings;

    public InMemory${moduleName}SettingsStore(BuildingBlocks.Domain.Time.IClock clock, ${moduleInfrastructureOptionsTypeName} options)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);

        var now = clock.GetCurrentInstant();
        _settings = new ${moduleSettingsTypeName}(
            options.Experience.Summary,
            options.Operations.PreviewLimit,
            Version: 1,
            UpdatedUtc: now,
            UpdatedByActorId: "system:${moduleKey}-bootstrap");
    }

    public ValueTask<${moduleSettingsTypeName}> GetAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_settings);
        }
    }

    public ValueTask<Result<${moduleSettingsTypeName}>> UpdateAsync(
        int expectedVersion,
        string operatorSummary,
        int previewLimit,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_settings.Version != expectedVersion)
            {
                return ValueTask.FromResult(Result<${moduleSettingsTypeName}>.Failure(${moduleName}SettingsErrors.ConcurrencyConflict()));
            }

            _settings = _settings with
            {
                OperatorSummary = operatorSummary,
                PreviewLimit = previewLimit,
                Version = _settings.Version + 1,
                UpdatedUtc = now,
                UpdatedByActorId = actorId
            };

            return ValueTask.FromResult(Result<${moduleSettingsTypeName}>.Success(_settings));
        }
    }
}
"@
    }
    else {
        ''
    }

@"
using BuildingBlocks.Infrastructure.Persistence;
using ${moduleName}.Infrastructure.Configuration;
using ${moduleName}.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
$settingsUsings

namespace ${moduleName}.Infrastructure;

public static class ${moduleName}InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection Add${moduleName}Infrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDatabaseMigrationSupport();
        services.AddOptions<${moduleInfrastructureOptionsTypeName}>()
            .Configure<IConfiguration>(static (options, configuration) =>
            {
                configuration.GetSection(${moduleInfrastructureOptionsTypeName}.SectionName).Bind(options);
            })
            .Validate(
                static options => !string.IsNullOrWhiteSpace(options.Experience.Title),
                "${moduleName} configuration requires a non-empty experience title.")
            .Validate(
                static options => !string.IsNullOrWhiteSpace(options.Experience.Summary),
                "${moduleName} configuration requires a non-empty experience summary.")
            .Validate(
                static options => options.Operations.PreviewLimit > 0,
                "${moduleName} configuration requires a positive operational preview limit.")
            .ValidateOnStart();

        services.AddDbContextFactory<${modulePersistenceDbContextTypeName}>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString(${modulePersistenceDefaultsTypeName}.ConnectionStringName);
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.Use${moduleName}Persistence(connectionString);
            }
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDatabaseMigration, ${moduleDatabaseMigrationTypeName}>());$settingsRegistration

        return services;
    }
}
$settingsStoreType
"@
}




function New-RecordContent {
    param(
        [Parameter(Mandatory)]
        [string]$Namespace,

        [Parameter(Mandatory)]
        [string]$TypeName,

        [Parameter(Mandatory)]
        [string]$Parameters
    )

@"
namespace $Namespace;

public sealed record $TypeName($Parameters);
"@
}



function New-StaticShellContent {
    param(
        [Parameter(Mandatory)]
        [string]$Namespace,

        [Parameter(Mandatory)]
        [string]$TypeName
    )

@"
namespace $Namespace;

public static class $TypeName
{
}
"@
}

function Add-GeneratedProjectsToSolution {
    param(
        [Parameter(Mandatory)]
        [string]$Root,

        [Parameter(Mandatory)]
        [string[]]$GeneratedPaths
    )

    $solutionFiles = @(
        @(Get-ChildItem -Path $Root -Filter '*.sln' -File)
        @(Get-ChildItem -Path $Root -Filter '*.slnx' -File)
    )
    if ($solutionFiles.Count -ne 1) {
        return
    }

    $solutionFile = $solutionFiles[0]
    $solutionContent = Get-Content -Path $solutionFile.FullName -Raw
    $missingProjectPaths = @($GeneratedPaths |
        Where-Object { $_.EndsWith('.csproj', [System.StringComparison]::Ordinal) } |
        Where-Object {
            $solutionRelativePath = $_.Replace('/', '\\')
            $solutionContent -notmatch [regex]::Escape($solutionRelativePath)
        } |
        ForEach-Object { Get-FullPath -Root $Root -RelativePath $_ })

    if ($missingProjectPaths.Count -eq 0) {
        return
    }

    & dotnet sln $solutionFile.FullName add $missingProjectPaths | Out-Null
}

function New-FileContent {
    param(
        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    if ($RelativePath.EndsWith("/${moduleApiProjectName}/${moduleApiProjectName}.csproj", [System.StringComparison]::Ordinal)) {
        return New-ProjectFileContent -ProjectKind 'Api'
    }

    if ($RelativePath.EndsWith("/${moduleApplicationProjectName}/${moduleApplicationProjectName}.csproj", [System.StringComparison]::Ordinal)) {
        return New-ProjectFileContent -ProjectKind 'Application'
    }

    if ($RelativePath.EndsWith("/${moduleDomainProjectName}/${moduleDomainProjectName}.csproj", [System.StringComparison]::Ordinal)) {
        return New-ProjectFileContent -ProjectKind 'Domain'
    }

    if ($RelativePath.EndsWith("/${moduleInfrastructureProjectName}/${moduleInfrastructureProjectName}.csproj", [System.StringComparison]::Ordinal)) {
        return New-ProjectFileContent -ProjectKind 'Infrastructure'
    }

    if ($RelativePath.EndsWith("/${modulePublicContractsProjectName}/${modulePublicContractsProjectName}.csproj", [System.StringComparison]::Ordinal)) {
        return New-ProjectFileContent -ProjectKind 'PublicContracts'
    }

    if ($RelativePath.EndsWith("/${moduleApiProjectName}/${moduleEntryPointTypeName}.cs", [System.StringComparison]::Ordinal)) {
        return New-ModuleClassContent
    }

    if ($RelativePath.EndsWith("/${moduleApplicationProjectName}/${moduleAssemblyMarkerTypeName}.cs", [System.StringComparison]::Ordinal)) {
        return New-AssemblyMarkerContent -Namespace "${moduleName}.Application" -TypeName $moduleAssemblyMarkerTypeName
    }

    if ($RelativePath.EndsWith("/${moduleDomainProjectName}/${moduleAssemblyMarkerTypeName}.cs", [System.StringComparison]::Ordinal)) {
        return New-AssemblyMarkerContent -Namespace "${moduleName}.Domain" -TypeName $moduleAssemblyMarkerTypeName
    }

    if ($RelativePath.EndsWith("/${moduleInfrastructureProjectName}/${moduleAssemblyMarkerTypeName}.cs", [System.StringComparison]::Ordinal)) {
        return New-AssemblyMarkerContent -Namespace "${moduleName}.Infrastructure" -TypeName $moduleAssemblyMarkerTypeName
    }

    if ($RelativePath.EndsWith("/${modulePublicContractsProjectName}/${moduleAssemblyMarkerTypeName}.cs", [System.StringComparison]::Ordinal)) {
        return New-AssemblyMarkerContent -Namespace "${moduleName}.PublicContracts" -TypeName $moduleAssemblyMarkerTypeName
    }

    if ($RelativePath.EndsWith("/Configuration/${moduleApiOptionsTypeName}.cs", [System.StringComparison]::Ordinal)) {
        return New-OptionsClassContent -Namespace "${moduleName}.Api.Configuration" -ClassName $moduleApiOptionsTypeName -OptionsKind 'Api'
    }

    if ($RelativePath.EndsWith("/Configuration/${moduleInfrastructureOptionsTypeName}.cs", [System.StringComparison]::Ordinal)) {
        return New-OptionsClassContent -Namespace "${moduleName}.Infrastructure.Configuration" -ClassName $moduleInfrastructureOptionsTypeName -OptionsKind 'Infrastructure'
    }

    if ($RelativePath.EndsWith("/Persistence/${modulePersistenceDefaultsTypeName}.cs", [System.StringComparison]::Ordinal)) {
        return New-PersistenceDefaultsContent
    }

    if ($RelativePath.EndsWith("/Persistence/${modulePersistenceDbContextTypeName}.cs", [System.StringComparison]::Ordinal)) {
        return New-PersistenceDbContextContent
    }

    if ($RelativePath.EndsWith("/Persistence/${moduleEntityTypeConfigurationExampleTypeName}.cs", [System.StringComparison]::Ordinal)) {
        return New-EntityTypeConfigurationExampleContent
    }

    if ($RelativePath.EndsWith("/Persistence/${moduleName}PersistenceOptionsExtensions.cs", [System.StringComparison]::Ordinal)) {
        return New-PersistenceOptionsExtensionsContent
    }

    if ($RelativePath.EndsWith("/Persistence/${modulePersistenceDbContextFactoryTypeName}.cs", [System.StringComparison]::Ordinal)) {
        return New-PersistenceDbContextFactoryContent
    }

    if ($RelativePath.EndsWith("/Persistence/${moduleDatabaseMigrationTypeName}.cs", [System.StringComparison]::Ordinal)) {
        return New-DatabaseMigrationContent
    }

    if ($RelativePath.EndsWith("/Settings/${moduleName}SettingsContracts.cs", [System.StringComparison]::Ordinal)) {
        return New-SettingsContractsContent
    }

    if ($RelativePath.EndsWith("/${moduleName}SettingsHttpModels.cs", [System.StringComparison]::Ordinal)) {
        return New-SettingsHttpModelsContent
    }

    if ($RelativePath.EndsWith("/${moduleName}InfrastructureServiceCollectionExtensions.cs", [System.StringComparison]::Ordinal)) {
        return New-InfrastructureServiceCollectionExtensionsContent
    }

    if ($RelativePath.EndsWith("/${moduleName}ModuleDescriptorTests.cs", [System.StringComparison]::Ordinal)) {
        return New-ModuleDescriptorTestContent
    }

    if ($RelativePath.EndsWith("/${moduleName}BootstrapManifestTests.cs", [System.StringComparison]::Ordinal)) {
        return New-BootstrapManifestIntegrationTestContent
    }

    if ($RelativePath.EndsWith("/${moduleName}SettingsIntegrationTests.cs", [System.StringComparison]::Ordinal)) {
        return New-SettingsIntegrationTestContent
    }

    if ($RelativePath.EndsWith("/${moduleName}ArchitectureTests.cs", [System.StringComparison]::Ordinal)) {
        return New-ModuleArchitectureTestContent
    }

    if ($RelativePath.EndsWith("/${moduleName}ConfigurationGovernanceTests.cs", [System.StringComparison]::Ordinal)) {
        return New-ConfigurationGovernanceArchitectureTestContent
    }

    if ($RelativePath.EndsWith("/Events/${moduleName}EventV1.cs", [System.StringComparison]::Ordinal)) {
        return New-EventContractContent
    }

    if ($RelativePath.EndsWith("/${moduleName}IntegrationEventContractTests.cs", [System.StringComparison]::Ordinal)) {
        return New-IntegrationEventContractTestContent
    }

    if ($RelativePath.EndsWith("/Outbox/${moduleName}OutboxRegistration.cs", [System.StringComparison]::Ordinal)) {
        return New-OutboxRegistrationContent
    }

    if ($RelativePath.EndsWith("/${moduleName}OutboxIntegrationTests.cs", [System.StringComparison]::Ordinal)) {
        return New-OutboxIntegrationTestContent
    }

    if ($RelativePath.EndsWith("/Consumers/${moduleName}IntegrationEventConsumer.cs", [System.StringComparison]::Ordinal)) {
        return New-IntegrationConsumerContent
    }

    if ($RelativePath.EndsWith("/Inbox/${moduleName}InboxStore.cs", [System.StringComparison]::Ordinal)) {
        return New-InboxStoreContent
    }

    if ($RelativePath.EndsWith("/${moduleName}ConsumerReplayIntegrationTests.cs", [System.StringComparison]::Ordinal)) {
        return New-ConsumerReplayIntegrationTestContent
    }

    if ($RelativePath.EndsWith("/Queries/${moduleName}ReadModel.cs", [System.StringComparison]::Ordinal)) {
        return New-RecordContent -Namespace "${moduleName}.PublicContracts.Queries" -TypeName "${moduleName}ReadModel" -Parameters 'string Value'
    }

    if ($RelativePath.EndsWith("/${moduleName}SharedQueryContractTests.cs", [System.StringComparison]::Ordinal)) {
        return New-SharedReadModelContractTestContent
    }

    if ($RelativePath.EndsWith("/Authorization/${moduleRecentAuthenticationPolicyTypeName}.cs", [System.StringComparison]::Ordinal)) {
        return New-RecentAuthenticationPolicyContent
    }

    if ($RelativePath.EndsWith("/${moduleName}RecentAuthenticationPolicyTests.cs", [System.StringComparison]::Ordinal)) {
        return New-RecentAuthenticationPolicyArchitectureTestContent
    }

    if ($RelativePath.EndsWith("/ProcessManagers/${moduleName}ProcessManager.cs", [System.StringComparison]::Ordinal)) {
        return New-ProcessManagerContent
    }

    if ($RelativePath.EndsWith("/ProcessManagers/${moduleName}ProcessManagerStateStore.cs", [System.StringComparison]::Ordinal)) {
        return New-ProcessManagerStateStoreContent
    }

    if ($RelativePath.EndsWith("/${moduleName}ProcessManagerIntegrationTests.cs", [System.StringComparison]::Ordinal)) {
        return New-ProcessManagerIntegrationTestContent
    }

    if ($RelativePath.EndsWith("/Idempotency/${moduleName}IdempotencyPolicy.cs", [System.StringComparison]::Ordinal)) {
        return New-StaticShellContent -Namespace "${moduleName}.Application.Idempotency" -TypeName "${moduleName}IdempotencyPolicy"
    }

    if ($RelativePath.EndsWith("/Workers/${moduleName}Worker.cs", [System.StringComparison]::Ordinal)) {
        return New-StaticShellContent -Namespace "${moduleName}.Infrastructure.Workers" -TypeName "${moduleName}Worker"
    }

    if ($RelativePath.EndsWith("/Contracts/${moduleName}ContractV1.cs", [System.StringComparison]::Ordinal)) {
        return New-HttpContractV1Content
    }

    if ($RelativePath.EndsWith("/Machine/${moduleName}MachineEndpoints.cs", [System.StringComparison]::Ordinal)) {
        return New-StaticShellContent -Namespace "${moduleName}.Api.Machine" -TypeName "${moduleName}MachineEndpoints"
    }

    if ($RelativePath.EndsWith("/${moduleFeatureComponentTypeName}.tsx", [System.StringComparison]::Ordinal)) {
        return New-FrontendFeatureComponentContent
    }

    if ($RelativePath.EndsWith("/${moduleSettingsTagsFileName}.ts", [System.StringComparison]::Ordinal)) {
        return New-FrontendSettingsTagsContent
    }

    if ($RelativePath.EndsWith("/${moduleSettingsApiFileName}.ts", [System.StringComparison]::Ordinal)) {
        return New-FrontendSettingsApiContent
    }

    if ($RelativePath.EndsWith("/${moduleSettingsProjectionFileName}.ts", [System.StringComparison]::Ordinal)) {
        return New-FrontendSettingsProjectionContent
    }

    if ($RelativePath.EndsWith("/${moduleSettingsApiUnitTestFileName}.ts", [System.StringComparison]::Ordinal)) {
        return New-FrontendSettingsApiTestContent
    }

    if ($RelativePath.EndsWith("/${moduleSettingsTagsUnitTestFileName}.ts", [System.StringComparison]::Ordinal)) {
        return New-FrontendSettingsTagsTestContent
    }

    if ($RelativePath.EndsWith("/${moduleFeatureUnitTestFileName}.tsx", [System.StringComparison]::Ordinal)) {
        return New-FrontendSettingsFeatureTestContent
    }

    if ($RelativePath.EndsWith("/${moduleFeatureE2eFileName}.ts", [System.StringComparison]::Ordinal)) {
        return New-FrontendSettingsE2eContent
    }

    if ($RelativePath.EndsWith('/realtime/index.ts', [System.StringComparison]::Ordinal)) {
        return New-FrontendRealtimeContent
    }

    if ($RelativePath.EndsWith('/index.ts', [System.StringComparison]::Ordinal)) {
        return New-FrontendFeatureManifestContent
    }

    throw "No scaffold content template exists for '$RelativePath'."
}

$declaredCrossModuleDependencies = @(@($spec['crossModulePublicContractDependencies']) | ForEach-Object { [string]$_ })
Write-CrossModulePublicContractDependencyPreflight -RepositoryRoot $repositoryRoot -ModuleName $moduleName -DeclaredDependencies $declaredCrossModuleDependencies

New-Item -Path $resolvedOutputRoot -ItemType Directory -Force | Out-Null

foreach ($relativePath in $expectedPaths) {
    $fullPath = Get-FullPath -Root $resolvedOutputRoot -RelativePath $relativePath
    $directoryPath = Split-Path -Path $fullPath -Parent

    New-Item -Path $directoryPath -ItemType Directory -Force | Out-Null
    $content = New-FileContent -RelativePath $relativePath
    Set-Content -Path $fullPath -Value $content
}

Add-GeneratedProjectsToSolution -Root $resolvedOutputRoot -GeneratedPaths $expectedPaths

Write-Host "Generated governed scaffold for module '$moduleName' under '$resolvedOutputRoot'."

if ([bool]$spec['hasFrontendSurface'] -or [bool]$spec['hasOperatorManagedSettings']) {
    Write-Host 'Next: run ./scripts/Generate-Contracts.ps1 after the module HTTP surface is available so frontend wrappers bind to refreshed generated contract types.'
}
