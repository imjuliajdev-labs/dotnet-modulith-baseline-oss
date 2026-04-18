using System.Globalization;
using BuildingBlocks.Application;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Infrastructure.Persistence;
using KnowledgeBase.Application.Entries;
using KnowledgeBase.Application.Settings;
using KnowledgeBase.Domain.Entries;
using KnowledgeBase.Infrastructure.Configuration;
using NodaTime;
using Npgsql;
using NpgsqlTypes;

namespace KnowledgeBase.Infrastructure.Persistence;

internal sealed class PostgresKnowledgeBaseStore : IKnowledgeBaseStore, IKnowledgeBaseSettingsStore, IDatabaseMigration
{
    private readonly string _connectionString;
    private readonly NpgsqlDataSource _dataSource;
    private readonly BuildingBlocks.Domain.Time.IClock _clock;
    private readonly KnowledgeBaseSettingsOptions _settingsDefaults;

    public PostgresKnowledgeBaseStore(
        IPostgresDataSourceResolver dataSourceResolver,
        BuildingBlocks.Domain.Time.IClock clock,
        KnowledgeBaseInfrastructureOptions options)
    {
        ArgumentNullException.ThrowIfNull(dataSourceResolver);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentNullException.ThrowIfNull(options);
        _settingsDefaults = options.Settings;

        _connectionString = dataSourceResolver.GetConnectionString(KnowledgeBasePersistenceDefaults.ConnectionStringName)
            ?? throw new InvalidOperationException(
                string.Equals(KnowledgeBasePersistenceDefaults.ConnectionStringName, SharedRuntimePersistenceDefaults.ConnectionStringName, StringComparison.Ordinal)
                    ? SharedRuntimePersistenceDefaults.MissingConnectionStringMessage
                    : $"KnowledgeBase persistence requires ConnectionStrings:{KnowledgeBasePersistenceDefaults.ConnectionStringName}.");
        _dataSource = dataSourceResolver.GetRequiredDataSource(KnowledgeBasePersistenceDefaults.ConnectionStringName);
    }

    public string Name => "knowledge_base_entries";

    public string? ConnectionString => _connectionString;

