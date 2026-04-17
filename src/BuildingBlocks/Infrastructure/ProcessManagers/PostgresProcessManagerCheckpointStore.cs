using System.Text.Json;
using BuildingBlocks.Application.ProcessManagers;
using BuildingBlocks.Infrastructure.Persistence;
using BuildingBlocks.Infrastructure.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace BuildingBlocks.Infrastructure.ProcessManagers;

public sealed class PostgresProcessManagerCheckpointStore : IProcessManagerCheckpointStore, IDatabaseMigration
{
    public const string DefaultTableName = "process_manager_checkpoints";

    private readonly string _connectionString;
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _schemaName;
    private readonly string _tableName;
    private readonly string _qualifiedTableName;
    private readonly JsonSerializerOptions _jsonOptions = StarterJsonSerializerOptions.Create();

    public PostgresProcessManagerCheckpointStore(
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
            ?? throw new InvalidOperationException(
                $"PostgreSQL process manager checkpoint store for module '{ModuleKey}' requires ConnectionStrings:{connectionStringName}.");
        _dataSource = dataSourceResolver.GetRequiredDataSource(connectionStringName);
    }

    public string ModuleKey { get; }

    public string Name => $"{ModuleKey}_process_manager_checkpoints";

    public string? ConnectionString => _connectionString;

