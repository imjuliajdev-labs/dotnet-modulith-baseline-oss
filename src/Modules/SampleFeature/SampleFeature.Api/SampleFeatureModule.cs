using System.Security.Claims;
using System.Threading.RateLimiting;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.Http;
using BuildingBlocks.Infrastructure.ProblemDetails;
using BuildingBlocks.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SampleFeature.Application.Authorization;
using SampleFeature.Application.Publishing;
using SampleFeature.Application.Scheduling;
using SampleFeature.Application.SharedReads;
using SampleFeature.Infrastructure;

namespace SampleFeature.Api;

public sealed class SampleFeatureModule : IApiModule, IModuleRateLimiterContributor
{
    public ModuleDescriptor Descriptor { get; } = new(
        Key: "sample-feature",
        DisplayName: "Sample Feature",
        RoutePrefix: "/sample-feature",
        SchemaName: "sample_feature",
        ModuleNamespace: "sample-feature",
        DefaultEnabled: true,
        CanBeDisabled: true,
        HasFrontendSurface: true);

    public string Key => Descriptor.Key;

    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDispatcher(typeof(SampleFeatureModuleInfo).Assembly);
        services.AddScoped<ISampleFeatureIdentityTimeZoneReader, SampleFeatureIdentityTimeZoneReader>();
        services.AddSampleFeatureInfrastructure();
    }

    public void RegisterRateLimiterPolicies(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<RateLimiterOptions>(static options =>
        {
            options.AddPolicy<string>(SampleFeatureEndpointPolicies.PublishAnnouncement, static httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetActorPartitionKey(httpContext),
                    factory: static _ => CreateFixedWindowOptions(permitLimit: 30)));
            options.AddPolicy<string>(SampleFeatureEndpointPolicies.ScheduleAnnouncement, static httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetActorPartitionKey(httpContext),
                    factory: static _ => CreateFixedWindowOptions(permitLimit: 30)));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
                "/announcements",
                static async (
                    PublishSampleAnnouncementRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var requestKey = IdempotencyKeyEndpointFilter.GetRequestKey(httpContext);

                    var result = await dispatcher.Send(new PublishSampleAnnouncementCommand(requestKey, request.Title, request.Body), cancellationToken);
                    return mapper.Match(result, httpContext, announcement => Results.Ok(PublishedSampleAnnouncementResponse.From(announcement!)));
                })
            .WithIdempotencyKey()
            .WithName("SampleFeature_PublishAnnouncement")
            .WithSummary("Publish a sample announcement and enqueue a public integration event.")
            .Accepts<PublishSampleAnnouncementRequest>("application/json")
            .Produces<PublishedSampleAnnouncementResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireRateLimiting(SampleFeatureEndpointPolicies.PublishAnnouncement);

        endpoints.MapGet(
                "/announcements/scheduled",
                static async (
                    int? limit,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(
                        new ListScheduledSampleAnnouncementsQuery(
                            limit ?? SampleAnnouncementSchedulingDefaults.DefaultListLimit),
                        cancellationToken);

                    return mapper.Match(
                        result,
                        httpContext,
                        announcements => Results.Ok(ScheduledSampleAnnouncementListResponse.From(announcements!)));
                })
            .WithName("SampleFeature_ListScheduledAnnouncements")
            .WithSummary("List the current actor's scheduled SampleFeature announcements.")
            .Produces<ScheduledSampleAnnouncementListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithNonCursorListEndpoint("Teaching-reference scheduled announcement listing; bounded by the per-request list limit enforced by ListScheduledSampleAnnouncementsQueryHandler. Authenticated surface scoped to the current actor.");

        endpoints.MapPost(
                "/announcements/scheduled",
                static async (
                    ScheduleSampleAnnouncementRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var requestKey = IdempotencyKeyEndpointFilter.GetRequestKey(httpContext);
                    var command = request.ToCommand(requestKey);
                    if (command.IsFailure)
                    {
                        return mapper.Failure(command.Error, httpContext);
                    }

                    var result = await dispatcher.Send(command.Value!, cancellationToken);

                    return mapper.Match(result, httpContext, announcement => Results.Ok(ScheduledSampleAnnouncementResponse.From(announcement!)));
                })
            .WithIdempotencyKey()
            .WithName("SampleFeature_ScheduleAnnouncement")
            .WithSummary("Schedule a SampleFeature announcement using the current actor's preferred IANA time zone.")
            .Accepts<ScheduleSampleAnnouncementRequest>("application/json")
            .Produces<ScheduledSampleAnnouncementResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting(SampleFeatureEndpointPolicies.ScheduleAnnouncement);
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

    private static string GetActorPartitionKey(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var actorId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(actorId))
        {
            return $"actor:{actorId}";
        }

        return $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}
