function New-SettingsContractsContent {
    $recentAuthUsings = if ([bool]$spec['requiresRecentAuthStepUp']) {
@"
using BuildingBlocks.Application.Authorization;
using ${moduleName}.Application.Authorization;
"@
    }
    else {
        ''
    }

    $recentAuthInterface = if ([bool]$spec['requiresRecentAuthStepUp']) {
        ', IRequireRecentAuthentication'
    }
    else {
        ''
    }

    $recentAuthProperty = if ([bool]$spec['requiresRecentAuthStepUp']) {
@"

    public Duration RecentAuthenticationWindow { get; } = ${moduleRecentAuthenticationPolicyTypeName}.SensitiveMutationWindow;
"@
    }
    else {
        ''
    }

@"
using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Auditing;
$recentAuthUsings
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using NodaTime;

using DomainClock = BuildingBlocks.Domain.Time.IClock;

namespace ${moduleName}.Application.Settings;

public sealed record ${moduleSettingsTypeName}(
    string OperatorSummary,
    int PreviewLimit,
    int Version,
    Instant UpdatedUtc,
    string UpdatedByActorId);

public interface ${moduleSettingsStoreTypeName}
{
    ValueTask<${moduleSettingsTypeName}> GetAsync(CancellationToken cancellationToken);

    ValueTask<Result<${moduleSettingsTypeName}>> UpdateAsync(
        int expectedVersion,
        string operatorSummary,
        int previewLimit,
        string actorId,
        Instant now,
        CancellationToken cancellationToken);
}

public static class ${moduleName}SettingsErrors
{
    public static Error ConcurrencyConflict()
    {
        return new Error(
            "${moduleKey}.settings_version_conflict",
            "${moduleDisplayName} settings were changed by another operation.",
            ErrorKind.Conflict);
    }

    public static Error OperatorSummaryRequired()
    {
        return new Error(
            "${moduleKey}.settings_summary_required",
            "${moduleDisplayName} settings require an operator summary.",
            ErrorKind.Validation);
    }

    public static Error PreviewLimitMustBePositive()
    {
        return new Error(
            "${moduleKey}.settings_preview_limit_required",
            "${moduleDisplayName} settings require a positive preview limit.",
            ErrorKind.Validation);
    }

    public static Error VersionMustBePositive()
    {
        return new Error(
            "${moduleKey}.settings_version_required",
            "${moduleDisplayName} settings updates require a positive expected version.",
            ErrorKind.Validation);
    }
}

public sealed record ${moduleGetSettingsQueryTypeName} : IQuery<${moduleSettingsTypeName}>, IModuleScoped
{
    public string ModuleKey => "$moduleKey";
}

internal sealed class ${moduleGetSettingsQueryTypeName}Handler : IQueryHandler<${moduleGetSettingsQueryTypeName}, ${moduleSettingsTypeName}>
{
    private readonly ${moduleSettingsStoreTypeName} _store;

    public ${moduleGetSettingsQueryTypeName}Handler(${moduleSettingsStoreTypeName} store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<${moduleSettingsTypeName}>> Handle(${moduleGetSettingsQueryTypeName} query, CancellationToken cancellationToken)
    {
        var settings = await _store.GetAsync(cancellationToken);
        return Result<${moduleSettingsTypeName}>.Success(settings);
    }
}

public sealed record ${moduleUpdateSettingsCommandTypeName}(int ExpectedVersion, string OperatorSummary, int PreviewLimit)
    : ICommand<${moduleSettingsTypeName}>, IModuleScoped$recentAuthInterface
{
    public string ModuleKey => "$moduleKey";$recentAuthProperty
}

internal sealed class ${moduleUpdateSettingsCommandTypeName}Handler : ICommandHandler<${moduleUpdateSettingsCommandTypeName}, ${moduleSettingsTypeName}>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly DomainClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly ${moduleSettingsStoreTypeName} _store;

    public ${moduleUpdateSettingsCommandTypeName}Handler(
        IAuditEventWriter auditEventWriter,
        DomainClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        ${moduleSettingsStoreTypeName} store)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<${moduleSettingsTypeName}>> Handle(${moduleUpdateSettingsCommandTypeName} command, CancellationToken cancellationToken)
    {
        if (command.ExpectedVersion <= 0)
        {
            return Result<${moduleSettingsTypeName}>.Failure(${moduleName}SettingsErrors.VersionMustBePositive());
        }

        if (string.IsNullOrWhiteSpace(command.OperatorSummary))
        {
            return Result<${moduleSettingsTypeName}>.Failure(${moduleName}SettingsErrors.OperatorSummaryRequired());
        }

        if (command.PreviewLimit <= 0)
        {
            return Result<${moduleSettingsTypeName}>.Failure(${moduleName}SettingsErrors.PreviewLimitMustBePositive());
        }

        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var actorId = string.IsNullOrWhiteSpace(actor.ActorId) ? "unknown" : actor.ActorId;
        var now = _clock.GetCurrentInstant();
        var result = await _store.UpdateAsync(command.ExpectedVersion, command.OperatorSummary.Trim(), command.PreviewLimit, actorId, now, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: "$moduleKey",
                Action: "${moduleKey}.settings.update",
                TargetType: "module-settings",
                TargetId: "default",
                Outcome: "updated",
                OccurredUtc: now,
                ActorId: actorId,
                CorrelationId: _requestContextAccessor.Current?.CorrelationId),
            cancellationToken);

        return result;
    }
}
"@
}

