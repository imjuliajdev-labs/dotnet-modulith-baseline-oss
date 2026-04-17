using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using Identity.Application.Authorization;
using Identity.Application.Administration;
using NodaTime;

namespace Identity.Application.Authentication;

public sealed record IdentityActorSession(
    string ActorId,
    string UserName,
    string DisplayName,
    IReadOnlyCollection<string> Roles,
    string PreferredTimeZoneId);

public interface IIdentityPasswordSignInService
{
    ValueTask<Result<IdentityActorSession>> SignInAsync(string userName, string password, CancellationToken cancellationToken);
}

public interface IIdentityCurrentActorReader
{
    ValueTask<Result<IdentityActorSession>> GetCurrentAsync(string actorId, CancellationToken cancellationToken);
}

public interface IIdentityCurrentActorPreferenceService
{
    ValueTask<Result<IdentityActorSession>> SetPreferredTimeZoneAsync(string actorId, string preferredTimeZoneId, CancellationToken cancellationToken);
}

public interface IIdentityCurrentActorStepUpService
{
    ValueTask<Result<IdentityActorSession>> StepUpAsync(string actorId, string password, CancellationToken cancellationToken);
}

public interface IIdentityCurrentActorPasswordService
{
    ValueTask<Result<IdentityActorSession>> ChangePasswordAsync(
        string actorId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken);
}

public static class IdentityAuthenticationErrors
{
    public static Error InvalidCredentials()
    {
        return new Error(
            "identity.invalid_credentials",
            "The supplied credentials were invalid.",
            ErrorKind.Unauthorized);
    }

    public static Error CurrentPasswordInvalid()
    {
        return new Error(
            "identity.current_password_invalid",
            "The supplied current password is incorrect.",
            ErrorKind.Unauthorized);
    }

    public static Error AccountLocked()
    {
        return new Error(
            "identity.account_locked",
            "The account is temporarily locked due to repeated failed sign-in attempts.",
            ErrorKind.Unauthorized);
    }

    public static Error SeededAdminNotConfigured()
    {
        return new Error(
            "identity.seeded_admin_not_configured",
            "Seeded admin credentials are not configured for the baseline.",
            ErrorKind.ServiceUnavailable);
    }

    public static Error CurrentActorNotFound()
    {
        return new Error(
            "identity.current_actor_not_found",
            "The current actor session is no longer valid.",
            ErrorKind.Unauthorized);
    }
}

public sealed record PasswordSignInCommand(string UserName, string Password) : ICommand<IdentityActorSession>, IModuleScoped
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;
}

internal sealed class PasswordSignInCommandHandler : ICommandHandler<PasswordSignInCommand, IdentityActorSession>
{
    private readonly IIdentityPasswordSignInService _signInService;

    public PasswordSignInCommandHandler(IIdentityPasswordSignInService signInService)
    {
        _signInService = signInService ?? throw new ArgumentNullException(nameof(signInService));
    }

    public async Task<Result<IdentityActorSession>> Handle(PasswordSignInCommand command, CancellationToken cancellationToken)
    {
        return await _signInService.SignInAsync(command.UserName, command.Password, cancellationToken);
    }
}

public sealed record StepUpCurrentActorCommand(string Password) : ICommand<IdentityActorSession>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } = Array.Empty<RoleRequirement>();
}

internal sealed class StepUpCurrentActorCommandHandler : ICommandHandler<StepUpCurrentActorCommand, IdentityActorSession>
{
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IIdentityCurrentActorStepUpService _stepUpService;

    public StepUpCurrentActorCommandHandler(
        ICurrentActorAccessor currentActorAccessor,
        IIdentityCurrentActorStepUpService stepUpService)
    {
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _stepUpService = stepUpService ?? throw new ArgumentNullException(nameof(stepUpService));
    }

    public async Task<Result<IdentityActorSession>> Handle(StepUpCurrentActorCommand command, CancellationToken cancellationToken)
    {
        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(actor.ActorId))
        {
            return Result<IdentityActorSession>.Failure(IdentityAuthenticationErrors.CurrentActorNotFound());
        }

        return await _stepUpService.StepUpAsync(actor.ActorId, command.Password, cancellationToken);
    }
}

public sealed record GetCurrentIdentityActorQuery : IQuery<IdentityActorSession>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } = Array.Empty<RoleRequirement>();
}

internal sealed class GetCurrentIdentityActorQueryHandler : IQueryHandler<GetCurrentIdentityActorQuery, IdentityActorSession>
{
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IIdentityCurrentActorReader _currentActorReader;

    public GetCurrentIdentityActorQueryHandler(
        ICurrentActorAccessor currentActorAccessor,
        IIdentityCurrentActorReader currentActorReader)
    {
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _currentActorReader = currentActorReader ?? throw new ArgumentNullException(nameof(currentActorReader));
    }

