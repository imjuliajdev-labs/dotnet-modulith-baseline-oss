using BuildingBlocks.Domain.Time;
using Identity.Application.Authentication;
using Identity.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

namespace Identity.Api.Authentication;

public sealed record PasswordSignInRequest(string UserName, string Password);

public sealed record StepUpCurrentActorRequest(string Password);

public sealed record ChangeCurrentActorPasswordRequest(string CurrentPassword, string NewPassword);

public sealed record IdentityActorSessionResponse(
    string ActorId,
    string UserName,
    string DisplayName,
    IReadOnlyCollection<string> Roles)
{
    public static IdentityActorSessionResponse From(IdentityActorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new IdentityActorSessionResponse(
            session.ActorId,
            session.UserName,
            session.DisplayName,
            session.Roles);
    }
}

public sealed record UpdatePreferredTimeZoneRequest(string PreferredTimeZoneId);

public sealed record PreferredTimeZoneResponse(string PreferredTimeZoneId)
{
    public static PreferredTimeZoneResponse From(IdentityActorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new PreferredTimeZoneResponse(session.PreferredTimeZoneId);
    }
}

public sealed record AntiforgeryTokenResponse(string HeaderName, string RequestToken);

internal sealed class IdentityCookieSignInHttpResult : IResult
{
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly IdentityActorSession _session;

    public IdentityCookieSignInHttpResult(IdentityActorSession session, BuildingBlocks.Domain.Time.IClock clock)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var authenticatedAt = _clock.GetCurrentInstant();
        var authenticationService = httpContext.RequestServices.GetRequiredService<IIdentityCookieAuthenticationService>();

        await authenticationService.SignInAsync(_session.ActorId, authenticatedAt, httpContext.RequestAborted);

        await Results.Ok(IdentityActorSessionResponse.From(_session)).ExecuteAsync(httpContext);
    }
}

internal sealed class IdentityCookieSignOutHttpResult : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var authenticationService = httpContext.RequestServices.GetRequiredService<IIdentityCookieAuthenticationService>();
        await authenticationService.SignOutAsync();
        httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}
