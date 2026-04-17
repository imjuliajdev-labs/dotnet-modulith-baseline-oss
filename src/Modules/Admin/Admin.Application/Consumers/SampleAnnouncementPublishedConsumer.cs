using Admin.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using Admin.Application.ProcessManagers;
using KnowledgeBase.PublicContracts.Events;
using SampleFeature.PublicContracts.Events;

namespace Admin.Application.Consumers;

public sealed record AdminAnnouncementProjection(
    Guid AnnouncementId,
    string Title,
    string Body,
    DateTimeOffset PublishedUtc,
    string PublishedByActorId,
    string SourceModuleKey,
    string? SourceReference);

internal static class AdminAnnouncementSources
{
    public const string KnowledgeBase = "knowledge-base";
    public const string SampleFeature = "sample-feature";
}

public interface IAdminAnnouncementInbox
{
    ValueTask StoreAsync(AdminAnnouncementProjection announcement, CancellationToken cancellationToken);
}

internal sealed class SampleAnnouncementPublishedConsumer : IModuleScopedIntegrationEventHandler<SampleAnnouncementPublishedEventV1>
{
    private readonly IAdminAnnouncementProjectionProcessManager _processManager;

    public SampleAnnouncementPublishedConsumer(IAdminAnnouncementProjectionProcessManager processManager)
    {
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
    }

    public string ModuleKey => AdminModuleInfo.ModuleKey;

    public async Task Handle(SampleAnnouncementPublishedEventV1 integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        await _processManager.HandleAsync(
            new AdminAnnouncementProjection(
                integrationEvent.AnnouncementId,
                integrationEvent.Title,
                integrationEvent.Body,
                integrationEvent.OccurredAt.ToDateTimeOffset(),
                integrationEvent.PublishedByActorId,
                AdminAnnouncementSources.SampleFeature,
                null),
            cancellationToken);
    }
}

/// <summary>
/// Legacy V1 consumer kept as a migration bridge until <see cref="KnowledgeEntryPublishedEventV1"/>
/// reaches its retirement date (2027-01-15). New consumption logic lives in
/// <see cref="KnowledgeEntryPublishedV2Consumer"/>. Remove this consumer once all V1
/// events have been drained and the V1 contract is retired.
/// </summary>
internal sealed class KnowledgeEntryPublishedConsumer : IModuleScopedIntegrationEventHandler<KnowledgeEntryPublishedEventV1>
{
    private readonly IAdminAnnouncementProjectionProcessManager _processManager;

    public KnowledgeEntryPublishedConsumer(IAdminAnnouncementProjectionProcessManager processManager)
    {
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
    }

    public string ModuleKey => AdminModuleInfo.ModuleKey;

    public async Task Handle(KnowledgeEntryPublishedEventV1 integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        await _processManager.HandleAsync(
            new AdminAnnouncementProjection(
                integrationEvent.EntryId,
                integrationEvent.Title,
                integrationEvent.Body,
                integrationEvent.OccurredAt.ToDateTimeOffset(),
                integrationEvent.PublishedByActorId,
                AdminAnnouncementSources.KnowledgeBase,
                integrationEvent.Slug),
            cancellationToken);
    }
}

/// <summary>
/// V2 consumer for knowledge base entry published events. Handles the active contract
/// version which includes the <c>Category</c> field. The V1 consumer remains as a
/// migration bridge until the V1 contract is retired.
/// </summary>
internal sealed class KnowledgeEntryPublishedV2Consumer : IModuleScopedIntegrationEventHandler<KnowledgeEntryPublishedEventV2>
{
    private readonly IAdminAnnouncementProjectionProcessManager _processManager;

    public KnowledgeEntryPublishedV2Consumer(IAdminAnnouncementProjectionProcessManager processManager)
    {
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
    }

    public string ModuleKey => AdminModuleInfo.ModuleKey;

    public async Task Handle(KnowledgeEntryPublishedEventV2 integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        await _processManager.HandleAsync(
            new AdminAnnouncementProjection(
                integrationEvent.EntryId,
                integrationEvent.Title,
                integrationEvent.Body,
                integrationEvent.OccurredAt.ToDateTimeOffset(),
                integrationEvent.PublishedByActorId,
                AdminAnnouncementSources.KnowledgeBase,
                integrationEvent.Slug),
            cancellationToken);
    }
}
