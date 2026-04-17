using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Testing.Time;
using Identity.Application.Administration;
using Identity.Application.Authorization;
using Module.UnitTests.Support;

namespace Module.UnitTests.Identity;

public sealed class CreateIdentityUserCommandHandlerTests
{
    [Fact]
    public async Task Handle_writes_enabled_outcome_audit_when_service_succeeds()
    {
        var fixture = new IdentityUserAdministrationTestFixture("admin-1", correlationId: "corr-1");
        var created = IdentityTestData.CreateUserAccount("user-9", enabled: true);
        fixture.Service.CreateResult = Result<IdentityUserAccount>.Success(created);

        var handler = fixture.CreateCreateHandler();
        var command = new CreateIdentityUserCommand("alice", "Alice Example", "P@ssw0rdLongEnough!", ["User"], "Etc/UTC", Enabled: true);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(created, result.Value);

        var audit = Assert.Single(fixture.AuditWriter.Events);
        Assert.Equal("identity.user.create", audit.Action);
        Assert.Equal("user", audit.TargetType);
        Assert.Equal("user-9", audit.TargetId);
        Assert.Equal("enabled", audit.Outcome);
        Assert.Equal("admin-1", audit.ActorId);
        Assert.Equal("corr-1", audit.CorrelationId);
        Assert.Equal(fixture.Clock.GetCurrentInstant(), audit.OccurredUtc);
    }

    [Fact]
    public async Task Handle_writes_disabled_outcome_audit_when_service_returns_disabled_user()
    {
        var fixture = new IdentityUserAdministrationTestFixture("admin-1");
        var created = IdentityTestData.CreateUserAccount("user-9", enabled: false);
        fixture.Service.CreateResult = Result<IdentityUserAccount>.Success(created);

        var handler = fixture.CreateCreateHandler();
        var command = new CreateIdentityUserCommand("alice", "Alice Example", "pw", ["User"], "Etc/UTC", Enabled: false);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var audit = Assert.Single(fixture.AuditWriter.Events);
        Assert.Equal("disabled", audit.Outcome);
    }

    [Fact]
    public async Task Handle_propagates_service_failure_and_writes_no_audit()
    {
        var fixture = new IdentityUserAdministrationTestFixture("admin-1");
        fixture.Service.CreateResult = Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.UserNameAlreadyExists("alice"));

        var handler = fixture.CreateCreateHandler();
        var command = new CreateIdentityUserCommand("alice", "Alice Example", "pw", ["User"], "Etc/UTC", Enabled: true);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("identity.user_name_already_exists", result.Error.Code);
        Assert.Empty(fixture.AuditWriter.Events);
    }

}

public sealed class SetIdentityUserRolesCommandHandlerTests
{
    [Fact]
    public async Task Handle_blocks_actor_from_removing_their_own_admin_role_without_touching_service()
    {
        var fixture = new IdentityUserAdministrationTestFixture("admin-1");

        var handler = fixture.CreateSetRolesHandler();
        var command = new SetIdentityUserRolesCommand("admin-1", ["User"]);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("identity.cannot_remove_own_admin_role", result.Error.Code);
        Assert.False(fixture.Service.SetRolesCalled);
        Assert.Empty(fixture.AuditWriter.Events);
    }

    [Fact]
    public async Task Handle_allows_actor_to_update_own_roles_when_admin_role_is_retained()
    {
        var fixture = new IdentityUserAdministrationTestFixture("admin-1");
        fixture.Service.SetRolesResult = Result<IdentityUserAccount>.Success(
            IdentityTestData.CreateUserAccount("admin-1", enabled: true, roles: [IdentityRoles.Admin, IdentityRoles.User]));

        var handler = fixture.CreateSetRolesHandler();
        var command = new SetIdentityUserRolesCommand("admin-1", [IdentityRoles.Admin, IdentityRoles.User]);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(fixture.Service.SetRolesCalled);
        var audit = Assert.Single(fixture.AuditWriter.Events);
        Assert.Equal("identity.user.roles.update", audit.Action);
        Assert.Equal("updated", audit.Outcome);
    }