function New-SettingsHttpModelsContent {
@"
using ${moduleName}.Application.Settings;

namespace ${moduleName}.Api;

public sealed record ${moduleUpdateSettingsRequestTypeName}(
    int ExpectedVersion,
    string OperatorSummary,
    int PreviewLimit);

public sealed record ${moduleSettingsResponseTypeName}(
    string OperatorSummary,
    int PreviewLimit,
    int Version,
    DateTimeOffset UpdatedUtc,
    string UpdatedByActorId)
{
    public static ${moduleSettingsResponseTypeName} From(${moduleSettingsTypeName} settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new ${moduleSettingsResponseTypeName}(
            settings.OperatorSummary,
            settings.PreviewLimit,
            settings.Version,
            settings.UpdatedUtc.ToDateTimeOffset(),
            settings.UpdatedByActorId);
    }
}
"@
}

function New-SettingsIntegrationTestContent {
    $recentAuthAssertion = if ([bool]$spec['requiresRecentAuthStepUp']) {
        '        Xunit.Assert.IsAssignableFrom<BuildingBlocks.Application.Authorization.IRequireRecentAuthentication>(updateCommand);'
    }
    else {
        ''
    }

@"
using BuildingBlocks.Application.Modules;
using ${moduleName}.Api;
using ${moduleName}.Application.Settings;
using NodaTime;

namespace Integration.Tests.ModuleCoverage.${moduleName}.Configuration;

public sealed class ${moduleName}SettingsIntegrationTests
{
    [Xunit.Fact]
    public void RuntimeSettingsShellAnchorsTheGeneratedQueryUpdateSlice()
    {
        var getQuery = new ${moduleGetSettingsQueryTypeName}();
        var updateCommand = new ${moduleUpdateSettingsCommandTypeName}(1, "Scaffolded operator summary", 5);
        var request = new ${moduleUpdateSettingsRequestTypeName}(1, "Scaffolded operator summary", 5);
        var response = ${moduleSettingsResponseTypeName}.From(
            new ${moduleSettingsTypeName}(
                "Scaffolded operator summary",
                5,
                Version: 2,
                UpdatedUtc: Instant.FromUtc(2026, 4, 5, 12, 0),
                UpdatedByActorId: "identity:operator"));

        Xunit.Assert.IsAssignableFrom<IModuleScoped>(getQuery);
        Xunit.Assert.IsAssignableFrom<IModuleScoped>(updateCommand);
        Xunit.Assert.Equal("$moduleKey", getQuery.ModuleKey);
        Xunit.Assert.Equal("$moduleKey", updateCommand.ModuleKey);
$recentAuthAssertion
        Xunit.Assert.Equal(request.OperatorSummary, response.OperatorSummary);
        Xunit.Assert.Equal(request.PreviewLimit, response.PreviewLimit);
        Xunit.Assert.Equal("identity:operator", response.UpdatedByActorId);
    }
}
"@
}

function New-ConfigurationGovernanceArchitectureTestContent {
@"
namespace Architecture.Tests.ModuleCoverage.${moduleName}.Configuration;

public sealed class ${moduleName}ConfigurationGovernanceTests
{
    [Xunit.Fact]
    public void InfrastructureWiringBindsTypedOptionsAndValidatesOnStart()
    {
        var source = RepositoryFiles.ReadAllText("src/Modules/${moduleName}/${moduleName}.Infrastructure/${moduleName}InfrastructureServiceCollectionExtensions.cs");
        var dbContextSource = RepositoryFiles.ReadAllText("src/Modules/${moduleName}/${moduleName}.Infrastructure/Persistence/${modulePersistenceDbContextTypeName}.cs");
        var exampleSource = RepositoryFiles.ReadAllText("src/Modules/${moduleName}/${moduleName}.Infrastructure/Persistence/${moduleEntityTypeConfigurationExampleTypeName}.cs");
        var optionsSource = RepositoryFiles.ReadAllText("src/Modules/${moduleName}/${moduleName}.Infrastructure/Persistence/${moduleName}PersistenceOptionsExtensions.cs");

        Xunit.Assert.Contains("AddOptions<${moduleInfrastructureOptionsTypeName}>()", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("${moduleInfrastructureOptionsTypeName}.SectionName", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("ValidateOnStart()", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("AddDbContextFactory<${modulePersistenceDbContextTypeName}>", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("Use${moduleName}Persistence(connectionString)", source, StringComparison.Ordinal);
        Xunit.Assert.Contains("HasDefaultSchema(${modulePersistenceDefaultsTypeName}.SchemaName)", dbContextSource, StringComparison.Ordinal);
        Xunit.Assert.Contains("ApplyConfiguration(new SomeAggregatePersistenceConfiguration())", dbContextSource, StringComparison.Ordinal);
        Xunit.Assert.Contains("IEntityTypeConfiguration<${moduleScaffoldedPersistenceRecordTypeName}>", exampleSource, StringComparison.Ordinal);
        Xunit.Assert.Contains("IsConcurrencyToken()", exampleSource, StringComparison.Ordinal);
        Xunit.Assert.Contains("MigrationsHistoryTable(", optionsSource, StringComparison.Ordinal);
    }
}
"@
}
