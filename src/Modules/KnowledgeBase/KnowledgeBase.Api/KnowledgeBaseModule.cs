using BuildingBlocks.Application;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.Http;
using BuildingBlocks.Infrastructure.ProblemDetails;
using BuildingBlocks.Infrastructure.Modules;
using KnowledgeBase.Application.Authorization;
using KnowledgeBase.Application.Entries;
using KnowledgeBase.Application.Settings;
using KnowledgeBase.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeBase.Api;

public sealed class KnowledgeBaseModule : IApiModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        Key: "knowledge-base",
        DisplayName: "Knowledge Base",
        RoutePrefix: "/knowledge-base",
        SchemaName: "knowledge_base",
        ModuleNamespace: "knowledge-base",
        DefaultEnabled: true,
        CanBeDisabled: true,
        HasFrontendSurface: true);

    public string Key => Descriptor.Key;

    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDispatcher(typeof(KnowledgeBaseModuleInfo).Assembly);
        services.AddKnowledgeBaseInfrastructure();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var management = endpoints.MapGroup("/manage");

        endpoints.MapGet(
                "/settings",
                static async (IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new GetKnowledgeBasePublicSettingsQuery(), cancellationToken);
                    return mapper.Match(result, httpContext, settings => Results.Ok(KnowledgeBasePublicSettingsResponse.From(settings!)));
                })
            .WithName("KnowledgeBase_GetPublicSettings")
            .WithSummary("Get the current public-facing knowledge base settings.")
            .Produces<KnowledgeBasePublicSettingsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        endpoints.MapGet(
                "/entries",
                static async (int? limit, string? after, IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new ListPublishedKnowledgeEntriesQuery(limit, after), cancellationToken);
                    return mapper.Match(result, httpContext, page => Results.Ok(KnowledgeEntryPagedListResponse.From(page!)));
                })
            .WithName("KnowledgeBase_ListPublishedEntries")
            .WithSummary("List published knowledge base entries for the public FAQ-style surface.")
            .Produces<KnowledgeEntryPagedListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        endpoints.MapGet(
                "/entries/{slug}",
                static async (string slug, IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new GetPublishedKnowledgeEntryBySlugQuery(slug), cancellationToken);
                    return mapper.Match(result, httpContext, entry => Results.Ok(KnowledgeEntryResponse.From(entry!)));
                })
            .WithName("KnowledgeBase_GetPublishedEntryBySlug")
            .WithSummary("Get one published knowledge base entry by slug.")
            .Produces<KnowledgeEntryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        management.MapGet(
                "/settings",
                static async (IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new GetKnowledgeBaseManagementSettingsQuery(), cancellationToken);
                    return mapper.Match(result, httpContext, settings => Results.Ok(KnowledgeBaseManagementSettingsResponse.From(settings!)));
                })
            .WithName("KnowledgeBase_GetManagementSettings")
            .WithSummary("Get the full knowledge base management settings surface.")
            .Produces<KnowledgeBaseManagementSettingsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        management.MapPut(
                "/settings",
                static async (
                    UpdateKnowledgeBaseSettingsRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new UpdateKnowledgeBaseSettingsCommand(
                            request.ExpectedVersion,
                            request.PublicExperienceTitle,
                            request.PublicExperienceBlurb,
                            request.SearchPlaceholder,
                            request.SearchEnabled,
                            request.ManagementPreviewLimit),
                        cancellationToken);

                    return mapper.Match(result, httpContext, settings => Results.Ok(KnowledgeBaseManagementSettingsResponse.From(settings!)));
                })
            .WithName("KnowledgeBase_UpdateSettings")
            .WithSummary("Update the live knowledge base settings and audit the change.")
            .Accepts<UpdateKnowledgeBaseSettingsRequest>("application/json")
            .Produces<KnowledgeBaseManagementSettingsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        management.MapGet(
                "/entries",
                static async (IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new ListKnowledgeEntriesForManagementQuery(), cancellationToken);
                    return mapper.Match(result, httpContext, entries => Results.Ok(KnowledgeEntryListResponse.From(entries!)));
                })
            .WithName("KnowledgeBase_ListEntriesForManagement")
            .WithSummary("List all knowledge base entries for operators with draft and publication visibility.")
            .Produces<KnowledgeEntryListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithNonCursorListEndpoint("KnowledgeBase editorial management listing; bounded by the per-request management limit enforced inside ListKnowledgeEntriesForManagementQueryHandler. Non-public management surface behind admin authorization.");

        management.MapPost(
            "/entries",
                static async (
                    CreateKnowledgeEntryRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new CreateKnowledgeEntryCommand(
                            request.Slug,
                            request.Title,
                            request.Body,
                            request.Category,
                            request.Featured,
                            request.SortOrder),
                        cancellationToken);

                    return mapper.Match(result, httpContext, entry => Results.Ok(KnowledgeEntryResponse.From(entry!)));
                })
            .WithName("KnowledgeBase_CreateEntry")
            .WithSummary("Create a draft knowledge base entry.")
            .Accepts<CreateKnowledgeEntryRequest>("application/json")
            .Produces<KnowledgeEntryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        management.MapPut(
            "/entries/{entryId:guid}",
                static async (
                    Guid entryId,
                    UpdateKnowledgeEntryRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new UpdateKnowledgeEntryCommand(
                            entryId,
                            request.ExpectedVersion,
                            request.Slug,
                            request.Title,
                            request.Body,
                            request.Category,
                            request.Featured,
                            request.SortOrder),
                        cancellationToken);

                    return mapper.Match(result, httpContext, entry => Results.Ok(KnowledgeEntryResponse.From(entry!)));
                })
            .WithName("KnowledgeBase_UpdateEntry")
            .WithSummary("Update an existing knowledge base entry draft or published item.")
            .Accepts<UpdateKnowledgeEntryRequest>("application/json")
            .Produces<KnowledgeEntryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        management.MapPut(
            "/entries/{entryId:guid}/publish",
                static async (
                    Guid entryId,
                    PublishKnowledgeEntryRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new PublishKnowledgeEntryCommand(
                            entryId,
                            request.ExpectedVersion,
                            IdempotencyKeyEndpointFilter.GetRequestKey(httpContext)),
                        cancellationToken);

                    return mapper.Match(result, httpContext, entry => Results.Ok(KnowledgeEntryResponse.From(entry!)));
                })
            .WithName("KnowledgeBase_PublishEntry")
            .WithSummary("Publish a knowledge base entry and enqueue its public integration event.")
            .Accepts<PublishKnowledgeEntryRequest>("application/json")
            .Produces<KnowledgeEntryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithIdempotencyKey();

        management.MapPut(
            "/entries/{entryId:guid}/status",
                static async (
                    Guid entryId,
                    UpdateKnowledgeEntryStatusRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new SetKnowledgeEntryStatusCommand(entryId, request.ExpectedVersion, request.Status),
                        cancellationToken);

                    return mapper.Match(result, httpContext, entry => Results.Ok(KnowledgeEntryResponse.From(entry!)));
                })
            .WithName("KnowledgeBase_SetEntryStatus")
            .WithSummary("Return a knowledge base entry to draft or move it to archived state.")
            .Accepts<UpdateKnowledgeEntryStatusRequest>("application/json")
            .Produces<KnowledgeEntryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
