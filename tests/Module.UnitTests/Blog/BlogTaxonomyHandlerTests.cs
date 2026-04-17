using Blog.Application.Taxonomy;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Testing.Time;
using Module.UnitTests.Support;
using GlobalBlogCategory = global::Blog.Domain.Taxonomy.BlogCategory;
using GlobalBlogTag = global::Blog.Domain.Taxonomy.BlogTag;
using GlobalBlogPostErrors = global::Blog.Application.Posts.BlogPostErrors;

namespace Module.UnitTests.Blog;

public sealed class ListBlogCategoriesQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_all_categories_from_the_store()
    {
        var category = BlogTestData.CreateCategory();
        var store = new RecordingBlogTaxonomyStore
        {
            Categories = [category]
        };
        var handler = new ListBlogCategoriesQueryHandler(store);

        var result = await handler.Handle(new ListBlogCategoriesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([category], result.Value!.Categories);
    }
}

public sealed class ListBlogTagsQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_all_tags_from_the_store()
    {
        var tag = BlogTestData.CreateTag();
        var store = new RecordingBlogTaxonomyStore
        {
            Tags = [tag]
        };
        var handler = new ListBlogTagsQueryHandler(store);

        var result = await handler.Handle(new ListBlogTagsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([tag], result.Value!.Tags);
    }
}

