using Blog.Application.Taxonomy;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.Http;
using BuildingBlocks.Infrastructure.ProblemDetails;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Blog.Api.Endpoints;

internal static class BlogTagEndpoints
{
    public static IEndpointRouteBuilder MapBlogTagEndpoints(this IEndpointRouteBuilder management)
    {
        management.MapGet(
                "/tags",
                static async (IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new ListBlogTagsQuery(), cancellationToken);
                    return mapper.Match(result, httpContext, tags => Results.Ok(BlogTagListResponse.From(tags!)));
                })
            .WithName("Blog_ListTags")
            .WithSummary("List managed blog tags for editorial operations.")
            .Produces<BlogTagListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithNonCursorListEndpoint("Blog tag management listing; bounded by the editorial tag cardinality. Management surface mounted under /manage behind admin authorization.");

        management.MapPost(
                "/tags",
                static async (
                    CreateBlogTagRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new CreateBlogTagCommand(request.Slug, request.DisplayName, request.Description),
                        cancellationToken);

                    return mapper.Match(result, httpContext, tag => Results.Ok(BlogTagResponse.From(tag!)));
                })
            .WithName("Blog_CreateTag")
            .WithSummary("Create a managed blog tag.")
            .Accepts<CreateBlogTagRequest>("application/json")
            .Produces<BlogTagResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        management.MapPut(
                "/tags/{slug}",
                static async (
                    string slug,
                    UpdateBlogTagRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new UpdateBlogTagCommand(slug, request.ExpectedVersion, request.DisplayName, request.Description),
                        cancellationToken);

                    return mapper.Match(result, httpContext, tag => Results.Ok(BlogTagResponse.From(tag!)));
                })
            .WithName("Blog_UpdateTag")
            .WithSummary("Update a managed blog tag.")
            .Accepts<UpdateBlogTagRequest>("application/json")
            .Produces<BlogTagResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return management;
    }
}
