using Admin.Application.SharedReads;
using KnowledgeBase.PublicContracts.Queries;

namespace Module.UnitTests.Admin.SharedReads;

public sealed class AdminKnowledgeBaseGuidanceReaderTests
{
    [Fact]
    public async Task ReturnsPublishedEntriesWhenKnowledgeBaseReadSucceeds()
    {
        var reader = new AdminKnowledgeBaseGuidanceReader(new StubKnowledgeBaseReader(
        [
            new KnowledgeBasePublishedEntryReadModel(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "baseline-guide",
                "Baseline guide",
                "Body",
                "Getting started",
                true,
                1,
                DateTimeOffset.Parse("2026-04-10T00:00:00Z"))
        ]));

        var entries = await reader.ListAsync(5, CancellationToken.None);

        var entry = Assert.Single(entries);
        Assert.Equal("baseline-guide", entry.Slug);
    }

    [Fact]
    public async Task ReturnsEmptyFallbackWhenKnowledgeBaseReadFails()
    {
        var reader = new AdminKnowledgeBaseGuidanceReader(new ThrowingKnowledgeBaseReader());

        var entries = await reader.ListAsync(5, CancellationToken.None);

        Assert.Empty(entries);
    }

    private sealed class StubKnowledgeBaseReader : IKnowledgeBasePublishedEntryQueryService
    {
        private readonly IReadOnlyCollection<KnowledgeBasePublishedEntryReadModel> _entries;

        public StubKnowledgeBaseReader(IReadOnlyCollection<KnowledgeBasePublishedEntryReadModel> entries)
        {
            _entries = entries;
        }

        public ValueTask<IReadOnlyCollection<KnowledgeBasePublishedEntryReadModel>> ListAsync(int limit, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_entries);
        }
    }

    private sealed class ThrowingKnowledgeBaseReader : IKnowledgeBasePublishedEntryQueryService
    {
        public ValueTask<IReadOnlyCollection<KnowledgeBasePublishedEntryReadModel>> ListAsync(int limit, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("KnowledgeBase read failed.");
        }
    }
}
