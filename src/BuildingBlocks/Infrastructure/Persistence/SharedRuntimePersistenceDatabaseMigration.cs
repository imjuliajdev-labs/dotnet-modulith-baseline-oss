using Npgsql;

namespace BuildingBlocks.Infrastructure.Persistence;

public sealed class SharedRuntimePersistenceDatabaseMigration : IDatabaseMigration
{
    private readonly string? _connectionString;
    private readonly NpgsqlDataSource? _dataSource;

    public SharedRuntimePersistenceDatabaseMigration(IPostgresDataSourceResolver dataSourceResolver)
    {
        ArgumentNullException.ThrowIfNull(dataSourceResolver);

        _connectionString = dataSourceResolver.GetConnectionString(SharedRuntimePersistenceDefaults.ConnectionStringName);
        _dataSource = string.IsNullOrWhiteSpace(_connectionString)
            ? null
            : dataSourceResolver.GetRequiredDataSource(SharedRuntimePersistenceDefaults.ConnectionStringName);
    }

    public string Name => "shared_runtime";

    public string? ConnectionString => _connectionString;

    public async Task<IReadOnlyCollection<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return Array.Empty<string>();
        }

        var pending = new List<string>();

        await using var connection = await _dataSource!.OpenConnectionAsync(cancellationToken);

        if (!await TableExistsAsync(connection, SharedRuntimePersistenceDefaults.CommandIdempotencyTableName, cancellationToken))
        {
            pending.Add($"{SharedRuntimePersistenceDefaults.SchemaName}.{SharedRuntimePersistenceDefaults.CommandIdempotencyTableName}");
        }

        if (!await TableExistsAsync(connection, SharedRuntimePersistenceDefaults.DataProtectionKeysTableName, cancellationToken))
        {
            pending.Add($"{SharedRuntimePersistenceDefaults.SchemaName}.{SharedRuntimePersistenceDefaults.DataProtectionKeysTableName}");
        }

        return pending;
    }

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        var sql = $$"""
            CREATE SCHEMA IF NOT EXISTS {{SharedRuntimePersistenceDefaults.SchemaName}};

            CREATE TABLE IF NOT EXISTS {{SharedRuntimePersistenceDefaults.SchemaName}}.{{SharedRuntimePersistenceDefaults.CommandIdempotencyTableName}}
            (
                command_type TEXT NOT NULL,
                module_key TEXT NOT NULL,
                caller_identity TEXT NOT NULL,
                request_key TEXT NOT NULL,
                request_hash TEXT NOT NULL,
                response_type TEXT NOT NULL,
                status TEXT NOT NULL,
                response_payload TEXT NULL,
                created_utc TIMESTAMPTZ NOT NULL,
                updated_utc TIMESTAMPTZ NOT NULL,
                completed_utc TIMESTAMPTZ NULL,
                expires_utc TIMESTAMPTZ NOT NULL,
                PRIMARY KEY (command_type, module_key, caller_identity, request_key)
            );

            CREATE INDEX IF NOT EXISTS ix_command_idempotency_expires_utc
                ON {{SharedRuntimePersistenceDefaults.SchemaName}}.{{SharedRuntimePersistenceDefaults.CommandIdempotencyTableName}} (expires_utc);

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = 'public'
                        AND table_name = '{{SharedRuntimePersistenceDefaults.CommandIdempotencyTableName}}') THEN
                    INSERT INTO {{SharedRuntimePersistenceDefaults.SchemaName}}.{{SharedRuntimePersistenceDefaults.CommandIdempotencyTableName}}
                        (command_type, module_key, caller_identity, request_key, request_hash, response_type, status, response_payload, created_utc, updated_utc, completed_utc, expires_utc)
                    SELECT legacy.command_type,
                        legacy.module_key,
                        '',
                        legacy.request_key,
                        legacy.request_hash,
                        legacy.response_type,
                        CASE WHEN legacy.is_completed THEN 'completed' ELSE 'abandoned' END,
                        legacy.response_payload,
                        legacy.created_utc,
                        legacy.updated_utc,
                        CASE WHEN legacy.is_completed THEN legacy.updated_utc ELSE NULL END,
                        CASE
                            WHEN legacy.is_completed THEN legacy.updated_utc + INTERVAL '24 hours'
                            ELSE legacy.updated_utc + INTERVAL '15 minutes'
                        END
                    FROM public.{{SharedRuntimePersistenceDefaults.CommandIdempotencyTableName}} AS legacy
                    ON CONFLICT (command_type, module_key, caller_identity, request_key) DO NOTHING;
                END IF;
            END $$;

            CREATE TABLE IF NOT EXISTS {{SharedRuntimePersistenceDefaults.SchemaName}}.{{SharedRuntimePersistenceDefaults.DataProtectionKeysTableName}}
            (
                id BIGSERIAL PRIMARY KEY,
                friendly_name TEXT NULL,
                xml TEXT NOT NULL,
                created_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE INDEX IF NOT EXISTS ix_data_protection_keys_friendly_name
                ON {{SharedRuntimePersistenceDefaults.SchemaName}}.{{SharedRuntimePersistenceDefaults.DataProtectionKeysTableName}} (friendly_name);
            """;

        await using var connection = await _dataSource!.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schemaName
                    AND table_name = @tableName);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schemaName", SharedRuntimePersistenceDefaults.SchemaName);
        command.Parameters.AddWithValue("tableName", tableName);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }
}
