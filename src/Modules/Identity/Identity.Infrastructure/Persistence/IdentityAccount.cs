using Microsoft.AspNetCore.Identity;
using NodaTime;

namespace Identity.Infrastructure.Persistence;

public sealed class IdentityAccount : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public string PreferredTimeZoneId { get; set; } = string.Empty;

    public Instant CreatedUtc { get; set; }

    public Instant UpdatedUtc { get; set; }
}
