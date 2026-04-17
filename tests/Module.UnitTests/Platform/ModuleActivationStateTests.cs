using BuildingBlocks.Domain.Modules;
using NodaTime;
using Platform.Domain.ModuleActivation;
using Xunit;

namespace Module.UnitTests.Platform;

public sealed class ModuleActivationStateTests
{
    private static readonly Instant T0 = Instant.FromUtc(2026, 1, 1, 12, 0);
    private static readonly Instant T1 = Instant.FromUtc(2026, 1, 1, 12, 5);

    [Fact]
    public void Initial_defaults_stable_state_from_descriptor_flag()
    {
        var enabled = ModuleActivationState.Initial("blog", defaultEnabled: true, T0);
        Assert.Equal(ModuleDesiredState.Enabled, enabled.DesiredState);
        Assert.Equal(ModuleRuntimeState.Enabled, enabled.RuntimeState);
        Assert.Equal(0, enabled.Version);
        Assert.Null(enabled.TransitionId);

        var disabled = ModuleActivationState.Initial("blog", defaultEnabled: false, T0);
        Assert.Equal(ModuleDesiredState.Disabled, disabled.DesiredState);
        Assert.Equal(ModuleRuntimeState.Disabled, disabled.RuntimeState);
    }

    [Fact]
    public void BeginTransition_from_disabled_to_enabled_moves_to_enabling_and_bumps_version()
    {
        var aggregate = ModuleActivationState.Initial("blog", defaultEnabled: false, T0);
        var transitionId = Guid.NewGuid();

        aggregate.BeginTransition(ModuleDesiredState.Enabled, canBeDisabled: true, transitionId, T1, "actor-1");

        Assert.Equal(ModuleDesiredState.Enabled, aggregate.DesiredState);
        Assert.Equal(ModuleRuntimeState.Enabling, aggregate.RuntimeState);
        Assert.Equal(1, aggregate.Version);
        Assert.Equal(transitionId, aggregate.TransitionId);
        Assert.Equal(T1, aggregate.UpdatedUtc);
        Assert.Equal("actor-1", aggregate.UpdatedByActorId);
        Assert.True(aggregate.IsInTransition);
    }

    [Fact]
    public void BeginTransition_to_disabled_when_module_not_disableable_throws()
    {
        var aggregate = ModuleActivationState.Initial("platform", defaultEnabled: true, T0);

        Assert.Throws<InvalidOperationException>(() => aggregate.BeginTransition(
            ModuleDesiredState.Disabled, canBeDisabled: false, Guid.NewGuid(), T1, actorId: null));
    }

    [Fact]
    public void BeginTransition_when_already_in_target_state_throws()
    {
        var aggregate = ModuleActivationState.Initial("blog", defaultEnabled: true, T0);

        Assert.Throws<InvalidOperationException>(() => aggregate.BeginTransition(
            ModuleDesiredState.Enabled, canBeDisabled: true, Guid.NewGuid(), T1, actorId: null));
    }

    [Fact]
    public void BeginTransition_when_already_in_transition_throws()
    {
        var aggregate = ModuleActivationState.Initial("blog", defaultEnabled: false, T0);
        aggregate.BeginTransition(ModuleDesiredState.Enabled, canBeDisabled: true, Guid.NewGuid(), T1, null);

        Assert.Throws<InvalidOperationException>(() => aggregate.BeginTransition(
            ModuleDesiredState.Disabled, canBeDisabled: true, Guid.NewGuid(), T1, null));
    }

    [Fact]
    public void CompleteTransition_finalizes_to_stable_state_and_clears_error()
    {
        var aggregate = ModuleActivationState.Initial("blog", defaultEnabled: false, T0);
        aggregate.BeginTransition(ModuleDesiredState.Enabled, canBeDisabled: true, Guid.NewGuid(), T1, "actor-1");

        aggregate.CompleteTransition(T1, "actor-1");

        Assert.Equal(ModuleRuntimeState.Enabled, aggregate.RuntimeState);
        Assert.Equal(ModuleDesiredState.Enabled, aggregate.DesiredState);
        Assert.Equal(2, aggregate.Version);
        Assert.Null(aggregate.LastErrorCode);
        Assert.Null(aggregate.LastErrorDetail);
        Assert.False(aggregate.IsInTransition);
    }

