using Admin.Application.Consumers;
using Admin.Application.ProcessManagers;
using Admin.Application.Queries;
using KnowledgeBase.PublicContracts.Events;
using SampleFeature.PublicContracts.Events;
using NodaTime;

namespace Module.UnitTests.Admin;

public sealed class GetAdminAnnouncementQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_not_found_when_the_reader_has_no_match()
    {
        var announcementId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var reader = new RecordingAdminAnnouncementQueryService();
        var handler = new GetAdminAnnouncementQueryHandler(reader);

        var result = await handler.Handle(new GetAdminAnnouncementQuery(announcementId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminAnnouncementErrors.NotFound(announcementId).Code, result.Error.Code);
        Assert.Equal(announcementId, reader.LastGetId);
    }

    [Fact]
    public async Task Handle_returns_the_announcement_when_the_reader_finds_it()
    {
        var announcement = AdminTestData.CreateAnnouncement();
        var reader = new RecordingAdminAnnouncementQueryService
        {
            GetResult = announcement
        };
        var handler = new GetAdminAnnouncementQueryHandler(reader);

        var result = await handler.Handle(new GetAdminAnnouncementQuery(announcement.AnnouncementId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(announcement, result.Value);
    }
}

public sealed class ListAdminAnnouncementsQueryHandlerTests
{
    [Fact]
    public async Task Handle_clamps_the_requested_limit()
    {
        var announcement = AdminTestData.CreateAnnouncement();
        var reader = new RecordingAdminAnnouncementQueryService
        {
            ListResult = [announcement]
        };
        var handler = new ListAdminAnnouncementsQueryHandler(reader);

        var result = await handler.Handle(new ListAdminAnnouncementsQuery(999), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AdminAnnouncementQueryDefaults.MaxListLimit, reader.LastListLimit);
        Assert.Equal([announcement], result.Value!.Announcements);
    }
}

public sealed class ListMachineAdminAnnouncementsQueryHandlerTests
{
    [Fact]
    public async Task Handle_clamps_the_requested_limit()
    {
        var announcement = AdminTestData.CreateAnnouncement();
        var reader = new RecordingAdminAnnouncementQueryService
        {
            ListResult = [announcement]
        };
        var handler = new ListMachineAdminAnnouncementsQueryHandler(reader);

        var result = await handler.Handle(new ListMachineAdminAnnouncementsQuery(999), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AdminAnnouncementQueryDefaults.MaxListLimit, reader.LastListLimit);
        Assert.Equal([announcement], result.Value!.Announcements);
    }
}

public sealed class ListAdminGuidanceQueryHandlerTests
{
    [Fact]
    public async Task Handle_maps_guidance_entries_and_clamps_the_requested_limit()
    {
        var guidance = AdminTestData.CreateGuidance();
        var reader = new RecordingAdminKnowledgeBaseGuidanceReader
        {
            Result = [guidance]
        };
        var handler = new ListAdminGuidanceQueryHandler(reader);

        var result = await handler.Handle(new ListAdminGuidanceQuery(999), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AdminGuidanceQueryDefaults.MaxListLimit, reader.LastLimit);
        var item = Assert.Single(result.Value!.Entries);
        Assert.Equal(guidance.EntryId, item.EntryId);
        Assert.Equal(guidance.Slug, item.Slug);
        Assert.Equal(guidance.Title, item.Title);
        Assert.Equal(guidance.Body, item.Body);
        Assert.Equal(guidance.Category, item.Category);
        Assert.Equal(guidance.Featured, item.Featured);
        Assert.Equal(guidance.PublishedUtc, item.PublishedUtc);
    }
}

public sealed class RecoverAdminAnnouncementProjectionCommandHandlerTests
{
    [Fact]
    public async Task Handle_delegates_to_the_projection_process_manager()
    {
        var projection = new AdminAnnouncementProjection(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "Operational announcement",
            "Body",
            AdminTestData.FixedPublishedUtc,
            "admin-1",
            AdminAnnouncementSources.SampleFeature,
            null);
        var processManager = new RecordingAdminAnnouncementProjectionProcessManager();
        var handler = new RecoverAdminAnnouncementProjectionCommandHandler(processManager);

        var result = await handler.Handle(new RecoverAdminAnnouncementProjectionCommand(projection), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([projection], processManager.Projections);
    }
}

public sealed class SampleAnnouncementPublishedConsumerTests
{
    [Fact]
    public async Task Handle_projects_the_sample_announcement_into_the_admin_projection_shape()
    {
        var processManager = new RecordingAdminAnnouncementProjectionProcessManager();
        var consumer = new SampleAnnouncementPublishedConsumer(processManager);
        var integrationEvent = new SampleAnnouncementPublishedEventV1(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Instant.FromDateTimeOffset(AdminTestData.FixedPublishedUtc),
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            "Sample title",
            "Sample body",
            "admin-2");

        await consumer.Handle(integrationEvent, CancellationToken.None);

        var projection = Assert.Single(processManager.Projections);
        Assert.Equal(integrationEvent.AnnouncementId, projection.AnnouncementId);
        Assert.Equal("Sample title", projection.Title);
        Assert.Equal("Sample body", projection.Body);
        Assert.Equal(AdminAnnouncementSources.SampleFeature, projection.SourceModuleKey);
        Assert.Null(projection.SourceReference);
    }
}

public sealed class KnowledgeEntryPublishedConsumerTests
{
    [Fact]
    public async Task Handle_projects_the_v1_knowledge_entry_into_the_admin_projection_shape()
    {
        var processManager = new RecordingAdminAnnouncementProjectionProcessManager();
        var consumer = new KnowledgeEntryPublishedConsumer(processManager);
        var integrationEvent = new KnowledgeEntryPublishedEventV1(
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            Instant.FromDateTimeOffset(AdminTestData.FixedPublishedUtc),
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            "ops-guide",
            "Knowledge title",
            "Knowledge body",
            "admin-3");

        await consumer.Handle(integrationEvent, CancellationToken.None);

        var projection = Assert.Single(processManager.Projections);
        Assert.Equal(integrationEvent.EntryId, projection.AnnouncementId);
        Assert.Equal("Knowledge title", projection.Title);
        Assert.Equal("Knowledge body", projection.Body);
        Assert.Equal(AdminAnnouncementSources.KnowledgeBase, projection.SourceModuleKey);
        Assert.Equal("ops-guide", projection.SourceReference);
    }
}

public sealed class KnowledgeEntryPublishedV2ConsumerTests
{
    [Fact]
    public async Task Handle_projects_the_v2_knowledge_entry_into_the_admin_projection_shape()
    {
        var processManager = new RecordingAdminAnnouncementProjectionProcessManager();
        var consumer = new KnowledgeEntryPublishedV2Consumer(processManager);
        var integrationEvent = new KnowledgeEntryPublishedEventV2(
            Guid.Parse("abababab-abab-abab-abab-abababababab"),
            Instant.FromDateTimeOffset(AdminTestData.FixedPublishedUtc),
            Guid.Parse("cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd"),
            "ops-guide-v2",
            "Knowledge title v2",
            "Knowledge body v2",
            "Operations",
            "admin-4");

        await consumer.Handle(integrationEvent, CancellationToken.None);

        var projection = Assert.Single(processManager.Projections);
        Assert.Equal(integrationEvent.EntryId, projection.AnnouncementId);
        Assert.Equal("Knowledge title v2", projection.Title);
        Assert.Equal("Knowledge body v2", projection.Body);
        Assert.Equal(AdminAnnouncementSources.KnowledgeBase, projection.SourceModuleKey);
        Assert.Equal("ops-guide-v2", projection.SourceReference);
    }
}
