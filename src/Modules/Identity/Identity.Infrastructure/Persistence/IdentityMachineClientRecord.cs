using NodaTime;

namespace Identity.Infrastructure.Persistence;

internal sealed class IdentityMachineClientRecord
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientName { get; set; } = string.Empty;

    public string NormalizedClientName { get; set; } = string.Empty;

    public string SecretHash { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public bool IsRevoked { get; set; }

    public string[] Roles { get; set; } = Array.Empty<string>();

    public Instant CreatedUtc { get; set; }

    public Instant UpdatedUtc { get; set; }

    public Instant? RevokedUtc { get; set; }
}
