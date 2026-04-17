using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Infrastructure.Http;

public sealed class BrowserMutationAntiforgeryMiddleware
{
    private const string MachineAuthenticationType = "Machine";

    private static readonly PathString ApiBasePath = new("/api/v1");

    private readonly RequestDelegate _next;

    public BrowserMutationAntiforgeryMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext httpContext, IAntiforgery antiforgery)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(antiforgery);

        if (RequiresValidation(httpContext))
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }

        await _next(httpContext);
    }

    internal static bool RequiresValidation(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return httpContext.Request.Path.StartsWithSegments(ApiBasePath, StringComparison.OrdinalIgnoreCase)
            && IsMutation(httpContext.Request.Method)
            && !IsMachineAuthenticated(httpContext.User);
    }

    private static bool IsMachineAuthenticated(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.Identities.Any(identity =>
            identity is { IsAuthenticated: true }
            && string.Equals(identity.AuthenticationType, MachineAuthenticationType, StringComparison.Ordinal));
    }

    private static bool IsMutation(string method)
    {
        return HttpMethods.IsPost(method)
            || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method)
            || HttpMethods.IsDelete(method);
    }
}

public static class BrowserMutationAntiforgeryApplicationBuilderExtensions
{
    public static IApplicationBuilder UseBrowserMutationAntiforgery(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<BrowserMutationAntiforgeryMiddleware>();
    }
}
