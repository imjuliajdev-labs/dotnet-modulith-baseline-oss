using Identity.Api.Authentication;
using Identity.Infrastructure.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Api.Endpoints;

internal static class AntiforgeryEndpoints
{
    public static IEndpointRouteBuilder MapAntiforgeryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/antiforgery",
                static (IAntiforgery antiforgery, HttpContext httpContext) =>
                {
                    var tokens = antiforgery.GetAndStoreTokens(httpContext);
                    return Results.Ok(new AntiforgeryTokenResponse(
                        IdentityAntiforgeryDefaults.HeaderName,
                        tokens.RequestToken ?? string.Empty));
                })
            .WithName("Identity_GetAntiforgeryToken")
            .WithSummary("Get the antiforgery header name and request token for browser mutations.")
            .Produces<AntiforgeryTokenResponse>(StatusCodes.Status200OK);

        return endpoints;
    }
}
