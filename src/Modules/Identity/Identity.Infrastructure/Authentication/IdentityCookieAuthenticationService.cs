using System.Globalization;
using System.Security.Claims;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using NodaTime;

namespace Identity.Infrastructure.Authentication;

public interface IIdentityCookieAuthenticationService
{
    Task SignInAsync(string actorId, Instant authenticatedAt, CancellationToken cancellationToken);

    Task SignOutAsync();
}

internal sealed class IdentityCookieAuthenticationService : IIdentityCookieAuthenticationService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly SignInManager<IdentityAccount> _signInManager;
    private readonly UserManager<IdentityAccount> _userManager;

    public IdentityCookieAuthenticationService(
        IHttpContextAccessor httpContextAccessor,
        SignInManager<IdentityAccount> signInManager,
        UserManager<IdentityAccount> userManager)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _signInManager = signInManager ?? throw new ArgumentNullException(nameof(signInManager));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    }

    public async Task SignInAsync(string actorId, Instant authenticatedAt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        _ = cancellationToken;

        var user = await _userManager.FindByIdAsync(actorId);
        if (user is null)
        {
            throw new InvalidOperationException($"Identity actor '{actorId}' was not found for cookie sign-in.");
        }

        var principal = await _signInManager.CreateUserPrincipalAsync(user);
        if (principal.Identity is ClaimsIdentity identity)
        {
            identity.AddClaim(new Claim(
                BuildingBlocks.Infrastructure.Authorization.HttpContextCurrentActorAccessor.AuthenticationInstantClaimType,
                authenticatedAt.ToUnixTimeTicks().ToString(CultureInfo.InvariantCulture)));
        }

        await GetHttpContext().SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                AllowRefresh = true,
                IssuedUtc = authenticatedAt.ToDateTimeOffset(),
                ExpiresUtc = authenticatedAt.Plus(Duration.FromHours(8)).ToDateTimeOffset(),
                IsPersistent = true
            });
    }

    public Task SignOutAsync()
    {
        return GetHttpContext().SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private HttpContext GetHttpContext()
    {
        return _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("Identity cookie authentication requires an active HttpContext.");
    }
}
