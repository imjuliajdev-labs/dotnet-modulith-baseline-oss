using Admin.PublicContracts.Queries;

namespace Architecture.Tests.Modules.Admin.SharedQueries;

public sealed class AdminSharedQueryContractTests
{
    [Fact]
    public void AdminAnnouncementReadModelLivesInPublicContractsAndCarriesStableProjectionShape()
    {
        var contractType = typeof(AdminAnnouncementReadModel);

        Assert.Equal("Admin.PublicContracts", contractType.Assembly.GetName().Name);
        Assert.NotNull(contractType.GetProperty(nameof(AdminAnnouncementReadModel.AnnouncementId)));
        Assert.NotNull(contractType.GetProperty(nameof(AdminAnnouncementReadModel.Title)));
        Assert.NotNull(contractType.GetProperty(nameof(AdminAnnouncementReadModel.Body)));
        Assert.NotNull(contractType.GetProperty(nameof(AdminAnnouncementReadModel.PublishedUtc)));
        Assert.NotNull(contractType.GetProperty(nameof(AdminAnnouncementReadModel.PublishedByActorId)));
    }

    [Fact]
    public void AdminAnnouncementListResponseLivesInPublicContractsAndWrapsAnnouncementReadModels()
    {
        var contractType = typeof(AdminAnnouncementListResponse);

        Assert.Equal("Admin.PublicContracts", contractType.Assembly.GetName().Name);
        Assert.NotNull(contractType.GetProperty(nameof(AdminAnnouncementListResponse.Announcements)));
    }
}
