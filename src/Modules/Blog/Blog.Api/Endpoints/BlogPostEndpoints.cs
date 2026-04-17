using Blog.Application.Posts;
using Blog.Application.Scheduling;
using Blog.Domain.Posts;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.Http;
using BuildingBlocks.Infrastructure.ProblemDetails;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace Blog.Api.Endpoints;

internal static class BlogPostEndpoints
{
    public static IEndpointRouteBuilder MapPublicBlogPostEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/posts",
                static async (int? limit, string? after, IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new ListPublishedBlogPostsQuery(limit, after), cancellationToken);
                    return mapper.Match(result, httpContext, page => Results.Ok(BlogPostPagedListResponse.From(page!)));
                })
            .WithName("Blog_ListPublishedPosts")
            .WithSummary("List the published Blog posts that are visible on the public surface.")
            .Produces<BlogPostPagedListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapGet(
                "/posts/{slug}",
                static async (string slug, IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new GetPublishedBlogPostBySlugQuery(slug), cancellationToken);
                    return mapper.Match(result, httpContext, post => Results.Ok(BlogPostResponse.From(post!)));
                })
            .WithName("Blog_GetPublishedPostBySlug")
            .WithSummary("Get one published Blog post by slug.")
            .Produces<BlogPostResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapPost(
                "/posts/{slug}/views",
                static async (
                    string slug,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(new RegisterBlogPostViewCommand(slug), cancellationToken);
                    return result.IsFailure
                        ? mapper.Failure(result.Error, httpContext)
                        : Results.NoContent();
                })
            .WithName("Blog_RegisterPublishedPostView")
            .WithSummary("Register a public view for a published blog post.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRateLimiting(BlogEndpointPolicies.RegisterPostView);

        return endpoints;
    }

    public static IEndpointRouteBuilder MapManagedBlogPostEndpoints(this IEndpointRouteBuilder management)
    {
        management.MapGet(
                "/posts",
                static async (IDispatcher dispatcher, ResultHttpMapper mapper, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Query(new ListBlogPostsForManagementQuery(), cancellationToken);
                    return mapper.Match(result, httpContext, posts => Results.Ok(BlogPostListResponse.From(posts!)));
                })
            .WithName("Blog_ListPostsForManagement")
            .WithSummary("List Blog posts for operators with draft and publication visibility.")
            .Produces<BlogPostListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithNonCursorListEndpoint("Blog post editorial management listing; bounded by the per-request limit enforced inside ListBlogPostsForManagementQueryHandler. Non-public management surface mounted under /manage behind admin authorization.");

        management.MapPost(
                "/posts",
                static async (
                    CreateBlogPostRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new CreateBlogPostCommand(
                            request.Slug,
                            request.Title,
                            request.Summary,
                            request.Body,
                            request.Featured,
                            request.CategorySlug,
                            request.TagNames,
                            request.SeoMetadata?.ToDomain() ?? new BlogSeoMetadata(null, null, null),
                            request.ShareTargets),
                        cancellationToken);

                    return mapper.Match(result, httpContext, post => Results.Ok(BlogPostResponse.From(post!)));
                })
            .WithName("Blog_CreatePost")
            .WithSummary("Create a draft Blog post.")
            .Accepts<CreateBlogPostRequest>("application/json")
            .Produces<BlogPostResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        management.MapPut(
                "/posts/{postId:guid}",
                static async (
                    Guid postId,
                    UpdateBlogPostRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new UpdateBlogPostCommand(
                            postId,
                            request.ExpectedVersion,
                            request.Slug,
                            request.Title,
                            request.Summary,
                            request.Body,
                            request.Featured,
                            request.CategorySlug,
                            request.TagNames,
                            request.SeoMetadata?.ToDomain() ?? new BlogSeoMetadata(null, null, null),
                            request.ShareTargets),
                        cancellationToken);

                    return mapper.Match(result, httpContext, post => Results.Ok(BlogPostResponse.From(post!)));
                })
            .WithName("Blog_UpdatePost")
            .WithSummary("Update an existing blog post draft or published item.")
            .Accepts<UpdateBlogPostRequest>("application/json")
            .Produces<BlogPostResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        management.MapPut(
                "/posts/{postId:guid}/publish",
                static async (
                    Guid postId,
                    PublishBlogPostRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new PublishBlogPostCommand(
                            postId,
                            request.ExpectedVersion,
                            IdempotencyKeyEndpointFilter.GetRequestKey(httpContext)),
                        cancellationToken);

                    return mapper.Match(result, httpContext, post => Results.Ok(BlogPostResponse.From(post!)));
                })
            .WithName("Blog_PublishPost")
            .WithSummary("Publish a Blog post and enqueue its public integration event.")
            .Accepts<PublishBlogPostRequest>("application/json")
            .Produces<BlogPostResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithIdempotencyKey();

        management.MapPut(
                "/posts/{postId:guid}/status",
                static async (
                    Guid postId,
                    UpdateBlogPostStatusRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.Send(
                        new SetBlogPostStatusCommand(postId, request.ExpectedVersion, request.Status),
                        cancellationToken);

                    return mapper.Match(result, httpContext, post => Results.Ok(BlogPostResponse.From(post!)));
                })
            .WithName("Blog_UpdatePostStatus")
            .WithSummary("Update the lifecycle state of a blog post without using the publish endpoint.")
            .Accepts<UpdateBlogPostStatusRequest>("application/json")
            .Produces<BlogPostResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        management.MapPut(
                "/posts/{postId:guid}/schedule",
                static async (
                    Guid postId,
                    ScheduleBlogPostLifecycleRequest request,
                    IDispatcher dispatcher,
                    ResultHttpMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var command = request.ToCommand(postId);
                    if (command.IsFailure)
                    {
                        return mapper.Failure(command.Error, httpContext);
                    }

                    var result = await dispatcher.Send(command.Value!, cancellationToken);
                    return mapper.Match(result, httpContext, post => Results.Ok(BlogPostResponse.From(post!)));
                })
            .WithName("Blog_SchedulePostLifecycle")
            .WithSummary("Schedule publish and unpublish transitions using the current actor's preferred IANA time zone.")
            .Accepts<ScheduleBlogPostLifecycleRequest>("application/json")
            .Produces<BlogPostResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return management;
    }
}
