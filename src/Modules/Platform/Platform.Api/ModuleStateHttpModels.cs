using BuildingBlocks.Application.Modules;
using BuildingBlocks.Domain.Modules;
using Platform.Application.ModuleState;
using Platform.Domain.ModuleActivation;

namespace Platform.Api;

public sealed record ModuleStateListResponse(IReadOnlyCollection<ModuleStateResponse> Modules)
{
    public static ModuleStateListResponse From(ModuleStateList moduleStates)
    {
        ArgumentNullException.ThrowIfNull(moduleStates);

        return new ModuleStateListResponse(
            moduleStates.Modules.Select(ModuleStateResponse.From).ToArray());
    }
}

public sealed record ModuleStateResponse(
    string Key,
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
    IReadOnlyCollection<ModuleStateChangeResponse> Changes)
{
    public static ModuleStateResponse From(ManagedModuleState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new ModuleStateResponse(
            state.ModuleKey,
            state.DisplayName,
            state.RoutePrefix,
            state.DefaultEnabled,
            state.CanBeDisabled,
            ModuleStateHttpValueFormatter.ToHttpValue(state.DesiredState),
            ModuleStateHttpValueFormatter.ToHttpValue(state.RuntimeState),
            state.Version,
            state.TransitionId,
            state.UpdatedUtc.ToDateTimeOffset(),
            state.UpdatedByActorId,
            state.LastErrorCode,
            state.LastErrorDetail,
            state.Changes.Select(ModuleStateChangeResponse.From).ToArray());
    }
}

public sealed record ModuleStateChangeResponse(
    string BeforeState,
    string AfterState,
    DateTimeOffset ChangedUtc,
    string? ActorId,
    Guid? TransitionId)
{
    public static ModuleStateChangeResponse From(ManagedModuleStateChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new ModuleStateChangeResponse(
            ModuleStateHttpValueFormatter.ToHttpValue(change.BeforeState),
            ModuleStateHttpValueFormatter.ToHttpValue(change.AfterState),
            change.ChangedUtc.ToDateTimeOffset(),
            change.ActorId,
            change.TransitionId);
    }
}

internal static class ModuleStateHttpValueFormatter
{
    public static string ToHttpValue(ModuleDesiredState desiredState)
    {
        return desiredState.ToString().ToLowerInvariant();
    }

    public static string ToHttpValue(ModuleRuntimeState runtimeState)
    {
        return runtimeState.ToString().ToLowerInvariant();
    }
}
