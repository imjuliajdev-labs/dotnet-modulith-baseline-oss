using System.Threading.RateLimiting;
using Blog.Api.Endpoints;
using Blog.Application.Authorization;
using Blog.Application.SharedReads;
using Blog.Infrastructure;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Blog.Api;

public sealed class BlogModule : IApiModule, IModuleRateLimiterContributor
{
    public ModuleDescriptor Descriptor { get; } = new(
        Key: "blog",
        DisplayName: "Blog",
        RoutePrefix: "/blog",
        SchemaName: "blog",
        ModuleNamespace: "blog",
        DefaultEnabled: true,
        CanBeDisabled: true,
        HasFrontendSurface: true);

    public string Key => Descriptor.Key;

    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDispatcher(typeof(BlogModuleInfo).Assembly);
        services.AddScoped<IBlogIdentityTimeZoneReader, BlogIdentityTimeZoneReader>();
        services.AddBlogInfrastructure();
    }

    public void RegisterRateLimiterPolicies(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<RateLimiterOptions>(static options =>
        {
            options.AddPolicy<string>(BlogEndpointPolicies.RegisterPostView, static httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                    factory: static _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints
            .MapPublicBlogPostEndpoints()
            .MapBlogSettingsEndpoints();

        var management = endpoints.MapGroup("/manage");

        management
            .MapBlogCategoryEndpoints()
            .MapBlogTagEndpoints()
            .MapManagedBlogPostEndpoints();
    }
}
