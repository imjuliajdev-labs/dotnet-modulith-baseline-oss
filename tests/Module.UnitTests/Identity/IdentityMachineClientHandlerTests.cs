using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Testing.Time;
using Identity.Application.Administration;
using Identity.Application.Authorization;
using Module.UnitTests.Support;
using NodaTime;

namespace Module.UnitTests.Identity;

public sealed class CreateMachineClientCommandHandlerTests
{
    [Fact]
    public async Task Handle_writes_create_audit_with_new_client_id_when_service_succeeds()
    {
        var fixture = new MachineClientTestFixture("admin-1", correlationId: "corr-mc");
        var created = new MachineClientCreated(
            ClientId: "client-1",
            ClientName: "ci-runner",
            PlaintextSecret: "plain-secret",
            Roles: ["Machine"],
            CreatedUtc: Instant.FromUtc(2026, 4, 14, 12, 0));
        fixture.Service.CreateResult = Result<MachineClientCreated>.Success(created);

        var handler = fixture.CreateCreateHandler();
        var result = await handler.Handle(new CreateMachineClientCommand("ci-runner", ["Machine"]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(created, result.Value);
        var audit = Assert.Single(fixture.AuditWriter.Events);
        Assert.Equal("identity.machine_client.create", audit.Action);
        Assert.Equal("machine_client", audit.TargetType);
        Assert.Equal("client-1", audit.TargetId);
        Assert.Equal("created", audit.Outcome);
        Assert.Equal("admin-1", audit.ActorId);
        Assert.Equal("corr-mc", audit.CorrelationId);
    }

    [Fact]
    public async Task Handle_propagates_service_failure_without_writing_audit()
    {
        var fixture = new MachineClientTestFixture("admin-1");
        fixture.Service.CreateResult = Result<MachineClientCreated>.Failure(IdentityMachineClientErrors.ClientNameAlreadyExists("ci-runner"));

        var handler = fixture.CreateCreateHandler();
        var result = await handler.Handle(new CreateMachineClientCommand("ci-runner", ["Machine"]), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("identity.machine_client_name_already_exists", result.Error.Code);
        Assert.Empty(fixture.AuditWriter.Events);
    }
}

public sealed class RotateMachineClientSecretCommandHandlerTests
{
    [Fact]
    public async Task Handle_writes_rotation_audit_when_service_succeeds()
    {
        var fixture = new MachineClientTestFixture("admin-1");
        fixture.Service.RotateSecretResult = Result<MachineClientSecretRotated>.Success(new MachineClientSecretRotated("client-1", "new-secret"));

        var handler = fixture.CreateRotateHandler();
        var result = await handler.Handle(new RotateMachineClientSecretCommand("client-1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var audit = Assert.Single(fixture.AuditWriter.Events);
        Assert.Equal("identity.machine_client.secret.rotate", audit.Action);
        Assert.Equal("client-1", audit.TargetId);
        Assert.Equal("rotated", audit.Outcome);
    }

    [Fact]
    public async Task Handle_refuses_to_write_audit_when_service_reports_revoked_client()
    {
        var fixture = new MachineClientTestFixture("admin-1");
        fixture.Service.RotateSecretResult = Result<MachineClientSecretRotated>.Failure(IdentityMachineClientErrors.ClientAlreadyRevoked("client-1"));

        var handler = fixture.CreateRotateHandler();
        var result = await handler.Handle(new RotateMachineClientSecretCommand("client-1"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("identity.machine_client_already_revoked", result.Error.Code);
        Assert.Empty(fixture.AuditWriter.Events);
    }
}

public sealed class RevokeMachineClientCommandHandlerTests
{
    [Fact]
    public async Task Handle_writes_revoke_audit_when_service_succeeds()
    {
        var fixture = new MachineClientTestFixture("admin-1");
        fixture.Service.RevokeResult = Result<MachineClientSummary>.Success(IdentityTestData.CreateMachineClientSummary("client-1", isRevoked: true));

        var handler = fixture.CreateRevokeHandler();
        var result = await handler.Handle(new RevokeMachineClientCommand("client-1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var audit = Assert.Single(fixture.AuditWriter.Events);
        Assert.Equal("identity.machine_client.revoke", audit.Action);
        Assert.Equal("revoked", audit.Outcome);
    }

    [Fact]
    public async Task Handle_propagates_not_found_without_writing_audit()
    {
        var fixture = new MachineClientTestFixture("admin-1");
        fixture.Service.RevokeResult = Result<MachineClientSummary>.Failure(IdentityMachineClientErrors.ClientNotFound("ghost"));

        var handler = fixture.CreateRevokeHandler();
        var result = await handler.Handle(new RevokeMachineClientCommand("ghost"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(fixture.AuditWriter.Events);
    }
}

public sealed class SetMachineClientStatusCommandHandlerTests
{
    [Theory]
    [InlineData(true, "activated")]
    [InlineData(false, "deactivated")]
    public async Task Handle_writes_status_audit_with_matching_outcome(bool isActive, string expectedOutcome)
    {
        var fixture = new MachineClientTestFixture("admin-1");
        fixture.Service.SetActiveResult = Result<MachineClientSummary>.Success(IdentityTestData.CreateMachineClientSummary("client-1", isActive: isActive));

        var handler = fixture.CreateSetStatusHandler();
        var result = await handler.Handle(new SetMachineClientStatusCommand("client-1", isActive), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var audit = Assert.Single(fixture.AuditWriter.Events);
        Assert.Equal("identity.machine_client.status.update", audit.Action);
        Assert.Equal(expectedOutcome, audit.Outcome);
    }

    [Fact]
    public async Task Handle_propagates_service_failure_without_writing_audit()
    {
        var fixture = new MachineClientTestFixture("admin-1");
        fixture.Service.SetActiveResult = Result<MachineClientSummary>.Failure(IdentityMachineClientErrors.ClientNotFound("ghost"));

        var handler = fixture.CreateSetStatusHandler();
        var result = await handler.Handle(new SetMachineClientStatusCommand("ghost", IsActive: false), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(fixture.AuditWriter.Events);
    }
}

internal sealed class MachineClientTestFixture
{
    public RecordingAuditEventWriter AuditWriter { get; } = new();
    public FakeClock Clock { get; } = new(IdentityTestData.FixedNow);
    public StubCurrentActorAccessor ActorAccessor { get; }
    public InMemoryRequestContextAccessor RequestContextAccessor { get; }
    public FakeMachineClientAdministrationService Service { get; } = new();

    public MachineClientTestFixture(string currentActorId, string? correlationId = null)
    {
        var actor = new CurrentActor(currentActorId, isAuthenticated: true, roles: [IdentityRoles.Admin]);
        ActorAccessor = new StubCurrentActorAccessor(actor);
        RequestContextAccessor = new InMemoryRequestContextAccessor
        {
            Current = correlationId is null ? null : new RequestContext(correlationId, "req-" + currentActorId)
        };
    }

    public CreateMachineClientCommandHandler CreateCreateHandler() =>
        new(AuditWriter, Clock, ActorAccessor, RequestContextAccessor, Service);

    public RotateMachineClientSecretCommandHandler CreateRotateHandler() =>
        new(AuditWriter, Clock, ActorAccessor, RequestContextAccessor, Service);

    public RevokeMachineClientCommandHandler CreateRevokeHandler() =>
        new(AuditWriter, Clock, ActorAccessor, RequestContextAccessor, Service);

    public SetMachineClientStatusCommandHandler CreateSetStatusHandler() =>
        new(AuditWriter, Clock, ActorAccessor, RequestContextAccessor, Service);

}

internal sealed class FakeMachineClientAdministrationService : IMachineClientAdministrationService
{
    public IReadOnlyCollection<MachineClientSummary> ListResult { get; set; } = Array.Empty<MachineClientSummary>();
    public Result<MachineClientCreated>? CreateResult { get; set; }
    public Result<MachineClientSecretRotated>? RotateSecretResult { get; set; }
    public Result<MachineClientSummary>? RevokeResult { get; set; }
    public Result<MachineClientSummary>? SetActiveResult { get; set; }
    public Result<MachineClientSummary>? GetResult { get; set; }

    public ValueTask<IReadOnlyCollection<MachineClientSummary>> ListAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(ListResult);
    }

    public ValueTask<Result<MachineClientCreated>> CreateAsync(string clientName, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(CreateResult ?? throw new InvalidOperationException("CreateResult not configured."));
    }

    public ValueTask<Result<MachineClientSecretRotated>> RotateSecretAsync(string clientId, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(RotateSecretResult ?? throw new InvalidOperationException("RotateSecretResult not configured."));
    }

    public ValueTask<Result<MachineClientSummary>> RevokeAsync(string clientId, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(RevokeResult ?? throw new InvalidOperationException("RevokeResult not configured."));
    }

    public ValueTask<Result<MachineClientSummary>> SetActiveAsync(string clientId, bool isActive, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(SetActiveResult ?? throw new InvalidOperationException("SetActiveResult not configured."));
    }

    public ValueTask<Result<MachineClientSummary>> GetAsync(string clientId, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(GetResult ?? throw new InvalidOperationException("GetResult not configured."));
    }
}
