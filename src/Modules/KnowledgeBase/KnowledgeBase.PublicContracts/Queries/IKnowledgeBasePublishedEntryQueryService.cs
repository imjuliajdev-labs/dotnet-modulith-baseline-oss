namespace KnowledgeBase.PublicContracts.Queries;

public interface IKnowledgeBasePublishedEntryQueryService
{
    ValueTask<IReadOnlyCollection<KnowledgeBasePublishedEntryReadModel>> ListAsync(int limit, CancellationToken cancellationToken);
}
