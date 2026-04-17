using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Domain.Modules;
using NodaTime;
using Instant = NodaTime.Instant;
using IClock = BuildingBlocks.Domain.Time.IClock;
using Platform.Application.Authorization;
using Platform.Domain.ModuleActivation;

namespace Platform.Application.ModuleState;

public sealed record ManagedModuleState(
    string ModuleKey,
    string DisplayName,
    string RoutePrefix,
    bool DefaultEnabled,
    bool CanBeDisabled,
    ModuleDesiredState DesiredState,
    ModuleRuntimeState RuntimeState,
    long Version,
    Guid? TransitionId,
    Instant UpdatedUtc,
    string? UpdatedByActorId,
    string? LastErrorCode,
    string? LastErrorDetail,
    IReadOnlyCollection<ManagedModuleStateChange> Changes);

public sealed record ManagedModuleStateChange(
    ModuleRuntimeState BeforeState,
    ModuleRuntimeState AfterState,
    Instant ChangedUtc,
    string? ActorId,
    Guid? TransitionId);

public sealed record PlatformModuleStateChangedNotification(
    string ModuleKey,
    string DisplayName,
    string RoutePrefix,
    bool DefaultEnabled,
    bool CanBeDisabled,
    string DesiredState,
    string RuntimeState,
    long Version,
    Guid? TransitionId,
    DateTimeOffset UpdatedUtc,
    string? UpdatedByActorId,
    string? LastErrorCode,
    string? LastErrorDetail,
    IReadOnlyCollection<PlatformModuleStateChangedNotificationChange> Changes)
{
    public static PlatformModuleStateChangedNotification From(ManagedModuleState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new PlatformModuleStateChangedNotification(
            state.ModuleKey,
            state.DisplayName,
            state.RoutePrefix,
            state.DefaultEnabled,
            state.CanBeDisabled,
            ToRealtimeValue(state.DesiredState),
            ToRealtimeValue(state.RuntimeState),
            state.Version,
            state.TransitionId,
            state.UpdatedUtc.ToDateTimeOffset(),
            state.UpdatedByActorId,
            state.LastErrorCode,
            state.LastErrorDetail,
            state.Changes.Select(PlatformModuleStateChangedNotificationChange.From).ToArray());
    }

    private static string ToRealtimeValue(ModuleDesiredState desiredState)
    {
        return desiredState.ToString().ToLowerInvariant();
    }

    private static string ToRealtimeValue(ModuleRuntimeState runtimeState)
    {
        return runtimeState.ToString().ToLowerInvariant();
    }
}

public sealed record PlatformModuleStateChangedNotificationChange(
    string BeforeState,
    string AfterState,
    DateTimeOffset ChangedUtc,
    string? ActorId,
    Guid? TransitionId)
{
    public static PlatformModuleStateChangedNotificationChange From(ManagedModuleStateChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new PlatformModuleStateChangedNotificationChange(
            change.BeforeState.ToString().ToLowerInvariant(),
            change.AfterState.ToString().ToLowerInvariant(),
            change.ChangedUtc.ToDateTimeOffset(),
            change.ActorId,
            change.TransitionId);
    }
}

public static class PlatformRealtimeChannels
{
    public const string ModuleStateChanged = "platform.module-state.changed";
}

public interface IPlatformModuleTransitionParticipant
{
    string ModuleKey { get; }

    Task OnEnablingAsync(CancellationToken cancellationToken);

    Task OnDisablingAsync(CancellationToken cancellationToken);
}

public interface IPlatformModuleStateStore : IModuleStateReader
{
    ValueTask<IReadOnlyCollection<ManagedModuleState>> ListAsync(CancellationToken cancellationToken);

    ValueTask<ModuleStateChangeResult> EnableAsync(
        string moduleKey,
        string? actorId,
        Instant changedUtc,
        CancellationToken cancellationToken);

