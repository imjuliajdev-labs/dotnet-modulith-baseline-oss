using Admin.Application.Authorization;
using Admin.PublicContracts.Queries;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;

namespace Admin.Application.Queries;

public static class AdminAnnouncementErrors
{
    public static Error NotFound(Guid announcementId)
    {
        return new Error(
            "admin.announcement_not_found",
            $"Admin announcement '{announcementId}' was not found.",
            ErrorKind.NotFound);
    }
}

public sealed record GetAdminAnnouncementQuery(Guid AnnouncementId) : IQuery<AdminAnnouncementReadModel>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => AdminModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class GetAdminAnnouncementQueryHandler : IQueryHandler<GetAdminAnnouncementQuery, AdminAnnouncementReadModel>
{
    private readonly IAdminAnnouncementQueryService _reader;

    public GetAdminAnnouncementQueryHandler(IAdminAnnouncementQueryService reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<Result<AdminAnnouncementReadModel>> Handle(GetAdminAnnouncementQuery query, CancellationToken cancellationToken)
    {
        var announcement = await _reader.GetAsync(query.AnnouncementId, cancellationToken);
        return announcement is null
            ? Result<AdminAnnouncementReadModel>.Failure(AdminAnnouncementErrors.NotFound(query.AnnouncementId))
            : Result<AdminAnnouncementReadModel>.Success(announcement);
    }
}