    public async ValueTask<ProcessManagerCheckpoint<TState>?> LoadAsync<TState>(
        string moduleKey,
        string processManagerName,
        string processId,
        CancellationToken cancellationToken)
    {
        EnsureModuleKeyMatches(moduleKey);

        var sql = $"""
            SELECT lifecycle_state, version, updated_utc, completed_utc, failure_code, failure_message, state_type, state_json
            FROM {_qualifiedTableName}
            WHERE process_manager_name = @processManagerName
                AND process_id = @processId;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("processManagerName", processManagerName);
        command.Parameters.AddWithValue("processId", processId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var storedStateType = reader.GetString(6);
        var expectedStateType = typeof(TState).AssemblyQualifiedName ?? typeof(TState).FullName ?? typeof(TState).Name;
        if (!string.Equals(storedStateType, expectedStateType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Stored checkpoint type {storedStateType} did not match requested type {expectedStateType} for {processManagerName}/{processId}.");
        }

        var stateJson = reader.GetString(7);
        var state = JsonSerializer.Deserialize<TState>(stateJson, _jsonOptions);

        return new ProcessManagerCheckpoint<TState>(
            ModuleKey,
            processManagerName,
            processId,
            ParseLifecycleState(reader.GetString(0)),
            reader.GetInt32(1),
            NodaTime.Instant.FromDateTimeOffset(reader.GetFieldValue<DateTimeOffset>(2)),
            state!,
            reader.IsDBNull(3) ? null : NodaTime.Instant.FromDateTimeOffset(reader.GetFieldValue<DateTimeOffset>(3)),
            reader.IsDBNull(4) && reader.IsDBNull(5)
                ? null
                : new BuildingBlocks.Application.Results.Error(
                    reader.IsDBNull(4) ? "unexpected.failure" : reader.GetString(4),
                    reader.IsDBNull(5) ? "An unexpected failure occurred." : reader.GetString(5)));
    }

    public async ValueTask<IReadOnlyCollection<ProcessManagerCheckpoint<TState>>> ListAsync<TState>(
        string moduleKey,
        string processManagerName,
        IReadOnlyCollection<ProcessManagerLifecycleState> lifecycleStates,
        NodaTime.Instant? updatedBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lifecycleStates);
        EnsureModuleKeyMatches(moduleKey);

        if (limit <= 0 || lifecycleStates.Count == 0)
        {
            return [];
        }

        var normalizedStates = lifecycleStates
            .Distinct()
            .Select(FormatLifecycleState)
            .ToArray();

        var sql = updatedBefore is null
            ? $"""
                SELECT process_id, lifecycle_state, version, updated_utc, completed_utc, failure_code, failure_message, state_type, state_json
                FROM {_qualifiedTableName}
                WHERE process_manager_name = @processManagerName
                    AND lifecycle_state = ANY(@lifecycleStates)
                ORDER BY updated_utc ASC, process_id ASC
                LIMIT @limit;
                """
            : $"""
                SELECT process_id, lifecycle_state, version, updated_utc, completed_utc, failure_code, failure_message, state_type, state_json
                FROM {_qualifiedTableName}
                WHERE process_manager_name = @processManagerName
                    AND lifecycle_state = ANY(@lifecycleStates)
                    AND updated_utc <= @updatedBefore
                ORDER BY updated_utc ASC, process_id ASC
                LIMIT @limit;
                """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("processManagerName", processManagerName);
        command.Parameters.AddWithValue("lifecycleStates", normalizedStates);
        command.Parameters.AddWithValue("limit", limit);

        if (updatedBefore is not null)
        {
            command.Parameters.AddWithValue("updatedBefore", updatedBefore.Value.ToDateTimeOffset());
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var expectedStateType = typeof(TState).AssemblyQualifiedName ?? typeof(TState).FullName ?? typeof(TState).Name;
        var checkpoints = new List<ProcessManagerCheckpoint<TState>>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var storedStateType = reader.GetString(7);
            if (!string.Equals(storedStateType, expectedStateType, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Stored checkpoint type {storedStateType} did not match requested type {expectedStateType} for {processManagerName}.");
            }

            var state = JsonSerializer.Deserialize<TState>(reader.GetString(8), _jsonOptions);
            checkpoints.Add(new ProcessManagerCheckpoint<TState>(
                ModuleKey,
                processManagerName,
                reader.GetString(0),
                ParseLifecycleState(reader.GetString(1)),
                reader.GetInt32(2),
                NodaTime.Instant.FromDateTimeOffset(reader.GetFieldValue<DateTimeOffset>(3)),
                state!,
                reader.IsDBNull(4) ? null : NodaTime.Instant.FromDateTimeOffset(reader.GetFieldValue<DateTimeOffset>(4)),
                reader.IsDBNull(5) && reader.IsDBNull(6)
                    ? null
                    : new BuildingBlocks.Application.Results.Error(
                        reader.IsDBNull(5) ? "unexpected.failure" : reader.GetString(5),
                        reader.IsDBNull(6) ? "An unexpected failure occurred." : reader.GetString(6))));
        }

        return checkpoints;
    }

    public async ValueTask<ProcessManagerCheckpoint<TState>> SaveAsync<TState>(
        ProcessManagerCheckpoint<TState> checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        EnsureModuleKeyMatches(checkpoint.ModuleKey);

        var stateType = typeof(TState).AssemblyQualifiedName ?? typeof(TState).FullName ?? typeof(TState).Name;
        var stateJson = JsonSerializer.Serialize(checkpoint.State, _jsonOptions);
        var lifecycleState = FormatLifecycleState(checkpoint.LifecycleState);
        var newVersion = checkpoint.Version + 1;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        if (checkpoint.Version == 0)
        {
            var insertSql = $"""
                INSERT INTO {_qualifiedTableName}
                    (process_manager_name, process_id, lifecycle_state, version, updated_utc, completed_utc, failure_code, failure_message, state_type, state_json)
                VALUES
                    (@processManagerName, @processId, @lifecycleState, 1, @updatedUtc, @completedUtc, @failureCode, @failureMessage, @stateType, @stateJson)
                ON CONFLICT (process_manager_name, process_id) DO NOTHING;
                """;

            await using var insertCommand = new NpgsqlCommand(insertSql, connection);
            PopulateParameters(insertCommand, checkpoint, lifecycleState, stateType, stateJson);

            var insertedRows = await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            if (insertedRows == 1)
            {
                return checkpoint with { ModuleKey = ModuleKey, Version = 1 };
            }
        }

        var updateSql = $"""
            UPDATE {_qualifiedTableName}
            SET lifecycle_state = @lifecycleState,
                version = @newVersion,
                updated_utc = @updatedUtc,
                completed_utc = @completedUtc,
                failure_code = @failureCode,
                failure_message = @failureMessage,
                state_type = @stateType,
                state_json = @stateJson
            WHERE process_manager_name = @processManagerName
                AND process_id = @processId
                AND version = @expectedVersion;
            """;

        await using var updateCommand = new NpgsqlCommand(updateSql, connection);
        PopulateParameters(updateCommand, checkpoint, lifecycleState, stateType, stateJson);
        updateCommand.Parameters.AddWithValue("expectedVersion", checkpoint.Version);
        updateCommand.Parameters.AddWithValue("newVersion", newVersion);

        var updatedRows = await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        if (updatedRows == 1)
        {
            return checkpoint with { ModuleKey = ModuleKey, Version = newVersion };
        }

        throw new InvalidOperationException(
            $"A concurrent process-manager checkpoint update was detected for {checkpoint.ProcessManagerName}/{checkpoint.ProcessId}.");
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
                process_manager_name TEXT NOT NULL,
                process_id TEXT NOT NULL,
                lifecycle_state TEXT NOT NULL,
                version INTEGER NOT NULL,
                updated_utc TIMESTAMPTZ NOT NULL,
                completed_utc TIMESTAMPTZ NULL,
                failure_code TEXT NULL,
                failure_message TEXT NULL,
                state_type TEXT NOT NULL,
                state_json JSONB NOT NULL,
                PRIMARY KEY (process_manager_name, process_id)
            );

            CREATE INDEX IF NOT EXISTS ix_{_schemaName}_process_manager_checkpoints_state
                ON {_qualifiedTableName} (lifecycle_state, updated_utc);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void EnsureModuleKeyMatches(string moduleKey)
    {
        if (!string.Equals(ModuleKey, NormalizeModuleKey(moduleKey), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The process manager checkpoint store for module '{ModuleKey}' cannot serve module '{moduleKey}'.");
        }
    }

    private static void PopulateParameters<TState>(
        NpgsqlCommand command,
        ProcessManagerCheckpoint<TState> checkpoint,
        string lifecycleState,
        string stateType,
        string stateJson)
    {
        command.Parameters.AddWithValue("processManagerName", checkpoint.ProcessManagerName);
        command.Parameters.AddWithValue("processId", checkpoint.ProcessId);
        command.Parameters.AddWithValue("lifecycleState", lifecycleState);
        command.Parameters.AddWithValue("updatedUtc", checkpoint.UpdatedAt.ToDateTimeOffset());
        command.Parameters.AddWithValue("completedUtc", checkpoint.CompletedAt?.ToDateTimeOffset() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("failureCode", checkpoint.Failure?.Code ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("failureMessage", checkpoint.Failure?.Message ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("stateType", stateType);
        command.Parameters.Add(new NpgsqlParameter("stateJson", NpgsqlDbType.Jsonb) { Value = stateJson });
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

    private static string FormatLifecycleState(ProcessManagerLifecycleState lifecycleState)
    {
        return lifecycleState switch
        {
            ProcessManagerLifecycleState.Running => "running",
            ProcessManagerLifecycleState.Completed => "completed",
            ProcessManagerLifecycleState.Failed => "failed",
            ProcessManagerLifecycleState.Compensating => "compensating",
            ProcessManagerLifecycleState.Compensated => "compensated",
            _ => throw new InvalidOperationException($"Unsupported process-manager lifecycle state '{lifecycleState}'.")
        };
    }

    private static ProcessManagerLifecycleState ParseLifecycleState(string lifecycleState)
    {
        return lifecycleState switch
        {
            "running" => ProcessManagerLifecycleState.Running,
            "completed" => ProcessManagerLifecycleState.Completed,
            "failed" => ProcessManagerLifecycleState.Failed,
            "compensating" => ProcessManagerLifecycleState.Compensating,
            "compensated" => ProcessManagerLifecycleState.Compensated,
            _ => throw new InvalidOperationException($"Unsupported process-manager lifecycle state '{lifecycleState}'.")
        };
    }
}
