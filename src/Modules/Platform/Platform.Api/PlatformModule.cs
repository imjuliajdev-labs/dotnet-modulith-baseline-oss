using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.Http;
using BuildingBlocks.Infrastructure.ProblemDetails;
using BuildingBlocks.Infrastructure.Modules;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.Application.Authorization;
using Platform.Application.Auditing;
using Platform.Application.Bootstrap;
using Platform.Application.Health;
using Platform.Application.ModuleState;
using Platform.Infrastructure;
using Platform.Infrastructure.Configuration;

namespace Platform.Api;

public sealed class PlatformModule : IApiModule, IModuleRateLimiterContributor
{
    public ModuleDescriptor Descriptor { get; } = new(
        Key: "platform",
        DisplayName: "Platform",
        RoutePrefix: "/platform",
        SchemaName: "platform",
        ModuleNamespace: "platform",
        DefaultEnabled: true,
        CanBeDisabled: false,
        CompositionOrder: 0,
        HasFrontendSurface: true);

    public string Key => Descriptor.Key;

    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDispatcher(typeof(GetBootstrapManifestQuery).Assembly);
        services.AddPlatformInfrastructure();
    }

    public void RegisterRateLimiterPolicies(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<PlatformRateLimiterOptions>>(static (options, platformOptions) =>
            {
                options.AddFixedWindowLimiter(PlatformEndpointPolicies.ModuleStateMutations, limiter =>
                {
                    limiter.PermitLimit = platformOptions.Value.ModuleStateMutationPermitLimit;
                    limiter.Window = TimeSpan.FromMinutes(1);
                    limiter.QueueLimit = 0;
                    limiter.AutoReplenishment = true;
                });
            });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints;
        var moduleMutations = group.MapGroup("/modules")
            .RequireRateLimiting(PlatformEndpointPolicies.ModuleStateMutations);

        group.MapGet(
                "/bootstrap",
                static async (IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new GetBootstrapManifestQuery(), cancellationToken);
                    return mapper.Match(result, httpContext, manifest => Results.Ok(BootstrapManifestResponse.From(manifest!)));
                })
            .WithName("Platform_GetBootstrapManifest")
            .WithSummary("Get the module bootstrap manifest for the frontend shell.")
            .Produces<BootstrapManifestResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet(
                "/machine/bootstrap",
                static async (
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    IAuditEventWriter auditEventWriter,
                    BuildingBlocks.Domain.Time.IClock clock,
                    IRequestContextAccessor requestContextAccessor,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var authenticationResult = await httpContext.AuthenticateAsync("Machine");
                    if (!authenticationResult.Succeeded || authenticationResult.Principal is null)
                    {
                        return mapper.Failure(AuthorizationErrors.Unauthorized(typeof(GetMachineBootstrapManifestQuery)), httpContext);
                    }

                    httpContext.User = authenticationResult.Principal;

                    var result = await dispatcher.Query(new GetMachineBootstrapManifestQuery(), cancellationToken);

                    await auditEventWriter.WriteAsync(
                        new AuditEvent(
                            ModuleKey: PlatformModuleInfo.ModuleKey,
                            Action: "platform.machine_bootstrap.read",
                            TargetType: "bootstrap_manifest",
                            TargetId: "bootstrap_manifest",
                            Outcome: result.IsSuccess ? "succeeded" : result.Error.Code,
                            OccurredUtc: clock.GetCurrentInstant(),
                            ActorId: authenticationResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier),
                            CorrelationId: requestContextAccessor.Current?.CorrelationId),
                        cancellationToken);

                    return mapper.Match(result, httpContext, manifest => Results.Ok(BootstrapManifestResponse.From(manifest!)));
                })
            .WithName("Platform_GetMachineBootstrapManifest")
            .WithSummary("Get the machine bootstrap manifest using machine authentication.")
            .Produces<BootstrapManifestResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet(
                "/health",
                static async (IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new GetOperationalHealthSummaryQuery(), cancellationToken);
                    return mapper.Match(result, httpContext, summary => Results.Ok(OperationalHealthSummaryResponse.From(summary!)));
                })
            .WithName("Platform_GetOperationalHealthSummary")
            .WithSummary("Get the operational health summary for registered modules.")
            .Produces<OperationalHealthSummaryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet(
                "/audit-events",
                static async (int? limit, string? after, IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new GetPlatformAuditEventsQuery(limit ?? 50, after), cancellationToken);
                    return mapper.Match(result, httpContext, page => Results.Ok(PlatformAuditTrailPagedResponse.From(page!)));
                })
            .WithName("Platform_ListAuditEvents")
            .WithSummary("List recent operational audit events.")
            .Produces<PlatformAuditTrailPagedResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet(
                "/modules",
                static async (IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new GetModuleStatesQuery(), cancellationToken);
                    return mapper.Match(result, httpContext, modules => Results.Ok(ModuleStateListResponse.From(modules!)));
                })
            .WithName("Platform_ListModuleState")
            .WithSummary("List live module states and transition history.")
            .Produces<ModuleStateListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithNonCursorListEndpoint("Fixed-size projection over the static module registry linked into ApiHost; cardinality is bounded by the count of IApiModule implementations composed at startup.");

        moduleMutations.MapPost(
                "/{moduleKey}/enable",
                static async (
                    string moduleKey,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(new EnableModuleCommand(moduleKey), cancellationToken);
                    return mapper.Match(result, httpContext, state => Results.Ok(ModuleStateResponse.From(state!)));
                })
            .WithName("Platform_EnableModule")
            .WithSummary("Enable a module through the Platform control plane.")
            .Produces<ModuleStateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        moduleMutations.MapPost(
                "/{moduleKey}/disable",
                static async (
                    string moduleKey,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(new DisableModuleCommand(moduleKey), cancellationToken);
                    return mapper.Match(result, httpContext, state => Results.Ok(ModuleStateResponse.From(state!)));
                })
            .WithName("Platform_DisableModule")
            .WithSummary("Disable a module through the Platform control plane.")
            .Produces<ModuleStateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }
}