    [Fact]
    public void CompleteTransition_when_not_in_transition_throws()
    {
        var aggregate = ModuleActivationState.Initial("blog", defaultEnabled: true, T0);

        Assert.Throws<InvalidOperationException>(() => aggregate.CompleteTransition(T1, null));
    }

    [Fact]
    public void FailTransition_from_enabling_reverts_to_disabled_and_records_error()
    {
        var aggregate = ModuleActivationState.Initial("blog", defaultEnabled: false, T0);
        aggregate.BeginTransition(ModuleDesiredState.Enabled, canBeDisabled: true, Guid.NewGuid(), T1, "actor-1");

        aggregate.FailTransition(T1, "actor-1", "platform.module_state.transition_failed", "participant failed");

        Assert.Equal(ModuleDesiredState.Disabled, aggregate.DesiredState);
        Assert.Equal(ModuleRuntimeState.Disabled, aggregate.RuntimeState);
        Assert.Equal(2, aggregate.Version);
        Assert.Equal("platform.module_state.transition_failed", aggregate.LastErrorCode);
        Assert.Equal("participant failed", aggregate.LastErrorDetail);
    }

    [Fact]
    public void FailTransition_from_disabling_reverts_to_enabled_and_records_error()
    {
        var aggregate = ModuleActivationState.Initial("blog", defaultEnabled: true, T0);
        aggregate.BeginTransition(ModuleDesiredState.Disabled, canBeDisabled: true, Guid.NewGuid(), T1, "actor-1");

        aggregate.FailTransition(T1, "actor-1", "platform.module_state.transition_failed", "drain timed out");

        Assert.Equal(ModuleDesiredState.Enabled, aggregate.DesiredState);
        Assert.Equal(ModuleRuntimeState.Enabled, aggregate.RuntimeState);
        Assert.Equal(2, aggregate.Version);
        Assert.Equal("platform.module_state.transition_failed", aggregate.LastErrorCode);
    }

    [Fact]
    public void FailTransition_when_not_in_transition_throws()
    {
        var aggregate = ModuleActivationState.Initial("blog", defaultEnabled: true, T0);

        Assert.Throws<InvalidOperationException>(() => aggregate.FailTransition(
            T1, null, "code", "detail"));
    }

    [Fact]
    public void Rehydrate_restores_all_fields_without_mutation()
    {
        var transitionId = Guid.NewGuid();

        var aggregate = ModuleActivationState.Rehydrate(
            moduleKey: "blog",
            desiredState: ModuleDesiredState.Enabled,
            runtimeState: ModuleRuntimeState.Enabling,
            version: 7,
            transitionId: transitionId,
            updatedUtc: T1,
            updatedByActorId: "actor-9",
            lastErrorCode: null,
            lastErrorDetail: null);

        Assert.Equal("blog", aggregate.ModuleKey);
        Assert.Equal(ModuleDesiredState.Enabled, aggregate.DesiredState);
        Assert.Equal(ModuleRuntimeState.Enabling, aggregate.RuntimeState);
        Assert.Equal(7, aggregate.Version);
        Assert.Equal(transitionId, aggregate.TransitionId);
        Assert.Equal("actor-9", aggregate.UpdatedByActorId);
        Assert.True(aggregate.IsInTransition);
    }

    [Fact]
    public void IsInStableTargetState_matches_only_when_stable_and_matching_target()
    {
        var aggregate = ModuleActivationState.Initial("blog", defaultEnabled: true, T0);
        Assert.True(aggregate.IsInStableTargetState(ModuleDesiredState.Enabled));
        Assert.False(aggregate.IsInStableTargetState(ModuleDesiredState.Disabled));

        aggregate.BeginTransition(ModuleDesiredState.Disabled, canBeDisabled: true, Guid.NewGuid(), T1, null);
        Assert.False(aggregate.IsInStableTargetState(ModuleDesiredState.Disabled));
        Assert.False(aggregate.IsInStableTargetState(ModuleDesiredState.Enabled));
    }
}