    public async Task<Result<IdentityActorSession>> Handle(GetCurrentIdentityActorQuery query, CancellationToken cancellationToken)
    {
        var currentActor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(currentActor.ActorId))
        {
            return Result<IdentityActorSession>.Failure(IdentityAuthenticationErrors.CurrentActorNotFound());
        }

        return await _currentActorReader.GetCurrentAsync(currentActor.ActorId, cancellationToken);
    }
}

public sealed record SignOutCommand : ICommand, IModuleScoped
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;
}

public sealed record SetCurrentActorPreferredTimeZoneCommand(string PreferredTimeZoneId)
    : ICommand<IdentityActorSession>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } = Array.Empty<RoleRequirement>();
}

internal sealed class SetCurrentActorPreferredTimeZoneCommandHandler : ICommandHandler<SetCurrentActorPreferredTimeZoneCommand, IdentityActorSession>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IIdentityCurrentActorPreferenceService _preferenceService;
    private readonly IRequestContextAccessor _requestContextAccessor;

    public SetCurrentActorPreferredTimeZoneCommandHandler(
        IAuditEventWriter auditEventWriter,
        BuildingBlocks.Domain.Time.IClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IIdentityCurrentActorPreferenceService preferenceService,
        IRequestContextAccessor requestContextAccessor)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _preferenceService = preferenceService ?? throw new ArgumentNullException(nameof(preferenceService));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
    }

    public async Task<Result<IdentityActorSession>> Handle(SetCurrentActorPreferredTimeZoneCommand command, CancellationToken cancellationToken)
    {
        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(actor.ActorId))
        {
            return Result<IdentityActorSession>.Failure(IdentityAuthenticationErrors.CurrentActorNotFound());
        }

        var occurredUtc = _clock.GetCurrentInstant();
        var result = await _preferenceService.SetPreferredTimeZoneAsync(actor.ActorId, command.PreferredTimeZoneId, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: IdentityModuleInfo.ModuleKey,
                Action: "identity.user.time_zone.update",
                TargetType: "user",
                TargetId: actor.ActorId,
                Outcome: result.Value!.PreferredTimeZoneId,
                OccurredUtc: occurredUtc,
                ActorId: actor.ActorId,
                CorrelationId: _requestContextAccessor.Current?.CorrelationId),
            cancellationToken);

        return result;
    }
}

internal sealed class SignOutCommandHandler : ICommandHandler<SignOutCommand>
{
    public Task<Result> Handle(SignOutCommand command, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success());
    }
}

public sealed record ChangeCurrentActorPasswordCommand(string CurrentPassword, string NewPassword)
    : ICommand<IdentityActorSession>, IModuleScoped, IAuthorizeRequest, IRequireRecentAuthentication
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;

    public Duration RecentAuthenticationWindow { get; } =
        IdentityUserAdministrationDefaults.RecentAuthenticationWindow;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } = Array.Empty<RoleRequirement>();
}

internal sealed class ChangeCurrentActorPasswordCommandHandler
    : ICommandHandler<ChangeCurrentActorPasswordCommand, IdentityActorSession>
{
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private readonly IIdentityCurrentActorPasswordService _passwordService;
    private readonly IRequestContextAccessor _requestContextAccessor;

    public ChangeCurrentActorPasswordCommandHandler(
        IAuditEventWriter auditEventWriter,
        BuildingBlocks.Domain.Time.IClock clock,
        ICurrentActorAccessor currentActorAccessor,
        IIdentityCurrentActorPasswordService passwordService,
        IRequestContextAccessor requestContextAccessor)
    {
        _auditEventWriter = auditEventWriter ?? throw new ArgumentNullException(nameof(auditEventWriter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _currentActorAccessor = currentActorAccessor ?? throw new ArgumentNullException(nameof(currentActorAccessor));
        _passwordService = passwordService ?? throw new ArgumentNullException(nameof(passwordService));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
    }

    public async Task<Result<IdentityActorSession>> Handle(
        ChangeCurrentActorPasswordCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await _currentActorAccessor.GetCurrentAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(actor.ActorId))
        {
            return Result<IdentityActorSession>.Failure(IdentityAuthenticationErrors.CurrentActorNotFound());
        }

        var occurredUtc = _clock.GetCurrentInstant();
        var result = await _passwordService.ChangePasswordAsync(
            actor.ActorId,
            command.CurrentPassword,
            command.NewPassword,
            cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: IdentityModuleInfo.ModuleKey,
                Action: "identity.user.password.change",
                TargetType: "user",
                TargetId: actor.ActorId,
                Outcome: "updated",
                OccurredUtc: occurredUtc,
                ActorId: actor.ActorId,
                CorrelationId: _requestContextAccessor.Current?.CorrelationId),
            cancellationToken);

        return result;
    }
}
