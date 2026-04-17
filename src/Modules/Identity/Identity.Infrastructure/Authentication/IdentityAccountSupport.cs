using BuildingBlocks.Application.Results;
using Identity.Application.Administration;
using NodaTime;

namespace Identity.Infrastructure.Authentication;

internal static class IdentityAccountSupport
{
    public const int MinimumPasswordLength = 12;
    public const int RequiredUniqueCharacterCount = 4;
    public const int MaxFailedAccessAttempts = 5;
    public static readonly TimeSpan DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

    public static string? NormalizeDisplayName(string? displayName)
    {
        return string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
    }

    public static string? GetPasswordOrNull(string? password)
    {
        return string.IsNullOrWhiteSpace(password) ? null : password;
    }

    public static Result<string> NormalizePreferredTimeZoneId(string? preferredTimeZoneId)
    {
        var candidate = string.IsNullOrWhiteSpace(preferredTimeZoneId)
            ? IdentityUserAdministrationDefaults.DefaultPreferredTimeZoneId
            : preferredTimeZoneId.Trim();

        return DateTimeZoneProviders.Tzdb.Ids.Contains(candidate)
            ? Result<string>.Success(candidate)
            : Result<string>.Failure(IdentityUserAdministrationErrors.InvalidPreferredTimeZone(candidate));
    }

    public static string? NormalizeUserName(string? userName)
    {
        return string.IsNullOrWhiteSpace(userName) ? null : userName.Trim();
    }

    public static string NormalizeMachineClientNameKey(string clientName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        return clientName.Trim().ToUpperInvariant();
    }

    public static Result<IReadOnlyCollection<string>> NormalizeRoles(
        IEnumerable<string>? roles,
        Func<string, Error> invalidRoleFactory)
    {
        ArgumentNullException.ThrowIfNull(invalidRoleFactory);

        if (roles is null)
        {
            return Result<IReadOnlyCollection<string>>.Success(Array.Empty<string>());
        }

        var normalized = new List<string>();
        foreach (var role in roles)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                continue;
            }

            var trimmed = role.Trim();
            if (!IsKnownRole(trimmed))
            {
                return Result<IReadOnlyCollection<string>>.Failure(invalidRoleFactory(trimmed));
            }

            if (!normalized.Any(existing => string.Equals(existing, trimmed, StringComparison.Ordinal)))
            {
                normalized.Add(trimmed);
            }
        }

        normalized.Sort(StringComparer.Ordinal);
        return Result<IReadOnlyCollection<string>>.Success(normalized);
    }

    public static bool IsKnownRole(string role)
    {
        return string.Equals(role, BuildingBlocks.Application.Authorization.IdentityRoles.Admin, StringComparison.Ordinal)
            || string.Equals(role, BuildingBlocks.Application.Authorization.IdentityRoles.User, StringComparison.Ordinal)
            || string.Equals(role, BuildingBlocks.Application.Authorization.IdentityRoles.Machine, StringComparison.Ordinal);
    }
}
