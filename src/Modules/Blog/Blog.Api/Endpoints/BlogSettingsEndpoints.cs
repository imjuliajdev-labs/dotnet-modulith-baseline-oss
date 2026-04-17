using Blog.Application.Settings;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.ProblemDetails;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Blog.Api.Endpoints;

internal static class BlogSettingsEndpoints
{
    public static IEndpointRouteBuilder MapBlogSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/settings",
                static async (IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new GetBlogSettingsQuery(), cancellationToken);
                    return mapper.Match(result, httpContext, settings => Results.Ok(BlogSettingsResponse.From(settings!)));
                })
            .WithName("Blog_GetSettings")
            .WithSummary("Get the current operator-managed runtime settings for the Blog module.")
            .Produces<BlogSettingsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        endpoints.MapPut(
                "/settings",
                static async (
                    UpdateBlogSettingsRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new UpdateBlogSettingsCommand(
                            request.ExpectedVersion,
                            request.OperatorSummary,
                            request.PreviewLimit),
                        cancellationToken);

                    return mapper.Match(result, httpContext, settings => Results.Ok(BlogSettingsResponse.From(settings!)));
                })
            .WithName("Blog_UpdateSettings")
            .WithSummary("Update the operator-managed runtime settings for the Blog module.")
            .Accepts<UpdateBlogSettingsRequest>("application/json")
            .Produces<BlogSettingsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }
}
