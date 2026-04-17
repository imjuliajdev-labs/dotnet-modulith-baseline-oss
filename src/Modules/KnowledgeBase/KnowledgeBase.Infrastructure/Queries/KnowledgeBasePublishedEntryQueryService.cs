using KnowledgeBase.Application.Entries;
using KnowledgeBase.PublicContracts.Queries;

namespace KnowledgeBase.Infrastructure.Queries;

internal sealed class KnowledgeBasePublishedEntryQueryService : IKnowledgeBasePublishedEntryQueryService
{
    private readonly IKnowledgeBaseStore _store;

    public KnowledgeBasePublishedEntryQueryService(IKnowledgeBaseStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async ValueTask<IReadOnlyCollection<KnowledgeBasePublishedEntryReadModel>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 25);
        var entries = await _store.ListPublishedAsync(cancellationToken);

        return entries
            .Take(normalizedLimit)
            .Select(static entry => new KnowledgeBasePublishedEntryReadModel(
                entry.EntryId,
                entry.Slug,
                entry.Title,
                entry.Body,
                entry.Category,
                entry.Featured,
                entry.SortOrder,
                (entry.PublishedUtc ?? entry.UpdatedUtc).ToDateTimeOffset()))
            .ToArray();
    }
}
