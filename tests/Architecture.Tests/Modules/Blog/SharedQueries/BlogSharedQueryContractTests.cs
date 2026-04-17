using Blog.PublicContracts.Queries;

namespace Architecture.Tests.ModuleCoverage.Blog.SharedQueries;

public sealed class BlogSharedQueryContractTests
{
    [Xunit.Fact]
    public void ReadModelAndQueryServiceLiveInPublicContractsQueries()
    {
        var contractType = typeof(BlogReadModel);
        var queryServiceType = typeof(IBlogPublishedPostQueryService);

        Xunit.Assert.Equal("Blog.PublicContracts", contractType.Assembly.GetName().Name);
        Xunit.Assert.Equal("Blog.PublicContracts.Queries", contractType.Namespace);
        Xunit.Assert.Equal("Blog.PublicContracts", queryServiceType.Assembly.GetName().Name);
        Xunit.Assert.Equal("Blog.PublicContracts.Queries", queryServiceType.Namespace);
        Xunit.Assert.NotNull(contractType.GetProperty(nameof(BlogReadModel.PostId)));
        Xunit.Assert.NotNull(contractType.GetProperty(nameof(BlogReadModel.Slug)));
        Xunit.Assert.NotNull(contractType.GetProperty(nameof(BlogReadModel.Title)));
        Xunit.Assert.NotNull(contractType.GetProperty(nameof(BlogReadModel.Summary)));
        Xunit.Assert.NotNull(contractType.GetProperty(nameof(BlogReadModel.PublishedUtc)));
        Xunit.Assert.NotNull(queryServiceType.GetMethod(nameof(IBlogPublishedPostQueryService.ListAsync)));
    }
}
