using BuildingBlocks.Application.SharedReads;
using KnowledgeBase.PublicContracts.Queries;

namespace Admin.Application.SharedReads;

public interface IAdminKnowledgeBaseGuidanceReader
{
    ValueTask<IReadOnlyCollection<KnowledgeBasePublishedEntryReadModel>> ListAsync(int limit, CancellationToken cancellationToken);
}

public sealed class AdminKnowledgeBaseGuidanceReader : IAdminKnowledgeBaseGuidanceReader
{
    internal static readonly SharedReadPolicy Policy = new(
        Name: "admin.knowledge-base-guidance",
        Timeout: TimeSpan.FromSeconds(1),
        FreshnessExpectation: "Admin guidance can tolerate bounded staleness and should fall back to an empty guidance surface when KnowledgeBase cannot be reached in time.",
        FallbackBehavior: SharedReadFallbackBehavior.ReturnFallback);

    private readonly IKnowledgeBasePublishedEntryQueryService _reader;

    public AdminKnowledgeBaseGuidanceReader(IKnowledgeBasePublishedEntryQueryService reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async ValueTask<IReadOnlyCollection<KnowledgeBasePublishedEntryReadModel>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Policy.Timeout);

        try
        {
            return await _reader.ListAsync(limit, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Array.Empty<KnowledgeBasePublishedEntryReadModel>();
        }
        catch (Exception)
        {
            return Array.Empty<KnowledgeBasePublishedEntryReadModel>();
        }
    }
}
