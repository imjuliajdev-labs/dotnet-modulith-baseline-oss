using Admin.Application.Consumers;
using BuildingBlocks.Infrastructure.Persistence;
using Npgsql;

namespace Admin.Infrastructure.Inbox;

internal sealed class AdminAnnouncementInboxWriter : IAdminAnnouncementInbox, IDatabaseMigration
{
    private readonly string _connectionString;
    private readonly NpgsqlDataSource _dataSource;

    public AdminAnnouncementInboxWriter(IPostgresDataSourceResolver dataSourceResolver)
    {
        ArgumentNullException.ThrowIfNull(dataSourceResolver);

        _connectionString = dataSourceResolver.GetConnectionString(AdminPersistenceDefaults.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Admin inbox persistence requires ConnectionStrings:{AdminPersistenceDefaults.ConnectionStringName}.");
        _dataSource = dataSourceResolver.GetRequiredDataSource(AdminPersistenceDefaults.ConnectionStringName);
    }

    public string Name => "admin_announcements";

    public string? ConnectionString => _connectionString;

    public async ValueTask StoreAsync(AdminAnnouncementProjection announcement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(announcement);

        var sql = $"""
            INSERT INTO {AdminPersistenceDefaults.SchemaName}.{AdminPersistenceDefaults.AnnouncementsTableName}
                (announcement_id, title, body, published_utc, published_by_actor_id, source_module_key, source_reference)
            VALUES
                (@announcementId, @title, @body, @publishedUtc, @publishedByActorId, @sourceModuleKey, @sourceReference)
            ON CONFLICT (announcement_id) DO NOTHING;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("announcementId", announcement.AnnouncementId);
        command.Parameters.AddWithValue("title", announcement.Title);
        command.Parameters.AddWithValue("body", announcement.Body);
        command.Parameters.AddWithValue("publishedUtc", announcement.PublishedUtc);
        command.Parameters.AddWithValue("publishedByActorId", announcement.PublishedByActorId);
        command.Parameters.AddWithValue("sourceModuleKey", announcement.SourceModuleKey);
        command.Parameters.AddWithValue("sourceReference", (object?)announcement.SourceReference ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        const string tableSql = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schemaName
                    AND table_name = @tableName);
            """;

        const string columnSql = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = @schemaName
                    AND table_name = @tableName
                    AND column_name = @columnName);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var pending = new List<string>();

        await using (var tableCommand = new NpgsqlCommand(tableSql, connection))
        {
            tableCommand.Parameters.AddWithValue("schemaName", AdminPersistenceDefaults.SchemaName);
            tableCommand.Parameters.AddWithValue("tableName", AdminPersistenceDefaults.AnnouncementsTableName);

            var exists = (bool)(await tableCommand.ExecuteScalarAsync(cancellationToken) ?? false);
            if (!exists)
            {
                pending.Add($"{AdminPersistenceDefaults.SchemaName}.{AdminPersistenceDefaults.AnnouncementsTableName}");
                return pending;
            }
        }

        foreach (var columnName in new[] { "source_module_key", "source_reference" })
        {
            await using var columnCommand = new NpgsqlCommand(columnSql, connection);
            columnCommand.Parameters.AddWithValue("schemaName", AdminPersistenceDefaults.SchemaName);
            columnCommand.Parameters.AddWithValue("tableName", AdminPersistenceDefaults.AnnouncementsTableName);
            columnCommand.Parameters.AddWithValue("columnName", columnName);

            var exists = (bool)(await columnCommand.ExecuteScalarAsync(cancellationToken) ?? false);
            if (!exists)
            {
                pending.Add($"{AdminPersistenceDefaults.SchemaName}.{AdminPersistenceDefaults.AnnouncementsTableName}.{columnName}");
            }
        }

        return pending;
    }

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        var sql = $"""
            CREATE SCHEMA IF NOT EXISTS {AdminPersistenceDefaults.SchemaName};

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_schema = '{AdminPersistenceDefaults.SchemaName}'
                      AND table_name = '{AdminPersistenceDefaults.LegacyAnnouncementsTableName}'
                ) AND NOT EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_schema = '{AdminPersistenceDefaults.SchemaName}'
                      AND table_name = '{AdminPersistenceDefaults.AnnouncementsTableName}'
                ) THEN
                    ALTER TABLE {AdminPersistenceDefaults.SchemaName}.{AdminPersistenceDefaults.LegacyAnnouncementsTableName}
                        RENAME TO {AdminPersistenceDefaults.AnnouncementsTableName};
                END IF;
            END $$;

            CREATE TABLE IF NOT EXISTS {AdminPersistenceDefaults.SchemaName}.{AdminPersistenceDefaults.AnnouncementsTableName}
            (
                announcement_id UUID PRIMARY KEY,
                title TEXT NOT NULL,
                body TEXT NOT NULL,
                published_utc TIMESTAMPTZ NOT NULL,
                published_by_actor_id TEXT NOT NULL,
                source_module_key TEXT NOT NULL DEFAULT 'sample-feature',
                source_reference TEXT NULL
            );

            ALTER TABLE {AdminPersistenceDefaults.SchemaName}.{AdminPersistenceDefaults.AnnouncementsTableName}
                ADD COLUMN IF NOT EXISTS source_module_key TEXT NOT NULL DEFAULT 'sample-feature';

            ALTER TABLE {AdminPersistenceDefaults.SchemaName}.{AdminPersistenceDefaults.AnnouncementsTableName}
                ADD COLUMN IF NOT EXISTS source_reference TEXT NULL;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
