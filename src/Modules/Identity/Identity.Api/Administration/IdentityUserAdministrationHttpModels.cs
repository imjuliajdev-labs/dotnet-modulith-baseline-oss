using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Identity.Application.Administration;

namespace Identity.Api.Administration;

public sealed class CreateIdentityUserRequest
{
    [SetsRequiredMembers]
    public CreateIdentityUserRequest()
    {
        UserName = string.Empty;
        DisplayName = string.Empty;
        Password = string.Empty;
        Roles = [];
    }

    [Required]
    public required string UserName { get; init; }

    [Required]
    public required string DisplayName { get; init; }

    [Required]
    public required string Password { get; init; }

    [Required]
    public required string[] Roles { get; init; }

    public string? PreferredTimeZoneId { get; init; }

    public bool Enabled { get; init; } = true;
}

public sealed record UpdateIdentityUserRolesRequest(string[] Roles);

public sealed record UpdateIdentityUserStatusRequest(bool Enabled);

public sealed record ResetIdentityUserPasswordRequest(string Password);

public sealed record IdentityUserAccountListResponse(IReadOnlyCollection<IdentityUserAccountResponse> Users)
{
    public static IdentityUserAccountListResponse From(IdentityUserAccountList users)
    {
        ArgumentNullException.ThrowIfNull(users);

        return new IdentityUserAccountListResponse(users.Users.Select(IdentityUserAccountResponse.From).ToArray());
    }
}

public sealed record IdentityUserAccountResponse(
    string ActorId,
    string UserName,
    string DisplayName,
    bool Enabled,
    IReadOnlyCollection<string> Roles,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc)
{
    public static IdentityUserAccountResponse From(IdentityUserAccount user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new IdentityUserAccountResponse(
            user.ActorId,
            user.UserName,
            user.DisplayName,
            user.Enabled,
            user.Roles,
            user.CreatedUtc.ToDateTimeOffset(),
            user.UpdatedUtc.ToDateTimeOffset());
    }
}
