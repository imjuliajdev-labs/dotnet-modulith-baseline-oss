using Admin.Application.Authorization;
using Admin.PublicContracts.Queries;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;

namespace Admin.Application.Queries;

public sealed record ListMachineAdminAnnouncementsQuery(int Limit = AdminAnnouncementQueryDefaults.DefaultListLimit)
    : IQuery<AdminAnnouncementListResponse>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => AdminModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.AdminOrMachine];
}

internal sealed class ListMachineAdminAnnouncementsQueryHandler : IQueryHandler<ListMachineAdminAnnouncementsQuery, AdminAnnouncementListResponse>
{
    private readonly IAdminAnnouncementQueryService _reader;

    public ListMachineAdminAnnouncementsQueryHandler(IAdminAnnouncementQueryService reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<Result<AdminAnnouncementListResponse>> Handle(ListMachineAdminAnnouncementsQuery query, CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(query.Limit, 1, AdminAnnouncementQueryDefaults.MaxListLimit);
        var announcements = await _reader.ListAsync(normalizedLimit, cancellationToken);
        return Result<AdminAnnouncementListResponse>.Success(new AdminAnnouncementListResponse(announcements));
    }
}