    public async ValueTask<IReadOnlyCollection<KnowledgeEntry>> ListPublishedAsync(CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT entry_id, slug, title, body, category, featured, sort_order, status, version, created_utc, updated_utc, updated_by_actor_id, published_utc, published_by_actor_id
            FROM {KnowledgeBasePersistenceDefaults.SchemaName}.{KnowledgeBasePersistenceDefaults.EntriesTableName}
            WHERE status = @status
            ORDER BY featured DESC, sort_order ASC, lower(category), lower(title);
            """;

        return await ReadManyAsync(sql, cancellationToken, static command =>
        {
            command.Parameters.AddWithValue("status", KnowledgeEntryStatusNames.Published);
        });
    }

    public async ValueTask<CursorPagedResult<KnowledgeEntry>> ListPublishedPagedAsync(int limit, string? afterCursor, CancellationToken cancellationToken)
    {
        var decoded = CursorEncoding.Decode(afterCursor);
        string cursorClause;
        Action<NpgsqlCommand>? configure;

        if (decoded.Status == CursorDecodeStatus.Valid
            && int.TryParse(decoded.SortValue, CultureInfo.InvariantCulture, out var lastSortOrder)
            && Guid.TryParse(decoded.Id, out var lastEntryId))
        {
            cursorClause = "AND (sort_order > @lastSortOrder OR (sort_order = @lastSortOrder AND entry_id > @lastEntryId))";
            configure = command =>
            {
                command.Parameters.AddWithValue("status", KnowledgeEntryStatusNames.Published);
                command.Parameters.AddWithValue("lastSortOrder", lastSortOrder);
                command.Parameters.AddWithValue("lastEntryId", lastEntryId);
            };
        }
        else
        {
            cursorClause = string.Empty;
            configure = command =>
            {
                command.Parameters.AddWithValue("status", KnowledgeEntryStatusNames.Published);
            };
        }

        var sql = $"""
            SELECT entry_id, slug, title, body, category, featured, sort_order, status, version, created_utc, updated_utc, updated_by_actor_id, published_utc, published_by_actor_id
            FROM {KnowledgeBasePersistenceDefaults.SchemaName}.{KnowledgeBasePersistenceDefaults.EntriesTableName}
            WHERE status = @status
                {cursorClause}
            ORDER BY sort_order ASC, entry_id ASC
            LIMIT {limit + 1};
            """;

        var entries = await ReadManyAsync(sql, cancellationToken, configure);

        var hasMore = entries.Count > limit;
        var items = hasMore ? entries.Take(limit).ToArray() : entries.ToArray();

        string? nextCursor = null;
        if (hasMore && items.Length > 0)
        {
            var last = items[^1];
            nextCursor = CursorEncoding.Encode(
                last.SortOrder.ToString(CultureInfo.InvariantCulture),
                last.EntryId.ToString());
        }

        return new CursorPagedResult<KnowledgeEntry>(items, nextCursor);
    }

    public async ValueTask<Result<KnowledgeEntry>> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT entry_id, slug, title, body, category, featured, sort_order, status, version, created_utc, updated_utc, updated_by_actor_id, published_utc, published_by_actor_id
            FROM {KnowledgeBasePersistenceDefaults.SchemaName}.{KnowledgeBasePersistenceDefaults.EntriesTableName}
            WHERE lower(slug) = lower(@slug)
                AND status = @status
            LIMIT 1;
            """;

        var entry = await ReadSingleAsync(sql, cancellationToken, command =>
        {
            command.Parameters.AddWithValue("slug", slug);
            command.Parameters.AddWithValue("status", KnowledgeEntryStatusNames.Published);
        });

        return entry is null
            ? Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.PublishedEntryNotFound(slug))
            : Result<KnowledgeEntry>.Success(entry);
    }

    public async ValueTask<Result<KnowledgeEntry>> GetByIdAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var entry = await ReadByIdAsync(entryId, cancellationToken);
        return entry is null
            ? Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.EntryNotFound(entryId))
            : Result<KnowledgeEntry>.Success(entry);
    }

    public ValueTask<IReadOnlyCollection<KnowledgeEntry>> ListAllAsync(CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT entry_id, slug, title, body, category, featured, sort_order, status, version, created_utc, updated_utc, updated_by_actor_id, published_utc, published_by_actor_id
            FROM {KnowledgeBasePersistenceDefaults.SchemaName}.{KnowledgeBasePersistenceDefaults.EntriesTableName}
            ORDER BY featured DESC, sort_order ASC, lower(category), lower(title);
            """;

        return new ValueTask<IReadOnlyCollection<KnowledgeEntry>>(ReadManyAsync(sql, cancellationToken));
    }

    public async ValueTask<Result<KnowledgeEntry>> CreateAsync(string slug, string title, string body, string category, bool featured, int sortOrder, string actorId, Instant now, CancellationToken cancellationToken)
    {
        var entry = KnowledgeEntry.CreateDraft(
            Guid.NewGuid(),
            slug,
            title,
            body,
            category,
            featured,
            sortOrder,
            actorId,
            now);

        var sql = $"""
            INSERT INTO {KnowledgeBasePersistenceDefaults.SchemaName}.{KnowledgeBasePersistenceDefaults.EntriesTableName}
                (entry_id, slug, title, body, category, featured, sort_order, status, version, created_utc, updated_utc, updated_by_actor_id, published_utc, published_by_actor_id)
            VALUES
                (@entryId, @slug, @title, @body, @category, @featured, @sortOrder, @status, @version, @createdUtc, @updatedUtc, @updatedByActorId, @publishedUtc, @publishedByActorId);
            """;

        try
        {
            await ExecuteAsync(sql, cancellationToken, command => AddParameters(command, entry));
            return Result<KnowledgeEntry>.Success(entry);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.SlugAlreadyExists(slug));
        }
    }

    public async ValueTask<Result<KnowledgeEntry>> UpdateAsync(Guid entryId, int expectedVersion, string slug, string title, string body, string category, bool featured, int sortOrder, string actorId, Instant now, CancellationToken cancellationToken)
    {
        var current = await ReadByIdAsync(entryId, cancellationToken);
        if (current is null)
        {
            return Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.EntryNotFound(entryId));
        }

        if (current.Version != expectedVersion)
        {
            return Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.ConcurrencyConflict(entryId));
        }

        var updated = current.WithEdits(slug, title, body, category, featured, sortOrder, actorId, now);

        var sql = $"""
            UPDATE {KnowledgeBasePersistenceDefaults.SchemaName}.{KnowledgeBasePersistenceDefaults.EntriesTableName}
            SET slug = @slug,
                title = @title,
                body = @body,
                category = @category,
                featured = @featured,
                sort_order = @sortOrder,
                version = @version,
                updated_utc = @updatedUtc,
                updated_by_actor_id = @updatedByActorId
            WHERE entry_id = @entryId
                AND version = @expectedVersion;
            """;

        try
        {
            var affected = await ExecuteAsync(sql, cancellationToken, command =>
            {
                AddParameters(command, updated);
                command.Parameters.AddWithValue("expectedVersion", expectedVersion);
            });

            return affected == 0
                ? Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.ConcurrencyConflict(entryId))
                : Result<KnowledgeEntry>.Success(updated);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.SlugAlreadyExists(slug));
        }
    }

    public async ValueTask<Result<KnowledgeEntry>> SetStatusAsync(Guid entryId, int expectedVersion, KnowledgeEntryStatus status, string actorId, Instant now, CancellationToken cancellationToken)
    {
        var current = await ReadByIdAsync(entryId, cancellationToken);
        if (current is null)
        {
            return Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.EntryNotFound(entryId));
        }

        if (current.Version != expectedVersion)
        {
            return Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.ConcurrencyConflict(entryId));
        }

        var updated = current.WithStatus(status, actorId, now);

        var sql = $"""
            UPDATE {KnowledgeBasePersistenceDefaults.SchemaName}.{KnowledgeBasePersistenceDefaults.EntriesTableName}
            SET status = @status,
                version = @version,
                updated_utc = @updatedUtc,
                updated_by_actor_id = @updatedByActorId,
                published_utc = @publishedUtc,
                published_by_actor_id = @publishedByActorId
            WHERE entry_id = @entryId
                AND version = @expectedVersion;
            """;

        var affected = await ExecuteAsync(sql, cancellationToken, command =>
        {
            AddParameters(command, updated);
            command.Parameters.AddWithValue("expectedVersion", expectedVersion);
        });

        return affected == 0
            ? Result<KnowledgeEntry>.Failure(KnowledgeBaseEntryErrors.ConcurrencyConflict(entryId))
            : Result<KnowledgeEntry>.Success(updated);
    }

    public async ValueTask<KnowledgeBaseModuleSettings> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await ReadSettingsAsync(cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        await InsertDefaultSettingsAsync(_clock.GetCurrentInstant(), cancellationToken);
        return await ReadSettingsAsync(cancellationToken)
            ?? throw new InvalidOperationException("KnowledgeBase settings could not be created.");
    }

    public async ValueTask<Result<KnowledgeBaseModuleSettings>> UpdateAsync(
        int expectedVersion,
        NormalizedKnowledgeBaseSettings settings,
        string actorId,
        Instant now,
        CancellationToken cancellationToken)
    {
        var current = await GetAsync(cancellationToken);
        if (current.Version != expectedVersion)
        {
            return Result<KnowledgeBaseModuleSettings>.Failure(KnowledgeBaseSettingsErrors.ConcurrencyConflict());
        }

        var sql = $"""
            UPDATE {KnowledgeBasePersistenceDefaults.SchemaName}.{KnowledgeBasePersistenceDefaults.SettingsTableName}
            SET public_experience_title = @publicExperienceTitle,
                public_experience_blurb = @publicExperienceBlurb,
                search_placeholder = @searchPlaceholder,
                search_enabled = @searchEnabled,
                management_preview_limit = @managementPreviewLimit,
                version = @version,
                updated_utc = @updatedUtc,
                updated_by_actor_id = @updatedByActorId
            WHERE settings_key = @settingsKey
                AND version = @expectedVersion;
            """;

        var updated = current with
        {
            PublicExperienceTitle = settings.PublicExperienceTitle,
            PublicExperienceBlurb = settings.PublicExperienceBlurb,
            SearchPlaceholder = settings.SearchPlaceholder,
            SearchEnabled = settings.SearchEnabled,
            ManagementPreviewLimit = settings.ManagementPreviewLimit,
            Version = current.Version + 1,
            UpdatedUtc = now,
            UpdatedByActorId = actorId
        };

        var affected = await ExecuteAsync(sql, cancellationToken, command => AddSettingsParameters(command, updated, expectedVersion));
        return affected == 0
            ? Result<KnowledgeBaseModuleSettings>.Failure(KnowledgeBaseSettingsErrors.ConcurrencyConflict())
            : Result<KnowledgeBaseModuleSettings>.Success(updated);
    }

    public async Task<IReadOnlyCollection<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schemaName
                        AND table_name = @tableName);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schemaName", KnowledgeBasePersistenceDefaults.SchemaName);
        command.Parameters.AddWithValue("tableName", KnowledgeBasePersistenceDefaults.EntriesTableName);

        var exists = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        return exists ? [] : [$"{KnowledgeBasePersistenceDefaults.SchemaName}.{KnowledgeBasePersistenceDefaults.EntriesTableName}"];
    }

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        var sql = $"""
            CREATE SCHEMA IF NOT EXISTS {KnowledgeBasePersistenceDefaults.SchemaName};

            CREATE TABLE IF NOT EXISTS {KnowledgeBasePersistenceDefaults.SchemaName}.{KnowledgeBasePersistenceDefaults.EntriesTableName}
            (
                entry_id UUID PRIMARY KEY,
                slug TEXT NOT NULL,
                title TEXT NOT NULL,
                body TEXT NOT NULL,
                category TEXT NOT NULL,
                featured BOOLEAN NOT NULL,
                sort_order INTEGER NOT NULL,
                status TEXT NOT NULL,
                version INTEGER NOT NULL,
                created_utc TIMESTAMPTZ NOT NULL,
                updated_utc TIMESTAMPTZ NOT NULL,
                updated_by_actor_id TEXT NOT NULL,
                published_utc TIMESTAMPTZ NULL,
                published_by_actor_id TEXT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_knowledge_base_entries_slug
                ON {KnowledgeBasePersistenceDefaults.SchemaName}.{KnowledgeBasePersistenceDefaults.EntriesTableName} ((lower(slug)));

            CREATE TABLE IF NOT EXISTS {KnowledgeBasePersistenceDefaults.SchemaName}.{KnowledgeBasePersistenceDefaults.SettingsTableName}
            (
                settings_key TEXT PRIMARY KEY,
                public_experience_title TEXT NOT NULL,
                public_experience_blurb TEXT NOT NULL,
                search_placeholder TEXT NOT NULL,
                search_enabled BOOLEAN NOT NULL,
                management_preview_limit INTEGER NOT NULL,
                version INTEGER NOT NULL,
                updated_utc TIMESTAMPTZ NOT NULL,
                updated_by_actor_id TEXT NOT NULL
            );
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var seed in KnowledgeBaseSeedData.Create(_clock.GetCurrentInstant()))
        {
            var seedSql = $"""
                INSERT INTO {KnowledgeBasePersistenceDefaults.SchemaName}.{KnowledgeBasePersistenceDefaults.EntriesTableName}
                    (entry_id, slug, title, body, category, featured, sort_order, status, version, created_utc, updated_utc, updated_by_actor_id, published_utc, published_by_actor_id)
                VALUES
                    (@entryId, @slug, @title, @body, @category, @featured, @sortOrder, @status, @version, @createdUtc, @updatedUtc, @updatedByActorId, @publishedUtc, @publishedByActorId)
                ON CONFLICT ((lower(slug))) DO NOTHING;
                """;

            await using var seedCommand = new NpgsqlCommand(seedSql, connection);
            AddParameters(seedCommand, seed);
            await seedCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertDefaultSettingsAsync(_clock.GetCurrentInstant(), cancellationToken);
    }

    private async Task<int> ExecuteAsync(string sql, CancellationToken cancellationToken, Action<NpgsqlCommand>? configure = null)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        configure?.Invoke(command);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<KnowledgeEntry?> ReadByIdAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT entry_id, slug, title, body, category, featured, sort_order, status, version, created_utc, updated_utc, updated_by_actor_id, published_utc, published_by_actor_id
            FROM {KnowledgeBasePersistenceDefaults.SchemaName}.{KnowledgeBasePersistenceDefaults.EntriesTableName}
            WHERE entry_id = @entryId
            LIMIT 1;
            """;

        return await ReadSingleAsync(sql, cancellationToken, command => command.Parameters.AddWithValue("entryId", entryId));
    }

    private async Task<KnowledgeBaseModuleSettings?> ReadSettingsAsync(CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT public_experience_title, public_experience_blurb, search_placeholder, search_enabled, management_preview_limit, version, updated_utc, updated_by_actor_id
            FROM {KnowledgeBasePersistenceDefaults.SchemaName}.{KnowledgeBasePersistenceDefaults.SettingsTableName}
            WHERE settings_key = @settingsKey
            LIMIT 1;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("settingsKey", KnowledgeBasePersistenceDefaults.SettingsRowKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new KnowledgeBaseModuleSettings(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                Instant.FromDateTimeOffset(reader.GetFieldValue<DateTimeOffset>(6)),
                reader.GetString(7))
            : null;
    }

    private async Task InsertDefaultSettingsAsync(Instant now, CancellationToken cancellationToken)
    {
        var defaults = new KnowledgeBaseModuleSettings(
            _settingsDefaults.PublicExperienceTitle,
            _settingsDefaults.PublicExperienceBlurb,
            _settingsDefaults.SearchPlaceholder,
            _settingsDefaults.SearchEnabled,
            _settingsDefaults.ManagementPreviewLimit,
            Version: 1,
            UpdatedUtc: now,
            UpdatedByActorId: "system:knowledge-base-bootstrap");

        var sql = $"""
            INSERT INTO {KnowledgeBasePersistenceDefaults.SchemaName}.{KnowledgeBasePersistenceDefaults.SettingsTableName}
                (settings_key, public_experience_title, public_experience_blurb, search_placeholder, search_enabled, management_preview_limit, version, updated_utc, updated_by_actor_id)
            VALUES
                (@settingsKey, @publicExperienceTitle, @publicExperienceBlurb, @searchPlaceholder, @searchEnabled, @managementPreviewLimit, @version, @updatedUtc, @updatedByActorId)
            ON CONFLICT (settings_key) DO NOTHING;
            """;

        await ExecuteAsync(sql, cancellationToken, command => AddSettingsParameters(command, defaults, expectedVersion: null));
    }

    private async Task<IReadOnlyCollection<KnowledgeEntry>> ReadManyAsync(string sql, CancellationToken cancellationToken, Action<NpgsqlCommand>? configure = null)
    {
        var results = new List<KnowledgeEntry>();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        configure?.Invoke(command);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadEntry(reader));
        }

        return results;
    }

    private async Task<KnowledgeEntry?> ReadSingleAsync(string sql, CancellationToken cancellationToken, Action<NpgsqlCommand> configure)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        configure(command);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEntry(reader) : null;
    }

    private static void AddParameters(NpgsqlCommand command, KnowledgeEntry entry)
    {
        command.Parameters.AddWithValue("entryId", entry.EntryId);
        command.Parameters.AddWithValue("slug", entry.Slug);
        command.Parameters.AddWithValue("title", entry.Title);
        command.Parameters.AddWithValue("body", entry.Body);
        command.Parameters.AddWithValue("category", entry.Category);
        command.Parameters.AddWithValue("featured", entry.Featured);
        command.Parameters.AddWithValue("sortOrder", entry.SortOrder);
        command.Parameters.AddWithValue("status", KnowledgeEntryStatusNames.From(entry.Status));
        command.Parameters.AddWithValue("version", entry.Version);
        command.Parameters.AddWithValue("createdUtc", entry.CreatedUtc.ToDateTimeOffset());
        command.Parameters.AddWithValue("updatedUtc", entry.UpdatedUtc.ToDateTimeOffset());
        command.Parameters.AddWithValue("updatedByActorId", entry.UpdatedByActorId);

        var publishedUtc = command.Parameters.Add("publishedUtc", NpgsqlDbType.TimestampTz);
        publishedUtc.Value = entry.PublishedUtc.HasValue ? entry.PublishedUtc.Value.ToDateTimeOffset() : DBNull.Value;

        var publishedByActorId = command.Parameters.Add("publishedByActorId", NpgsqlDbType.Text);
        publishedByActorId.Value = string.IsNullOrWhiteSpace(entry.PublishedByActorId) ? DBNull.Value : entry.PublishedByActorId;
    }

    private static void AddSettingsParameters(NpgsqlCommand command, KnowledgeBaseModuleSettings settings, int? expectedVersion)
    {
        command.Parameters.AddWithValue("settingsKey", KnowledgeBasePersistenceDefaults.SettingsRowKey);
        command.Parameters.AddWithValue("publicExperienceTitle", settings.PublicExperienceTitle);
        command.Parameters.AddWithValue("publicExperienceBlurb", settings.PublicExperienceBlurb);
        command.Parameters.AddWithValue("searchPlaceholder", settings.SearchPlaceholder);
        command.Parameters.AddWithValue("searchEnabled", settings.SearchEnabled);
        command.Parameters.AddWithValue("managementPreviewLimit", settings.ManagementPreviewLimit);
        command.Parameters.AddWithValue("version", settings.Version);
        command.Parameters.AddWithValue("updatedUtc", settings.UpdatedUtc.ToDateTimeOffset());
        command.Parameters.AddWithValue("updatedByActorId", settings.UpdatedByActorId);

        if (expectedVersion.HasValue)
        {
            command.Parameters.AddWithValue("expectedVersion", expectedVersion.Value);
        }
    }

    private static KnowledgeEntry ReadEntry(NpgsqlDataReader reader)
    {
        return new KnowledgeEntry(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetBoolean(5),
            reader.GetInt32(6),
            ParseStatus(reader.GetString(7)),
            reader.GetInt32(8),
            Instant.FromDateTimeOffset(reader.GetFieldValue<DateTimeOffset>(9)),
            Instant.FromDateTimeOffset(reader.GetFieldValue<DateTimeOffset>(10)),
            reader.GetString(11),
            reader.IsDBNull(12) ? null : Instant.FromDateTimeOffset(reader.GetFieldValue<DateTimeOffset>(12)),
            reader.IsDBNull(13) ? null : reader.GetString(13));
    }

    private static KnowledgeEntryStatus ParseStatus(string status)
    {
        return status switch
        {
            KnowledgeEntryStatusNames.Published => KnowledgeEntryStatus.Published,
            KnowledgeEntryStatusNames.Archived => KnowledgeEntryStatus.Archived,
            _ => KnowledgeEntryStatus.Draft
        };
    }
}
