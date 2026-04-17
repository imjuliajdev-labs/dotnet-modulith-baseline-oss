using System.Text;
using BuildingBlocks.Application;
using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using KnowledgeBase.Application.Authorization;
using KnowledgeBase.Domain.Entries;

namespace KnowledgeBase.Application.Entries;

public sealed record KnowledgeEntryCollection(IReadOnlyCollection<KnowledgeEntry> Entries);

public interface IKnowledgeBaseStore
{
    ValueTask<IReadOnlyCollection<KnowledgeEntry>> ListPublishedAsync(CancellationToken cancellationToken);

    ValueTask<CursorPagedResult<KnowledgeEntry>> ListPublishedPagedAsync(int limit, string? afterCursor, CancellationToken cancellationToken);

    ValueTask<Result<KnowledgeEntry>> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken);

    ValueTask<Result<KnowledgeEntry>> GetByIdAsync(Guid entryId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<KnowledgeEntry>> ListAllAsync(CancellationToken cancellationToken);

    ValueTask<Result<KnowledgeEntry>> CreateAsync(
        string slug,
        string title,
        string body,
        string category,
        bool featured,
        int sortOrder,
        string actorId,
        NodaTime.Instant now,
        CancellationToken cancellationToken);

    ValueTask<Result<KnowledgeEntry>> UpdateAsync(
        Guid entryId,
        int expectedVersion,
        string slug,
        string title,
        string body,
        string category,
        bool featured,
        int sortOrder,
        string actorId,
        NodaTime.Instant now,
        CancellationToken cancellationToken);

    ValueTask<Result<KnowledgeEntry>> SetStatusAsync(
        Guid entryId,
        int expectedVersion,
        KnowledgeEntryStatus status,
        string actorId,
        NodaTime.Instant now,
        CancellationToken cancellationToken);
}

public static class KnowledgeBaseEntryErrors
{
    public static Error BodyRequired()
    {
        return new Error(
            "knowledge-base.body_required",
            "Entry body is required.",
            ErrorKind.Validation);
    }

    public static Error ConcurrencyConflict(Guid entryId)
    {
        return new Error(
            "knowledge-base.entry_version_conflict",
            $"Knowledge base entry '{entryId}' was changed by another operation.",
            ErrorKind.Conflict);
    }

    public static Error EntryNotFound(Guid entryId)
    {
        return new Error(
            "knowledge-base.entry_not_found",
            $"Knowledge base entry '{entryId}' was not found.",
            ErrorKind.NotFound);
    }

    public static Error InvalidCursor()
    {
        return new Error(
            "knowledge-base.invalid_cursor",
            "The pagination cursor is malformed and was rejected.",
            ErrorKind.Validation);
    }

    public static Error InvalidStatus(string status)
    {
        return new Error(
            "knowledge-base.invalid_status",
            $"Knowledge base status '{status}' is not supported.",
            ErrorKind.Validation);
    }

    public static Error PublishRequiresDedicatedEndpoint()
    {
        return new Error(
            "knowledge-base.publish_requires_dedicated_endpoint",
            "Publishing requires the dedicated knowledge base publish endpoint.",
            ErrorKind.Validation);
    }

    public static Error PublishedEntryNotFound(string slug)
    {
        return new Error(
            "knowledge-base.published_entry_not_found",
            $"Published knowledge base entry '{slug}' was not found.",
            ErrorKind.NotFound);
    }

    public static Error SlugAlreadyExists(string slug)
    {
        return new Error(
            "knowledge-base.slug_already_exists",
            $"Knowledge base slug '{slug}' already exists.",
            ErrorKind.Conflict);
    }

    public static Error TitleRequired()
    {
        return new Error(
            "knowledge-base.title_required",
            "Entry title is required.",
            ErrorKind.Validation);
    }

    public static Error VersionMustBePositive()
    {
        return new Error(
            "knowledge-base.version_required",
            "A positive expected version is required for knowledge base mutations.",
            ErrorKind.Validation);
    }
}

public sealed record ListPublishedKnowledgeEntriesQuery(int? Limit = null, string? After = null) : IQuery<CursorPagedResult<KnowledgeEntry>>, IModuleScoped
{
    public string ModuleKey => KnowledgeBaseModuleInfo.ModuleKey;
}

internal sealed class ListPublishedKnowledgeEntriesQueryHandler : IQueryHandler<ListPublishedKnowledgeEntriesQuery, CursorPagedResult<KnowledgeEntry>>
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;

    private readonly IKnowledgeBaseStore _store;

    public ListPublishedKnowledgeEntriesQueryHandler(IKnowledgeBaseStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<CursorPagedResult<KnowledgeEntry>>> Handle(ListPublishedKnowledgeEntriesQuery query, CancellationToken cancellationToken)
    {
        var limit = query.Limit is > 0
            ? Math.Min(query.Limit.Value, MaxLimit)
            : DefaultLimit;

        if (!string.IsNullOrEmpty(query.After))
        {
            var decoded = CursorEncoding.Decode(query.After);
            if (decoded.Status != CursorDecodeStatus.Valid
                || !int.TryParse(decoded.SortValue, System.Globalization.CultureInfo.InvariantCulture, out _)
                || !System.Guid.TryParse(decoded.Id, out _))
            {
                return Result<CursorPagedResult<KnowledgeEntry>>.Failure(KnowledgeBaseEntryErrors.InvalidCursor());
            }
        }

        var result = await _store.ListPublishedPagedAsync(limit, query.After, cancellationToken);
        return Result<CursorPagedResult<KnowledgeEntry>>.Success(result);
    }
}

