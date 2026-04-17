using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Infrastructure.Persistence;
using NodaTime;
using Npgsql;

namespace BuildingBlocks.Infrastructure.IntegrationEvents;

public sealed class PostgresIntegrationEventInboxStore : IIntegrationEventInboxStore, IDatabaseMigration
{
    public const string DefaultTableName = "integration_event_inbox";
    public const int DefaultDeadLetterThreshold = 10;

    private readonly string _connectionString;
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _schemaName;
    private readonly string _tableName;
    private readonly string _qualifiedTableName;
    private readonly int _deadLetterThreshold;

    public PostgresIntegrationEventInboxStore(
        IPostgresDataSourceResolver dataSourceResolver,
        string moduleKey,
        string schemaName,
        string tableName = DefaultTableName,
        string connectionStringName = SharedRuntimePersistenceDefaults.ConnectionStringName,
        int deadLetterThreshold = DefaultDeadLetterThreshold)
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
        _deadLetterThreshold = deadLetterThreshold;
        _connectionString = dataSourceResolver.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException(
                $"PostgreSQL integration event inbox for module '{ModuleKey}' requires ConnectionStrings:{connectionStringName}.");
        _dataSource = dataSourceResolver.GetRequiredDataSource(connectionStringName);
    }

    public string ModuleKey { get; }

    public string Name => $"{ModuleKey}_integration_event_inbox";

    public string? ConnectionString => _connectionString;

    public async ValueTask<IntegrationEventDeliveryDecision> BeginAsync(
        IntegrationEventDeliveryContext context,
        Instant attemptedAt,
        CancellationToken cancellationToken)
    {
        EnsureContextBelongsToThisModule(context);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var insertSql = $"""
            INSERT INTO {_qualifiedTableName}
                (consumer_name, event_id, event_type, occurred_utc, status, attempts, last_attempt_utc, completed_utc, last_error_code, last_error_message)
            VALUES
                (@consumerName, @eventId, @eventType, @occurredUtc, 'running', 1, @attemptedAt, NULL, NULL, NULL)
            ON CONFLICT (consumer_name, event_id) DO NOTHING;
            """;

        await using (var insertCommand = new NpgsqlCommand(insertSql, connection))
        {
            insertCommand.Parameters.AddWithValue("consumerName", context.ConsumerName.Value);
            insertCommand.Parameters.AddWithValue("eventId", context.EventId);
            insertCommand.Parameters.AddWithValue("eventType", context.EventType);
            insertCommand.Parameters.AddWithValue("occurredUtc", context.OccurredAt.ToDateTimeOffset());
            insertCommand.Parameters.AddWithValue("attemptedAt", attemptedAt.ToDateTimeOffset());

            var insertedRows = await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            if (insertedRows == 1)
            {
                return IntegrationEventDeliveryDecision.Process;
            }
        }

        while (true)
        {
            var readSql = $"""
                SELECT status, attempts
                FROM {_qualifiedTableName}
                WHERE consumer_name = @consumerName
                    AND event_id = @eventId;
                """;

            int currentAttempts;
            await using (var readCommand = new NpgsqlCommand(readSql, connection))
            {
                readCommand.Parameters.AddWithValue("consumerName", context.ConsumerName.Value);
                readCommand.Parameters.AddWithValue("eventId", context.EventId);

                await using var reader = await readCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException(
                        $"Could not locate inbox state for integration event {context.EventId} and consumer {context.ConsumerName.Value}.");
                }

                var status = reader.GetString(0);
                currentAttempts = reader.GetInt32(1);

                if (string.Equals(status, "completed", StringComparison.Ordinal))
                {
                    return IntegrationEventDeliveryDecision.SkipCompleted;
                }

                if (string.Equals(status, "dead_lettered", StringComparison.Ordinal))
                {
                    return IntegrationEventDeliveryDecision.SkipDeadLettered;
                }

                if (string.Equals(status, "running", StringComparison.Ordinal))
                {
                    return IntegrationEventDeliveryDecision.SkipInFlight;
                }

                if (!string.Equals(status, "failed", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Unsupported inbox delivery status '{status}' for consumer {context.ConsumerName.Value}.");
                }
            }

            if (currentAttempts >= _deadLetterThreshold)
            {
                var deadLetterSql = $"""
                    UPDATE {_qualifiedTableName}
                    SET status = 'dead_lettered',
                        dead_lettered_utc = @deadLetteredUtc
                    WHERE consumer_name = @consumerName
                        AND event_id = @eventId
                        AND status = 'failed';
                    """;

                await using var deadLetterCommand = new NpgsqlCommand(deadLetterSql, connection);
                deadLetterCommand.Parameters.AddWithValue("consumerName", context.ConsumerName.Value);
                deadLetterCommand.Parameters.AddWithValue("eventId", context.EventId);
                deadLetterCommand.Parameters.AddWithValue("deadLetteredUtc", attemptedAt.ToDateTimeOffset());
                await deadLetterCommand.ExecuteNonQueryAsync(cancellationToken);

                return IntegrationEventDeliveryDecision.SkipDeadLettered;
            }

            var retrySql = $"""
                UPDATE {_qualifiedTableName}
                SET status = 'running',
                    attempts = attempts + 1,
                    last_attempt_utc = @attemptedAt,
                    last_error_code = NULL,
                    last_error_message = NULL,
                    completed_utc = NULL,
                    event_type = @eventType,
                    occurred_utc = @occurredUtc
                WHERE consumer_name = @consumerName
                    AND event_id = @eventId
                    AND status = 'failed';
                """;

            await using var retryCommand = new NpgsqlCommand(retrySql, connection);
            retryCommand.Parameters.AddWithValue("consumerName", context.ConsumerName.Value);
            retryCommand.Parameters.AddWithValue("eventId", context.EventId);
            retryCommand.Parameters.AddWithValue("attemptedAt", attemptedAt.ToDateTimeOffset());
            retryCommand.Parameters.AddWithValue("eventType", context.EventType);
            retryCommand.Parameters.AddWithValue("occurredUtc", context.OccurredAt.ToDateTimeOffset());

            var updatedRows = await retryCommand.ExecuteNonQueryAsync(cancellationToken);
            if (updatedRows == 1)
            {
                return IntegrationEventDeliveryDecision.Process;
            }
        }
    }

    public async ValueTask MarkSucceededAsync(
        IntegrationEventDeliveryContext context,
        Instant completedAt,
        CancellationToken cancellationToken)
    {
        EnsureContextBelongsToThisModule(context);

        var sql = $"""
            UPDATE {_qualifiedTableName}
            SET status = 'completed',
                completed_utc = @completedAt,
                last_attempt_utc = @completedAt,
                last_error_code = NULL,
                last_error_message = NULL
            WHERE consumer_name = @consumerName
                AND event_id = @eventId;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("consumerName", context.ConsumerName.Value);
        command.Parameters.AddWithValue("eventId", context.EventId);
        command.Parameters.AddWithValue("completedAt", completedAt.ToDateTimeOffset());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask MarkFailedAsync(
        IntegrationEventDeliveryContext context,
        Instant failedAt,
        Error error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(error);
        EnsureContextBelongsToThisModule(context);

        var sql = $"""
            UPDATE {_qualifiedTableName}
            SET status = 'failed',
                last_attempt_utc = @failedAt,
                completed_utc = NULL,
                last_error_code = @errorCode,
                last_error_message = @errorMessage
            WHERE consumer_name = @consumerName
                AND event_id = @eventId;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("consumerName", context.ConsumerName.Value);
        command.Parameters.AddWithValue("eventId", context.EventId);
        command.Parameters.AddWithValue("failedAt", failedAt.ToDateTimeOffset());
        command.Parameters.AddWithValue("errorCode", error.Code);
        command.Parameters.AddWithValue("errorMessage", error.Message);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        const string tableSql = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schemaName
                    AND table_name = @tableName);
            """;

        await using var command = new NpgsqlCommand(tableSql, connection);
        command.Parameters.AddWithValue("schemaName", _schemaName);
        command.Parameters.AddWithValue("tableName", _tableName);

        var exists = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        return exists
            ? Array.Empty<string>()
            : new[] { $"{_schemaName}.{_tableName}" };
    }

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        var sql = $"""
            CREATE SCHEMA IF NOT EXISTS {QuoteIdentifier(_schemaName)};

            CREATE TABLE IF NOT EXISTS {_qualifiedTableName}
            (
                consumer_name TEXT NOT NULL,
                event_id UUID NOT NULL,
                event_type TEXT NOT NULL,
                occurred_utc TIMESTAMPTZ NOT NULL,
                status TEXT NOT NULL,
                attempts INTEGER NOT NULL,
                last_attempt_utc TIMESTAMPTZ NOT NULL,
                completed_utc TIMESTAMPTZ NULL,
                last_error_code TEXT NULL,
                last_error_message TEXT NULL,
                dead_lettered_utc TIMESTAMPTZ NULL,
                PRIMARY KEY (consumer_name, event_id)
            );

            ALTER TABLE {_qualifiedTableName}
                ADD COLUMN IF NOT EXISTS dead_lettered_utc TIMESTAMPTZ NULL;

            CREATE INDEX IF NOT EXISTS ix_{_schemaName}_integration_event_inbox_status
                ON {_qualifiedTableName} (status, last_attempt_utc);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void EnsureContextBelongsToThisModule(IntegrationEventDeliveryContext context)
    {
        if (!string.Equals(ModuleKey, NormalizeModuleKey(context.ModuleKey), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The integration event inbox for module '{ModuleKey}' cannot track deliveries for module '{context.ModuleKey}'.");
        }
    }

    private static string NormalizeModuleKey(string moduleKey)
    {
        return string.IsNullOrWhiteSpace(moduleKey)
            ? string.Empty
            : moduleKey.Trim();
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
