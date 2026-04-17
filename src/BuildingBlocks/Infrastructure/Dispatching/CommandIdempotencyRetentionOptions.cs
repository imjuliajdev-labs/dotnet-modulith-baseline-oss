using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BuildingBlocks.Infrastructure.Dispatching;

public sealed class CommandIdempotencyRetentionOptions
{
    public const string SectionName = "SharedRuntime:CommandIdempotency";

    public TimeSpan RunningEntryTtl { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan CompletedEntryTtl { get; init; } = TimeSpan.FromHours(24);

    public TimeSpan AbandonedEntryTtl { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromMinutes(5);
}

internal sealed class CommandIdempotencyRetentionHostedService : BackgroundService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<CommandIdempotencyRetentionHostedService> _logger;
    private readonly CommandIdempotencyRetentionOptions _options;

    public CommandIdempotencyRetentionHostedService(
        IPostgresDataSourceResolver dataSourceResolver,
        CommandIdempotencyRetentionOptions options,
        ILogger<CommandIdempotencyRetentionHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(dataSourceResolver);

        _dataSource = dataSourceResolver.GetRequiredDataSource(SharedRuntimePersistenceDefaults.ConnectionStringName);
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.CleanupInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeExpiredEntriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Shared command-idempotency retention cleanup failed.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PurgeExpiredEntriesAsync(CancellationToken cancellationToken)
    {
        var sql = $$"""
            DELETE FROM {{SharedRuntimePersistenceDefaults.SchemaName}}.{{SharedRuntimePersistenceDefaults.CommandIdempotencyTableName}}
            WHERE expires_utc <= NOW();
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
