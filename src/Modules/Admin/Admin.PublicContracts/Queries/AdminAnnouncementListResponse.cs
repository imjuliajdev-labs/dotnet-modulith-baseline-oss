namespace Admin.PublicContracts.Queries;

public sealed record AdminAnnouncementListResponse(IReadOnlyCollection<AdminAnnouncementReadModel> Announcements);
