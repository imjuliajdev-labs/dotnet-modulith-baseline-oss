using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Results;
using NodaTime;
using IClock = BuildingBlocks.Domain.Time.IClock;

namespace BuildingBlocks.Application.Authorization;

public readonly record struct RoleRequirement
{
    private readonly IReadOnlyCollection<string> _allowedRoles;

    public RoleRequirement(params string[] allowedRoles)
    {
        ArgumentNullException.ThrowIfNull(allowedRoles);
        if (allowedRoles.Length == 0)
        {
            throw new ArgumentException("At least one role must be specified.", nameof(allowedRoles));
        }

        var normalized = allowedRoles
            .Where(static role => !string.IsNullOrWhiteSpace(role))
            .Select(static role => role.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("At least one non-empty role must be specified.", nameof(allowedRoles));
        }

        _allowedRoles = normalized;
    }

    public IReadOnlyCollection<string> AllowedRoles => _allowedRoles ?? Array.Empty<string>();

    public static RoleRequirement Admin { get; } = new("Admin");

    public static RoleRequirement User { get; } = new("User");

    public static RoleRequirement Machine { get; } = new("Machine");

    public static RoleRequirement AdminOrMachine { get; } = new("Admin", "Machine");
}

public interface IAuthorizeRequest
{
    IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; }
}

public interface IRequireRecentAuthentication
{
    Duration RecentAuthenticationWindow { get; }
}

public interface IRoleAuthorizer
{
    ValueTask<Error> GetFailureOrNoneAsync(
        Type requestType,
        CurrentActor actor,
        IReadOnlyCollection<RoleRequirement> requirements,
        CancellationToken cancellationToken);
}

public interface IRecentAuthenticationAuthorizer
{
    ValueTask<Error> GetFailureOrNoneAsync(
    Type requestType,
    CurrentActor actor,
    Duration recentAuthenticationWindow,
    CancellationToken cancellationToken);
}

public static class AuthorizationErrors
{
    public static Error Unauthorized(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);

        return new Error(
            "auth.unauthorized",
            $"Request '{requestType.Name}' requires an authenticated actor.",
            ErrorKind.Unauthorized);
    }

    public static Error Forbidden(Type requestType, IEnumerable<RoleRequirement> unmetRequirements)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(unmetRequirements);

        var descriptions = unmetRequirements
            .Select(static requirement => string.Join(" or ", requirement.AllowedRoles.OrderBy(static role => role, StringComparer.OrdinalIgnoreCase)))
            .Where(static description => !string.IsNullOrWhiteSpace(description))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static description => description, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var message = descriptions.Length == 0
            ? $"Actor is not authorized to execute '{requestType.Name}'."
            : $"Actor is missing required role(s) for '{requestType.Name}': {string.Join("; ", descriptions)}.";

        return new Error("auth.forbidden", message, ErrorKind.Forbidden);
    }

    public static Error RecentAuthenticationRequired(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);

        return new Error(
            "auth.recent_auth_required",
            $"Request '{requestType.Name}' requires recent authentication or step-up verification.",
            ErrorKind.Unauthorized);
    }
}

internal sealed class CurrentActorRoleAuthorizer : IRoleAuthorizer
{
    public ValueTask<Error> GetFailureOrNoneAsync(
        Type requestType,
        CurrentActor actor,
        IReadOnlyCollection<RoleRequirement> requirements,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(requirements);

        if (requirements.Count == 0)
        {
            return ValueTask.FromResult(Error.None);
        }

        var unmet = requirements
            .Where(requirement => !requirement.AllowedRoles.Any(actor.IsInRole))
            .ToArray();

        return unmet.Length == 0
            ? ValueTask.FromResult(Error.None)
            : ValueTask.FromResult(AuthorizationErrors.Forbidden(requestType, unmet));
    }
}

internal sealed class CurrentActorRecentAuthenticationAuthorizer : IRecentAuthenticationAuthorizer
{
    private readonly IClock? _clock;

    public CurrentActorRecentAuthenticationAuthorizer(IClock? clock)
    {
        _clock = clock;
    }

    public ValueTask<Error> GetFailureOrNoneAsync(
        Type requestType,
        CurrentActor actor,
        Duration recentAuthenticationWindow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(actor);

        if (recentAuthenticationWindow <= Duration.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recentAuthenticationWindow),
                recentAuthenticationWindow,
                "Recent authentication windows must be greater than zero.");
        }

        if (_clock is null)
        {
            throw new InvalidOperationException(
                $"Recent authentication enforcement for '{requestType.Name}' requires an {nameof(IClock)} registration.");
        }

        if (actor.AuthenticatedAt is null)
        {
            return ValueTask.FromResult(AuthorizationErrors.RecentAuthenticationRequired(requestType));
        }

        var elapsed = _clock.GetCurrentInstant() - actor.AuthenticatedAt.Value;
        return elapsed <= recentAuthenticationWindow
            ? ValueTask.FromResult(Error.None)
            : ValueTask.FromResult(AuthorizationErrors.RecentAuthenticationRequired(requestType));
    }
}

public static class IdentityRoles
{
    public const string Admin = "Admin";
    public const string User = "User";
    public const string Machine = "Machine";

    public static IReadOnlyCollection<string> All { get; } = new[] { Admin, User, Machine };
}
