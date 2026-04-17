using System.Security.Claims;
using BuildingBlocks.Application.Actors;
using Microsoft.AspNetCore.Http;
using NodaTime;
using System.Globalization;

namespace BuildingBlocks.Infrastructure.Authorization;

public sealed class HttpContextCurrentActorAccessor : ICurrentActorAccessor
{
    public const string AuthenticationInstantClaimType = "auth_time";
    public const string SubjectClaimType = "sub";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentActorAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public ValueTask<CurrentActor> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return ValueTask.FromResult(CurrentActor.Anonymous);
        }

        var actorId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst(SubjectClaimType)?.Value
            ?? principal.Identity?.Name;

        var roles = principal.Claims
            .Where(static claim => string.Equals(claim.Type, ClaimTypes.Role, StringComparison.OrdinalIgnoreCase))
            .Select(static claim => claim.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var authenticatedAt = TryGetAuthenticatedAt(principal);

        return ValueTask.FromResult(new CurrentActor(actorId, isAuthenticated: true, roles, authenticatedAt));
    }

    private static Instant? TryGetAuthenticatedAt(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var claimValue = principal.FindFirst(AuthenticationInstantClaimType)?.Value;
        if (!long.TryParse(claimValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixTimeTicks))
        {
            return null;
        }

        return Instant.FromUnixTimeTicks(unixTimeTicks);
    }
}
