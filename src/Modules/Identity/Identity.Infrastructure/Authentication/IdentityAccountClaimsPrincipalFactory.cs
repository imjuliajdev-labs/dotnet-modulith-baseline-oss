using System.Security.Claims;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Authentication;

internal sealed class IdentityAccountClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<IdentityAccount, IdentityRole>
{
    public IdentityAccountClaimsPrincipalFactory(
        UserManager<IdentityAccount> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor)
        : base(userManager, roleManager, optionsAccessor)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(IdentityAccount user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var identity = await base.GenerateClaimsAsync(user);
        if (!string.IsNullOrEmpty(user.DisplayName))
        {
            identity.AddClaim(new Claim(IdentityAuthenticationClaimTypes.DisplayName, user.DisplayName));
        }

        return identity;
    }
}
