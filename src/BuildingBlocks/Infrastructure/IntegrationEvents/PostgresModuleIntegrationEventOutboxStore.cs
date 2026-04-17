using System.Text.Json;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Domain.Events;
using BuildingBlocks.Infrastructure.Persistence;
using BuildingBlocks.Infrastructure.Serialization;
using Npgsql;
using NpgsqlTypes;
using NodaTime;

namespace BuildingBlocks.Infrastructure.IntegrationEvents;

public sealed class PostgresModuleIntegrationEventOutboxStore : IIntegrationEventOutboxStore, IDatabaseMigration
{
    public const string DefaultTableName = "integration_outbox";

    private readonly string _connectionString;
    private readonly NpgsqlDataSource _dataSource;
    private readonly JsonSerializerOptions _jsonOptions = StarterJsonSerializerOptions.Create();
    private readonly string _qualifiedTableName;
    private readonly string _schemaName;
    private readonly string _tableName;

    public PostgresModuleIntegrationEventOutboxStore(
        IPostgresDataSourceResolver dataSourceResolver,
        string moduleKey,
        string schemaName,
        string tableName = DefaultTableName,
        string connectionStringName = SharedRuntimePersistenceDefaults.ConnectionStringName)
    {
        ArgumentNullException.ThrowIfNull(dataSourceResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);

        ModuleKey = NormalizeModuleKey(moduleKey);
        _schemaName = schemaName.Trim();
        _tableName = tableName.Trim();
        _qualifiedTableName = $"{QuoteIdentifier(_schemaName)}.{QuoteIdentifier(_tableName)}";
        _connectionString = dataSourceResolver.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"PostgreSQL integration-event outbox for module '{ModuleKey}' requires ConnectionStrings:{connectionStringName}.");
        _dataSource = dataSourceResolver.GetRequiredDataSource(connectionStringName);
    }

    public string ModuleKey { get; }

    public string Name => $"{ModuleKey}_integration_outbox";

    public string? ConnectionString => _connectionString;

    public async ValueTask EnqueueAsync(IntegrationEventOutboxPublishRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.IntegrationEvent);

        if (!string.Equals(ModuleKey, NormalizeModuleKey(request.ModuleKey), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The outbox for module '{ModuleKey}' cannot publish events for '{request.ModuleKey}'.");
        }

        var headers = request.Headers is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(request.Headers, StringComparer.Ordinal);

        var eventType = request.IntegrationEvent.GetType();
        var payloadJson = JsonSerializer.Serialize(request.IntegrationEvent, eventType, _jsonOptions);
        var headersJson = JsonSerializer.Serialize(headers, _jsonOptions);

        var sql = $$"""
            INSERT INTO {{_qualifiedTableName}}
                (event_id, event_type, event_version, payload, headers, occurred_utc, available_utc, attempts, leased_until_utc, processed_utc, dead_lettered_utc, last_error_code, last_error_message)
            VALUES
                (@eventId, @eventType, @eventVersion, @payload, @headers, @occurredUtc, @availableUtc, 0, NULL, NULL, NULL, NULL, NULL)
            ON CONFLICT (event_id) DO NOTHING;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("eventId", request.IntegrationEvent.EventId);
        command.Parameters.AddWithValue("eventType", IntegrationEventTypeNames.GetName(eventType));
        command.Parameters.AddWithValue("eventVersion", ResolveEventVersion(eventType));
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payloadJson });
        command.Parameters.Add(new NpgsqlParameter("headers", NpgsqlDbType.Jsonb) { Value = headersJson });
        command.Parameters.AddWithValue("occurredUtc", request.IntegrationEvent.OccurredAt.ToDateTimeOffset());
        command.Parameters.AddWithValue("availableUtc", (request.AvailableAt ?? request.IntegrationEvent.OccurredAt).ToDateTimeOffset());
        await command.ExecuteNonQueryAsync(cancellationToken);

        try
        {
            await IntegrationEventOutboxSignal.NotifyAsync(connection, cancellationToken);
        }
        catch
        {
            // Signal failure should not fail the enqueue operation.
        }
    }

    public async ValueTask<IReadOnlyCollection<IntegrationEventOutboxLeasedMessage>> LeaseAvailableAsync(
        int batchSize,
        Instant now,
        Instant leaseUntil,
        CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Outbox batch size must be greater than zero.");
        }

        var sql = $$"""
            WITH candidate AS (
                SELECT id
                FROM {{_qualifiedTableName}}
                WHERE processed_utc IS NULL
                    AND dead_lettered_utc IS NULL
                    AND available_utc <= @now
                    AND (leased_until_utc IS NULL OR leased_until_utc < @now)
                ORDER BY available_utc, id
                FOR UPDATE SKIP LOCKED
                LIMIT @batchSize
            )
            UPDATE {{_qualifiedTableName}} AS outbox
            SET leased_until_utc = @leaseUntil,
                attempts = outbox.attempts + 1
            FROM candidate
            WHERE outbox.id = candidate.id
            RETURNING outbox.id,
                outbox.event_id,
                outbox.event_type,
                outbox.event_version,
                outbox.payload::text,
                outbox.headers::text,
                outbox.occurred_utc,
                outbox.available_utc,
                outbox.attempts;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("now", now.ToDateTimeOffset());
        command.Parameters.AddWithValue("leaseUntil", leaseUntil.ToDateTimeOffset());
        command.Parameters.AddWithValue("batchSize", batchSize);

        var leased = new List<IntegrationEventOutboxLeasedMessage>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var headersJson = reader.GetString(reader.GetOrdinal("headers"));
            var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson, _jsonOptions)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);

            leased.Add(new IntegrationEventOutboxLeasedMessage(
                reader.GetInt64(reader.GetOrdinal("id")),
                ModuleKey,
                reader.GetGuid(reader.GetOrdinal("event_id")),
                reader.GetString(reader.GetOrdinal("event_type")),
                reader.GetInt32(reader.GetOrdinal("event_version")),
                reader.GetString(reader.GetOrdinal("payload")),
                headers,
                Instant.FromDateTimeOffset(reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("occurred_utc"))),
                Instant.FromDateTimeOffset(reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("available_utc"))),
                reader.GetInt32(reader.GetOrdinal("attempts"))));
        }

        return leased;
    }

    public async ValueTask MarkDispatchedAsync(long messageId, Instant processedAt, CancellationToken cancellationToken)
    {
        var sql = $$"""
            UPDATE {{_qualifiedTableName}}
            SET processed_utc = @processedAt,
                leased_until_utc = NULL,
                last_error_code = NULL,
                last_error_message = NULL
            WHERE id = @messageId;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("messageId", messageId);
        command.Parameters.AddWithValue("processedAt", processedAt.ToDateTimeOffset());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask MarkFailedAsync(
        long messageId,
        Instant failedAt,
        Instant nextAvailableAt,
        bool deadLettered,
        Error error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(error);

        var sql = $$"""
            UPDATE {{_qualifiedTableName}}
            SET leased_until_utc = NULL,
                available_utc = @availableUtc,
                dead_lettered_utc = @deadLetteredUtc,
                last_error_code = @errorCode,
                last_error_message = @errorMessage
            WHERE id = @messageId;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("messageId", messageId);
        command.Parameters.AddWithValue("availableUtc", nextAvailableAt.ToDateTimeOffset());
        command.Parameters.AddWithValue("deadLetteredUtc", deadLettered ? failedAt.ToDateTimeOffset() : (object)DBNull.Value);
        command.Parameters.AddWithValue("errorCode", error.Code);
        command.Parameters.AddWithValue("errorMessage", error.Message);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<IntegrationEventOutboxDeadLetterEntry>> GetDeadLetteredAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var sql = $$"""
            SELECT event_id, event_type, dead_lettered_utc, attempts, last_error_code, last_error_message
            FROM {{_qualifiedTableName}}
            WHERE dead_lettered_utc IS NOT NULL
            ORDER BY dead_lettered_utc DESC
            LIMIT @limit;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("limit", limit);

        var entries = new List<IntegrationEventOutboxDeadLetterEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var errorCodeOrdinal = reader.GetOrdinal("last_error_code");
            var errorMessageOrdinal = reader.GetOrdinal("last_error_message");

            entries.Add(new IntegrationEventOutboxDeadLetterEntry(
                reader.GetGuid(reader.GetOrdinal("event_id")),
                reader.GetString(reader.GetOrdinal("event_type")),
                ModuleKey,
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("dead_lettered_utc")),
                reader.GetInt32(reader.GetOrdinal("attempts")),
                reader.IsDBNull(errorCodeOrdinal) ? null : reader.GetString(errorCodeOrdinal),
                reader.IsDBNull(errorMessageOrdinal) ? null : reader.GetString(errorMessageOrdinal)));
        }

        return entries;
    }

    public async Task<IReadOnlyCollection<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        var pending = new List<string>();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        if (!await TableExistsAsync(connection, cancellationToken))
        {
            pending.Add($"{_schemaName}.{_tableName}");
        }

        return pending;
    }

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        var sql = $$"""
            CREATE SCHEMA IF NOT EXISTS {{QuoteIdentifier(_schemaName)}};

            CREATE TABLE IF NOT EXISTS {{_qualifiedTableName}}
            (
                id BIGSERIAL PRIMARY KEY,
                event_id UUID NOT NULL,
                event_type TEXT NOT NULL,
                event_version INTEGER NOT NULL,
                payload JSONB NOT NULL,
                headers JSONB NOT NULL,
                occurred_utc TIMESTAMPTZ NOT NULL,
                available_utc TIMESTAMPTZ NOT NULL,
                attempts INTEGER NOT NULL,
                leased_until_utc TIMESTAMPTZ NULL,
                processed_utc TIMESTAMPTZ NULL,
                dead_lettered_utc TIMESTAMPTZ NULL,
                last_error_code TEXT NULL,
                last_error_message TEXT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS {{QuoteIdentifier($"ix_{_tableName}_event_id")}}
                ON {{_qualifiedTableName}} (event_id);

            CREATE INDEX IF NOT EXISTS {{QuoteIdentifier($"ix_{_tableName}_dispatch_queue")}}
                ON {{_qualifiedTableName}} (processed_utc, dead_lettered_utc, available_utc, leased_until_utc, id);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string NormalizeModuleKey(string moduleKey)
    {
        return string.IsNullOrWhiteSpace(moduleKey)
            ? string.Empty
            : moduleKey.Trim();
    }

    private static int ResolveEventVersion(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        var name = eventType.Name;
        var markerIndex = name.LastIndexOf('V');
        if (markerIndex >= 0 && markerIndex < name.Length - 1 && int.TryParse(name[(markerIndex + 1)..], out var parsedVersion))
        {
            return parsedVersion;
        }

        return 1;
    }

    private async Task<bool> TableExistsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schemaName
                    AND table_name = @tableName);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schemaName", _schemaName);
        command.Parameters.AddWithValue("tableName", _tableName);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

}
