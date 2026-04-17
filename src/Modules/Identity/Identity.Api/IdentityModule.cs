using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.Modules;
using Identity.Api.Authentication;
using Identity.Api.Endpoints;
using Identity.Application.Authentication;
using Identity.Infrastructure;
using Identity.Infrastructure.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace Identity.Api;

public sealed class IdentityModule : IApiModule, IModuleRateLimiterContributor
{
    public ModuleDescriptor Descriptor { get; } = new(
        Key: "identity",
        DisplayName: "Identity",
        RoutePrefix: "/identity",
        SchemaName: "identity",
        ModuleNamespace: "identity",
        DefaultEnabled: true,
        CanBeDisabled: false,
        HasFrontendSurface: false);

    public string Key => Descriptor.Key;

    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDispatcher(typeof(PasswordSignInCommand).Assembly);
        services.AddIdentityInfrastructure();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, MachineAuthenticationHandler>(
                IdentityMachineAuthenticationDefaults.SchemeName,
                static _ =>
                {
                });
        services.AddAntiforgery(options =>
        {
            options.HeaderName = IdentityAntiforgeryDefaults.HeaderName;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });
    }

    public void RegisterRateLimiterPolicies(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<RateLimiterOptions>(static options =>
        {
            options.AddPolicy<string>(IdentityEndpointPolicies.AuthenticationAttempt, static httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"ip:{GetRemoteIpPartitionKey(httpContext)}",
                    factory: static _ => CreateFixedWindowOptions(permitLimit: 10)));
            options.AddPolicy<string>(IdentityEndpointPolicies.PasswordMutation, static httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetPasswordMutationPartitionKey(httpContext),
                    factory: static _ => CreateFixedWindowOptions(permitLimit: 5)));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints
            .MapAntiforgeryEndpoints()
            .MapSignInEndpoints()
            .MapAdminUserEndpoints()
            .MapMachineClientEndpoints()
            .MapCurrentUserEndpoints();
    }

    private static FixedWindowRateLimiterOptions CreateFixedWindowOptions(int permitLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(permitLimit, 1);

        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        };
    }

    private static string GetPasswordMutationPartitionKey(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var actorId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrWhiteSpace(actorId)
            ? $"ip:{GetRemoteIpPartitionKey(httpContext)}"
            : $"actor:{actorId}";
    }

    private static string GetRemoteIpPartitionKey(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
