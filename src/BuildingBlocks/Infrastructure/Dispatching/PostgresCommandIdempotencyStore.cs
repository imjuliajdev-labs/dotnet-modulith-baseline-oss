using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Text.Json;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Infrastructure.Persistence;
using BuildingBlocks.Infrastructure.Serialization;
using Npgsql;

namespace BuildingBlocks.Infrastructure.Dispatching;

public sealed class PostgresCommandIdempotencyStore : ICommandIdempotencyStore
{
    private static readonly JsonSerializerOptions JsonOptions = StarterJsonSerializerOptions.Create();

    private static readonly ConcurrentDictionary<Type, Func<object, (bool IsSuccess, Error Error, object? Value)>> ResultReaders = new();
    private static readonly ConcurrentDictionary<Type, Func<object, object>> SuccessFactories = new();
    private static readonly ConcurrentDictionary<Type, Func<Error, object>> FailureFactories = new();

    private readonly NpgsqlDataSource _dataSource;
    private readonly CommandIdempotencyRetentionOptions _options;

    public PostgresCommandIdempotencyStore(
        IPostgresDataSourceResolver dataSourceResolver,
        CommandIdempotencyRetentionOptions options)
    {
        ArgumentNullException.ThrowIfNull(dataSourceResolver);

        _dataSource = dataSourceResolver.GetRequiredDataSource(SharedRuntimePersistenceDefaults.ConnectionStringName);
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<CommandIdempotencyAcquireResult> BeginAsync(
        Type responseType,
        CommandIdempotencyContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(responseType);

        var moduleKey = NormalizeModuleKey(context.ModuleKey);
        var callerIdentity = NormalizeCallerIdentity(context.CallerIdentity);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await DeleteExpiredEntryAsync(connection, context, moduleKey, callerIdentity, cancellationToken);

        const string insertSql = $$"""
            INSERT INTO {{SharedRuntimePersistenceDefaults.SchemaName}}.{{SharedRuntimePersistenceDefaults.CommandIdempotencyTableName}}
                (command_type, module_key, caller_identity, request_key, request_hash, response_type, status, response_payload, created_utc, updated_utc, completed_utc, expires_utc)
            VALUES
                (@commandType, @moduleKey, @callerIdentity, @requestKey, @requestHash, @responseType, 'running', NULL, NOW(), NOW(), NULL, NOW() + @runningEntryTtl)
            ON CONFLICT (command_type, module_key, caller_identity, request_key) DO NOTHING;
            """;

        await using (var insertCommand = new NpgsqlCommand(insertSql, connection))
        {
            PopulateIdentityParameters(insertCommand, context, moduleKey, callerIdentity);
            insertCommand.Parameters.AddWithValue("responseType", responseType.AssemblyQualifiedName ?? responseType.FullName ?? responseType.Name);
            insertCommand.Parameters.AddWithValue("runningEntryTtl", _options.RunningEntryTtl);

            var insertedRows = await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            if (insertedRows == 1)
            {
                return CommandIdempotencyAcquireResult.Acquired(new PostgresCommandIdempotencyExecution(
                    _dataSource,
                    context,
                    moduleKey,
                    callerIdentity,
                    _options));
            }
        }

        const string readSql = $$"""
            SELECT request_hash, response_type, status, response_payload
            FROM {{SharedRuntimePersistenceDefaults.SchemaName}}.{{SharedRuntimePersistenceDefaults.CommandIdempotencyTableName}}
            WHERE command_type = @commandType
                AND module_key = @moduleKey
                AND caller_identity = @callerIdentity
                AND request_key = @requestKey;
            """;

        await using var readCommand = new NpgsqlCommand(readSql, connection);
        PopulateIdentityParameters(readCommand, context, moduleKey, callerIdentity);

        await using var reader = await readCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return CommandIdempotencyAcquireResult.Reject(CommandIdempotencyErrors.RequestInFlight(context.CommandType, context.RequestKey));
        }

        var storedRequestHash = reader.GetString(0);
        if (!string.Equals(storedRequestHash, context.RequestHash, StringComparison.Ordinal))
        {
            return CommandIdempotencyAcquireResult.Reject(CommandIdempotencyErrors.RequestConflict(context.CommandType, context.RequestKey));
        }

        var storedResponseType = reader.GetString(1);
        var status = reader.GetString(2);

        if (string.Equals(status, IdempotencyStatuses.Completed, StringComparison.Ordinal))
        {
            if (reader.IsDBNull(3))
            {
                throw new InvalidOperationException($"Stored idempotent response for {context.CommandType.FullName} was completed without a payload.");
            }

            var expectedResponseType = responseType.AssemblyQualifiedName ?? responseType.FullName ?? responseType.Name;
            if (!string.Equals(storedResponseType, expectedResponseType, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Stored idempotent response type {storedResponseType} did not match requested response type {expectedResponseType}.");
            }

            var payload = reader.GetString(3);
            return CommandIdempotencyAcquireResult.Replay(DeserializeResponse(responseType, payload));
        }

        if (string.Equals(status, IdempotencyStatuses.Abandoned, StringComparison.Ordinal))
        {
            reader.Close();

            const string reacquireSql = $$"""
                UPDATE {{SharedRuntimePersistenceDefaults.SchemaName}}.{{SharedRuntimePersistenceDefaults.CommandIdempotencyTableName}}
                SET status = 'running',
                    response_payload = NULL,
                    updated_utc = NOW(),
                    completed_utc = NULL,
                    expires_utc = NOW() + @runningEntryTtl
                WHERE command_type = @commandType
                    AND module_key = @moduleKey
                    AND caller_identity = @callerIdentity
                    AND request_key = @requestKey
                    AND request_hash = @requestHash
                    AND status = 'abandoned';
                """;

            await using var reacquireCommand = new NpgsqlCommand(reacquireSql, connection);
            PopulateIdentityParameters(reacquireCommand, context, moduleKey, callerIdentity);
            reacquireCommand.Parameters.AddWithValue("runningEntryTtl", _options.RunningEntryTtl);

            var reacquiredRows = await reacquireCommand.ExecuteNonQueryAsync(cancellationToken);
            if (reacquiredRows == 1)
            {
                return CommandIdempotencyAcquireResult.Acquired(new PostgresCommandIdempotencyExecution(
                    _dataSource,
                    context,
                    moduleKey,
                    callerIdentity,
                    _options));
            }
        }

        if (!string.Equals(status, IdempotencyStatuses.Running, StringComparison.Ordinal)
            && !string.Equals(status, IdempotencyStatuses.Abandoned, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported idempotency status '{status}' for {context.CommandType.FullName}.");
        }

        return CommandIdempotencyAcquireResult.Reject(CommandIdempotencyErrors.RequestInFlight(context.CommandType, context.RequestKey));
    }

    private static async Task DeleteExpiredEntryAsync(
        NpgsqlConnection connection,
        CommandIdempotencyContext context,
        string moduleKey,
        string callerIdentity,
        CancellationToken cancellationToken)
    {
        const string deleteSql = $$"""
            DELETE FROM {{SharedRuntimePersistenceDefaults.SchemaName}}.{{SharedRuntimePersistenceDefaults.CommandIdempotencyTableName}}
            WHERE command_type = @commandType
                AND module_key = @moduleKey
                AND caller_identity = @callerIdentity
                AND request_key = @requestKey
                AND expires_utc <= NOW();
            """;

        await using var deleteCommand = new NpgsqlCommand(deleteSql, connection);
        PopulateIdentityParameters(deleteCommand, context, moduleKey, callerIdentity);
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void PopulateIdentityParameters(
        NpgsqlCommand command,
        CommandIdempotencyContext context,
        string moduleKey,
        string callerIdentity)
    {
        command.Parameters.AddWithValue("commandType", context.CommandType.AssemblyQualifiedName ?? context.CommandType.FullName ?? context.CommandType.Name);
        command.Parameters.AddWithValue("moduleKey", moduleKey);
        command.Parameters.AddWithValue("callerIdentity", callerIdentity);
        command.Parameters.AddWithValue("requestKey", context.RequestKey);
        command.Parameters.AddWithValue("requestHash", context.RequestHash);
    }

    private static string SerializeResponse(Type responseType, object response)
    {
        if (responseType == typeof(Result))
        {
            var typed = (Result)response;
            var envelope = new ResultEnvelope
            {
                Kind = "result",
                IsSuccess = typed.IsSuccess,
                Error = typed.Error
            };

            return JsonSerializer.Serialize(envelope, JsonOptions);
        }

        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var reader = ResultReaders.GetOrAdd(responseType, BuildResultReader);
            var (isSuccess, error, value) = reader(response);
            var valueType = responseType.GetGenericArguments()[0];

            var envelope = new ResultEnvelope
            {
                Kind = "result-generic",
                IsSuccess = isSuccess,
                Error = error,
                ValueType = valueType.AssemblyQualifiedName,
                ValueJson = value is null ? null : JsonSerializer.Serialize(value, valueType, JsonOptions)
            };

            return JsonSerializer.Serialize(envelope, JsonOptions);
        }

        var rawEnvelope = new ResultEnvelope
        {
            Kind = "json",
            ValueJson = JsonSerializer.Serialize(response, responseType, JsonOptions)
        };

        return JsonSerializer.Serialize(rawEnvelope, JsonOptions);
    }

    private static Func<object, (bool IsSuccess, Error Error, object? Value)> BuildResultReader(Type resultType)
    {
        var param = Expression.Parameter(typeof(object), "response");
        var typed = Expression.Convert(param, resultType);

        var isSuccess = Expression.Property(typed, "IsSuccess");
        var error = Expression.Property(typed, "Error");
        var value = Expression.Convert(Expression.Property(typed, "Value"), typeof(object));

        var tupleType = typeof(ValueTuple<bool, Error, object?>);
        var tupleConstructor = tupleType.GetConstructor(new[] { typeof(bool), typeof(Error), typeof(object) })!;
        var newTuple = Expression.New(tupleConstructor, isSuccess, error, value);

        return Expression.Lambda<Func<object, (bool, Error, object?)>>(newTuple, param).Compile();
    }

    private static Func<object, object> BuildSuccessFactory(Type resultType)
    {
        var valueType = resultType.GetGenericArguments()[0];
        var param = Expression.Parameter(typeof(object), "value");
        var typedValue = Expression.Convert(param, valueType);
        var successMethod = resultType.GetMethod("Success", new[] { valueType })!;
        var call = Expression.Call(successMethod, typedValue);
        return Expression.Lambda<Func<object, object>>(Expression.Convert(call, typeof(object)), param).Compile();
    }

    private static Func<Error, object> BuildFailureFactory(Type resultType)
    {
        var param = Expression.Parameter(typeof(Error), "error");
        var failureMethod = resultType.GetMethod("Failure", new[] { typeof(Error) })!;
        var call = Expression.Call(failureMethod, param);
        return Expression.Lambda<Func<Error, object>>(Expression.Convert(call, typeof(object)), param).Compile();
    }

    private static object DeserializeResponse(Type responseType, string payload)
    {
        var envelope = JsonSerializer.Deserialize<ResultEnvelope>(payload, JsonOptions)
            ?? throw new InvalidOperationException("Stored idempotency payload was empty.");

        if (string.Equals(envelope.Kind, "result", StringComparison.Ordinal))
        {
            return envelope.IsSuccess
                ? Result.Success()
                : Result.Failure(envelope.Error ?? new Error("unexpected.failure", "Unexpected failure."));
        }

        if (string.Equals(envelope.Kind, "result-generic", StringComparison.Ordinal))
        {
            var valueType = responseType.GetGenericArguments()[0];
            if (envelope.IsSuccess)
            {
                var value = envelope.ValueJson is null
                    ? null
                    : JsonSerializer.Deserialize(envelope.ValueJson, valueType, JsonOptions);

                var factory = SuccessFactories.GetOrAdd(responseType, BuildSuccessFactory);
                return factory(value!);
            }

            var failureFactory = FailureFactories.GetOrAdd(responseType, BuildFailureFactory);
            return failureFactory(envelope.Error ?? new Error("unexpected.failure", "Unexpected failure."));
        }

        if (!string.Equals(envelope.Kind, "json", StringComparison.Ordinal) || envelope.ValueJson is null)
        {
            throw new InvalidOperationException($"Unsupported idempotency payload kind '{envelope.Kind}'.");
        }

        return JsonSerializer.Deserialize(envelope.ValueJson, responseType, JsonOptions)
            ?? throw new InvalidOperationException($"Could not deserialize idempotency payload to {responseType.FullName}.");
    }

    private static string NormalizeModuleKey(string? moduleKey)
    {
        return string.IsNullOrWhiteSpace(moduleKey)
            ? string.Empty
            : moduleKey.Trim();
    }

    private static string NormalizeCallerIdentity(string? callerIdentity)
    {
        return string.IsNullOrWhiteSpace(callerIdentity)
            ? string.Empty
            : callerIdentity.Trim();
    }

    private static class IdempotencyStatuses
    {
        public const string Abandoned = "abandoned";
        public const string Completed = "completed";
        public const string Running = "running";
    }

    private sealed class PostgresCommandIdempotencyExecution : ICommandIdempotencyExecution
    {
        private readonly string _callerIdentity;
        private readonly NpgsqlDataSource _dataSource;
        private readonly CommandIdempotencyContext _context;
        private readonly string _moduleKey;
        private readonly CommandIdempotencyRetentionOptions _options;
        private int _finalized;

        public PostgresCommandIdempotencyExecution(
            NpgsqlDataSource dataSource,
            CommandIdempotencyContext context,
            string moduleKey,
            string callerIdentity,
            CommandIdempotencyRetentionOptions options)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            _context = context;
            _moduleKey = moduleKey;
            _callerIdentity = callerIdentity;
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async ValueTask CompleteAsync(object response, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(response);

            if (Interlocked.Exchange(ref _finalized, 1) == 1)
            {
                return;
            }

            var payload = SerializeResponse(response.GetType(), response);

            const string sql = $$"""
                UPDATE {{SharedRuntimePersistenceDefaults.SchemaName}}.{{SharedRuntimePersistenceDefaults.CommandIdempotencyTableName}}
                SET status = 'completed',
                    response_type = @responseType,
                    response_payload = @responsePayload,
                    updated_utc = NOW(),
                    completed_utc = NOW(),
                    expires_utc = NOW() + @completedEntryTtl
                WHERE command_type = @commandType
                    AND module_key = @moduleKey
                    AND caller_identity = @callerIdentity
                    AND request_key = @requestKey;
                """;

            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("responseType", response.GetType().AssemblyQualifiedName ?? response.GetType().FullName ?? response.GetType().Name);
            command.Parameters.AddWithValue("responsePayload", payload);
            command.Parameters.AddWithValue("completedEntryTtl", _options.CompletedEntryTtl);
            PopulateIdentityParameters(command, _context, _moduleKey, _callerIdentity);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async ValueTask AbandonAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _finalized, 1) == 1)
            {
                return;
            }

            const string sql = $$"""
                UPDATE {{SharedRuntimePersistenceDefaults.SchemaName}}.{{SharedRuntimePersistenceDefaults.CommandIdempotencyTableName}}
                SET status = 'abandoned',
                    response_payload = NULL,
                    updated_utc = NOW(),
                    completed_utc = NULL,
                    expires_utc = NOW() + @abandonedEntryTtl
                WHERE command_type = @commandType
                    AND module_key = @moduleKey
                    AND caller_identity = @callerIdentity
                    AND request_key = @requestKey
                    AND status = 'running';
                """;

            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("abandonedEntryTtl", _options.AbandonedEntryTtl);
            PopulateIdentityParameters(command, _context, _moduleKey, _callerIdentity);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ResultEnvelope
    {
        public string Kind { get; set; } = string.Empty;

        public bool IsSuccess { get; set; }

        public Error? Error { get; set; }

        public string? ValueType { get; set; }

        public string? ValueJson { get; set; }
    }
}
