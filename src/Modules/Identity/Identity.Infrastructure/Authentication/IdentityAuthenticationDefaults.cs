namespace Identity.Infrastructure.Authentication;

public static class IdentityAuthenticationClaimTypes
{
    public const string DisplayName = "baseline:display_name";
}

public static class IdentityAntiforgeryDefaults
{
    public const string HeaderName = "X-CSRF-TOKEN";
}
