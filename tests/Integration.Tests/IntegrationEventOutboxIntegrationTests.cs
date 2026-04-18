using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Domain.Events;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.IntegrationEvents;
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Npgsql;
using Platform.Application.Outbox; // BP-031 dispatch-coverage marker
using Testcontainers.PostgreSql;
using Xunit;

namespace Integration.Tests;

public sealed class IntegrationEventOutboxIntegrationTests
{
    [Fact]
    public async Task CommandTransactionCommitsModuleWriteAndOutboxTogetherOnSuccess()
    {
        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("baseline")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCommand("-c", "max_prepared_transactions=64")
            .Build();

        await postgres.StartAsync();

        var configuration = PostgresBackedApiApplication.CreateConfiguration(postgres.GetConnectionString());
        var probe = new OutboxDeliveryProbe();
        var eventId = Guid.Parse("03ba5844-0aee-4b10-b113-5eacbb7a5912");

        await using var provider = await BuildProviderAsync(configuration, probe);
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.Send(
            new PublishCatalogCommand(
                "catalog-commit",
                eventId,
                Instant.FromUtc(2026, 4, 4, 10, 0),
                FailAfterPublish: false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(await CatalogRecordExistsAsync(postgres.GetConnectionString(), "catalog-commit"));
        Assert.True(await OutboxMessageExistsAsync(postgres.GetConnectionString(), eventId));
    }

    [Fact]
    public async Task CommandTransactionRollsBackModuleWriteAndOutboxTogetherOnFailureResponse()
    {
        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("baseline")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCommand("-c", "max_prepared_transactions=64")
            .Build();

        await postgres.StartAsync();

        var configuration = PostgresBackedApiApplication.CreateConfiguration(postgres.GetConnectionString());
        var probe = new OutboxDeliveryProbe();
        var eventId = Guid.Parse("0f535cd5-f7c4-443f-a52c-e6d7d1cbe17f");

        await using var provider = await BuildProviderAsync(configuration, probe);
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.Send(
            new PublishCatalogCommand(
                "catalog-rollback",
                eventId,
                Instant.FromUtc(2026, 4, 4, 10, 5),
                FailAfterPublish: true),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.False(await CatalogRecordExistsAsync(postgres.GetConnectionString(), "catalog-rollback"));
        Assert.False(await OutboxMessageExistsAsync(postgres.GetConnectionString(), eventId));
    }

    [Fact]
    public async Task OutboxDispatchSurvivesProviderRestarts()
    {
        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("baseline")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCommand("-c", "max_prepared_transactions=64")
            .Build();

        await postgres.StartAsync();

        var configuration = PostgresBackedApiApplication.CreateConfiguration(postgres.GetConnectionString());
        var probe = new OutboxDeliveryProbe();
        var integrationEvent = new CatalogPublishedEvent(
            Guid.Parse("e58c8ab2-2eec-4f81-b39b-1304d7d33d0e"),
            Instant.FromUtc(2026, 4, 3, 13, 0),
            "catalog-123");

        await using (var firstProvider = await BuildProviderAsync(configuration, probe))
        {
            var publisher = firstProvider.GetRequiredService<IIntegrationEventOutboxPublisher>();
            await publisher.PublishAsync(new IntegrationEventOutboxPublishRequest("publishing", integrationEvent), CancellationToken.None);
        }

        var storedEventType = await ReadOutboxEventTypeAsync(postgres.GetConnectionString(), integrationEvent.EventId);
        Assert.Equal(IntegrationEventTypeNames.GetName(typeof(CatalogPublishedEvent)), storedEventType);

        await using (var secondProvider = await BuildProviderAsync(configuration, probe))
        {
            var dispatcher = secondProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
            var dispatched = await dispatcher.DispatchAvailableAsync(CancellationToken.None);

            Assert.Equal(1, dispatched);
        }

        await using (var thirdProvider = await BuildProviderAsync(configuration, probe))
        {
            var dispatcher = thirdProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
            var dispatched = await dispatcher.DispatchAvailableAsync(CancellationToken.None);

            Assert.Equal(0, dispatched);
        }

        Assert.Equal(1, probe.SuccessCount);
        Assert.Equal(1, probe.AttemptCount);
    }

    [Fact]
    public async Task OutboxDispatchSkipsDisabledModulesUntilTheyAreReEnabled()
    {
        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("baseline")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCommand("-c", "max_prepared_transactions=64")
            .Build();

        await postgres.StartAsync();

        var configuration = PostgresBackedApiApplication.CreateConfiguration(postgres.GetConnectionString());
        var probe = new OutboxDeliveryProbe();
        var integrationEvent = new CatalogPublishedEvent(
            Guid.Parse("2d6ea295-3d54-4d48-9af5-6d1406e63b3f"),
            Instant.FromUtc(2026, 4, 3, 13, 10),
            "catalog-disabled");

        await using (var disabledProvider = await BuildProviderAsync(configuration, probe, defaultEnabled: false))
        {
            var publisher = disabledProvider.GetRequiredService<IIntegrationEventOutboxPublisher>();
            var dispatcher = disabledProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();

            await publisher.PublishAsync(new IntegrationEventOutboxPublishRequest("publishing", integrationEvent), CancellationToken.None);
            Assert.Equal(0, await dispatcher.DispatchAvailableAsync(CancellationToken.None));
        }

        Assert.Equal(0, probe.SuccessCount);
        Assert.Equal(0, probe.AttemptCount);

        await using (var enabledProvider = await BuildProviderAsync(configuration, probe, defaultEnabled: true))
        {
            var dispatcher = enabledProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
            Assert.Equal(1, await dispatcher.DispatchAvailableAsync(CancellationToken.None));
        }

        Assert.Equal(1, probe.SuccessCount);
        Assert.Equal(1, probe.AttemptCount);
    }

    [Fact]
    public async Task OutboxDispatchSupportsLegacyAssemblyQualifiedEventTypeRows()
    {
        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("baseline")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCommand("-c", "max_prepared_transactions=64")
            .Build();

        await postgres.StartAsync();

        var configuration = PostgresBackedApiApplication.CreateConfiguration(postgres.GetConnectionString());
        var probe = new OutboxDeliveryProbe();
        var integrationEvent = new CatalogPublishedEvent(
            Guid.Parse("428d0044-2b67-43f1-8c70-d5efa23d8453"),
            Instant.FromUtc(2026, 4, 3, 13, 30),
            "catalog-legacy");

        await using (var firstProvider = await BuildProviderAsync(configuration, probe))
        {
            var publisher = firstProvider.GetRequiredService<IIntegrationEventOutboxPublisher>();
            await publisher.PublishAsync(new IntegrationEventOutboxPublishRequest("publishing", integrationEvent), CancellationToken.None);
        }

        Assert.NotNull(typeof(CatalogPublishedEvent).AssemblyQualifiedName);
        await UpdateOutboxEventTypeAsync(
            postgres.GetConnectionString(),
            integrationEvent.EventId,
            typeof(CatalogPublishedEvent).AssemblyQualifiedName!);

        await using (var secondProvider = await BuildProviderAsync(configuration, probe))
        {
            var dispatcher = secondProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
            var dispatched = await dispatcher.DispatchAvailableAsync(CancellationToken.None);

            Assert.Equal(1, dispatched);
        }

        Assert.Equal(1, probe.SuccessCount);
        Assert.Equal(1, probe.AttemptCount);
    }

    [Fact]
    public async Task OutboxRetriesAndDeadLettersAfterTheConfiguredThreshold()
    {
        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("baseline")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCommand("-c", "max_prepared_transactions=64")
            .Build();

        await postgres.StartAsync();

        var configuration = PostgresBackedApiApplication.CreateConfiguration(postgres.GetConnectionString());
        var probe = new OutboxDeliveryProbe(failuresBeforeSuccess: int.MaxValue);
        var integrationEvent = new CatalogPublishedEvent(
            Guid.Parse("2d376a1c-891b-4dd2-a055-06fa4db3b53c"),
            Instant.FromUtc(2026, 4, 3, 13, 15),
            "catalog-dead-letter");

        await using var provider = await BuildProviderAsync(
            configuration,
            probe,
            new IntegrationEventOutboxProcessingOptions
            {
                BatchSize = 10,
                DeadLetterThreshold = 2,
                LeaseDuration = TimeSpan.FromSeconds(5),
                PollInterval = TimeSpan.FromMilliseconds(50),
                RetryBackoff = TimeSpan.Zero
            });

        var publisher = provider.GetRequiredService<IIntegrationEventOutboxPublisher>();
        var dispatcher = provider.GetRequiredService<IIntegrationEventOutboxDispatcher>();

        await publisher.PublishAsync(new IntegrationEventOutboxPublishRequest("publishing", integrationEvent), CancellationToken.None);

        Assert.Equal(0, await dispatcher.DispatchAvailableAsync(CancellationToken.None));
        Assert.Equal(0, await dispatcher.DispatchAvailableAsync(CancellationToken.None));
        Assert.Equal(0, await dispatcher.DispatchAvailableAsync(CancellationToken.None));

        Assert.Equal(2, probe.AttemptCount);
        Assert.Equal(0, probe.SuccessCount);

        var outboxState = await ReadOutboxStateAsync(postgres.GetConnectionString(), integrationEvent.EventId);

        Assert.True(outboxState.IsDeadLettered);
        Assert.False(outboxState.IsProcessed);
        Assert.Equal(2, outboxState.Attempts);
        Assert.Equal("unexpected.failure", outboxState.LastErrorCode);
    }

    private static async Task<ServiceProvider> BuildProviderAsync(
        IReadOnlyDictionary<string, string?> configuration,
        OutboxDeliveryProbe probe,
        IntegrationEventOutboxProcessingOptions? options = null,
        bool defaultEnabled = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
        services.AddSingleton(probe);
        services.AddSingleton<IModule>(new PublishingModule(defaultEnabled));
        services.AddSingleton<PublishingCatalogWriteStore>();
        services.AddSingleton<IDatabaseMigration, PublishingCatalogDatabaseMigration>();
        services.AddBuildingBlocksInfrastructureDefaults();
        services.AddPostgresIntegrationEventInbox("publishing", "publishing");
        if (options is not null)
        {
            services.AddSingleton(options);
        }

        services.AddDispatcher(typeof(IntegrationEventOutboxIntegrationTests).Assembly);
        services.AddPostgresIntegrationEventOutbox("publishing", "publishing");

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IDatabaseMigrationRunner>().ApplyConfiguredMigrationsAsync(CancellationToken.None);
        return provider;
    }

    private static async Task<OutboxState> ReadOutboxStateAsync(string connectionString, Guid eventId)
    {
        const string sql = """
            SELECT attempts,
                processed_utc IS NOT NULL,
                dead_lettered_utc IS NOT NULL,
                last_error_code
            FROM publishing.integration_outbox
            WHERE event_id = @eventId;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("eventId", eventId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return new OutboxState(
            reader.GetInt32(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static async Task<bool> CatalogRecordExistsAsync(string connectionString, string catalogId)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM publishing.catalog_exports
                WHERE catalog_id = @catalogId);
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("catalogId", catalogId);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<bool> OutboxMessageExistsAsync(string connectionString, Guid eventId)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM publishing.integration_outbox
                WHERE event_id = @eventId);
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("eventId", eventId);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<string> ReadOutboxEventTypeAsync(string connectionString, Guid eventId)
    {
        const string sql = """
            SELECT event_type
            FROM publishing.integration_outbox
            WHERE event_id = @eventId;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("eventId", eventId);

        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException($"Could not locate outbox message {eventId}."));
    }

    private static async Task UpdateOutboxEventTypeAsync(string connectionString, Guid eventId, string eventType)
    {
        const string sql = """
            UPDATE publishing.integration_outbox
            SET event_type = @eventType
            WHERE event_id = @eventId;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("eventId", eventId);
        command.Parameters.AddWithValue("eventType", eventType);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record PublishCatalogCommand(
        string CatalogId,
        Guid EventId,
        Instant OccurredAt,
        bool FailAfterPublish) : ICommand, IModuleScoped
    {
        public string ModuleKey => "publishing";
    }

    private sealed class PublishCatalogCommandHandler : ICommandHandler<PublishCatalogCommand>
    {
        private readonly PublishingCatalogWriteStore _writeStore;
        private readonly IIntegrationEventOutboxPublisher _outboxPublisher;

        public PublishCatalogCommandHandler(
            PublishingCatalogWriteStore writeStore,
            IIntegrationEventOutboxPublisher outboxPublisher)
        {
            _writeStore = writeStore;
            _outboxPublisher = outboxPublisher;
        }

        public async Task<Result> Handle(PublishCatalogCommand command, CancellationToken cancellationToken)
        {
            await _writeStore.WriteAsync(command.CatalogId, command.EventId, command.OccurredAt, cancellationToken);

            await _outboxPublisher.PublishAsync(
                new IntegrationEventOutboxPublishRequest(
                    "publishing",
                    new CatalogPublishedEvent(command.EventId, command.OccurredAt, command.CatalogId)),
                cancellationToken);

            if (command.FailAfterPublish)
            {
                return Result.Failure(new Error("publishing.synthetic_failure", "Synthetic publishing failure after outbox enqueue.", ErrorKind.Failure));
            }

            return Result.Success();
        }
    }

    private sealed record CatalogPublishedEvent(Guid EventId, Instant OccurredAt, string CatalogId) : IIntegrationEvent;

    private sealed class PublishingCatalogWriteStore
    {
        private readonly string _connectionString;

        public PublishingCatalogWriteStore(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString(SharedRuntimePersistenceDefaults.ConnectionStringName)
                ?? throw new InvalidOperationException("Publishing writes require BaselineDatabase connection string.");
        }

        public async Task WriteAsync(string catalogId, Guid eventId, Instant createdUtc, CancellationToken cancellationToken)
        {
            const string sql = """
                INSERT INTO publishing.catalog_exports (catalog_id, event_id, created_utc)
                VALUES (@catalogId, @eventId, @createdUtc);
                """;

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("catalogId", catalogId);
            command.Parameters.AddWithValue("eventId", eventId);
            command.Parameters.AddWithValue("createdUtc", createdUtc.ToDateTimeOffset());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private sealed class CatalogPublishedHandler : IModuleScopedIntegrationEventHandler<CatalogPublishedEvent>
    {
        private readonly OutboxDeliveryProbe _probe;

        public CatalogPublishedHandler(OutboxDeliveryProbe probe)
        {
            _probe = probe;
        }

        public string ModuleKey => "publishing";

        public Task Handle(CatalogPublishedEvent integrationEvent, CancellationToken cancellationToken)
        {
            _probe.RecordAttempt();
            if (_probe.ShouldFailCurrentAttempt())
            {
                throw new InvalidOperationException("Synthetic outbox delivery failure.");
            }

            _probe.RecordSuccess();
            return Task.CompletedTask;
        }
    }

    private sealed class OutboxDeliveryProbe
    {
        private readonly int _failuresBeforeSuccess;
        private int _attemptCount;
        private int _successCount;

        public OutboxDeliveryProbe(int failuresBeforeSuccess = 0)
        {
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public int AttemptCount => Volatile.Read(ref _attemptCount);

        public int SuccessCount => Volatile.Read(ref _successCount);

        public void RecordAttempt()
        {
            Interlocked.Increment(ref _attemptCount);
        }

        public bool ShouldFailCurrentAttempt()
        {
            return AttemptCount <= _failuresBeforeSuccess;
        }

        public void RecordSuccess()
        {
            Interlocked.Increment(ref _successCount);
        }
    }

    private sealed class PublishingModule : IModule
    {
        public PublishingModule(bool defaultEnabled)
        {
            Descriptor = new ModuleDescriptor(
                "publishing",
                "Publishing",
                "/api/publishing",
                "publishing",
                "publishing",
                DefaultEnabled: defaultEnabled,
                CanBeDisabled: true);
        }

        public ModuleDescriptor Descriptor { get; }

        public string Key => "publishing";
    }

    private sealed class PublishingCatalogDatabaseMigration : IDatabaseMigration
    {
        private readonly string _connectionString;

        public PublishingCatalogDatabaseMigration(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString(SharedRuntimePersistenceDefaults.ConnectionStringName)
                ?? throw new InvalidOperationException("Publishing catalog migration requires BaselineDatabase connection string.");
        }

        public string Name => "publishing_catalog_exports";

        public string? ConnectionString => _connectionString;

        public async Task<IReadOnlyCollection<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
        {
            const string sql = """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = 'publishing'
                        AND table_name = 'catalog_exports');
                """;

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            var exists = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
            return exists ? [] : ["publishing.catalog_exports"];
        }

        public async Task ApplyAsync(CancellationToken cancellationToken)
        {
            const string sql = """
                CREATE SCHEMA IF NOT EXISTS publishing;

                CREATE TABLE IF NOT EXISTS publishing.catalog_exports
                (
                    catalog_id TEXT PRIMARY KEY,
                    event_id UUID NOT NULL,
                    created_utc TIMESTAMPTZ NOT NULL
                );
                """;

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private sealed record OutboxState(int Attempts, bool IsProcessed, bool IsDeadLettered, string? LastErrorCode);
}
