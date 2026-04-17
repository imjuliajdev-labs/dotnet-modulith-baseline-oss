using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Domain.Modules;
using Platform.Application.Authorization;
using Platform.Application.ModuleState;

namespace Platform.Application.Health;

public enum OperationalHealthStatus
{
    Healthy = 0,
    Degraded = 1,
    Unhealthy = 2
}

public sealed record OperationalModuleHealth(
    string ModuleKey,
    string DisplayName,
    bool IsCritical,
    ModuleRuntimeState RuntimeState,
    OperationalHealthStatus Status);

public sealed record OperationalHealthSummary(
    OperationalHealthStatus OverallStatus,
    int TotalModules,
    int EnabledModules,
    int DisabledModules,
    IReadOnlyCollection<OperationalModuleHealth> Modules);

public sealed record GetOperationalHealthSummaryQuery : IQuery<OperationalHealthSummary>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => PlatformModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class GetOperationalHealthSummaryQueryHandler : IQueryHandler<GetOperationalHealthSummaryQuery, OperationalHealthSummary>
{
    private readonly IPlatformModuleStateStore _moduleStateStore;

    public GetOperationalHealthSummaryQueryHandler(IPlatformModuleStateStore moduleStateStore)
    {
        _moduleStateStore = moduleStateStore ?? throw new ArgumentNullException(nameof(moduleStateStore));
    }

    public async Task<Result<OperationalHealthSummary>> Handle(GetOperationalHealthSummaryQuery query, CancellationToken cancellationToken)
    {
        var modules = (await _moduleStateStore.ListAsync(cancellationToken))
            .Select(static state => new OperationalModuleHealth(
                state.ModuleKey,
                state.DisplayName,
                IsCritical: !state.CanBeDisabled,
                state.RuntimeState,
                DetermineModuleStatus(state)))
            .ToArray();

        var overallStatus = modules.Any(static module => module.Status == OperationalHealthStatus.Unhealthy)
            ? OperationalHealthStatus.Unhealthy
            : modules.Any(static module => module.Status == OperationalHealthStatus.Degraded)
                ? OperationalHealthStatus.Degraded
                : OperationalHealthStatus.Healthy;

        var summary = new OperationalHealthSummary(
            overallStatus,
            modules.Length,
            modules.Count(static module => module.RuntimeState == ModuleRuntimeState.Enabled),
            modules.Count(static module => module.RuntimeState == ModuleRuntimeState.Disabled),
            modules);

        return Result<OperationalHealthSummary>.Success(summary);
    }

    private static OperationalHealthStatus DetermineModuleStatus(ManagedModuleState state)
    {
        return state.RuntimeState switch
        {
            ModuleRuntimeState.Enabled => OperationalHealthStatus.Healthy,
            _ when !state.CanBeDisabled => OperationalHealthStatus.Unhealthy,
            _ => OperationalHealthStatus.Degraded
        };
    }
}
