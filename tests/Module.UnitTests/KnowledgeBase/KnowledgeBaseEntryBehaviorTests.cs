using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Testing.Time;
using KnowledgeBase.Application.Entries;
using KnowledgeBase.Domain.Entries;
using KnowledgeBase.PublicContracts.Events;
using Module.UnitTests.Support;
using NodaTime;

namespace Module.UnitTests.KnowledgeBase;

public sealed class KnowledgeBaseEntryNormalizationTests
{
    [Fact]
    public void Normalize_trims_values_generates_slug_and_defaults_category()
    {
        var result = KnowledgeBaseEntryNormalization.Normalize(
            slug: null,
            title: "  Incident Runbook Overview  ",
            body: "  Steps for operators.  ",
            category: "   ");

        Assert.True(result.IsSuccess);
        Assert.Equal("incident-runbook-overview", result.Value!.Slug);
        Assert.Equal("Incident Runbook Overview", result.Value.Title);
        Assert.Equal("Steps for operators.", result.Value.Body);
        Assert.Equal("General", result.Value.Category);
    }
}

public sealed class CreateKnowledgeEntryCommandHandlerTests
{
    [Fact]
    public async Task Handle_passes_normalized_values_to_the_store_and_writes_audit()
    {
        var store = new RecordingKnowledgeBaseStore
        {
            CreateResult = Result<KnowledgeEntry>.Success(KnowledgeBaseTestData.CreateEntry(KnowledgeEntryStatus.Draft, version: 1))
        };
        var auditWriter = new RecordingAuditEventWriter();
        var handler = new CreateKnowledgeEntryCommandHandler(
            auditWriter,
            new FakeClock(KnowledgeBaseTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("operator-1", isAuthenticated: true)),
            new InMemoryRequestContextAccessor { Current = new RequestContext("corr-1", "req-1") },
            store);

        var result = await handler.Handle(
            new CreateKnowledgeEntryCommand(
                Slug: null,
                Title: "  Entry Title  ",
                Body: "  Entry Body  ",
                Category: "  Operations  ",
                Featured: true,
                SortOrder: 25),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("entry-title", store.LastCreateSlug);
        Assert.Equal("Entry Title", store.LastCreateTitle);
        Assert.Equal("Entry Body", store.LastCreateBody);
        Assert.Equal("Operations", store.LastCreateCategory);
        Assert.True(store.LastCreateFeatured);
        Assert.Equal(25, store.LastCreateSortOrder);
        Assert.Equal("operator-1", store.LastCreateActorId);
        Assert.Equal(KnowledgeBaseTestData.FixedNow, store.LastCreateNow);

        var audit = Assert.Single(auditWriter.Events);
        Assert.Equal("knowledge-base.entry.create", audit.Action);
        Assert.Equal("knowledge-entry", audit.TargetType);
        Assert.Equal("operator-1", audit.ActorId);
        Assert.Equal("corr-1", audit.CorrelationId);
    }
}

public sealed class UpdateKnowledgeEntryCommandHandlerTests
{
    [Fact]
    public async Task Handle_passes_normalized_values_to_the_store_and_writes_audit()
    {
        var entryId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var store = new RecordingKnowledgeBaseStore
        {
            UpdateResult = Result<KnowledgeEntry>.Success(KnowledgeBaseTestData.CreateEntry(KnowledgeEntryStatus.Draft, version: 2, entryId: entryId))
        };
        var auditWriter = new RecordingAuditEventWriter();
        var handler = new UpdateKnowledgeEntryCommandHandler(
            auditWriter,
            new FakeClock(KnowledgeBaseTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("operator-2", isAuthenticated: true)),
            new InMemoryRequestContextAccessor { Current = new RequestContext("corr-2", "req-2") },
            store);

        var result = await handler.Handle(
            new UpdateKnowledgeEntryCommand(
                EntryId: entryId,
                ExpectedVersion: 2,
                Slug: "  custom-slug  ",
                Title: "  Updated Title  ",
                Body: "  Updated Body  ",
                Category: "  Field Ops  ",
                Featured: false,
                SortOrder: 5),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(entryId, store.LastUpdateEntryId);
        Assert.Equal(2, store.LastUpdateExpectedVersion);
        Assert.Equal("custom-slug", store.LastUpdateSlug);
        Assert.Equal("Updated Title", store.LastUpdateTitle);
        Assert.Equal("Updated Body", store.LastUpdateBody);
        Assert.Equal("Field Ops", store.LastUpdateCategory);
        Assert.False(store.LastUpdateFeatured);
        Assert.Equal(5, store.LastUpdateSortOrder);
        Assert.Equal("operator-2", store.LastUpdateActorId);
        Assert.Equal(KnowledgeBaseTestData.FixedNow, store.LastUpdateNow);

        var audit = Assert.Single(auditWriter.Events);
        Assert.Equal("knowledge-base.entry.update", audit.Action);
        Assert.Equal(entryId.ToString(), audit.TargetId);
        Assert.Equal("updated", audit.Outcome);
    }
}

public sealed class PublishKnowledgeEntryCommandHandlerTests
{
    [Fact]
    public async Task Handle_returns_current_entry_without_duplicate_publish_when_already_published_at_expected_version()
    {
        var existingEntry = KnowledgeBaseTestData.CreateEntry(
            status: KnowledgeEntryStatus.Published,
            version: 3,
            publishedUtc: KnowledgeBaseTestData.FixedNow);
        var store = new RecordingKnowledgeBaseStore
        {
            GetByIdResult = Result<KnowledgeEntry>.Success(existingEntry)
        };
        var auditWriter = new RecordingAuditEventWriter();
        var outboxPublisher = new RecordingKnowledgeBaseOutboxPublisher();
        var handler = new PublishKnowledgeEntryCommandHandler(
            auditWriter,
            new FakeClock(KnowledgeBaseTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("operator-1", isAuthenticated: true)),
            outboxPublisher,
            new InMemoryRequestContextAccessor { Current = new RequestContext("corr-1", "req-1") },
            store);

        var result = await handler.Handle(
            new PublishKnowledgeEntryCommand(existingEntry.EntryId, ExpectedVersion: 3, RequestKey: "publish-request-1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(existingEntry, result.Value);
        Assert.Equal(0, store.SetStatusCalls);
        Assert.Empty(outboxPublisher.Requests);
        Assert.Empty(auditWriter.Events);
    }

    [Fact]
    public async Task Handle_publishes_v1_and_v2_events_and_writes_audit_when_transitioning_to_published()
    {
        var entryId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var currentEntry = KnowledgeBaseTestData.CreateEntry(KnowledgeEntryStatus.Draft, version: 4, entryId: entryId, slug: "ops-runbook");
        var publishedEntry = KnowledgeBaseTestData.CreateEntry(
            KnowledgeEntryStatus.Published,
            version: 5,
            publishedUtc: KnowledgeBaseTestData.FixedNow,
            entryId: entryId,
            slug: "ops-runbook");

        var store = new RecordingKnowledgeBaseStore
        {
            GetByIdResult = Result<KnowledgeEntry>.Success(currentEntry),
            SetStatusResult = Result<KnowledgeEntry>.Success(publishedEntry)
        };
        var auditWriter = new RecordingAuditEventWriter();
        var outboxPublisher = new RecordingKnowledgeBaseOutboxPublisher();
        var handler = new PublishKnowledgeEntryCommandHandler(
            auditWriter,
            new FakeClock(KnowledgeBaseTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("operator-3", isAuthenticated: true)),
            outboxPublisher,
            new InMemoryRequestContextAccessor { Current = new RequestContext("corr-3", "req-3") },
            store);

        var result = await handler.Handle(
            new PublishKnowledgeEntryCommand(entryId, ExpectedVersion: 4, RequestKey: "publish-request-2"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(entryId, store.LastGetByIdEntryId);
        Assert.Equal(1, store.SetStatusCalls);
        Assert.Equal(entryId, store.LastSetStatusEntryId);
        Assert.Equal(4, store.LastSetStatusExpectedVersion);
        Assert.Equal(KnowledgeEntryStatus.Published, store.LastSetStatus);
        Assert.Equal("operator-3", store.LastSetStatusActorId);
        Assert.Equal(KnowledgeBaseTestData.FixedNow, store.LastSetStatusNow);

        Assert.Equal(2, outboxPublisher.Requests.Count);
        Assert.IsType<KnowledgeEntryPublishedEventV1>(outboxPublisher.Requests[0].IntegrationEvent);
        Assert.IsType<KnowledgeEntryPublishedEventV2>(outboxPublisher.Requests[1].IntegrationEvent);

        var audit = Assert.Single(auditWriter.Events);
        Assert.Equal("knowledge-base.entry.publish", audit.Action);
        Assert.Equal(entryId.ToString(), audit.TargetId);
        Assert.Equal(KnowledgeEntryStatusNames.Published, audit.Outcome);
        Assert.Equal("operator-3", audit.ActorId);
        Assert.Equal("corr-3", audit.CorrelationId);
    }
}

public sealed class ListPublishedKnowledgeEntriesQueryHandlerTests
{
    [Fact]
    public async Task Handle_rejects_a_malformed_cursor()
    {
        var handler = new ListPublishedKnowledgeEntriesQueryHandler(new RecordingKnowledgeBaseStore());

        var result = await handler.Handle(new ListPublishedKnowledgeEntriesQuery(Limit: 20, After: "not-a-cursor"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(KnowledgeBaseEntryErrors.InvalidCursor().Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_returns_store_results_and_applies_limit()
    {
        var expectedEntry = KnowledgeBaseTestData.CreateEntry(KnowledgeEntryStatus.Published, version: 4, publishedUtc: KnowledgeBaseTestData.FixedNow);
        var store = new RecordingKnowledgeBaseStore
        {
            PublishedEntries = [expectedEntry]
        };
        var handler = new ListPublishedKnowledgeEntriesQueryHandler(store);

        var result = await handler.Handle(new ListPublishedKnowledgeEntriesQuery(Limit: 999), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(100, store.LastPublishedLimit);
        Assert.Single(result.Value.Items);
        Assert.Equal(expectedEntry, result.Value.Items.Single());
    }
}

public sealed class GetPublishedKnowledgeEntryBySlugQueryHandlerTests
{
    [Fact]
    public async Task Handle_trims_the_slug_before_querying_the_store()
    {
        var expectedEntry = KnowledgeBaseTestData.CreateEntry(KnowledgeEntryStatus.Published, version: 4, publishedUtc: KnowledgeBaseTestData.FixedNow);
        var store = new RecordingKnowledgeBaseStore
        {
            PublishedBySlugResult = Result<KnowledgeEntry>.Success(expectedEntry)
        };
        var handler = new GetPublishedKnowledgeEntryBySlugQueryHandler(store);

        var result = await handler.Handle(new GetPublishedKnowledgeEntryBySlugQuery("  entry-title  "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedEntry, result.Value);
        Assert.Equal("entry-title", store.LastPublishedSlugLookup);
    }
}

public sealed class ListKnowledgeEntriesForManagementQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_all_store_entries()
    {
        var draftEntry = KnowledgeBaseTestData.CreateEntry(status: KnowledgeEntryStatus.Draft, version: 1, entryId: Guid.Parse("44444444-4444-4444-4444-444444444444"));
        var archivedEntry = KnowledgeBaseTestData.CreateEntry(status: KnowledgeEntryStatus.Archived, version: 5, entryId: Guid.Parse("55555555-5555-5555-5555-555555555555"));
        var store = new RecordingKnowledgeBaseStore
        {
            AllEntries = [draftEntry, archivedEntry]
        };
        var handler = new ListKnowledgeEntriesForManagementQueryHandler(store);

        var result = await handler.Handle(new ListKnowledgeEntriesForManagementQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Entries.Count);
        Assert.Equal(new[] { draftEntry, archivedEntry }, result.Value.Entries);
    }
}

public sealed class SetKnowledgeEntryStatusCommandHandlerTests
{
    [Fact]
    public async Task Handle_rejects_published_status_value()
    {
        var store = new RecordingKnowledgeBaseStore();
        var handler = new SetKnowledgeEntryStatusCommandHandler(
            new RecordingAuditEventWriter(),
            new FakeClock(KnowledgeBaseTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("operator-1", isAuthenticated: true)),
            new InMemoryRequestContextAccessor { Current = new RequestContext("corr-1", "req-1") },
            store);

        var result = await handler.Handle(
            new SetKnowledgeEntryStatusCommand(Guid.NewGuid(), ExpectedVersion: 2, Status: "published"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("knowledge-base.publish_requires_dedicated_endpoint", result.Error.Code);
        Assert.Equal(0, store.SetStatusCalls);
    }

    [Fact]
    public async Task Handle_sets_status_and_writes_audit_when_the_value_is_supported()
    {
        var entryId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var archivedEntry = KnowledgeBaseTestData.CreateEntry(KnowledgeEntryStatus.Archived, version: 3, entryId: entryId);
        var store = new RecordingKnowledgeBaseStore
        {
            SetStatusResult = Result<KnowledgeEntry>.Success(archivedEntry)
        };
        var auditWriter = new RecordingAuditEventWriter();
        var handler = new SetKnowledgeEntryStatusCommandHandler(
            auditWriter,
            new FakeClock(KnowledgeBaseTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("operator-4", isAuthenticated: true)),
            new InMemoryRequestContextAccessor { Current = new RequestContext("corr-4", "req-4") },
            store);

        var result = await handler.Handle(
            new SetKnowledgeEntryStatusCommand(entryId, ExpectedVersion: 2, Status: "archived"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, store.SetStatusCalls);
        Assert.Equal(entryId, store.LastSetStatusEntryId);
        Assert.Equal(2, store.LastSetStatusExpectedVersion);
        Assert.Equal(KnowledgeEntryStatus.Archived, store.LastSetStatus);
        Assert.Equal("operator-4", store.LastSetStatusActorId);

        var audit = Assert.Single(auditWriter.Events);
        Assert.Equal("knowledge-base.entry.status.update", audit.Action);
        Assert.Equal(entryId.ToString(), audit.TargetId);
        Assert.Equal("archived", audit.Outcome);
        Assert.Equal("corr-4", audit.CorrelationId);
    }
}
