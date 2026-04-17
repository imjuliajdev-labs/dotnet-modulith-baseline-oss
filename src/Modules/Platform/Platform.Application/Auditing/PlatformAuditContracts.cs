using BuildingBlocks.Application;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using NodaTime;
using Platform.Application.Authorization;

namespace Platform.Application.Auditing;

public sealed record PlatformAuditEntry(
    string ModuleKey,
    string Action,
    string TargetType,
    string TargetId,
    string Outcome,
    Instant OccurredUtc,
    string? ActorId,
    string? CorrelationId);

public sealed record PlatformAuditTrail(IReadOnlyCollection<PlatformAuditEntry> Events);

public static class PlatformAuditErrors
{
    public static Error InvalidCursor()
    {
        return new Error(
            "platform.invalid_cursor",
            "The pagination cursor is malformed and was rejected.",
            ErrorKind.Validation);
    }
}

public interface IPlatformAuditEventReader
{
    ValueTask<IReadOnlyCollection<PlatformAuditEntry>> ListAsync(int limit, CancellationToken cancellationToken);

    ValueTask<CursorPagedResult<PlatformAuditEntry>> ListPagedAsync(int limit, string? afterCursor, CancellationToken cancellationToken);
}

public sealed record GetPlatformAuditEventsQuery(int Limit = 50, string? After = null) : IQuery<CursorPagedResult<PlatformAuditEntry>>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => PlatformModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class GetPlatformAuditEventsQueryHandler : IQueryHandler<GetPlatformAuditEventsQuery, CursorPagedResult<PlatformAuditEntry>>
{
    private readonly IPlatformAuditEventReader _auditEventReader;

    public GetPlatformAuditEventsQueryHandler(IPlatformAuditEventReader auditEventReader)
    {
        _auditEventReader = auditEventReader ?? throw new ArgumentNullException(nameof(auditEventReader));
    }

    public async Task<Result<CursorPagedResult<PlatformAuditEntry>>> Handle(GetPlatformAuditEventsQuery query, CancellationToken cancellationToken)
    {
        var limit = query.Limit <= 0
            ? 50
            : Math.Min(query.Limit, 200);

        if (!string.IsNullOrEmpty(query.After))
        {
            var decoded = CursorEncoding.Decode(query.After);
            if (decoded.Status != CursorDecodeStatus.Valid
                || !long.TryParse(decoded.SortValue, System.Globalization.CultureInfo.InvariantCulture, out _)
                || !long.TryParse(decoded.Id, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                return Result<CursorPagedResult<PlatformAuditEntry>>.Failure(PlatformAuditErrors.InvalidCursor());
            }
        }

        var result = await _auditEventReader.ListPagedAsync(limit, query.After, cancellationToken);
        return Result<CursorPagedResult<PlatformAuditEntry>>.Success(result);
    }
}
