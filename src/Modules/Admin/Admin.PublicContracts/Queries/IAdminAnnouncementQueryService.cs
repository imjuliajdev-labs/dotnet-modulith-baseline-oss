namespace Admin.PublicContracts.Queries;

public interface IAdminAnnouncementQueryService
{
    ValueTask<AdminAnnouncementReadModel?> GetAsync(Guid announcementId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<AdminAnnouncementReadModel>> ListAsync(int limit, CancellationToken cancellationToken);
}
