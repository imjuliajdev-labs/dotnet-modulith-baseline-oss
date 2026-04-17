using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using Identity.Application.Authentication;
using Identity.Application.Authorization;
using NodaTime;

namespace Identity.Application.Administration;

public sealed record CreateIdentityUserCommand(
    string UserName,
    string DisplayName,
    string Password,
    IReadOnlyCollection<string> Roles,
    string PreferredTimeZoneId,
    bool Enabled)
    : ICommand<IdentityUserAccount>, IModuleScoped, IAuthorizeRequest, IRequireRecentAuthentication
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;

    public Duration RecentAuthenticationWindow { get; } = IdentityUserAdministrationDefaults.RecentAuthenticationWindow;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class CreateIdentityUserCommandHandler : ICommandHandler<CreateIdentityUserCommand, IdentityUserAccount>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IIdentityUserAdministrationService _service;

    public CreateIdentityUserCommandHandler(
        IAuditEventWriter auditEventWriter,
        BuildingBlocks.Domain.Time.IClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IIdentityUserAdministrationService service)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<Result<IdentityUserAccount>> Handle(CreateIdentityUserCommand command, CancellationToken cancellationToken)
    {
        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var occurredUtc = _clock.GetCurrentInstant();
        var result = await _service.CreateAsync(
            command.UserName,
            command.DisplayName,
            command.Password,
            command.Roles,
            command.PreferredTimeZoneId,
            command.Enabled,
            cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: IdentityModuleInfo.ModuleKey,
                Action: "identity.user.create",
                TargetType: "user",
                TargetId: result.Value!.ActorId,
                Outcome: result.Value.Enabled ? "enabled" : "disabled",
                OccurredUtc: occurredUtc,
                ActorId: actor.ActorId,
                CorrelationId: _requestContextAccessor.Current?.CorrelationId),
            cancellationToken);

        return result;
    }
}

public sealed record SetIdentityUserRolesCommand(string TargetActorId, IReadOnlyCollection<string> Roles)
    : ICommand<IdentityUserAccount>, IModuleScoped, IAuthorizeRequest, IRequireRecentAuthentication
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;

    public Duration RecentAuthenticationWindow { get; } = IdentityUserAdministrationDefaults.RecentAuthenticationWindow;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class SetIdentityUserRolesCommandHandler : ICommandHandler<SetIdentityUserRolesCommand, IdentityUserAccount>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IIdentityUserAdministrationService _service;

    public SetIdentityUserRolesCommandHandler(
        IAuditEventWriter auditEventWriter,
        BuildingBlocks.Domain.Time.IClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IIdentityUserAdministrationService service)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<Result<IdentityUserAccount>> Handle(SetIdentityUserRolesCommand command, CancellationToken cancellationToken)
    {
        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);

        if (string.Equals(actor.ActorId, command.TargetActorId, StringComparison.Ordinal)
            && !command.Roles.Contains(BuildingBlocks.Application.Authorization.IdentityRoles.Admin, StringComparer.Ordinal))
        {
            return Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.CannotRemoveOwnAdminRole());
        }

        var occurredUtc = _clock.GetCurrentInstant();
        var result = await _service.SetRolesAsync(command.TargetActorId, command.Roles, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: IdentityModuleInfo.ModuleKey,
                Action: "identity.user.roles.update",
                TargetType: "user",
                TargetId: command.TargetActorId,
                Outcome: "updated",
                OccurredUtc: occurredUtc,
                ActorId: actor.ActorId,
                CorrelationId: _requestContextAccessor.Current?.CorrelationId),
            cancellationToken);

        return result;
    }
}

public sealed record SetIdentityUserStatusCommand(string TargetActorId, bool Enabled)
    : ICommand<IdentityUserAccount>, IModuleScoped, IAuthorizeRequest, IRequireRecentAuthentication
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;

    public Duration RecentAuthenticationWindow { get; } = IdentityUserAdministrationDefaults.RecentAuthenticationWindow;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class SetIdentityUserStatusCommandHandler : ICommandHandler<SetIdentityUserStatusCommand, IdentityUserAccount>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IIdentityUserAdministrationService _service;

    public SetIdentityUserStatusCommandHandler(
        IAuditEventWriter auditEventWriter,
        BuildingBlocks.Domain.Time.IClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IIdentityUserAdministrationService service)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<Result<IdentityUserAccount>> Handle(SetIdentityUserStatusCommand command, CancellationToken cancellationToken)
    {
        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        if (!command.Enabled && string.Equals(actor.ActorId, command.TargetActorId, StringComparison.Ordinal))
        {
            return Result<IdentityUserAccount>.Failure(IdentityUserAdministrationErrors.CannotDisableCurrentActor());
        }

        var occurredUtc = _clock.GetCurrentInstant();
        var result = await _service.SetEnabledAsync(command.TargetActorId, command.Enabled, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: IdentityModuleInfo.ModuleKey,
                Action: "identity.user.status.update",
                TargetType: "user",
                TargetId: command.TargetActorId,
                Outcome: command.Enabled ? "enabled" : "disabled",
                OccurredUtc: occurredUtc,
                ActorId: actor.ActorId,
                CorrelationId: _requestContextAccessor.Current?.CorrelationId),
            cancellationToken);

        return result;
    }
}

