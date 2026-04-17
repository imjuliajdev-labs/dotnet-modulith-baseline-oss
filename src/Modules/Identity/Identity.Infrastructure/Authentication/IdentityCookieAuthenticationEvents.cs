using System.Security.Claims;
using BuildingBlocks.Infrastructure.Authorization;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Authentication;

public sealed class IdentityCookieAuthenticationEvents : CookieAuthenticationEvents
{
    private readonly SignInManager<IdentityAccount> _signInManager;
    private readonly UserManager<IdentityAccount> _userManager;
    private readonly string _securityStampClaimType;

    public IdentityCookieAuthenticationEvents(
        SignInManager<IdentityAccount> signInManager,
        UserManager<IdentityAccount> userManager,
        IOptions<IdentityOptions> identityOptions)
    {
        _signInManager = signInManager ?? throw new ArgumentNullException(nameof(signInManager));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        ArgumentNullException.ThrowIfNull(identityOptions);

        _securityStampClaimType = identityOptions.Value.ClaimsIdentity.SecurityStampClaimType;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var principal = context.Principal;
        if (principal is null)
        {
            await RejectAsync(context);
            return;
        }

        var actorId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(actorId))
        {
            await RejectAsync(context);
            return;
        }

        var user = await _userManager.FindByIdAsync(actorId);
        if (user is null || !user.Enabled)
        {
            await RejectAsync(context);
            return;
        }

        // Preserve the recent-authentication instant across any principal renewal so
        // IRequireRecentAuthentication checks continue to see the original sign-in time.
        var authenticationInstant = principal.FindFirst(HttpContextCurrentActorAccessor.AuthenticationInstantClaimType)?.Value;

        var cookieSecurityStamp = principal.FindFirst(_securityStampClaimType)?.Value;
        if (string.IsNullOrWhiteSpace(cookieSecurityStamp))
        {
            // Legacy cookie without a security stamp claim: upgrade the principal using
            // SignInManager so it carries current role + security stamp claims.
            var refreshedPrincipal = await _signInManager.CreateUserPrincipalAsync(user);
            if (!string.IsNullOrWhiteSpace(authenticationInstant)
                && refreshedPrincipal.Identity is ClaimsIdentity identity)
            {
                identity.AddClaim(new Claim(HttpContextCurrentActorAccessor.AuthenticationInstantClaimType, authenticationInstant));
            }

            context.ReplacePrincipal(refreshedPrincipal);
            context.ShouldRenew = true;
            return;
        }

        if (!string.Equals(user.SecurityStamp, cookieSecurityStamp, StringComparison.Ordinal))
        {
            await RejectAsync(context);
            return;
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    }
}
