using KnowledgeBase.PublicContracts.Queries;

namespace Architecture.Tests.Modules.KnowledgeBase.SharedQueries;

public sealed class KnowledgeBaseSharedQueryContractTests
{
    [Fact]
    public void PublishedEntryReadModelLivesInPublicContractsAndCarriesStablePublishedShape()
    {
        var contractType = typeof(KnowledgeBasePublishedEntryReadModel);

        Assert.Equal("KnowledgeBase.PublicContracts", contractType.Assembly.GetName().Name);
        Assert.NotNull(contractType.GetProperty(nameof(KnowledgeBasePublishedEntryReadModel.EntryId)));
        Assert.NotNull(contractType.GetProperty(nameof(KnowledgeBasePublishedEntryReadModel.Slug)));
        Assert.NotNull(contractType.GetProperty(nameof(KnowledgeBasePublishedEntryReadModel.Title)));
        Assert.NotNull(contractType.GetProperty(nameof(KnowledgeBasePublishedEntryReadModel.Body)));
        Assert.NotNull(contractType.GetProperty(nameof(KnowledgeBasePublishedEntryReadModel.Category)));
        Assert.NotNull(contractType.GetProperty(nameof(KnowledgeBasePublishedEntryReadModel.Featured)));
        Assert.NotNull(contractType.GetProperty(nameof(KnowledgeBasePublishedEntryReadModel.PublishedUtc)));
    }

    [Fact]
    public void PublishedEntryListResponseLivesInPublicContractsAndWrapsEntryReadModels()
    {
        var contractType = typeof(KnowledgeBasePublishedEntryListResponse);

        Assert.Equal("KnowledgeBase.PublicContracts", contractType.Assembly.GetName().Name);
        Assert.NotNull(contractType.GetProperty(nameof(KnowledgeBasePublishedEntryListResponse.Entries)));
    }
}
