using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Domain.Modules;
using BuildingBlocks.Testing.Time;
using Module.UnitTests.Support;
using NodaTime;
using Platform.Application.Authorization;
using Platform.Application.ModuleState;
using Platform.Domain.ModuleActivation;

namespace Module.UnitTests.Platform;

public sealed class EnableModuleCommandHandlerTests
{
    [Fact]
    public async Task Handle_returns_success_and_writes_audit_event_on_successful_enable()
    {
        var fixture = ModuleStateTestFixture.ForActor("admin-1", correlationId: "corr-42");
        var targetState = TestModuleState("sample-feature", ModuleRuntimeState.Enabled);
        fixture.Store.EnableOutcome = new ModuleStateChangeResult(ModuleStateChangeStatus.Success, targetState);

        var handler = fixture.CreateEnableHandler();
        var result = await handler.Handle(new EnableModuleCommand("sample-feature"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(targetState, result.Value);
        Assert.Equal("sample-feature", fixture.Store.LastEnableModuleKey);
        Assert.Equal("admin-1", fixture.Store.LastEnableActorId);
        Assert.Equal(fixture.Clock.GetCurrentInstant(), fixture.Store.LastEnableOccurredUtc);

        var audit = Assert.Single(fixture.AuditWriter.Events);
        Assert.Equal(PlatformModuleInfo.ModuleKey, audit.ModuleKey);
        Assert.Equal("platform.module_state.enable", audit.Action);
        Assert.Equal("module", audit.TargetType);
        Assert.Equal("sample-feature", audit.TargetId);
        Assert.Equal("Enabled", audit.Outcome);
        Assert.Equal(fixture.Clock.GetCurrentInstant(), audit.OccurredUtc);
        Assert.Equal("admin-1", audit.ActorId);
        Assert.Equal("corr-42", audit.CorrelationId);
    }

    [Theory]
    [InlineData(ModuleStateChangeStatus.UnknownModule, "platform.module_state.unknown_module", ErrorKind.NotFound)]
    [InlineData(ModuleStateChangeStatus.AlreadyEnabled, "platform.module_state.already_enabled", ErrorKind.Conflict)]
    [InlineData(ModuleStateChangeStatus.TransitionFailed, "platform.module_state.transition_failed", ErrorKind.Failure)]
    public async Task Handle_returns_mapped_failure_and_writes_no_audit_when_store_rejects_enable(
        ModuleStateChangeStatus status,
        string expectedCode,
        ErrorKind expectedKind)
    {
        var fixture = ModuleStateTestFixture.ForActor("admin-1");
        fixture.Store.EnableOutcome = new ModuleStateChangeResult(status, State: null);

        var handler = fixture.CreateEnableHandler();
        var result = await handler.Handle(new EnableModuleCommand("ghost"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Equal(expectedKind, result.Error.Kind);
        Assert.Empty(fixture.AuditWriter.Events);
    }

    internal static ManagedModuleState TestModuleState(string key, ModuleRuntimeState runtime)
    {
        return new ManagedModuleState(
            ModuleKey: key,
            DisplayName: key,
            RoutePrefix: "/" + key,
            DefaultEnabled: true,
            CanBeDisabled: true,
            DesiredState: ModuleDesiredState.Enabled,
            RuntimeState: runtime,
            Version: 1,
            TransitionId: null,
            UpdatedUtc: Instant.FromUtc(2026, 4, 14, 12, 0),
            UpdatedByActorId: "admin-1",
            LastErrorCode: null,
            LastErrorDetail: null,
            Changes: Array.Empty<ManagedModuleStateChange>());
    }
}

public sealed class DisableModuleCommandHandlerTests
{
    [Fact]
    public async Task Handle_returns_success_and_writes_audit_event_on_successful_disable()
    {
        var fixture = ModuleStateTestFixture.ForActor("admin-9", correlationId: "corr-disable");
        var targetState = EnableModuleCommandHandlerTests.TestModuleState("sample-feature", ModuleRuntimeState.Disabled);
        fixture.Store.DisableOutcome = new ModuleStateChangeResult(ModuleStateChangeStatus.Success, targetState);

        var handler = fixture.CreateDisableHandler();
        var result = await handler.Handle(new DisableModuleCommand("sample-feature"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(targetState, result.Value);
        Assert.Equal("sample-feature", fixture.Store.LastDisableModuleKey);
        Assert.Equal("admin-9", fixture.Store.LastDisableActorId);

        var audit = Assert.Single(fixture.AuditWriter.Events);
        Assert.Equal("platform.module_state.disable", audit.Action);
        Assert.Equal("Disabled", audit.Outcome);
        Assert.Equal("admin-9", audit.ActorId);
        Assert.Equal("corr-disable", audit.CorrelationId);
    }

    [Theory]
    [InlineData(ModuleStateChangeStatus.AlreadyDisabled, "platform.module_state.already_disabled", ErrorKind.Conflict)]
    [InlineData(ModuleStateChangeStatus.NotDisableable, "platform.module_state.cannot_disable", ErrorKind.Conflict)]
    [InlineData(ModuleStateChangeStatus.UnknownModule, "platform.module_state.unknown_module", ErrorKind.NotFound)]
    public async Task Handle_returns_mapped_failure_and_writes_no_audit_when_store_rejects_disable(
        ModuleStateChangeStatus status,
        string expectedCode,
        ErrorKind expectedKind)
    {
        var fixture = ModuleStateTestFixture.ForActor("admin-1");
        fixture.Store.DisableOutcome = new ModuleStateChangeResult(status, State: null);

        var handler = fixture.CreateDisableHandler();
        var result = await handler.Handle(new DisableModuleCommand("platform"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Equal(expectedKind, result.Error.Kind);
        Assert.Empty(fixture.AuditWriter.Events);
    }
}

internal sealed class ModuleStateTestFixture
{
    public RecordingAuditEventWriter AuditWriter { get; } = new();
    public FakeClock Clock { get; } = new(Instant.FromUtc(2026, 4, 14, 12, 0));
    public StubCurrentActorAccessor ActorAccessor { get; }
    public InMemoryRequestContextAccessor RequestContextAccessor { get; }
    public FakePlatformModuleStateStore Store { get; } = new();

    private ModuleStateTestFixture(StubCurrentActorAccessor actorAccessor, InMemoryRequestContextAccessor requestContextAccessor)
    {
        ActorAccessor = actorAccessor;
        RequestContextAccessor = requestContextAccessor;
    }

    public static ModuleStateTestFixture ForActor(string actorId, string? correlationId = null)
    {
        var actor = new CurrentActor(actorId, isAuthenticated: true, roles: ["Admin"]);
        var requestContext = new InMemoryRequestContextAccessor
        {
            Current = correlationId is null ? null : new RequestContext(correlationId, "req-" + actorId)
        };
        return new ModuleStateTestFixture(new StubCurrentActorAccessor(actor), requestContext);
    }

    public EnableModuleCommandHandler CreateEnableHandler()
    {
        return new EnableModuleCommandHandler(AuditWriter, Clock, ActorAccessor, Store, RequestContextAccessor);
    }

    public DisableModuleCommandHandler CreateDisableHandler()
    {
        return new DisableModuleCommandHandler(AuditWriter, Clock, ActorAccessor, Store, RequestContextAccessor);
    }
}

internal sealed class FakePlatformModuleStateStore : IPlatformModuleStateStore
{
    public ModuleStateChangeResult EnableOutcome { get; set; } = new(ModuleStateChangeStatus.TransitionFailed, State: null);
    public ModuleStateChangeResult DisableOutcome { get; set; } = new(ModuleStateChangeStatus.TransitionFailed, State: null);

    public string? LastEnableModuleKey { get; private set; }
    public string? LastEnableActorId { get; private set; }
    public Instant? LastEnableOccurredUtc { get; private set; }

    public string? LastDisableModuleKey { get; private set; }
    public string? LastDisableActorId { get; private set; }
    public Instant? LastDisableOccurredUtc { get; private set; }

    public ValueTask<IReadOnlyCollection<ManagedModuleState>> ListAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<IReadOnlyCollection<ManagedModuleState>>(Array.Empty<ManagedModuleState>());
    }

    public ValueTask<ModuleStateChangeResult> EnableAsync(string moduleKey, string? actorId, Instant changedUtc, CancellationToken cancellationToken)
    {
        LastEnableModuleKey = moduleKey;
        LastEnableActorId = actorId;
        LastEnableOccurredUtc = changedUtc;
        return ValueTask.FromResult(EnableOutcome);
    }

    public ValueTask<ModuleStateChangeResult> DisableAsync(string moduleKey, string? actorId, Instant changedUtc, CancellationToken cancellationToken)
    {
        LastDisableModuleKey = moduleKey;
        LastDisableActorId = actorId;
        LastDisableOccurredUtc = changedUtc;
        return ValueTask.FromResult(DisableOutcome);
    }

    public ValueTask<ModuleStateSnapshot> GetRequiredStateAsync(string moduleKey, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new ModuleStateSnapshot(moduleKey, ModuleRuntimeState.Enabled));
    }
}
