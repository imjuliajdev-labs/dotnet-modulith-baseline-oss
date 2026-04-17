using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Infrastructure.Persistence;

internal sealed class DatabaseMigrationRunner : IDatabaseMigrationRunner
{
    private const string PostgresMigrationLockName = "dotnet-modulith-baseline:migrations";

    private readonly ILogger<DatabaseMigrationRunner> _logger;
    private readonly IReadOnlyCollection<IDatabaseMigration> _migrations;

    public DatabaseMigrationRunner(
        IEnumerable<IDatabaseMigration> migrations,
        ILogger<DatabaseMigrationRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(migrations);

        _migrations = migrations.ToArray();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyCollection<DatabaseMigrationStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        var statuses = new List<DatabaseMigrationStatus>(_migrations.Count);

        foreach (var migration in _migrations.OrderBy(static migration => migration.Name, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(migration.ConnectionString))
            {
                statuses.Add(new DatabaseMigrationStatus(migration.Name, false, Array.Empty<string>()));
                continue;
            }

            try
            {
                var pendingMigrations = await migration.GetPendingMigrationsAsync(cancellationToken);
                statuses.Add(new DatabaseMigrationStatus(migration.Name, true, pendingMigrations));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Failed to inspect database migration readiness for '{migration.Name}'.",
                    exception);
            }
        }

        return statuses;
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        var statuses = await GetStatusAsync(cancellationToken);
        var pending = statuses
            .Where(static status => status.IsConfigured && status.PendingMigrations.Count > 0)
            .ToArray();

        if (pending.Length == 0)
        {
            return;
        }

        var details = string.Join(
            "; ",
            pending.Select(static status => $"{status.Name}: {string.Join(", ", status.PendingMigrations)}"));

        throw new InvalidOperationException(
            $"Pending database migrations were found. Run DbMigrator before starting the host. {details}");
    }

    public async Task ApplyConfiguredMigrationsAsync(CancellationToken cancellationToken)
    {
        var configuredMigrations = _migrations
            .Where(static migration => !string.IsNullOrWhiteSpace(migration.ConnectionString))
            .OrderBy(static migration => migration.Name, StringComparer.Ordinal)
            .ToArray();

        if (configuredMigrations.Length == 0)
        {
            _logger.LogInformation("No configured database migrations were found.");
            return;
        }

        foreach (var connectionGroup in configuredMigrations.GroupBy(static migration => migration.ConnectionString!, StringComparer.Ordinal))
        {
            await using var advisoryLock = await PostgresAdvisoryLock.AcquireAsync(
                connectionGroup.Key,
                PostgresMigrationLockName,
                cancellationToken);

            foreach (var migration in connectionGroup)
            {
                _logger.LogInformation("Applying migrations for {DatabaseMigration}.", migration.Name);
                await migration.ApplyAsync(cancellationToken);
                _logger.LogInformation("Finished migrations for {DatabaseMigration}.", migration.Name);
            }
        }
    }
}

internal sealed class DatabaseMigrationReadinessHostedService : IHostedService
{
    private readonly IDatabaseMigrationRunner _databaseMigrationRunner;

    public DatabaseMigrationReadinessHostedService(IDatabaseMigrationRunner databaseMigrationRunner)
    {
        _databaseMigrationRunner = databaseMigrationRunner ?? throw new ArgumentNullException(nameof(databaseMigrationRunner));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _databaseMigrationRunner.EnsureReadyAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