    [Fact]
    public async Task Handle_allows_admin_to_update_another_users_roles_and_writes_audit()
    {
        var fixture = new IdentityUserAdministrationTestFixture("admin-1");
        fixture.Service.SetRolesResult = Result<IdentityUserAccount>.Success(
            IdentityTestData.CreateUserAccount("user-9", enabled: true));

        var handler = fixture.CreateSetRolesHandler();
        var command = new SetIdentityUserRolesCommand("user-9", ["User"]);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var audit = Assert.Single(fixture.AuditWriter.Events);
        Assert.Equal("user-9", audit.TargetId);
    }

    [Fact]
    public async Task Handle_propagates_service_failure_without_writing_audit()
    {
        var fixture = new IdentityUserAdministrationTestFixture("admin-1");
        fixture.Service.SetRolesResult = Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.AccountNotFound("ghost"));

        var handler = fixture.CreateSetRolesHandler();
        var result = await handler.Handle(new SetIdentityUserRolesCommand("ghost", ["User"]), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("identity.user_not_found", result.Error.Code);
        Assert.Empty(fixture.AuditWriter.Events);
    }
}

public sealed class SetIdentityUserStatusCommandHandlerTests
{
    [Fact]
    public async Task Handle_blocks_actor_from_disabling_their_own_account_without_touching_service()
    {
        var fixture = new IdentityUserAdministrationTestFixture("admin-1");

        var handler = fixture.CreateSetStatusHandler();
        var result = await handler.Handle(new SetIdentityUserStatusCommand("admin-1", Enabled: false), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("identity.cannot_disable_current_actor", result.Error.Code);
        Assert.False(fixture.Service.SetEnabledCalled);
        Assert.Empty(fixture.AuditWriter.Events);
    }

    [Fact]
    public async Task Handle_allows_actor_to_re_enable_their_own_account()
    {
        var fixture = new IdentityUserAdministrationTestFixture("admin-1");
        fixture.Service.SetEnabledResult = Result<IdentityUserAccount>.Success(
            IdentityTestData.CreateUserAccount("admin-1", enabled: true));

        var handler = fixture.CreateSetStatusHandler();
        var result = await handler.Handle(new SetIdentityUserStatusCommand("admin-1", Enabled: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var audit = Assert.Single(fixture.AuditWriter.Events);
        Assert.Equal("enabled", audit.Outcome);
    }

    [Fact]
    public async Task Handle_writes_disabled_outcome_when_disabling_another_user()
    {
        var fixture = new IdentityUserAdministrationTestFixture("admin-1");
        fixture.Service.SetEnabledResult = Result<IdentityUserAccount>.Success(
            IdentityTestData.CreateUserAccount("user-9", enabled: false));

        var handler = fixture.CreateSetStatusHandler();
        var result = await handler.Handle(new SetIdentityUserStatusCommand("user-9", Enabled: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var audit = Assert.Single(fixture.AuditWriter.Events);
        Assert.Equal("identity.user.status.update", audit.Action);
        Assert.Equal("disabled", audit.Outcome);
    }

    [Fact]
    public async Task Handle_propagates_service_failure_without_writing_audit()
    {
        var fixture = new IdentityUserAdministrationTestFixture("admin-1");
        fixture.Service.SetEnabledResult = Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.AccountNotFound("ghost"));

        var handler = fixture.CreateSetStatusHandler();
        var result = await handler.Handle(new SetIdentityUserStatusCommand("ghost", Enabled: true), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(fixture.AuditWriter.Events);
    }
}

public sealed class ResetIdentityUserPasswordCommandHandlerTests
{
    [Fact]
    public async Task Handle_writes_password_reset_audit_when_service_succeeds()
    {
        var fixture = new IdentityUserAdministrationTestFixture("admin-1");
        fixture.Service.ResetPasswordResult = Result<IdentityUserAccount>.Success(
            IdentityTestData.CreateUserAccount("user-9", enabled: true));

        var handler = fixture.CreateResetPasswordHandler();
        var result = await handler.Handle(new ResetIdentityUserPasswordCommand("user-9", "N3wP@ssword!"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var audit = Assert.Single(fixture.AuditWriter.Events);
        Assert.Equal("identity.user.password.reset", audit.Action);
        Assert.Equal("updated", audit.Outcome);
        Assert.Equal("user-9", audit.TargetId);
    }

    [Fact]
    public async Task Handle_propagates_service_failure_without_writing_audit()
    {
        var fixture = new IdentityUserAdministrationTestFixture("admin-1");
        fixture.Service.ResetPasswordResult = Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.PasswordTooShort(12));

        var handler = fixture.CreateResetPasswordHandler();
        var result = await handler.Handle(new ResetIdentityUserPasswordCommand("user-9", "short"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("identity.password_too_short", result.Error.Code);
        Assert.Empty(fixture.AuditWriter.Events);
    }
}

public sealed class RevokeIdentityUserSessionsCommandHandlerTests
{
    [Fact]
    public async Task Handle_writes_session_revocation_audit_when_service_succeeds()
    {
        var fixture = new IdentityUserAdministrationTestFixture("admin-1");
        fixture.Service.RevokeSessionsResult = Result<IdentityUserAccount>.Success(
            IdentityTestData.CreateUserAccount("user-9", enabled: true));

        var handler = fixture.CreateRevokeSessionsHandler();
        var result = await handler.Handle(new RevokeIdentityUserSessionsCommand("user-9"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var audit = Assert.Single(fixture.AuditWriter.Events);
        Assert.Equal("identity.user.sessions.revoke", audit.Action);
        Assert.Equal("revoked", audit.Outcome);
    }

    [Fact]
    public async Task Handle_propagates_service_failure_without_writing_audit()
    {
        var fixture = new IdentityUserAdministrationTestFixture("admin-1");
        fixture.Service.RevokeSessionsResult = Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.AccountNotFound("ghost"));

        var handler = fixture.CreateRevokeSessionsHandler();
        var result = await handler.Handle(new RevokeIdentityUserSessionsCommand("ghost"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(fixture.AuditWriter.Events);
    }
}

public sealed class UnlockIdentityUserCommandHandlerTests
{
    [Fact]
    public async Task Handle_writes_unlock_audit_when_service_succeeds()
    {
        var fixture = new IdentityUserAdministrationTestFixture("admin-1");
        fixture.Service.UnlockResult = Result<IdentityUserAccount>.Success(
            IdentityTestData.CreateUserAccount("user-9", enabled: true));

        var handler = fixture.CreateUnlockHandler();
        var result = await handler.Handle(new UnlockIdentityUserCommand("user-9"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var audit = Assert.Single(fixture.AuditWriter.Events);
        Assert.Equal("identity.user.unlock", audit.Action);
        Assert.Equal("unlocked", audit.Outcome);
    }

    [Fact]
    public async Task Handle_propagates_service_failure_without_writing_audit()
    {
        var fixture = new IdentityUserAdministrationTestFixture("admin-1");
        fixture.Service.UnlockResult = Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.AccountNotFound("ghost"));

        var handler = fixture.CreateUnlockHandler();
        var result = await handler.Handle(new UnlockIdentityUserCommand("ghost"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(fixture.AuditWriter.Events);
    }
}

internal sealed class IdentityUserAdministrationTestFixture
{
    public RecordingAuditEventWriter AuditWriter { get; } = new();
    public FakeClock Clock { get; } = new(IdentityTestData.FixedNow);
    public StubCurrentActorAccessor ActorAccessor { get; }
    public InMemoryRequestContextAccessor RequestContextAccessor { get; }
    public FakeIdentityUserAdministrationService Service { get; } = new();

    public IdentityUserAdministrationTestFixture(string currentActorId, string? correlationId = null)
    {
        var actor = new CurrentActor(currentActorId, isAuthenticated: true, roles: [IdentityRoles.Admin]);
        ActorAccessor = new StubCurrentActorAccessor(actor);
        RequestContextAccessor = new InMemoryRequestContextAccessor
        {
            Current = correlationId is null ? null : new RequestContext(correlationId, "req-" + currentActorId)
        };
    }

    public CreateIdentityUserCommandHandler CreateCreateHandler() =>
        new(AuditWriter, Clock, ActorAccessor, RequestContextAccessor, Service);

    public SetIdentityUserRolesCommandHandler CreateSetRolesHandler() =>
        new(AuditWriter, Clock, ActorAccessor, RequestContextAccessor, Service);

    public SetIdentityUserStatusCommandHandler CreateSetStatusHandler() =>
        new(AuditWriter, Clock, ActorAccessor, RequestContextAccessor, Service);

    public ResetIdentityUserPasswordCommandHandler CreateResetPasswordHandler() =>
        new(AuditWriter, Clock, ActorAccessor, RequestContextAccessor, Service);

    public RevokeIdentityUserSessionsCommandHandler CreateRevokeSessionsHandler() =>
        new(AuditWriter, Clock, ActorAccessor, RequestContextAccessor, Service);

    public UnlockIdentityUserCommandHandler CreateUnlockHandler() =>
        new(AuditWriter, Clock, ActorAccessor, RequestContextAccessor, Service);
}

internal sealed class FakeIdentityUserAdministrationService : IIdentityUserAdministrationService
{
    public IReadOnlyCollection<IdentityUserAccount> ListResult { get; set; } = Array.Empty<IdentityUserAccount>();
    public Result<IdentityUserAccount>? CreateResult { get; set; }
    public Result<IdentityUserAccount>? SetRolesResult { get; set; }
    public Result<IdentityUserAccount>? SetEnabledResult { get; set; }
    public Result<IdentityUserAccount>? ResetPasswordResult { get; set; }
    public Result<IdentityUserAccount>? RevokeSessionsResult { get; set; }
    public Result<IdentityUserAccount>? UnlockResult { get; set; }

    public bool SetRolesCalled { get; private set; }
    public bool SetEnabledCalled { get; private set; }

    public ValueTask<IReadOnlyCollection<IdentityUserAccount>> ListAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(ListResult);
    }

    public ValueTask<Result<IdentityUserAccount>> CreateAsync(string userName, string displayName, string password, IReadOnlyCollection<string> roles, string preferredTimeZoneId, bool enabled, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(CreateResult ?? throw new InvalidOperationException("CreateResult not configured."));
    }

    public ValueTask<Result<IdentityUserAccount>> SetRolesAsync(string actorId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        SetRolesCalled = true;
        return ValueTask.FromResult(SetRolesResult ?? throw new InvalidOperationException("SetRolesResult not configured."));
    }

    public ValueTask<Result<IdentityUserAccount>> SetEnabledAsync(string actorId, bool enabled, CancellationToken cancellationToken)
    {
        SetEnabledCalled = true;
        return ValueTask.FromResult(SetEnabledResult ?? throw new InvalidOperationException("SetEnabledResult not configured."));
    }

    public ValueTask<Result<IdentityUserAccount>> ResetPasswordAsync(string actorId, string password, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(ResetPasswordResult ?? throw new InvalidOperationException("ResetPasswordResult not configured."));
    }

    public ValueTask<Result<IdentityUserAccount>> RevokeSessionsAsync(string actorId, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(RevokeSessionsResult ?? throw new InvalidOperationException("RevokeSessionsResult not configured."));
    }

    public ValueTask<Result<IdentityUserAccount>> UnlockAsync(string actorId, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(UnlockResult ?? throw new InvalidOperationException("UnlockResult not configured."));
    }
}
