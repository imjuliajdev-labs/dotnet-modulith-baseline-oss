using Blog.Application.Posts;
using Blog.Domain.Posts;
using Blog.PublicContracts.Events;
using BuildingBlocks.Application;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Testing.Time;
using Module.UnitTests.Support;
using NodaTime;

namespace Module.UnitTests.Blog;

public sealed class ListPublishedBlogPostsQueryHandlerTests
{
    [Fact]
    public async Task Handle_rejects_a_malformed_cursor()
    {
        var handler = new ListPublishedBlogPostsQueryHandler(new RecordingBlogPostReadQueries());

        var result = await handler.Handle(new ListPublishedBlogPostsQuery(Limit: 20, After: "not-a-cursor"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BlogPostErrors.InvalidCursor().Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_returns_store_results_and_applies_max_limit()
    {
        var post = BlogTestData.CreatePost(status: BlogPostStatus.Published, publishedUtc: BlogTestData.FixedNow);
        var readQueries = new RecordingBlogPostReadQueries
        {
            ListPublishedResult = new CursorPagedResult<BlogPost>([post], null)
        };
        var handler = new ListPublishedBlogPostsQueryHandler(readQueries);

        var result = await handler.Handle(new ListPublishedBlogPostsQuery(Limit: 999), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(100, readQueries.LastPublishedLimit);
        Assert.Single(result.Value.Items);
        Assert.Equal(post, result.Value.Items.Single());
    }
}

public sealed class GetPublishedBlogPostBySlugQueryHandlerTests
{
    [Fact]
    public async Task Handle_trims_the_slug_before_querying_the_store()
    {
        var post = BlogTestData.CreatePost(status: BlogPostStatus.Published, publishedUtc: BlogTestData.FixedNow);
        var readQueries = new RecordingBlogPostReadQueries
        {
            PublishedBySlugResult = Result<BlogPost>.Success(post)
        };
        var handler = new GetPublishedBlogPostBySlugQueryHandler(readQueries);

        var result = await handler.Handle(new GetPublishedBlogPostBySlugQuery("  governed-blog-post  "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(post, result.Value);
        Assert.Equal("governed-blog-post", readQueries.LastSlugLookup);
    }
}

public sealed class RegisterBlogPostViewCommandHandlerTests
{
    [Fact]
    public async Task Handle_trims_the_slug_before_registering_the_view()
    {
        var store = new RecordingBlogPostStore();
        var handler = new RegisterBlogPostViewCommandHandler(store);

        var result = await handler.Handle(new RegisterBlogPostViewCommand("  governed-blog-post  "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("governed-blog-post", store.LastViewedSlug);
    }
}

public sealed class ListBlogPostsForManagementQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_all_posts_from_the_store()
    {
        var draft = BlogTestData.CreatePost(postId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), status: BlogPostStatus.Draft);
        var archived = BlogTestData.CreatePost(postId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), status: BlogPostStatus.Archived, slug: "archived-post");
        var readQueries = new RecordingBlogPostReadQueries
        {
            AllPosts = [draft, archived]
        };
        var handler = new ListBlogPostsForManagementQueryHandler(readQueries);

        var result = await handler.Handle(new ListBlogPostsForManagementQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([draft, archived], result.Value!.Posts);
    }
}

public sealed class CreateBlogPostCommandHandlerTests
{
    [Fact]
    public async Task Handle_rejects_invalid_drafts_before_touching_the_store()
    {
        var store = new RecordingBlogPostStore();
        var handler = new CreateBlogPostCommandHandler(
            new RecordingAuditEventWriter(),
            new FakeClock(BlogTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("admin-1", isAuthenticated: true, roles: ["Admin"])),
            new InMemoryRequestContextAccessor(),
            store);

        var result = await handler.Handle(
            new CreateBlogPostCommand(null, "", "Summary", "Body", false, null, null, new BlogSeoMetadata(null, null, null), null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BlogPostErrors.TitleRequired().Code, result.Error.Code);
        Assert.Null(store.LastCreateDraft);
    }

    [Fact]
    public async Task Handle_normalizes_the_draft_and_writes_audit()
    {
        var post = BlogTestData.CreatePost();
        var store = new RecordingBlogPostStore
        {
            CreateResult = Result<BlogPost>.Success(post)
        };
        var auditWriter = new RecordingAuditEventWriter();
        var handler = new CreateBlogPostCommandHandler(
            auditWriter,
            new FakeClock(BlogTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("admin-1", isAuthenticated: true, roles: ["Admin"])),
            new InMemoryRequestContextAccessor { Current = new BuildingBlocks.Application.Dispatching.RequestContext("corr-blog-create", "req-blog-create") },
            store);

        var result = await handler.Handle(
            new CreateBlogPostCommand(
                null,
                "  Governed blog post  ",
                "  A governed summary.  ",
                "  A governed body.  ",
                true,
                " Platform ",
                ["Architecture", " governance "],
                new BlogSeoMetadata("  SEO title  ", "  SEO description  ", "  keywords  "),
                ["LinkedIn", " Email Newsletter "]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(store.LastCreateDraft);
        Assert.Equal("governed-blog-post", store.LastCreateDraft!.Slug);
        Assert.Equal("Governed blog post", store.LastCreateDraft.Title);
        Assert.Equal("A governed summary.", store.LastCreateDraft.Summary);
        Assert.Equal("A governed body.", store.LastCreateDraft.Body);
        Assert.Equal("platform", store.LastCreateDraft.CategorySlug);
        Assert.Equal(["architecture", "governance"], store.LastCreateDraft.TagNames);
        Assert.Equal(["email-newsletter", "linkedin"], store.LastCreateDraft.ShareTargets);
        Assert.True(store.LastCreateFeatured);
        Assert.Equal("admin-1", store.LastCreateActorId);
        Assert.Equal(BlogTestData.FixedNow, store.LastCreateNow);

        var audit = Assert.Single(auditWriter.Events);
        Assert.Equal("blog.post.create", audit.Action);
        Assert.Equal("blog-post", audit.TargetType);
        Assert.Equal(post.PostId.ToString(), audit.TargetId);
        Assert.Equal("draft", audit.Outcome);
        Assert.Equal("corr-blog-create", audit.CorrelationId);
    }
}

public sealed class UpdateBlogPostCommandHandlerTests
{
    [Fact]
    public async Task Handle_normalizes_the_draft_and_writes_audit()
    {
        var postId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var updatedPost = BlogTestData.CreatePost(postId: postId, version: 2, revisionNumber: 2);
        var store = new RecordingBlogPostStore
        {
            UpdateResult = Result<BlogPost>.Success(updatedPost)
        };
        var auditWriter = new RecordingAuditEventWriter();
        var handler = new UpdateBlogPostCommandHandler(
            auditWriter,
            new FakeClock(BlogTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("admin-2", isAuthenticated: true, roles: ["Admin"])),
            new InMemoryRequestContextAccessor { Current = new BuildingBlocks.Application.Dispatching.RequestContext("corr-blog-update", "req-blog-update") },
            store);

        var result = await handler.Handle(
            new UpdateBlogPostCommand(
                postId,
                2,
                "  custom-post  ",
                "  Updated title  ",
                "  Updated summary  ",
                "  Updated body  ",
                false,
                " Platform ",
                ["Architecture"],
                new BlogSeoMetadata("  SEO title  ", "  SEO description  ", "  keywords  "),
                ["LinkedIn"]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(postId, store.LastUpdatePostId);
        Assert.Equal(2, store.LastUpdateExpectedVersion);
        Assert.NotNull(store.LastUpdateDraft);
        Assert.Equal("custom-post", store.LastUpdateDraft!.Slug);
        Assert.Equal("Updated title", store.LastUpdateDraft.Title);
        Assert.Equal("Updated summary", store.LastUpdateDraft.Summary);
        Assert.Equal("Updated body", store.LastUpdateDraft.Body);
        Assert.Equal("platform", store.LastUpdateDraft.CategorySlug);
        Assert.False(store.LastUpdateFeatured);
        Assert.Equal("admin-2", store.LastUpdateActorId);
        Assert.Equal(BlogTestData.FixedNow, store.LastUpdateNow);

        var audit = Assert.Single(auditWriter.Events);
        Assert.Equal("blog.post.update", audit.Action);
        Assert.Equal(postId.ToString(), audit.TargetId);
        Assert.Equal("updated", audit.Outcome);
        Assert.Equal("corr-blog-update", audit.CorrelationId);
    }
}

public sealed class PublishBlogPostCommandHandlerTests
{
    [Fact]
    public async Task Handle_returns_current_post_without_duplicate_publish_when_already_published_at_expected_version()
    {
        var existing = BlogTestData.CreatePost(status: BlogPostStatus.Published, version: 3, publishedUtc: BlogTestData.FixedNow);
        var store = new RecordingBlogPostStore
        {
            GetByIdResult = Result<BlogPost>.Success(existing)
        };
        var auditWriter = new RecordingAuditEventWriter();
        var outboxPublisher = new RecordingBlogOutboxPublisher();
        var handler = new PublishBlogPostCommandHandler(
            auditWriter,
            new FakeClock(BlogTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("admin-1", isAuthenticated: true, roles: ["Admin"])),
            outboxPublisher,
            new InMemoryRequestContextAccessor(),
            store);

        var result = await handler.Handle(new PublishBlogPostCommand(existing.PostId, 3, "request-1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(existing, result.Value);
        Assert.Equal(existing.PostId, store.LastGetByIdPostId);
        Assert.Equal(Guid.Empty, store.LastPublishPostId);
        Assert.Empty(outboxPublisher.Requests);
        Assert.Empty(auditWriter.Events);
    }

    [Fact]
    public async Task Handle_publishes_the_post_emits_an_event_and_writes_audit()
    {
        var postId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var current = BlogTestData.CreatePost(postId: postId, status: BlogPostStatus.Draft, version: 4, slug: "publish-me");
        var published = BlogTestData.CreatePost(postId: postId, status: BlogPostStatus.Published, version: 5, slug: "publish-me", publishedUtc: BlogTestData.FixedNow, publishedByActorId: "admin-3");
        var store = new RecordingBlogPostStore
        {
            GetByIdResult = Result<BlogPost>.Success(current),
            PublishResult = Result<BlogPost>.Success(published)
        };
        var auditWriter = new RecordingAuditEventWriter();
        var outboxPublisher = new RecordingBlogOutboxPublisher();
        var handler = new PublishBlogPostCommandHandler(
            auditWriter,
            new FakeClock(BlogTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("admin-3", isAuthenticated: true, roles: ["Admin"])),
            outboxPublisher,
            new InMemoryRequestContextAccessor { Current = new BuildingBlocks.Application.Dispatching.RequestContext("corr-blog-publish", "req-blog-publish") },
            store);

        var result = await handler.Handle(new PublishBlogPostCommand(postId, 4, "request-2"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(postId, store.LastPublishPostId);
        Assert.Equal(4, store.LastPublishExpectedVersion);
        Assert.Equal("admin-3", store.LastPublishActorId);
        Assert.Equal(BlogTestData.FixedNow, store.LastPublishNow);
        var request = Assert.Single(outboxPublisher.Requests);
        Assert.IsType<BlogPostPublishedEventV1>(request.IntegrationEvent);

        var audit = Assert.Single(auditWriter.Events);
        Assert.Equal("blog.post.publish", audit.Action);
        Assert.Equal(postId.ToString(), audit.TargetId);
        Assert.Equal(BlogPostStatusNames.Published, audit.Outcome);
        Assert.Equal("corr-blog-publish", audit.CorrelationId);
    }
}

public sealed class SetBlogPostStatusCommandHandlerTests
{
    [Fact]
    public async Task Handle_sets_supported_status_and_writes_audit()
    {
        var postId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var archived = BlogTestData.CreatePost(postId: postId, status: BlogPostStatus.Archived, version: 3, slug: "archived-post");
        var store = new RecordingBlogPostStore
        {
            SetStatusResult = Result<BlogPost>.Success(archived)
        };
        var auditWriter = new RecordingAuditEventWriter();
        var handler = new SetBlogPostStatusCommandHandler(
            auditWriter,
            new FakeClock(BlogTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("admin-4", isAuthenticated: true, roles: ["Admin"])),
            new InMemoryRequestContextAccessor { Current = new BuildingBlocks.Application.Dispatching.RequestContext("corr-blog-status", "req-blog-status") },
            store);

        var result = await handler.Handle(new SetBlogPostStatusCommand(postId, 2, "archived"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(postId, store.LastSetStatusPostId);
        Assert.Equal(2, store.LastSetStatusExpectedVersion);
        Assert.Equal(BlogPostStatus.Archived, store.LastSetStatus);
        Assert.Equal("admin-4", store.LastSetStatusActorId);
        Assert.Equal(BlogTestData.FixedNow, store.LastSetStatusNow);

        var audit = Assert.Single(auditWriter.Events);
        Assert.Equal("blog.post.status.update", audit.Action);
        Assert.Equal(postId.ToString(), audit.TargetId);
        Assert.Equal("archived", audit.Outcome);
        Assert.Equal("corr-blog-status", audit.CorrelationId);
    }
}
