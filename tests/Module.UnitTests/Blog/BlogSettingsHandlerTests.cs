using Blog.Application.Settings;
using BuildingBlocks.Testing.Time;
using Module.UnitTests.Support;

namespace Module.UnitTests.Blog;

public sealed class GetBlogSettingsQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_the_current_settings()
    {
        var settings = BlogTestData.CreateSettings(operatorSummary: "Configured summary", version: 7);
        var store = new RecordingBlogSettingsStore
        {
            CurrentSettings = settings
        };
        var handler = new GetBlogSettingsQueryHandler(store);

        var result = await handler.Handle(new GetBlogSettingsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(settings, result.Value);
    }
}

public sealed class UpdateBlogSettingsCommandHandlerTests
{
    [Fact]
    public async Task Handle_rejects_non_positive_versions_before_touching_the_store()
    {
        var store = new RecordingBlogSettingsStore();
        var handler = new UpdateBlogSettingsCommandHandler(
            new RecordingAuditEventWriter(),
            new FakeClock(BlogTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("admin-1", isAuthenticated: true, roles: ["Admin"])),
            new InMemoryRequestContextAccessor(),
            store);

        var result = await handler.Handle(new UpdateBlogSettingsCommand(0, "Summary", 5), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BlogSettingsErrors.VersionMustBePositive().Code, result.Error.Code);
        Assert.Equal(0, store.UpdateCalls);
    }

    [Fact]
    public async Task Handle_updates_trimmed_settings_and_writes_audit()
    {
        var updatedSettings = BlogTestData.CreateSettings(operatorSummary: "Updated summary", previewLimit: 9, version: 3, updatedByActorId: "admin-2");
        var store = new RecordingBlogSettingsStore
        {
            UpdateResult = BuildingBlocks.Application.Results.Result<BlogModuleSettings>.Success(updatedSettings)
        };
        var auditWriter = new RecordingAuditEventWriter();
        var handler = new UpdateBlogSettingsCommandHandler(
            auditWriter,
            new FakeClock(BlogTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("admin-2", isAuthenticated: true, roles: ["Admin"])),
            new InMemoryRequestContextAccessor { Current = new BuildingBlocks.Application.Dispatching.RequestContext("corr-blog-settings", "req-blog-settings") },
            store);

        var result = await handler.Handle(new UpdateBlogSettingsCommand(2, "  Updated summary  ", 9), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, store.UpdateCalls);
        Assert.Equal(2, store.LastExpectedVersion);
        Assert.Equal("Updated summary", store.LastOperatorSummary);
        Assert.Equal(9, store.LastPreviewLimit);
        Assert.Equal("admin-2", store.LastActorId);
        Assert.Equal(BlogTestData.FixedNow, store.LastUpdatedUtc);
        Assert.Equal(updatedSettings, result.Value);

        var audit = Assert.Single(auditWriter.Events);
        Assert.Equal("blog.settings.update", audit.Action);
        Assert.Equal("module-settings", audit.TargetType);
        Assert.Equal(BlogSettingsDefaults.SettingsTargetId, audit.TargetId);
        Assert.Equal("updated", audit.Outcome);
        Assert.Equal("admin-2", audit.ActorId);
        Assert.Equal("corr-blog-settings", audit.CorrelationId);
    }
}