    ValueTask<ModuleStateChangeResult> DisableAsync(
        string moduleKey,
        string? actorId,
        Instant changedUtc,
        CancellationToken cancellationToken);
}

public sealed record ModuleStateChangeResult(ModuleStateChangeStatus Status, ManagedModuleState? State);

public enum ModuleStateChangeStatus
{
    Success = 0,
    UnknownModule = 1,
    AlreadyEnabled = 2,
    AlreadyDisabled = 3,
    NotDisableable = 4,
    TransitionFailed = 5
}

public static class PlatformModuleStateErrors
{
    public static Error UnknownModule(string moduleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);

        return new Error(
            "platform.module_state.unknown_module",
            $"Module '{moduleKey}' is not registered.",
            ErrorKind.NotFound);
    }

    public static Error AlreadyEnabled(string moduleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);

        return new Error(
            "platform.module_state.already_enabled",
            $"Module '{moduleKey}' is already enabled.",
            ErrorKind.Conflict);
    }

    public static Error AlreadyDisabled(string moduleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);

        return new Error(
            "platform.module_state.already_disabled",
            $"Module '{moduleKey}' is already disabled.",
            ErrorKind.Conflict);
    }

    public static Error NotDisableable(string moduleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);

        return new Error(
            "platform.module_state.cannot_disable",
            $"Module '{moduleKey}' cannot be disabled at runtime.",
            ErrorKind.Conflict);
    }

    public static Error TransitionFailed(string moduleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);

        return new Error(
            "platform.module_state.transition_failed",
            $"Module '{moduleKey}' could not complete the requested runtime transition.",
            ErrorKind.Failure);
    }
}

public sealed record GetModuleStatesQuery : IQuery<ModuleStateList>, IModuleScoped
{
    public string ModuleKey => PlatformModuleInfo.ModuleKey;
}

public sealed record ModuleStateList(IReadOnlyCollection<ManagedModuleState> Modules);

internal sealed class GetModuleStatesQueryHandler : IQueryHandler<GetModuleStatesQuery, ModuleStateList>
{
    private readonly IPlatformModuleStateStore _moduleStateStore;

    public GetModuleStatesQueryHandler(IPlatformModuleStateStore moduleStateStore)
    {
        _moduleStateStore = moduleStateStore ?? throw new ArgumentNullException(nameof(moduleStateStore));
    }

    public async Task<Result<ModuleStateList>> Handle(GetModuleStatesQuery query, CancellationToken cancellationToken)
    {
        var modules = await _moduleStateStore.ListAsync(cancellationToken);
        return Result<ModuleStateList>.Success(new ModuleStateList(modules));
    }
}

public sealed record EnableModuleCommand(string TargetModuleKey) : ICommand<ManagedModuleState>, IModuleScoped, IAuthorizeRequest, IRequireRecentAuthentication
{
    public string ModuleKey => PlatformModuleInfo.ModuleKey;

    public Duration RecentAuthenticationWindow { get; } = Duration.FromMinutes(5);

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
    [RoleRequirement.Admin];
}

internal sealed class EnableModuleCommandHandler : ICommandHandler<EnableModuleCommand, ManagedModuleState>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly IClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IPlatformModuleStateStore _moduleStateStore;
    private readonly IRequestContextAccessor _requestContextAccessor;

    public EnableModuleCommandHandler(
        IAuditEventWriter auditEventWriter,
        IClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IPlatformModuleStateStore moduleStateStore,
        IRequestContextAccessor requestContextAccessor)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _moduleStateStore = moduleStateStore ?? throw new ArgumentNullException(nameof(moduleStateStore));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
    }

    public async Task<Result<ManagedModuleState>> Handle(EnableModuleCommand command, CancellationToken cancellationToken)
    {
        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var occurredUtc = _clock.GetCurrentInstant();
        var outcome = await _moduleStateStore.EnableAsync(
            command.TargetModuleKey,
            actor.ActorId,
            occurredUtc,
            cancellationToken);

        var result = PlatformModuleStateResultMapper.ToResult(command.TargetModuleKey, outcome);
        if (result.IsFailure)
        {
            return result;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: PlatformModuleInfo.ModuleKey,
                Action: "platform.module_state.enable",
                TargetType: "module",
                TargetId: command.TargetModuleKey,
                Outcome: outcome.State!.RuntimeState.ToString(),
                OccurredUtc: occurredUtc,
                ActorId: actor.ActorId,
                CorrelationId: _requestContextAccessor.Current?.CorrelationId),
            cancellationToken);

        return result;
    }
}

