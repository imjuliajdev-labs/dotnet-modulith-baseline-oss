using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.Http;
using BuildingBlocks.Infrastructure.ProblemDetails;
using BuildingBlocks.Infrastructure.Modules;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Admin.Application.Authorization;
using Admin.Application.Queries;
using Admin.Application.SharedReads;
using Admin.Api.Contracts;
using Admin.Infrastructure;
using Admin.PublicContracts.Queries;

namespace Admin.Api;

public sealed class AdminModule : IApiModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        Key: "admin",
        DisplayName: "Admin",
        RoutePrefix: "/admin",
        SchemaName: "admin",
        ModuleNamespace: "admin",
        DefaultEnabled: true,
        CanBeDisabled: true,
        HasFrontendSurface: true);

    public string Key => Descriptor.Key;

    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDispatcher(typeof(AdminModuleInfo).Assembly);
        services.AddScoped<IAdminKnowledgeBaseGuidanceReader, AdminKnowledgeBaseGuidanceReader>();
        services.AddAdminInfrastructure();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
                "/machine/announcements",
                static async (
                    int? limit,
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
                        return mapper.Failure(AuthorizationErrors.Unauthorized(typeof(ListMachineAdminAnnouncementsQuery)), httpContext);
                    }

                    httpContext.User = authenticationResult.Principal;

                    var result = await dispatcher.Query(
                        new ListMachineAdminAnnouncementsQuery(limit ?? AdminAnnouncementQueryDefaults.DefaultListLimit),
                        cancellationToken);

                    await auditEventWriter.WriteAsync(
                        new AuditEvent(
                            ModuleKey: AdminModuleInfo.ModuleKey,
                            Action: "admin.machine_announcements.read",
                            TargetType: "announcement_feed",
                            TargetId: "recent_announcements",
                            Outcome: result.IsSuccess ? "succeeded" : result.Error.Code,
                            OccurredUtc: clock.GetCurrentInstant(),
                            ActorId: authenticationResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier),
                            CorrelationId: requestContextAccessor.Current?.CorrelationId),
                        cancellationToken);

                    return mapper.Match(
                        result,
                        httpContext,
                        announcements => Results.Ok(AdminMachineAnnouncementsResponseV1.From(announcements!)));
                })
            .WithName("Admin_ListMachineAnnouncements")
            .WithSummary("List recent Admin announcement projections using machine authentication.")
            .Produces<AdminMachineAnnouncementsResponseV1>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        endpoints.MapGet(
                "/announcements",
                static async (
                    int? limit,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(
                        new ListAdminAnnouncementsQuery(limit ?? AdminAnnouncementQueryDefaults.DefaultListLimit),
                        cancellationToken);

                    return mapper.Match(result, httpContext, announcements => Results.Ok(announcements));
                })
            .WithName("Admin_ListAnnouncements")
            .WithSummary("List the most recent Admin announcement projections from participating modules.")
            .Produces<AdminAnnouncementListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithNonCursorListEndpoint("Administrator announcement listing; bounded by the per-request limit clamped in ListAdminAnnouncementsQueryHandler. Admin-authenticated surface.");

        endpoints.MapGet(
                "/announcements/{announcementId:guid}",
                static async (
                    Guid announcementId,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new GetAdminAnnouncementQuery(announcementId), cancellationToken);
                    return mapper.Match(result, httpContext, announcement => Results.Ok(announcement));
                })
            .WithName("Admin_GetAnnouncement")
            .WithSummary("Get the Admin projection for one published module announcement.")
            .Produces<AdminAnnouncementReadModel>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapGet(
                "/guidance",
                static async (
                    int? limit,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(
                        new ListAdminGuidanceQuery(limit ?? AdminGuidanceQueryDefaults.DefaultListLimit),
                        cancellationToken);

                    return mapper.Match(result, httpContext, guidance => Results.Ok(guidance));
                })
            .WithName("Admin_ListGuidance")
            .WithSummary("List published Knowledge Base guidance through the Admin shared-query consumer surface.")
            .Produces<AdminGuidanceListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithNonCursorListEndpoint("Administrator knowledge-base guidance listing; bounded by the limit clamped in ListAdminGuidanceQueryHandler against AdminGuidanceQueryDefaults. Admin-authenticated surface.");
    }
}
