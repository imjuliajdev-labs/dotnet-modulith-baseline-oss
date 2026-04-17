using BuildingBlocks.Domain.Modules;
using NodaTime;

namespace Platform.Domain.ModuleActivation;

// ModuleActivationState owns the runtime activation invariants for a single module.
// Infrastructure persists and translates; all transition rules live here.
public sealed class ModuleActivationState
{
    private ModuleActivationState(
        string moduleKey,
        ModuleDesiredState desiredState,
        ModuleRuntimeState runtimeState,
        long version,
        Guid? transitionId,
        Instant updatedUtc,
        string? updatedByActorId,
        string? lastErrorCode,
        string? lastErrorDetail)
    {
        ModuleKey = moduleKey;
        DesiredState = desiredState;
        RuntimeState = runtimeState;
        Version = version;
        TransitionId = transitionId;
        UpdatedUtc = updatedUtc;
        UpdatedByActorId = updatedByActorId;
        LastErrorCode = lastErrorCode;
        LastErrorDetail = lastErrorDetail;
    }

    public string ModuleKey { get; }

    public ModuleDesiredState DesiredState { get; private set; }

    public ModuleRuntimeState RuntimeState { get; private set; }

    public long Version { get; private set; }

    public Guid? TransitionId { get; private set; }

    public Instant UpdatedUtc { get; private set; }

    public string? UpdatedByActorId { get; private set; }

    public string? LastErrorCode { get; private set; }

    public string? LastErrorDetail { get; private set; }

    public bool IsInTransition =>
        RuntimeState is ModuleRuntimeState.Enabling or ModuleRuntimeState.Disabling;

    public bool IsInStableTargetState(ModuleDesiredState target) =>
        DesiredState == target && RuntimeState == StableRuntimeFor(target);

    public static ModuleActivationState Initial(string moduleKey, bool defaultEnabled, Instant changedUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);

        var desired = defaultEnabled ? ModuleDesiredState.Enabled : ModuleDesiredState.Disabled;
        return new ModuleActivationState(
            moduleKey,
            desired,
            StableRuntimeFor(desired),
            version: 0,
            transitionId: null,
            updatedUtc: changedUtc,
            updatedByActorId: null,
            lastErrorCode: null,
            lastErrorDetail: null);
    }

    public static ModuleActivationState Rehydrate(
        string moduleKey,
        ModuleDesiredState desiredState,
        ModuleRuntimeState runtimeState,
        long version,
        Guid? transitionId,
        Instant updatedUtc,
        string? updatedByActorId,
        string? lastErrorCode,
        string? lastErrorDetail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Version must be non-negative.");
        }

        return new ModuleActivationState(
            moduleKey,
            desiredState,
            runtimeState,
            version,
            transitionId,
            updatedUtc,
            updatedByActorId,
            lastErrorCode,
            lastErrorDetail);
    }

    public void BeginTransition(
        ModuleDesiredState target,
        bool canBeDisabled,
        Guid transitionId,
        Instant changedUtc,
        string? actorId)
    {
        if (target == ModuleDesiredState.Disabled && !canBeDisabled)
        {
            throw new InvalidOperationException(
                $"Module '{ModuleKey}' cannot be disabled at runtime.");
        }

        if (IsInStableTargetState(target))
        {
            throw new InvalidOperationException(
                $"Module '{ModuleKey}' is already in desired state '{target}'.");
        }

        if (IsInTransition)
        {
            throw new InvalidOperationException(
                $"Module '{ModuleKey}' has an in-flight transition and cannot begin a new one.");
        }

        DesiredState = target;
        RuntimeState = IntermediateRuntimeFor(target);
        Version++;
        TransitionId = transitionId;
        UpdatedUtc = changedUtc;
        UpdatedByActorId = actorId;
        LastErrorCode = null;
        LastErrorDetail = null;
    }

    public void CompleteTransition(Instant changedUtc, string? actorId)
    {
        if (!IsInTransition)
        {
            throw new InvalidOperationException(
                $"Module '{ModuleKey}' has no in-flight transition to complete.");
        }

        RuntimeState = StableRuntimeFor(DesiredState);
        Version++;
        UpdatedUtc = changedUtc;
        UpdatedByActorId = actorId;
        LastErrorCode = null;
        LastErrorDetail = null;
    }

    public void FailTransition(
        Instant changedUtc,
        string? actorId,
        string errorCode,
        string? errorDetail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        if (!IsInTransition)
        {
            throw new InvalidOperationException(
                $"Module '{ModuleKey}' has no in-flight transition to fail.");
        }

        var reverted = Opposite(DesiredState);
        DesiredState = reverted;
        RuntimeState = StableRuntimeFor(reverted);
        Version++;
        UpdatedUtc = changedUtc;
        UpdatedByActorId = actorId;
        LastErrorCode = errorCode;
        LastErrorDetail = errorDetail;
    }

    private static ModuleRuntimeState StableRuntimeFor(ModuleDesiredState desiredState) =>
        desiredState == ModuleDesiredState.Enabled
            ? ModuleRuntimeState.Enabled
            : ModuleRuntimeState.Disabled;

    private static ModuleRuntimeState IntermediateRuntimeFor(ModuleDesiredState desiredState) =>
        desiredState == ModuleDesiredState.Enabled
            ? ModuleRuntimeState.Enabling
            : ModuleRuntimeState.Disabling;

    private static ModuleDesiredState Opposite(ModuleDesiredState desiredState) =>
        desiredState == ModuleDesiredState.Enabled
            ? ModuleDesiredState.Disabled
            : ModuleDesiredState.Enabled;
}
