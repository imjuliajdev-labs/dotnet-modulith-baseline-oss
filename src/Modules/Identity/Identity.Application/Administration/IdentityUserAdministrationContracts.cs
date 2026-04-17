using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using Identity.Application.Authentication;
using Identity.Application.Authorization;
using NodaTime;

namespace Identity.Application.Administration;

public sealed record IdentityUserAccount(
    string ActorId,
    string UserName,
    string DisplayName,
    bool Enabled,
    IReadOnlyCollection<string> Roles,
    string PreferredTimeZoneId,
    Instant CreatedUtc,
    Instant UpdatedUtc);

public sealed record IdentityUserAccountList(IReadOnlyCollection<IdentityUserAccount> Users);

public interface IIdentityUserAdministrationService
{
    ValueTask<IReadOnlyCollection<IdentityUserAccount>> ListAsync(CancellationToken cancellationToken);

    ValueTask<Result<IdentityUserAccount>> CreateAsync(
        string userName,
        string displayName,
        string password,
        IReadOnlyCollection<string> roles,
        string preferredTimeZoneId,
        bool enabled,
        CancellationToken cancellationToken);

    ValueTask<Result<IdentityUserAccount>> SetRolesAsync(
        string actorId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);

    ValueTask<Result<IdentityUserAccount>> SetEnabledAsync(
        string actorId,
        bool enabled,
        CancellationToken cancellationToken);

    ValueTask<Result<IdentityUserAccount>> ResetPasswordAsync(
        string actorId,
        string password,
        CancellationToken cancellationToken);

    ValueTask<Result<IdentityUserAccount>> RevokeSessionsAsync(
        string actorId,
        CancellationToken cancellationToken);

    ValueTask<Result<IdentityUserAccount>> UnlockAsync(
        string actorId,
        CancellationToken cancellationToken);
}

public interface IIdentityAccountStore :
    IIdentityPasswordSignInService,
    IIdentityCurrentActorReader,
    IIdentityCurrentActorPreferenceService,
    IIdentityCurrentActorStepUpService,
    IIdentityCurrentActorPasswordService,
    Identity.PublicContracts.Queries.IIdentityTimeZonePreferenceQueryService,
    IIdentityUserAdministrationService
{
}

public static class IdentityUserAdministrationDefaults
{
    public const string DefaultPreferredTimeZoneId = "Etc/UTC";

    public static readonly Duration RecentAuthenticationWindow = Duration.FromMinutes(5);
}

public static class IdentityUserAdministrationErrors
{
    public static Error AccountNotFound(string actorId)
    {
        return new Error(
            "identity.user_not_found",
            $"Identity user '{actorId}' was not found.",
            ErrorKind.NotFound);
    }

    public static Error CannotDisableCurrentActor()
    {
        return new Error(
            "identity.cannot_disable_current_actor",
            "The current actor cannot disable their own account.",
            ErrorKind.Validation);
    }

    public static Error CannotRemoveOwnAdminRole()
    {
        return new Error(
            "identity.cannot_remove_own_admin_role",
            "The current actor cannot remove their own Admin role.",
            ErrorKind.Validation);
    }

    public static Error DisplayNameRequired()
    {
        return new Error(
            "identity.display_name_required",
            "Display name is required.",
            ErrorKind.Validation);
    }

    public static Error InvalidRole(string role)
    {
        return new Error(
            "identity.invalid_role",
            $"Role '{role}' is not a recognised identity role. Expected one of Admin, User, Machine.",
            ErrorKind.Validation);
    }

    public static Error InvalidPreferredTimeZone(string preferredTimeZoneId)
    {
        return new Error(
            "identity.invalid_preferred_time_zone",
            $"Preferred time zone '{preferredTimeZoneId}' is not a valid IANA time zone id.",
            ErrorKind.Validation);
    }

    public static Error PasswordRequired()
    {
        return new Error(
            "identity.password_required",
            "Password is required.",
            ErrorKind.Validation);
    }

    public static Error PasswordTooShort(int minimumLength)
    {
        return new Error(
            "identity.password_too_short",
            $"Password must be at least {minimumLength} characters long.",
            ErrorKind.Validation);
    }

    public static Error PasswordPolicyNotMet(string detail)
    {
        return new Error(
            "identity.password_policy_not_met",
            string.IsNullOrWhiteSpace(detail) ? "Password did not meet the configured password policy." : detail,
            ErrorKind.Validation);
    }

    public static Error UserNameAlreadyExists(string userName)
    {
        return new Error(
            "identity.user_name_already_exists",
            $"Identity user '{userName}' already exists.",
            ErrorKind.Conflict);
    }

    public static Error UserNameRequired()
    {
        return new Error(
            "identity.user_name_required",
            "User name is required.",
            ErrorKind.Validation);
    }
}

public sealed record ListIdentityUsersQuery : IQuery<IdentityUserAccountList>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => IdentityModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class ListIdentityUsersQueryHandler : IQueryHandler<ListIdentityUsersQuery, IdentityUserAccountList>
{
    private readonly IIdentityUserAdministrationService _service;

    public ListIdentityUsersQueryHandler(IIdentityUserAdministrationService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<Result<IdentityUserAccountList>> Handle(ListIdentityUsersQuery query, CancellationToken cancellationToken)
    {
        var users = await _service.ListAsync(cancellationToken);
        return Result<IdentityUserAccountList>.Success(new IdentityUserAccountList(users));
    }
}