public sealed record DisableModuleCommand(string TargetModuleKey) : ICommand<ManagedModuleState>, IModuleScoped, IAuthorizeRequest, IRequireRecentAuthentication
{
    public string ModuleKey => PlatformModuleInfo.ModuleKey;

    public Duration RecentAuthenticationWindow { get; } = Duration.FromMinutes(5);

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
    [RoleRequirement.Admin];
}

internal sealed class DisableModuleCommandHandler : ICommandHandler<DisableModuleCommand, ManagedModuleState>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly IClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IPlatformModuleStateStore _moduleStateStore;
    private readonly IRequestContextAccessor _requestContextAccessor;

    public DisableModuleCommandHandler(
        IAuditEventWriter auditEventWriter,
        IClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IPlatformModuleStateStore moduleStateStore,
        IRequestContextAccessor requestContextAccessor)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _moduleStateStore = moduleStateStore ?? throw new ArgumentNullException(nameof(moduleStateStore));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
    }

    public async Task<Result<ManagedModuleState>> Handle(DisableModuleCommand command, CancellationToken cancellationToken)
    {
        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var occurredUtc = _clock.GetCurrentInstant();
        var outcome = await _moduleStateStore.DisableAsync(
            command.TargetModuleKey,
            actor.ActorId,
            occurredUtc,
            cancellationToken);

        var result = PlatformModuleStateResultMapper.ToResult(command.TargetModuleKey, outcome);
        if (result.IsFailure)
        {
            return result;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: PlatformModuleInfo.ModuleKey,
                Action: "platform.module_state.disable",
                TargetType: "module",
                TargetId: command.TargetModuleKey,
                Outcome: outcome.State!.RuntimeState.ToString(),
                OccurredUtc: occurredUtc,
                ActorId: actor.ActorId,
                CorrelationId: _requestContextAccessor.Current?.CorrelationId),
            cancellationToken);

        return result;
    }
}

internal static class PlatformModuleStateResultMapper
{
    public static Result<ManagedModuleState> ToResult(string moduleKey, ModuleStateChangeResult outcome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        ArgumentNullException.ThrowIfNull(outcome);

        return outcome.Status switch
        {
            ModuleStateChangeStatus.Success when outcome.State is not null => Result<ManagedModuleState>.Success(outcome.State),
            ModuleStateChangeStatus.UnknownModule => Result<ManagedModuleState>.Failure(PlatformModuleStateErrors.UnknownModule(moduleKey)),
            ModuleStateChangeStatus.AlreadyEnabled => Result<ManagedModuleState>.Failure(PlatformModuleStateErrors.AlreadyEnabled(moduleKey)),
            ModuleStateChangeStatus.AlreadyDisabled => Result<ManagedModuleState>.Failure(PlatformModuleStateErrors.AlreadyDisabled(moduleKey)),
            ModuleStateChangeStatus.NotDisableable => Result<ManagedModuleState>.Failure(PlatformModuleStateErrors.NotDisableable(moduleKey)),
            ModuleStateChangeStatus.TransitionFailed => Result<ManagedModuleState>.Failure(PlatformModuleStateErrors.TransitionFailed(moduleKey)),
            _ => throw new InvalidOperationException($"Unexpected module-state change result '{outcome.Status}'.")
        };
    }
}
