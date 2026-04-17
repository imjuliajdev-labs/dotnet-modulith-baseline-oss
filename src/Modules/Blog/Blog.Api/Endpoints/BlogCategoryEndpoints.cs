using Blog.Application.Taxonomy;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.Http;
using BuildingBlocks.Infrastructure.ProblemDetails;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Blog.Api.Endpoints;

internal static class BlogCategoryEndpoints
{
    public static IEndpointRouteBuilder MapBlogCategoryEndpoints(this IEndpointRouteBuilder management)
    {
        management.MapGet(
                "/categories",
                static async (IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new ListBlogCategoriesQuery(), cancellationToken);
                    return mapper.Match(result, httpContext, categories => Results.Ok(BlogCategoryListResponse.From(categories!)));
                })
            .WithName("Blog_ListCategories")
            .WithSummary("List managed blog categories for editorial operations.")
            .Produces<BlogCategoryListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithNonCursorListEndpoint("Blog category management listing; bounded by the editorial taxonomy cardinality. Management surface mounted under /manage behind admin authorization.");

        management.MapPost(
                "/categories",
                static async (
                    CreateBlogCategoryRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new CreateBlogCategoryCommand(request.Slug, request.Name, request.Description),
                        cancellationToken);

                    return mapper.Match(result, httpContext, category => Results.Ok(BlogCategoryResponse.From(category!)));
                })
            .WithName("Blog_CreateCategory")
            .WithSummary("Create a managed blog category.")
            .Accepts<CreateBlogCategoryRequest>("application/json")
            .Produces<BlogCategoryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        management.MapPut(
                "/categories/{slug}",
                static async (
                    string slug,
                    UpdateBlogCategoryRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new UpdateBlogCategoryCommand(slug, request.ExpectedVersion, request.Name, request.Description),
                        cancellationToken);

                    return mapper.Match(result, httpContext, category => Results.Ok(BlogCategoryResponse.From(category!)));
                })
            .WithName("Blog_UpdateCategory")
            .WithSummary("Update a managed blog category.")
            .Accepts<UpdateBlogCategoryRequest>("application/json")
            .Produces<BlogCategoryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return management;
    }
}
