using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Integration.Tests;

public sealed class DatabaseMigrationRunnerIntegrationTests
{
    [Xunit.Fact]
    public async Task ConcurrentMigrationRunnersSerializeThroughPostgresAdvisoryLock()
    {
        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("baseline")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await postgres.StartAsync();

        var probe = new MigrationExecutionProbe();

        await using var firstProvider = BuildProvider(postgres.GetConnectionString(), probe);
        await using var secondProvider = BuildProvider(postgres.GetConnectionString(), probe);

        var firstRunner = firstProvider.GetRequiredService<IDatabaseMigrationRunner>();
        var secondRunner = secondProvider.GetRequiredService<IDatabaseMigrationRunner>();

        var firstRun = firstRunner.ApplyConfiguredMigrationsAsync(CancellationToken.None);
        await probe.WaitForFirstEntryAsync();

        var secondRun = secondRunner.ApplyConfiguredMigrationsAsync(CancellationToken.None);

        var secondEnteredBeforeRelease = await probe.WaitForSecondEntryAsync(TimeSpan.FromMilliseconds(750));
        Xunit.Assert.False(secondEnteredBeforeRelease);

        probe.ReleaseFirstEntry();

        await Task.WhenAll(firstRun, secondRun);

        Xunit.Assert.Equal(2, probe.TotalEntries);
        Xunit.Assert.Equal(1, probe.MaxConcurrentEntries);
    }

    private static ServiceProvider BuildProvider(string connectionString, MigrationExecutionProbe probe)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDatabaseMigrationSupport();
        services.AddSingleton(probe);
        services.AddSingleton<IDatabaseMigration>(provider =>
            new BlockingMigration(
                "platform",
                provider.GetRequiredService<MigrationExecutionProbe>(),
                connectionString));

        return services.BuildServiceProvider();
    }

    private sealed class BlockingMigration : IDatabaseMigration
    {
        private readonly string _connectionString;
        private readonly MigrationExecutionProbe _probe;

        public BlockingMigration(string name, MigrationExecutionProbe probe, string connectionString)
        {
            Name = name;
            _probe = probe ?? throw new ArgumentNullException(nameof(probe));
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public string Name { get; }

        public string? ConnectionString => _connectionString;

        public Task<IReadOnlyCollection<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
        {
            IReadOnlyCollection<string> pendingMigrations = ["fake_migration"];
            return Task.FromResult(pendingMigrations);
        }

        public Task ApplyAsync(CancellationToken cancellationToken)
        {
            return _probe.RecordEntryAsync(cancellationToken);
        }
    }

    private sealed class MigrationExecutionProbe
    {
        private readonly TaskCompletionSource _allowFirstEntryToComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstEntryReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondEntryReached = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _activeEntries;
        private int _maxConcurrentEntries;
        private int _totalEntries;

        public int MaxConcurrentEntries => Volatile.Read(ref _maxConcurrentEntries);

        public int TotalEntries => Volatile.Read(ref _totalEntries);

        public Task WaitForFirstEntryAsync()
        {
            return _firstEntryReached.Task;
        }

        public async Task<bool> WaitForSecondEntryAsync(TimeSpan timeout)
        {
            var completedTask = await Task.WhenAny(_secondEntryReached.Task, Task.Delay(timeout));
            return completedTask == _secondEntryReached.Task;
        }

        public void ReleaseFirstEntry()
        {
            _allowFirstEntryToComplete.TrySetResult();
        }

        public async Task RecordEntryAsync(CancellationToken cancellationToken)
        {
            var currentConcurrency = Interlocked.Increment(ref _activeEntries);
            UpdateMaxConcurrentEntries(currentConcurrency);

            try
            {
                var entryNumber = Interlocked.Increment(ref _totalEntries);
                if (entryNumber == 1)
                {
                    _firstEntryReached.TrySetResult();
                    await _allowFirstEntryToComplete.Task.WaitAsync(cancellationToken);
                }
                else if (entryNumber == 2)
                {
                    _secondEntryReached.TrySetResult();
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeEntries);
            }
        }

        private void UpdateMaxConcurrentEntries(int currentConcurrency)
        {
            while (true)
            {
                var observed = Volatile.Read(ref _maxConcurrentEntries);
                if (currentConcurrency <= observed)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxConcurrentEntries, currentConcurrency, observed) == observed)
                {
                    return;
                }
            }
        }
    }
}
