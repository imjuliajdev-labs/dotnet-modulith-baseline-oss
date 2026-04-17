using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Testing.Time;
using KnowledgeBase.Application.Settings;
using Module.UnitTests.Support;

namespace Module.UnitTests.KnowledgeBase;

public sealed class GetKnowledgeBasePublicSettingsQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_the_current_settings()
    {
        var settings = KnowledgeBaseTestData.CreateSettings(publicExperienceTitle: "Public knowledge", version: 7);
        var store = new RecordingKnowledgeBaseSettingsStore
        {
            CurrentSettings = settings
        };
        var handler = new GetKnowledgeBasePublicSettingsQueryHandler(store);

        var result = await handler.Handle(new GetKnowledgeBasePublicSettingsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(settings, result.Value);
    }
}

public sealed class GetKnowledgeBaseManagementSettingsQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_the_current_settings()
    {
        var settings = KnowledgeBaseTestData.CreateSettings(publicExperienceTitle: "Operator knowledge", version: 8);
        var store = new RecordingKnowledgeBaseSettingsStore
        {
            CurrentSettings = settings
        };
        var handler = new GetKnowledgeBaseManagementSettingsQueryHandler(store);

        var result = await handler.Handle(new GetKnowledgeBaseManagementSettingsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(settings, result.Value);
    }
}

public sealed class UpdateKnowledgeBaseSettingsCommandHandlerTests
{
    [Fact]
    public async Task Handle_rejects_non_positive_versions_before_touching_the_store()
    {
        var store = new RecordingKnowledgeBaseSettingsStore();
        var handler = new UpdateKnowledgeBaseSettingsCommandHandler(
            new RecordingAuditEventWriter(),
            new FakeClock(KnowledgeBaseTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("operator-1", isAuthenticated: true)),
            new InMemoryRequestContextAccessor(),
            store);

        var result = await handler.Handle(
            new UpdateKnowledgeBaseSettingsCommand(
                ExpectedVersion: 0,
                PublicExperienceTitle: "Knowledge",
                PublicExperienceBlurb: "Blurb",
                SearchPlaceholder: "Search",
                SearchEnabled: true,
                ManagementPreviewLimit: 12),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(KnowledgeBaseSettingsErrors.VersionMustBePositive().Code, result.Error.Code);
        Assert.Equal(0, store.UpdateCalls);
    }

    [Fact]
    public async Task Handle_updates_normalized_settings_and_writes_audit()
    {
        var updatedSettings = KnowledgeBaseTestData.CreateSettings(
            publicExperienceTitle: "Knowledge Base",
            publicExperienceBlurb: "Operator help and reference.",
            searchPlaceholder: "Find an answer",
            managementPreviewLimit: 18,
            version: 4,
            updatedByActorId: "operator-2");

        var store = new RecordingKnowledgeBaseSettingsStore
        {
            UpdateResult = BuildingBlocks.Application.Results.Result<KnowledgeBaseModuleSettings>.Success(updatedSettings)
        };
        var auditWriter = new RecordingAuditEventWriter();
        var handler = new UpdateKnowledgeBaseSettingsCommandHandler(
            auditWriter,
            new FakeClock(KnowledgeBaseTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("operator-2", isAuthenticated: true)),
            new InMemoryRequestContextAccessor { Current = new RequestContext("corr-kb-settings", "req-kb-settings") },
            store);

        var result = await handler.Handle(
            new UpdateKnowledgeBaseSettingsCommand(
                ExpectedVersion: 3,
                PublicExperienceTitle: "  Knowledge Base  ",
                PublicExperienceBlurb: "  Operator help and reference.  ",
                SearchPlaceholder: "  Find an answer  ",
                SearchEnabled: true,
                ManagementPreviewLimit: 18),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, store.UpdateCalls);
        Assert.Equal(3, store.LastExpectedVersion);
        Assert.NotNull(store.LastSettings);
        Assert.Equal("Knowledge Base", store.LastSettings!.PublicExperienceTitle);
        Assert.Equal("Operator help and reference.", store.LastSettings.PublicExperienceBlurb);
        Assert.Equal("Find an answer", store.LastSettings.SearchPlaceholder);
        Assert.True(store.LastSettings.SearchEnabled);
        Assert.Equal(18, store.LastSettings.ManagementPreviewLimit);
        Assert.Equal("operator-2", store.LastActorId);
        Assert.Equal(KnowledgeBaseTestData.FixedNow, store.LastUpdatedUtc);
        Assert.Equal(updatedSettings, result.Value);

        var audit = Assert.Single(auditWriter.Events);
        Assert.Equal("knowledge-base.settings.update", audit.Action);
        Assert.Equal("knowledge-base-settings", audit.TargetType);
        Assert.Equal(KnowledgeBaseModuleSettingsDefaults.SettingsTargetId, audit.TargetId);
        Assert.Equal("updated", audit.Outcome);
        Assert.Equal("operator-2", audit.ActorId);
        Assert.Equal("corr-kb-settings", audit.CorrelationId);
    }
}