public sealed class CreateBlogCategoryCommandHandlerTests
{
    [Fact]
    public async Task Handle_normalizes_the_category_and_writes_audit()
    {
        var category = BlogTestData.CreateCategory(slug: "platform-strategy", name: "Platform Strategy");
        var store = new RecordingBlogTaxonomyStore
        {
            CreateCategoryResult = Result<GlobalBlogCategory>.Success(category)
        };
        var auditWriter = new RecordingAuditEventWriter();
        var handler = new CreateBlogCategoryCommandHandler(
            auditWriter,
            new FakeClock(BlogTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("admin-1", isAuthenticated: true, roles: ["Admin"])),
            new InMemoryRequestContextAccessor { Current = new BuildingBlocks.Application.Dispatching.RequestContext("corr-category-create", "req-category-create") },
            store);

        var result = await handler.Handle(new CreateBlogCategoryCommand(null, "  Platform Strategy  ", "  Durable architecture docs  "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("platform-strategy", store.LastCategorySlug);
        Assert.Equal("Platform Strategy", store.LastCategoryName);
        Assert.Equal("Durable architecture docs", store.LastCategoryDescription);
        Assert.Equal("admin-1", store.LastCategoryActorId);
        Assert.Equal(BlogTestData.FixedNow, store.LastCategoryNow);

        var audit = Assert.Single(auditWriter.Events);
        Assert.Equal("blog.category.create", audit.Action);
        Assert.Equal("blog-category", audit.TargetType);
        Assert.Equal("platform-strategy", audit.TargetId);
        Assert.Equal("platform-strategy", audit.Outcome);
        Assert.Equal("corr-category-create", audit.CorrelationId);
    }
}

public sealed class UpdateBlogCategoryCommandHandlerTests
{
    [Fact]
    public async Task Handle_requires_a_positive_version_before_updating()
    {
        var handler = new UpdateBlogCategoryCommandHandler(
            new RecordingAuditEventWriter(),
            new FakeClock(BlogTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("admin-1", isAuthenticated: true, roles: ["Admin"])),
            new InMemoryRequestContextAccessor(),
            new RecordingBlogTaxonomyStore());

        var result = await handler.Handle(new UpdateBlogCategoryCommand("platform", 0, "Platform", null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(GlobalBlogPostErrors.VersionMustBePositive().Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_updates_the_category_and_writes_audit()
    {
        var category = BlogTestData.CreateCategory(slug: "platform", name: "Platform", version: 2);
        var store = new RecordingBlogTaxonomyStore
        {
            UpdateCategoryResult = Result<GlobalBlogCategory>.Success(category)
        };
        var auditWriter = new RecordingAuditEventWriter();
        var handler = new UpdateBlogCategoryCommandHandler(
            auditWriter,
            new FakeClock(BlogTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("admin-2", isAuthenticated: true, roles: ["Admin"])),
            new InMemoryRequestContextAccessor { Current = new BuildingBlocks.Application.Dispatching.RequestContext("corr-category-update", "req-category-update") },
            store);

        var result = await handler.Handle(new UpdateBlogCategoryCommand("platform", 2, "  Platform  ", "  Updated description  "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("platform", store.LastCategorySlug);
        Assert.Equal(2, store.LastCategoryExpectedVersion);
        Assert.Equal("Platform", store.LastCategoryName);
        Assert.Equal("Updated description", store.LastCategoryDescription);
        Assert.Equal("admin-2", store.LastCategoryActorId);

        var audit = Assert.Single(auditWriter.Events);
        Assert.Equal("blog.category.update", audit.Action);
        Assert.Equal("platform", audit.TargetId);
        Assert.Equal("platform", audit.Outcome);
        Assert.Equal("corr-category-update", audit.CorrelationId);
    }
}

public sealed class CreateBlogTagCommandHandlerTests
{
    [Fact]
    public async Task Handle_normalizes_the_tag_and_writes_audit()
    {
        var tag = BlogTestData.CreateTag(slug: "governance", displayName: "Governance");
        var store = new RecordingBlogTaxonomyStore
        {
            CreateTagResult = Result<GlobalBlogTag>.Success(tag)
        };
        var auditWriter = new RecordingAuditEventWriter();
        var handler = new CreateBlogTagCommandHandler(
            auditWriter,
            new FakeClock(BlogTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("admin-1", isAuthenticated: true, roles: ["Admin"])),
            new InMemoryRequestContextAccessor { Current = new BuildingBlocks.Application.Dispatching.RequestContext("corr-tag-create", "req-tag-create") },
            store);

        var result = await handler.Handle(new CreateBlogTagCommand(null, "  Governance  ", "  Governance patterns  "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("governance", store.LastTagSlug);
        Assert.Equal("Governance", store.LastTagDisplayName);
        Assert.Equal("Governance patterns", store.LastTagDescription);
        Assert.Equal("admin-1", store.LastTagActorId);

        var audit = Assert.Single(auditWriter.Events);
        Assert.Equal("blog.tag.create", audit.Action);
        Assert.Equal("blog-tag", audit.TargetType);
        Assert.Equal("governance", audit.TargetId);
        Assert.Equal("governance", audit.Outcome);
        Assert.Equal("corr-tag-create", audit.CorrelationId);
    }
}

public sealed class UpdateBlogTagCommandHandlerTests
{
    [Fact]
    public async Task Handle_requires_a_positive_version_before_updating()
    {
        var handler = new UpdateBlogTagCommandHandler(
            new RecordingAuditEventWriter(),
            new FakeClock(BlogTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("admin-1", isAuthenticated: true, roles: ["Admin"])),
            new InMemoryRequestContextAccessor(),
            new RecordingBlogTaxonomyStore());

        var result = await handler.Handle(new UpdateBlogTagCommand("governance", 0, "Governance", null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(GlobalBlogPostErrors.VersionMustBePositive().Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_updates_the_tag_and_writes_audit()
    {
        var tag = BlogTestData.CreateTag(slug: "governance", displayName: "Governance", version: 3);
        var store = new RecordingBlogTaxonomyStore
        {
            UpdateTagResult = Result<GlobalBlogTag>.Success(tag)
        };
        var auditWriter = new RecordingAuditEventWriter();
        var handler = new UpdateBlogTagCommandHandler(
            auditWriter,
            new FakeClock(BlogTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("admin-2", isAuthenticated: true, roles: ["Admin"])),
            new InMemoryRequestContextAccessor { Current = new BuildingBlocks.Application.Dispatching.RequestContext("corr-tag-update", "req-tag-update") },
            store);

        var result = await handler.Handle(new UpdateBlogTagCommand("governance", 3, "  Governance  ", "  Updated tag description  "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("governance", store.LastTagSlug);
        Assert.Equal(3, store.LastTagExpectedVersion);
        Assert.Equal("Governance", store.LastTagDisplayName);
        Assert.Equal("Updated tag description", store.LastTagDescription);
        Assert.Equal("admin-2", store.LastTagActorId);

        var audit = Assert.Single(auditWriter.Events);
        Assert.Equal("blog.tag.update", audit.Action);
        Assert.Equal("governance", audit.TargetId);
        Assert.Equal("governance", audit.Outcome);
        Assert.Equal("corr-tag-update", audit.CorrelationId);
    }
}
