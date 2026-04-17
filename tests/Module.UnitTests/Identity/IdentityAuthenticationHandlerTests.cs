using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Testing.Time;
using Identity.Application.Authentication;
using Module.UnitTests.Support;

namespace Module.UnitTests.Identity;

public sealed class PasswordSignInCommandHandlerTests
{
    [Fact]
    public async Task Handle_delegates_to_the_sign_in_service()
    {
        var fixture = new IdentityAuthenticationTestFixture(CurrentActor.Anonymous);
        fixture.Services.SignInResult = Result<IdentityActorSession>.Success(IdentityTestData.CreateActorSession("user-1"));
        var handler = fixture.CreatePasswordSignInHandler();

        var result = await handler.Handle(new PasswordSignInCommand("alice", "P@ssword"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("alice", fixture.Services.LastSignInUserName);
        Assert.Equal("P@ssword", fixture.Services.LastSignInPassword);
    }
}

public sealed class StepUpCurrentActorCommandHandlerTests
{
    [Fact]
    public async Task Handle_requires_a_current_actor_before_attempting_step_up()
    {
        var fixture = new IdentityAuthenticationTestFixture(CurrentActor.Anonymous);
        var handler = fixture.CreateStepUpHandler();

        var result = await handler.Handle(new StepUpCurrentActorCommand("password"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IdentityAuthenticationErrors.CurrentActorNotFound().Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_delegates_to_the_step_up_service_for_the_current_actor()
    {
        var fixture = new IdentityAuthenticationTestFixture(new CurrentActor("user-1", isAuthenticated: true, roles: ["User"]));
        fixture.Services.StepUpResult = Result<IdentityActorSession>.Success(IdentityTestData.CreateActorSession("user-1"));
        var handler = fixture.CreateStepUpHandler();

        var result = await handler.Handle(new StepUpCurrentActorCommand("password"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("user-1", fixture.Services.LastStepUpActorId);
        Assert.Equal("password", fixture.Services.LastStepUpPassword);
    }
}

public sealed class GetCurrentIdentityActorQueryHandlerTests
{
    [Fact]
    public async Task Handle_requires_a_current_actor_before_querying_the_reader()
    {
        var fixture = new IdentityAuthenticationTestFixture(CurrentActor.Anonymous);
        var handler = fixture.CreateCurrentActorHandler();

        var result = await handler.Handle(new GetCurrentIdentityActorQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IdentityAuthenticationErrors.CurrentActorNotFound().Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_queries_the_reader_for_the_current_actor()
    {
        var fixture = new IdentityAuthenticationTestFixture(new CurrentActor("user-2", isAuthenticated: true, roles: ["User"]));
        fixture.Services.CurrentResult = Result<IdentityActorSession>.Success(IdentityTestData.CreateActorSession("user-2"));
        var handler = fixture.CreateCurrentActorHandler();

        var result = await handler.Handle(new GetCurrentIdentityActorQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("user-2", fixture.Services.LastCurrentActorId);
    }
}

public sealed class SetCurrentActorPreferredTimeZoneCommandHandlerTests
{
    [Fact]
    public async Task Handle_updates_the_preference_and_writes_audit()
    {
        var fixture = new IdentityAuthenticationTestFixture(new CurrentActor("user-3", isAuthenticated: true, roles: ["User"]), correlationId: "corr-tz");
        fixture.Services.SetPreferredTimeZoneResult = Result<IdentityActorSession>.Success(IdentityTestData.CreateActorSession("user-3", preferredTimeZoneId: "America/New_York"));
        var handler = fixture.CreateSetTimeZoneHandler();

        var result = await handler.Handle(new SetCurrentActorPreferredTimeZoneCommand("America/New_York"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("user-3", fixture.Services.LastSetPreferredTimeZoneActorId);
        Assert.Equal("America/New_York", fixture.Services.LastPreferredTimeZoneId);

        var audit = Assert.Single(fixture.AuditWriter.Events);
        Assert.Equal("identity.user.time_zone.update", audit.Action);
        Assert.Equal("user", audit.TargetType);
        Assert.Equal("user-3", audit.TargetId);
        Assert.Equal("America/New_York", audit.Outcome);
        Assert.Equal("corr-tz", audit.CorrelationId);
    }
}

public sealed class SignOutCommandHandlerTests
{
    [Fact]
    public async Task Handle_returns_success()
    {
        var handler = new SignOutCommandHandler();

        var result = await handler.Handle(new SignOutCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}

public sealed class ChangeCurrentActorPasswordCommandHandlerTests
{
    [Fact]
    public async Task Handle_updates_the_password_and_writes_audit()
    {
        var fixture = new IdentityAuthenticationTestFixture(new CurrentActor("user-4", isAuthenticated: true, roles: ["User"]), correlationId: "corr-password");
        fixture.Services.ChangePasswordResult = Result<IdentityActorSession>.Success(IdentityTestData.CreateActorSession("user-4"));
        var handler = fixture.CreateChangePasswordHandler();

        var result = await handler.Handle(new ChangeCurrentActorPasswordCommand("old-pass", "new-pass"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("user-4", fixture.Services.LastChangePasswordActorId);
        Assert.Equal("old-pass", fixture.Services.LastCurrentPassword);
        Assert.Equal("new-pass", fixture.Services.LastNewPassword);

        var audit = Assert.Single(fixture.AuditWriter.Events);
        Assert.Equal("identity.user.password.change", audit.Action);
        Assert.Equal("user-4", audit.TargetId);
        Assert.Equal("updated", audit.Outcome);
        Assert.Equal("corr-password", audit.CorrelationId);
    }
}

internal sealed class IdentityAuthenticationTestFixture
{
    public RecordingAuditEventWriter AuditWriter { get; } = new();
    public FakeClock Clock { get; } = new(IdentityTestData.FixedNow);
    public StubCurrentActorAccessor ActorAccessor { get; }
    public InMemoryRequestContextAccessor RequestContextAccessor { get; }
    public FakeIdentityAuthenticationServices Services { get; } = new();

    public IdentityAuthenticationTestFixture(CurrentActor actor, string? correlationId = null)
    {
        ActorAccessor = new StubCurrentActorAccessor(actor);
        RequestContextAccessor = new InMemoryRequestContextAccessor
        {
            Current = correlationId is null ? null : new BuildingBlocks.Application.Dispatching.RequestContext(correlationId, "req-" + (actor.ActorId ?? "anonymous"))
        };
    }

    public PasswordSignInCommandHandler CreatePasswordSignInHandler() => new(Services);

    public StepUpCurrentActorCommandHandler CreateStepUpHandler() => new(ActorAccessor, Services);

    public GetCurrentIdentityActorQueryHandler CreateCurrentActorHandler() => new(ActorAccessor, Services);

    public SetCurrentActorPreferredTimeZoneCommandHandler CreateSetTimeZoneHandler() =>
        new(AuditWriter, Clock, ActorAccessor, Services, RequestContextAccessor);

    public ChangeCurrentActorPasswordCommandHandler CreateChangePasswordHandler() =>
        new(AuditWriter, Clock, ActorAccessor, Services, RequestContextAccessor);

}

internal sealed class FakeIdentityAuthenticationServices :
    IIdentityPasswordSignInService,
    IIdentityCurrentActorReader,
    IIdentityCurrentActorPreferenceService,
    IIdentityCurrentActorStepUpService,
    IIdentityCurrentActorPasswordService
{
    public Result<IdentityActorSession>? SignInResult { get; set; }
    public Result<IdentityActorSession>? CurrentResult { get; set; }
    public Result<IdentityActorSession>? SetPreferredTimeZoneResult { get; set; }
    public Result<IdentityActorSession>? StepUpResult { get; set; }
    public Result<IdentityActorSession>? ChangePasswordResult { get; set; }

    public string? LastSignInUserName { get; private set; }
    public string? LastSignInPassword { get; private set; }
    public string? LastCurrentActorId { get; private set; }
    public string? LastSetPreferredTimeZoneActorId { get; private set; }
    public string? LastPreferredTimeZoneId { get; private set; }
    public string? LastStepUpActorId { get; private set; }
    public string? LastStepUpPassword { get; private set; }
    public string? LastChangePasswordActorId { get; private set; }
    public string? LastCurrentPassword { get; private set; }
    public string? LastNewPassword { get; private set; }

    public ValueTask<Result<IdentityActorSession>> SignInAsync(string userName, string password, CancellationToken cancellationToken)
    {
        LastSignInUserName = userName;
        LastSignInPassword = password;
        return ValueTask.FromResult(SignInResult ?? throw new InvalidOperationException("SignInResult not configured."));
    }

    public ValueTask<Result<IdentityActorSession>> GetCurrentAsync(string actorId, CancellationToken cancellationToken)
    {
        LastCurrentActorId = actorId;
        return ValueTask.FromResult(CurrentResult ?? throw new InvalidOperationException("CurrentResult not configured."));
    }

    public ValueTask<Result<IdentityActorSession>> SetPreferredTimeZoneAsync(string actorId, string preferredTimeZoneId, CancellationToken cancellationToken)
    {
        LastSetPreferredTimeZoneActorId = actorId;
        LastPreferredTimeZoneId = preferredTimeZoneId;
        return ValueTask.FromResult(SetPreferredTimeZoneResult ?? throw new InvalidOperationException("SetPreferredTimeZoneResult not configured."));
    }

    public ValueTask<Result<IdentityActorSession>> StepUpAsync(string actorId, string password, CancellationToken cancellationToken)
    {
        LastStepUpActorId = actorId;
        LastStepUpPassword = password;
        return ValueTask.FromResult(StepUpResult ?? throw new InvalidOperationException("StepUpResult not configured."));
    }

    public ValueTask<Result<IdentityActorSession>> ChangePasswordAsync(string actorId, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        LastChangePasswordActorId = actorId;
        LastCurrentPassword = currentPassword;
        LastNewPassword = newPassword;
        return ValueTask.FromResult(ChangePasswordResult ?? throw new InvalidOperationException("ChangePasswordResult not configured."));
    }
}