public sealed record ResetIdentityUserPasswordCommand(string TargetActorId, string Password)
    : ICommand<IdentityUserAccount>, IModuleScoped, IAuthorizeRequest, IRequireRecentAuthentication
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;

    public Duration RecentAuthenticationWindow { get; } = IdentityUserAdministrationDefaults.RecentAuthenticationWindow;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class ResetIdentityUserPasswordCommandHandler : ICommandHandler<ResetIdentityUserPasswordCommand, IdentityUserAccount>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IIdentityUserAdministrationService _service;

    public ResetIdentityUserPasswordCommandHandler(
        IAuditEventWriter auditEventWriter,
        BuildingBlocks.Domain.Time.IClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IIdentityUserAdministrationService service)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<Result<IdentityUserAccount>> Handle(ResetIdentityUserPasswordCommand command, CancellationToken cancellationToken)
    {
        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var occurredUtc = _clock.GetCurrentInstant();
        var result = await _service.ResetPasswordAsync(command.TargetActorId, command.Password, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: IdentityModuleInfo.ModuleKey,
                Action: "identity.user.password.reset",
                TargetType: "user",
                TargetId: command.TargetActorId,
                Outcome: "updated",
                OccurredUtc: occurredUtc,
                ActorId: actor.ActorId,
                CorrelationId: _requestContextAccessor.Current?.CorrelationId),
            cancellationToken);

        return result;
    }
}

public sealed record RevokeIdentityUserSessionsCommand(string TargetActorId)
    : ICommand<IdentityUserAccount>, IModuleScoped, IAuthorizeRequest, IRequireRecentAuthentication
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;

    public Duration RecentAuthenticationWindow { get; } = IdentityUserAdministrationDefaults.RecentAuthenticationWindow;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class RevokeIdentityUserSessionsCommandHandler : ICommandHandler<RevokeIdentityUserSessionsCommand, IdentityUserAccount>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IIdentityUserAdministrationService _service;

    public RevokeIdentityUserSessionsCommandHandler(
        IAuditEventWriter auditEventWriter,
        BuildingBlocks.Domain.Time.IClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IIdentityUserAdministrationService service)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<Result<IdentityUserAccount>> Handle(RevokeIdentityUserSessionsCommand command, CancellationToken cancellationToken)
    {
        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var occurredUtc = _clock.GetCurrentInstant();
        var result = await _service.RevokeSessionsAsync(command.TargetActorId, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: IdentityModuleInfo.ModuleKey,
                Action: "identity.user.sessions.revoke",
                TargetType: "user",
                TargetId: command.TargetActorId,
                Outcome: "revoked",
                OccurredUtc: occurredUtc,
                ActorId: actor.ActorId,
                CorrelationId: _requestContextAccessor.Current?.CorrelationId),
            cancellationToken);

        return result;
    }
}

public sealed record UnlockIdentityUserCommand(string TargetActorId)
    : ICommand<IdentityUserAccount>, IModuleScoped, IAuthorizeRequest, IRequireRecentAuthentication
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;

    public Duration RecentAuthenticationWindow { get; } = IdentityUserAdministrationDefaults.RecentAuthenticationWindow;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class UnlockIdentityUserCommandHandler : ICommandHandler<UnlockIdentityUserCommand, IdentityUserAccount>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IIdentityUserAdministrationService _service;

    public UnlockIdentityUserCommandHandler(
        IAuditEventWriter auditEventWriter,
        BuildingBlocks.Domain.Time.IClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IRequestContextAccessor requestContextAccessor,
        IIdentityUserAdministrationService service)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<Result<IdentityUserAccount>> Handle(UnlockIdentityUserCommand command, CancellationToken cancellationToken)
    {
        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        var occurredUtc = _clock.GetCurrentInstant();
        var result = await _service.UnlockAsync(command.TargetActorId, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: IdentityModuleInfo.ModuleKey,
                Action: "identity.user.unlock",
                TargetType: "user",
                TargetId: command.TargetActorId,
                Outcome: "unlocked",
                OccurredUtc: occurredUtc,
                ActorId: actor.ActorId,
                CorrelationId: _requestContextAccessor.Current?.CorrelationId),
            cancellationToken);

        return result;
    }
}
