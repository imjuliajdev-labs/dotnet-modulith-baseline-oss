using Identity.Application.Administration;
using Identity.Application.Authentication;
using NodaTime;

namespace Module.UnitTests.Identity;

internal static class IdentityTestData
{
    public static readonly Instant FixedNow = Instant.FromUtc(2026, 4, 14, 12, 0);

    public static IdentityActorSession CreateActorSession(
        string actorId,
        string preferredTimeZoneId = "Etc/UTC",
        IReadOnlyCollection<string>? roles = null)
    {
        return new IdentityActorSession(
            actorId,
            actorId,
            actorId,
            roles ?? ["User"],
            preferredTimeZoneId);
    }

    public static IdentityUserAccount CreateUserAccount(
        string actorId,
        bool enabled,
        IReadOnlyCollection<string>? roles = null,
        string preferredTimeZoneId = "Etc/UTC",
        Instant? createdUtc = null,
        Instant? updatedUtc = null)
    {
        return new IdentityUserAccount(
            ActorId: actorId,
            UserName: actorId,
            DisplayName: actorId,
            Enabled: enabled,
            Roles: roles ?? ["User"],
            PreferredTimeZoneId: preferredTimeZoneId,
            CreatedUtc: createdUtc ?? Instant.FromUtc(2026, 1, 1, 0, 0),
            UpdatedUtc: updatedUtc ?? FixedNow);
    }

    public static MachineClientSummary CreateMachineClientSummary(
        string clientId,
        bool isActive = true,
        bool isRevoked = false,
        IReadOnlyCollection<string>? roles = null,
        Instant? createdUtc = null,
        Instant? updatedUtc = null,
        Instant? revokedUtc = null)
    {
        return new MachineClientSummary(
            ClientId: clientId,
            ClientName: clientId,
            IsActive: isActive,
            IsRevoked: isRevoked,
            Roles: roles ?? ["Machine"],
            CreatedUtc: createdUtc ?? Instant.FromUtc(2026, 1, 1, 0, 0),
            UpdatedUtc: updatedUtc ?? FixedNow,
            RevokedUtc: isRevoked ? revokedUtc ?? FixedNow : null);
    }
}
