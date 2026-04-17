using Admin.Application.Authorization;
using Admin.Application.SharedReads;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;

namespace Admin.Application.Queries;

public static class AdminGuidanceQueryDefaults
{
    public const int DefaultListLimit = 4;
    public const int MaxListLimit = 12;
}

public sealed record AdminGuidanceEntryReadModel(
    Guid EntryId,
    string Slug,
    string Title,
    string Body,
    string Category,
    bool Featured,
    DateTimeOffset PublishedUtc);

public sealed record AdminGuidanceListResponse(IReadOnlyCollection<AdminGuidanceEntryReadModel> Entries);

public sealed record ListAdminGuidanceQuery(int Limit = AdminGuidanceQueryDefaults.DefaultListLimit)
    : IQuery<AdminGuidanceListResponse>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => AdminModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class ListAdminGuidanceQueryHandler : IQueryHandler<ListAdminGuidanceQuery, AdminGuidanceListResponse>
{
    private readonly IAdminKnowledgeBaseGuidanceReader _reader;

    public ListAdminGuidanceQueryHandler(IAdminKnowledgeBaseGuidanceReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<Result<AdminGuidanceListResponse>> Handle(ListAdminGuidanceQuery query, CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(query.Limit, 1, AdminGuidanceQueryDefaults.MaxListLimit);
        var entries = await _reader.ListAsync(normalizedLimit, cancellationToken);

        return Result<AdminGuidanceListResponse>.Success(
            new AdminGuidanceListResponse(
                entries.Select(static entry => new AdminGuidanceEntryReadModel(
                    entry.EntryId,
                    entry.Slug,
                    entry.Title,
                    entry.Body,
                    entry.Category,
                    entry.Featured,
                    entry.PublishedUtc))
                .ToArray()));
    }
}