public sealed record GetPublishedKnowledgeEntryBySlugQuery(string Slug) : IQuery<KnowledgeEntry>, IModuleScoped
{
    public string ModuleKey => KnowledgeBaseModuleInfo.ModuleKey;
}

internal sealed class GetPublishedKnowledgeEntryBySlugQueryHandler : IQueryHandler<GetPublishedKnowledgeEntryBySlugQuery, KnowledgeEntry>
{
    private readonly IKnowledgeBaseStore _store;

    public GetPublishedKnowledgeEntryBySlugQueryHandler(IKnowledgeBaseStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<Result<KnowledgeEntry>> Handle(GetPublishedKnowledgeEntryBySlugQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Slug))
        {
            return Task.FromResult(Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.PublishedEntryNotFound(string.Empty)));
        }

        return _store.GetPublishedBySlugAsync(query.Slug.Trim(), cancellationToken).AsTask();
    }
}

public sealed record ListKnowledgeEntriesForManagementQuery : IQuery<KnowledgeEntryCollection>, IModuleScoped, IAuthorizeRequest
{
    public string ModuleKey => KnowledgeBaseModuleInfo.ModuleKey;

    public IReadOnlyCollection<RoleRequirement> AuthorizationRequirements { get; } =
        [RoleRequirement.Admin];
}

internal sealed class ListKnowledgeEntriesForManagementQueryHandler : IQueryHandler<ListKnowledgeEntriesForManagementQuery, KnowledgeEntryCollection>
{
    private readonly IKnowledgeBaseStore _store;

    public ListKnowledgeEntriesForManagementQueryHandler(IKnowledgeBaseStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<KnowledgeEntryCollection>> Handle(ListKnowledgeEntriesForManagementQuery query, CancellationToken cancellationToken)
    {
        var entries = await _store.ListAllAsync(cancellationToken);
        return Result<KnowledgeEntryCollection>.Success(new KnowledgeEntryCollection(entries));
    }
}

public static class KnowledgeEntryStatusNames
{
    public const string Archived = "archived";
    public const string Draft = "draft";
    public const string Published = "published";

    public static string From(KnowledgeEntryStatus status)
    {
        return status switch
        {
            KnowledgeEntryStatus.Archived => Archived,
            KnowledgeEntryStatus.Published => Published,
            _ => Draft
        };
    }
}

internal sealed record NormalizedKnowledgeEntry(string Slug, string Title, string Body, string Category);

internal static partial class KnowledgeBaseEntryNormalization
{
    public static Result<NormalizedKnowledgeEntry> Normalize(string? slug, string title, string body, string? category)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Result<NormalizedKnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.TitleRequired());
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Result<NormalizedKnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.BodyRequired());
        }

        var normalizedTitle = title.Trim();
        var normalizedBody = body.Trim();
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
        var normalizedSlug = CreateSlug(string.IsNullOrWhiteSpace(slug) ? normalizedTitle : slug.Trim());

        return Result<NormalizedKnowledgeEntry>.Success(
            new NormalizedKnowledgeEntry(normalizedSlug, normalizedTitle, normalizedBody, normalizedCategory));
    }

    public static bool TryParseStatus(string status, out KnowledgeEntryStatus parsed)
    {
        switch (status.Trim().ToLowerInvariant())
        {
            case KnowledgeEntryStatusNames.Published:
                parsed = KnowledgeEntryStatus.Published;
                return true;
            case KnowledgeEntryStatusNames.Archived:
                parsed = KnowledgeEntryStatus.Archived;
                return true;
            case KnowledgeEntryStatusNames.Draft:
                parsed = KnowledgeEntryStatus.Draft;
                return true;
            default:
                parsed = KnowledgeEntryStatus.Draft;
                return false;
        }
    }

    public static string CreateSlug(string value)
    {
        var builder = new StringBuilder();
        var previousWasSeparator = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
                continue;
            }

            if (previousWasSeparator || builder.Length == 0)
            {
                continue;
            }

            builder.Append('-');
            previousWasSeparator = true;
        }

        while (builder.Length > 0 && builder[^1] == '-')
        {
            builder.Length -= 1;
        }

        return builder.Length == 0 ? "knowledge-entry" : builder.ToString();
    }

    internal static Task WriteAuditAsync(
        IAuditEventWriter auditEventWriter,
        IRequestContextAccessor requestContextAccessor,
        string actorId,
        NodaTime.Instant occurredUtc,
        string action,
        string targetId,
        string outcome,
        CancellationToken cancellationToken)
    {
        return auditEventWriter.WriteAsync(
            new AuditEvent(
                ModuleKey: KnowledgeBaseModuleInfo.ModuleKey,
                Action: action,
                TargetType: "knowledge-entry",
                TargetId: targetId,
                Outcome: outcome,
                OccurredUtc: occurredUtc,
                ActorId: actorId,
                CorrelationId: requestContextAccessor.Current?.CorrelationId),
            cancellationToken);
    }
}
