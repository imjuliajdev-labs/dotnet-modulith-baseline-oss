// BP-016 / BP-019 chaos coverage. These tests exercise the real outbox dispatch
// path against a real Testcontainers PostgreSQL instance and inject failures at
// the handler boundary by wrapping the outbox subscriber with a fault-injecting
// decorator. The dispatcher, outbox store, module-execution gate, and Postgres
// driver are all the production implementations — no mocks of the system under
// test, no [Skip] attributes, no try/catch that swallows the injected failure.
//
// Coverage:
//   - 4a: a transient NpgsqlException is caught by the outbox dispatcher's
//         retry loop and the message eventually delivers exactly once.
//   - 4a: a non-transient exception fails loudly — the message dead-letters
//         after the configured threshold and the dead-letter row carries the
//         injected error code as the observability signal.
//   - 4b: when the outbox is saturated beyond its batch size and one of the
//         messages experiences a single transient fault, every message still
//         delivers exactly once after the retry/lease cycles complete.
//   - 4d: disabling a module after a command has enqueued an outbox row leaves
//         the row durably parked (no orphans, no dead-letter), and re-enabling
//         the module flushes the queue exactly once.
//
// Each test is self-contained and uses distinct command/event types so the
// chaos file's dispatcher registrations never collide with
// IntegrationEventOutboxIntegrationTests' private types.

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
using Platform.Application.Outbox;
using Testcontainers.PostgreSql;
using Xunit;

namespace Integration.Tests.ChaosScenarios;

public sealed class OutboxResilienceChaosTests
{
    [Fact]
    public async Task OutboxDispatchSurvivesOneTransientNpgsqlExceptionAndDeliversExactlyOnce()
    {
        // 4a transient. Inject a single transient NpgsqlException on the first
        // dispatch attempt. The outbox dispatcher's retry loop must catch it,
        // the message must be retried on the next dispatch cycle, and the
        // probe must record exactly one successful delivery.
        await using var postgres = await StartPostgresAsync();
        var configuration = PostgresBackedApiApplication.CreateConfiguration(postgres.GetConnectionString());

        var faultPlan = ChaosFaultPlan.NTransientThenSucceed(
            count: 1,
            transientFactory: static () => MakeTransientNpgsqlException("postgres-transient"));
        var probe = new ChaosOutboxProbe();
        var integrationEvent = new ChaosCatalogPublishedEvent(
            EventId: Guid.Parse("c1a05a4a-1f24-4a3a-9c46-77c3f50a5e01"),
            OccurredAt: Instant.FromUtc(2026, 4, 13, 9, 0),
            CatalogId: "chaos-transient");

        await using var provider = await BuildProviderAsync(
            configuration,
            probe,
            faultPlan,
            new IntegrationEventOutboxProcessingOptions
            {
                BatchSize = 5,
                DeadLetterThreshold = 5,
                LeaseDuration = TimeSpan.FromSeconds(5),
                PollInterval = TimeSpan.FromMilliseconds(50),
                RetryBackoff = TimeSpan.Zero
            });

        var publisher = provider.GetRequiredService<IIntegrationEventOutboxPublisher>();
        var dispatcher = provider.GetRequiredService<IIntegrationEventOutboxDispatcher>();

        await publisher.PublishAsync(
            new IntegrationEventOutboxPublishRequest("chaos", integrationEvent),
            CancellationToken.None);

        // First attempt fails transiently and is parked for retry.
        var firstAttempt = await dispatcher.DispatchAvailableAsync(CancellationToken.None);
        Assert.Equal(0, firstAttempt);

        // Second attempt succeeds because the fault plan only injected one fault.
        var secondAttempt = await dispatcher.DispatchAvailableAsync(CancellationToken.None);
        Assert.Equal(1, secondAttempt);

        // Third attempt observes nothing left to dispatch — proves exactly-once.
        var thirdAttempt = await dispatcher.DispatchAvailableAsync(CancellationToken.None);
        Assert.Equal(0, thirdAttempt);

        Assert.Equal(2, probe.AttemptCount);
        Assert.Equal(1, probe.SuccessCount);
        Assert.Equal(1, probe.TransientFaultsInjected);

        var state = await ReadOutboxStateAsync(postgres.GetConnectionString(), integrationEvent.EventId);
        Assert.True(state.IsProcessed);
        Assert.False(state.IsDeadLettered);
    }

    [Fact]
    public async Task OutboxDispatchFailsLoudlyWhenInjectedFaultIsNonTransientAndDeadLettersWithObservableErrorCode()
    {
        // 4a non-transient + 4b poison observability. A non-recoverable injected
        // fault must NOT be silently swallowed: after DeadLetterThreshold attempts
        // the message must be dead-lettered and the dead-letter row must surface
        // the injected error so an operator can see it via the observability
        // path (last_error_code column / GetOutboxDeadLettersQueryHandler).
        await using var postgres = await StartPostgresAsync();
        var configuration = PostgresBackedApiApplication.CreateConfiguration(postgres.GetConnectionString());

        var faultPlan = ChaosFaultPlan.AlwaysThrow(
            static () => new InvalidOperationException("chaos.poison: handler logic is permanently broken"));
        var probe = new ChaosOutboxProbe();
        var integrationEvent = new ChaosCatalogPublishedEvent(
            EventId: Guid.Parse("c1a05a4a-1f24-4a3a-9c46-77c3f50a5e02"),
            OccurredAt: Instant.FromUtc(2026, 4, 13, 9, 5),
            CatalogId: "chaos-poison");

        await using var provider = await BuildProviderAsync(
            configuration,
            probe,
            faultPlan,
            new IntegrationEventOutboxProcessingOptions
            {
                BatchSize = 5,
                DeadLetterThreshold = 2,
                LeaseDuration = TimeSpan.FromSeconds(5),
                PollInterval = TimeSpan.FromMilliseconds(50),
                RetryBackoff = TimeSpan.Zero
            });

        var publisher = provider.GetRequiredService<IIntegrationEventOutboxPublisher>();
        var dispatcher = provider.GetRequiredService<IIntegrationEventOutboxDispatcher>();

        await publisher.PublishAsync(
            new IntegrationEventOutboxPublishRequest("chaos", integrationEvent),
            CancellationToken.None);

        Assert.Equal(0, await dispatcher.DispatchAvailableAsync(CancellationToken.None));
        Assert.Equal(0, await dispatcher.DispatchAvailableAsync(CancellationToken.None));
        Assert.Equal(0, await dispatcher.DispatchAvailableAsync(CancellationToken.None));

        Assert.Equal(2, probe.AttemptCount);
        Assert.Equal(0, probe.SuccessCount);

        var state = await ReadOutboxStateAsync(postgres.GetConnectionString(), integrationEvent.EventId);
        Assert.True(state.IsDeadLettered, "Persistent failure must be dead-lettered, not silently retried forever.");
        Assert.False(state.IsProcessed);
        Assert.Equal(2, state.Attempts);

        // Observability signal: the dead-letter row carries a non-empty error code/message
        // produced by the dispatcher's IExceptionToErrorMapper. The mapper normalizes the
        // raw exception message, so the test asserts on presence + recognized error code
        // rather than on the original literal text. The point is "no silent swallow".
        Assert.False(string.IsNullOrWhiteSpace(state.LastErrorCode), "Dead-letter row must record an error code for observability.");
        Assert.False(string.IsNullOrWhiteSpace(state.LastErrorMessage), "Dead-letter row must record an error message for observability.");
    }

    [Fact]
    public async Task OutboxDeliversEveryMessageExactlyOnceUnderSaturationWithOneTransientFailure()
    {
        // 4b. Enqueue more messages than the dispatcher's batch size and inject
        // a single transient failure on one of them. The dispatcher must drain
        // the entire queue exactly once after enough cycles, with the
        // transiently-failed message also delivered.
        const int messageCount = 17;
        const int batchSize = 5;

        await using var postgres = await StartPostgresAsync();
        var configuration = PostgresBackedApiApplication.CreateConfiguration(postgres.GetConnectionString());

        var failTargetCatalogId = "chaos-saturation-target";
        var faultPlan = ChaosFaultPlan.OneTransientForCatalog(
            failTargetCatalogId,
            transientFactory: static () => MakeTransientNpgsqlException("postgres-transient-saturation"));
        var probe = new ChaosOutboxProbe();

        await using var provider = await BuildProviderAsync(
            configuration,
            probe,
            faultPlan,
            new IntegrationEventOutboxProcessingOptions
            {
                BatchSize = batchSize,
                DeadLetterThreshold = 5,
                LeaseDuration = TimeSpan.FromSeconds(5),
                PollInterval = TimeSpan.FromMilliseconds(50),
                RetryBackoff = TimeSpan.Zero
            });

        var publisher = provider.GetRequiredService<IIntegrationEventOutboxPublisher>();
        var dispatcher = provider.GetRequiredService<IIntegrationEventOutboxDispatcher>();

        var publishedEventIds = new List<Guid>(messageCount);
        for (var i = 0; i < messageCount; i++)
        {
            var eventId = Guid.NewGuid();
            publishedEventIds.Add(eventId);
            var catalogId = i == 7 ? failTargetCatalogId : $"chaos-saturation-{i:D2}";
            var integrationEvent = new ChaosCatalogPublishedEvent(eventId, Instant.FromUtc(2026, 4, 13, 10, i), catalogId);
            await publisher.PublishAsync(
                new IntegrationEventOutboxPublishRequest("chaos", integrationEvent),
                CancellationToken.None);
        }

        // Drain the queue with bounded cycles. Each cycle dispatches up to BatchSize
        // messages; a transient failure on one message means we expect at most
        // ceil(messageCount/batchSize) + 1 cycles to complete.
        var totalDispatched = 0;
        var maxCycles = (messageCount / batchSize) + 4;
        for (var cycle = 0; cycle < maxCycles; cycle++)
        {
            var dispatchedThisCycle = await dispatcher.DispatchAvailableAsync(CancellationToken.None);
            totalDispatched += dispatchedThisCycle;
            if (totalDispatched >= messageCount)
            {
                break;
            }
        }

        Assert.Equal(messageCount, totalDispatched);
        Assert.Equal(messageCount + 1, probe.AttemptCount); // one extra attempt for the transient retry
        Assert.Equal(messageCount, probe.SuccessCount);
        Assert.Equal(1, probe.TransientFaultsInjected);

        // Final dispatch must be a no-op; no orphaned rows remain.
        Assert.Equal(0, await dispatcher.DispatchAvailableAsync(CancellationToken.None));

        foreach (var eventId in publishedEventIds)
        {
            var state = await ReadOutboxStateAsync(postgres.GetConnectionString(), eventId);
            Assert.True(state.IsProcessed, $"Outbox row {eventId} was not processed.");
            Assert.False(state.IsDeadLettered, $"Outbox row {eventId} was unexpectedly dead-lettered.");
        }
    }

    [Fact]
    public async Task DisablingModuleAfterEnqueueLeavesOutboxRowsParkedAndReEnableDeliversExactlyOnce()
    {
        // 4d. Enqueue an outbox row while the chaos module is enabled, recreate
        // the provider with the module composed as disabled, and assert the
        // dispatcher refuses to deliver. The connection string is shared so
        // the row persists in Postgres across the toggle. Then recreate the
        // provider with the module enabled again and assert the message
        // delivers exactly once. No row may remain unprocessed-and-not-dead-
        // lettered after re-enable, and no row may be dead-lettered as a side
        // effect of the disable transition.
        //
        // This mirrors OutboxDispatchSkipsDisabledModulesUntilTheyAreReEnabled
        // in IntegrationEventOutboxIntegrationTests, which is the established
        // pattern for module-state toggling against the real module-execution
        // gate without booting a separate platform module-state stack.
        await using var postgres = await StartPostgresAsync();
        var configuration = PostgresBackedApiApplication.CreateConfiguration(postgres.GetConnectionString());

        var faultPlan = ChaosFaultPlan.NoFaults();
        var probe = new ChaosOutboxProbe();
        var integrationEvent = new ChaosCatalogPublishedEvent(
            EventId: Guid.Parse("c1a05a4a-1f24-4a3a-9c46-77c3f50a5e04"),
            OccurredAt: Instant.FromUtc(2026, 4, 13, 11, 0),
            CatalogId: "chaos-disable");

        // Step 1: enqueue while enabled, then drop the provider without dispatching.
        await using (var enabledProvider = await BuildProviderAsync(configuration, probe, faultPlan, defaultEnabled: true))
        {
            var publisher = enabledProvider.GetRequiredService<IIntegrationEventOutboxPublisher>();
            await publisher.PublishAsync(
                new IntegrationEventOutboxPublishRequest("chaos", integrationEvent),
                CancellationToken.None);
        }

        // Step 2: disabled provider observes the row but refuses to dispatch it.
        await using (var disabledProvider = await BuildProviderAsync(configuration, probe, faultPlan, defaultEnabled: false))
        {
            var dispatcher = disabledProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
            var disabledDispatched = await dispatcher.DispatchAvailableAsync(CancellationToken.None);
            Assert.Equal(0, disabledDispatched);
        }

        Assert.Equal(0, probe.AttemptCount);

        // Row is parked — present, not processed, not dead-lettered.
        var parked = await ReadOutboxStateAsync(postgres.GetConnectionString(), integrationEvent.EventId);
        Assert.False(parked.IsProcessed);
        Assert.False(parked.IsDeadLettered);
        Assert.Equal(0, parked.Attempts);

        // Step 3: re-enable and re-dispatch — exactly-once delivery.
        await using (var reEnabledProvider = await BuildProviderAsync(configuration, probe, faultPlan, defaultEnabled: true))
        {
            var dispatcher = reEnabledProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
            var afterEnable = await dispatcher.DispatchAvailableAsync(CancellationToken.None);
            Assert.Equal(1, afterEnable);

            var finalDispatch = await dispatcher.DispatchAvailableAsync(CancellationToken.None);
            Assert.Equal(0, finalDispatch);
        }

        Assert.Equal(1, probe.AttemptCount);
        Assert.Equal(1, probe.SuccessCount);

        var delivered = await ReadOutboxStateAsync(postgres.GetConnectionString(), integrationEvent.EventId);
        Assert.True(delivered.IsProcessed);
        Assert.False(delivered.IsDeadLettered);
    }

    [Fact]
    public async Task OutboxDeadLettersImmediatelyWhenStoredEventTypeIsUnresolvable()
    {
        // F3 scenario 1. An outbox row with a stored event_type that does not resolve to any loaded IIntegrationEvent
        // is a deterministic failure: no number of retries will load the missing assembly into the running process.
        // The dispatcher must dead-letter on the very first attempt, mapped to the operator-actionable error code
        // `outbox.type_unresolvable`.
        await using var postgres = await StartPostgresAsync();
        var configuration = PostgresBackedApiApplication.CreateConfiguration(postgres.GetConnectionString());

        await using var provider = await BuildProviderAsync(
            configuration,
            new ChaosOutboxProbe(),
            ChaosFaultPlan.NoFaults(),
            new IntegrationEventOutboxProcessingOptions
            {
                BatchSize = 5,
                DeadLetterThreshold = 5, // deliberately high to prove immediate dead-letter bypasses the threshold
                LeaseDuration = TimeSpan.FromSeconds(5),
                PollInterval = TimeSpan.FromMilliseconds(50),
                RetryBackoff = TimeSpan.Zero
            });

        var dispatcher = provider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
        var eventId = Guid.Parse("c1a05a4a-1f24-4a3a-9c46-77c3f50a5e10");

        await InsertRawOutboxRowAsync(
            postgres.GetConnectionString(),
            eventId,
            storedEventType: "Unknown.Namespace.CatalogUnknownEvent",
            storedEventVersion: 1,
            payloadJson: "{\"eventId\":\"c1a05a4a-1f24-4a3a-9c46-77c3f50a5e10\",\"catalogId\":\"chaos-unknown\"}");

        Assert.Equal(0, await dispatcher.DispatchAvailableAsync(CancellationToken.None));

        var state = await ReadOutboxStateAsync(postgres.GetConnectionString(), eventId);
        Assert.True(state.IsDeadLettered, "Unresolvable type must dead-letter on the first attempt.");
        Assert.False(state.IsProcessed);
        Assert.Equal(1, state.Attempts);
        Assert.Equal("outbox.type_unresolvable", state.LastErrorCode);

        Assert.Equal(0, await dispatcher.DispatchAvailableAsync(CancellationToken.None));
    }

    [Fact]
    public async Task OutboxDeadLettersImmediatelyWhenStoredPayloadFailsToDeserialize()
    {
        // F3 scenario 2. A row whose payload JSON is not valid for its stored event_type is also deterministic —
        // the dispatcher must dead-letter on the first attempt with `outbox.deserialize_failed`. Retrying would only
        // waste I/O because the payload bytes will not change between attempts.
        await using var postgres = await StartPostgresAsync();
        var configuration = PostgresBackedApiApplication.CreateConfiguration(postgres.GetConnectionString());

        await using var provider = await BuildProviderAsync(
            configuration,
            new ChaosOutboxProbe(),
            ChaosFaultPlan.NoFaults(),
            new IntegrationEventOutboxProcessingOptions
            {
                BatchSize = 5,
                DeadLetterThreshold = 5,
                LeaseDuration = TimeSpan.FromSeconds(5),
                PollInterval = TimeSpan.FromMilliseconds(50),
                RetryBackoff = TimeSpan.Zero
            });

        var dispatcher = provider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
        var eventId = Guid.Parse("c1a05a4a-1f24-4a3a-9c46-77c3f50a5e11");

        // Stored event type is the real chaos event, but the payload is a JSON structure that cannot be bound to it:
        // `occurredAt` is required to be an Instant-serializable value, and `catalogId` must be a string. A numeric
        // value in its place triggers a deserialize error.
        await InsertRawOutboxRowAsync(
            postgres.GetConnectionString(),
            eventId,
            storedEventType: typeof(ChaosCatalogPublishedEvent).FullName!,
            storedEventVersion: 1,
            payloadJson: "{\"eventId\":\"c1a05a4a-1f24-4a3a-9c46-77c3f50a5e11\",\"occurredAt\":\"not-an-instant\",\"catalogId\":123}");

        Assert.Equal(0, await dispatcher.DispatchAvailableAsync(CancellationToken.None));

        var state = await ReadOutboxStateAsync(postgres.GetConnectionString(), eventId);
        Assert.True(state.IsDeadLettered, "Deserialize failure must dead-letter on the first attempt.");
        Assert.False(state.IsProcessed);
        Assert.Equal(1, state.Attempts);
        Assert.Equal("outbox.deserialize_failed", state.LastErrorCode);
    }

    [Fact]
    public async Task OutboxDeadLettersImmediatelyWhenDeserializedEventIdDoesNotMatchStoredEventId()
    {
        // F3 scenario 3. The stored event_id column and the event_id inside the payload JSON must match. If they do
        // not, the row has been corrupted or written by a buggy publisher — either way the dispatcher must dead-letter
        // on the first attempt with `outbox.event_id_mismatch`.
        await using var postgres = await StartPostgresAsync();
        var configuration = PostgresBackedApiApplication.CreateConfiguration(postgres.GetConnectionString());

        await using var provider = await BuildProviderAsync(
            configuration,
            new ChaosOutboxProbe(),
            ChaosFaultPlan.NoFaults(),
            new IntegrationEventOutboxProcessingOptions
            {
                BatchSize = 5,
                DeadLetterThreshold = 5,
                LeaseDuration = TimeSpan.FromSeconds(5),
                PollInterval = TimeSpan.FromMilliseconds(50),
                RetryBackoff = TimeSpan.Zero
            });

        var dispatcher = provider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
        var storedEventId = Guid.Parse("c1a05a4a-1f24-4a3a-9c46-77c3f50a5e12");
        var payloadEventId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var payload = $"{{\"eventId\":\"{payloadEventId}\",\"occurredAt\":\"2026-04-13T09:00:00Z\",\"catalogId\":\"chaos-mismatch\"}}";
        await InsertRawOutboxRowAsync(
            postgres.GetConnectionString(),
            storedEventId,
            storedEventType: typeof(ChaosCatalogPublishedEvent).FullName!,
            storedEventVersion: 1,
            payloadJson: payload);

        Assert.Equal(0, await dispatcher.DispatchAvailableAsync(CancellationToken.None));

        var state = await ReadOutboxStateAsync(postgres.GetConnectionString(), storedEventId);
        Assert.True(state.IsDeadLettered, "Event id mismatch must dead-letter on the first attempt.");
        Assert.False(state.IsProcessed);
        Assert.Equal(1, state.Attempts);
        Assert.Equal("outbox.event_id_mismatch", state.LastErrorCode);
    }

    private static async Task InsertRawOutboxRowAsync(
        string connectionString,
        Guid eventId,
        string storedEventType,
        int storedEventVersion,
        string payloadJson)
    {
        // `available_utc` must be strictly in the past so the dispatcher's `available_utc <= @now` lease predicate
        // matches even if the test and Postgres disagree about sub-second wall-clock alignment.
        const string sql = """
            INSERT INTO chaos.integration_outbox
                (event_id, event_type, event_version, payload, headers, occurred_utc, available_utc, attempts, leased_until_utc, processed_utc, dead_lettered_utc, last_error_code, last_error_message)
            VALUES
                (@eventId, @eventType, @eventVersion, @payload::jsonb, '{}'::jsonb, NOW() - INTERVAL '1 second', NOW() - INTERVAL '1 second', 0, NULL, NULL, NULL, NULL, NULL);
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("eventId", eventId);
        command.Parameters.AddWithValue("eventType", storedEventType);
        command.Parameters.AddWithValue("eventVersion", storedEventVersion);
        command.Parameters.AddWithValue("payload", payloadJson);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync()
    {
        var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("baseline")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync();
        return postgres;
    }

    private static async Task<ServiceProvider> BuildProviderAsync(
        IReadOnlyDictionary<string, string?> configuration,
        ChaosOutboxProbe probe,
        ChaosFaultPlan faultPlan,
        IntegrationEventOutboxProcessingOptions? options = null,
        bool defaultEnabled = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
        services.AddSingleton(probe);
        services.AddSingleton(faultPlan);
        services.AddSingleton<IModule>(new ChaosModule(defaultEnabled));
        services.AddSingleton<IDatabaseMigration, ChaosCatalogDatabaseMigration>();
        services.AddBuildingBlocksInfrastructureDefaults();
        if (options is not null)
        {
            services.AddSingleton(options);
        }

        services.AddDispatcher(typeof(OutboxResilienceChaosTests).Assembly);
        services.AddPostgresIntegrationEventOutbox("chaos", "chaos");
        services.AddPostgresIntegrationEventInbox("chaos", "chaos");

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IDatabaseMigrationRunner>().ApplyConfiguredMigrationsAsync(CancellationToken.None);
        return provider;
    }

    private static async Task<OutboxRowState> ReadOutboxStateAsync(string connectionString, Guid eventId)
    {
        const string sql = """
            SELECT attempts,
                processed_utc IS NOT NULL,
                dead_lettered_utc IS NOT NULL,
                last_error_code,
                last_error_message
            FROM chaos.integration_outbox
            WHERE event_id = @eventId;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("eventId", eventId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"Expected outbox row for event id '{eventId}'.");

        return new OutboxRowState(
            reader.GetInt32(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private static NpgsqlException MakeTransientNpgsqlException(string detail)
    {
        // Npgsql's exceptions can be expensive to construct from real socket
        // failures. The chaos plan only needs *an* NpgsqlException so the test
        // proves the dispatcher's retry loop catches DB-typed exceptions, not a
        // particular SqlState. The dispatcher does not currently switch on
        // exception type; a vanilla NpgsqlException is sufficient evidence.
        return new NpgsqlException(detail);
    }

    internal sealed record ChaosCatalogPublishedEvent(Guid EventId, Instant OccurredAt, string CatalogId) : IIntegrationEvent;

    internal sealed class ChaosOutboxProbe
    {
        private int _attemptCount;
        private int _successCount;
        private int _transientFaultsInjected;

        public int AttemptCount => Volatile.Read(ref _attemptCount);

        public int SuccessCount => Volatile.Read(ref _successCount);

        public int TransientFaultsInjected => Volatile.Read(ref _transientFaultsInjected);

        public void RecordAttempt() => Interlocked.Increment(ref _attemptCount);

        public void RecordSuccess() => Interlocked.Increment(ref _successCount);

        public void RecordTransientFaultInjected() => Interlocked.Increment(ref _transientFaultsInjected);
    }

    internal sealed class ChaosFaultPlan
    {
        private readonly Func<ChaosCatalogPublishedEvent, ChaosOutboxProbe, Exception?> _decide;

        private ChaosFaultPlan(Func<ChaosCatalogPublishedEvent, ChaosOutboxProbe, Exception?> decide)
        {
            _decide = decide;
        }

        public Exception? GetFaultFor(ChaosCatalogPublishedEvent integrationEvent, ChaosOutboxProbe probe)
        {
            return _decide(integrationEvent, probe);
        }

        public static ChaosFaultPlan NoFaults() => new((_, _) => null);

        public static ChaosFaultPlan AlwaysThrow(Func<Exception> factory) => new((_, _) => factory());

        public static ChaosFaultPlan NTransientThenSucceed(int count, Func<Exception> transientFactory)
        {
            var injected = 0;
            return new ChaosFaultPlan((_, probe) =>
            {
                if (Interlocked.Increment(ref injected) > count)
                {
                    return null;
                }

                probe.RecordTransientFaultInjected();
                return transientFactory();
            });
        }

        public static ChaosFaultPlan OneTransientForCatalog(string catalogId, Func<Exception> transientFactory)
        {
            var triggered = 0;
            return new ChaosFaultPlan((@event, probe) =>
            {
                if (!string.Equals(@event.CatalogId, catalogId, StringComparison.Ordinal))
                {
                    return null;
                }

                if (Interlocked.Increment(ref triggered) > 1)
                {
                    return null;
                }

                probe.RecordTransientFaultInjected();
                return transientFactory();
            });
        }
    }

    internal sealed class ChaosCatalogPublishedHandler : IModuleScopedIntegrationEventHandler<ChaosCatalogPublishedEvent>
    {
        private readonly ChaosOutboxProbe _probe;
        private readonly ChaosFaultPlan _faultPlan;

        public ChaosCatalogPublishedHandler(ChaosOutboxProbe probe, ChaosFaultPlan faultPlan)
        {
            _probe = probe;
            _faultPlan = faultPlan;
        }

        public string ModuleKey => "chaos";

        public Task Handle(ChaosCatalogPublishedEvent integrationEvent, CancellationToken cancellationToken)
        {
            _probe.RecordAttempt();
            var fault = _faultPlan.GetFaultFor(integrationEvent, _probe);
            if (fault is not null)
            {
                throw fault;
            }

            _probe.RecordSuccess();
            return Task.CompletedTask;
        }
    }

    internal sealed class ChaosModule : IModule
    {
        public ChaosModule(bool defaultEnabled)
        {
            Descriptor = new ModuleDescriptor(
                "chaos",
                "Chaos",
                "/api/chaos",
                "chaos",
                "chaos",
                DefaultEnabled: defaultEnabled,
                CanBeDisabled: true);
        }

        public ModuleDescriptor Descriptor { get; }

        public string Key => "chaos";
    }

    internal sealed class ChaosCatalogDatabaseMigration : IDatabaseMigration
    {
        private readonly string _connectionString;

        public ChaosCatalogDatabaseMigration(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString(SharedRuntimePersistenceDefaults.ConnectionStringName)
                ?? throw new InvalidOperationException("Chaos schema migration requires the shared runtime connection string.");
        }

        public string Name => "chaos_schema";

        public string? ConnectionString => _connectionString;

        public async Task<IReadOnlyCollection<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
        {
            const string sql = """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.schemata
                    WHERE schema_name = 'chaos');
                """;

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            var exists = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
            return exists ? [] : ["chaos.schema"];
        }

        public async Task ApplyAsync(CancellationToken cancellationToken)
        {
            const string sql = """
                CREATE SCHEMA IF NOT EXISTS chaos;
                """;

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private sealed record OutboxRowState(
        int Attempts,
        bool IsProcessed,
        bool IsDeadLettered,
        string? LastErrorCode,
        string? LastErrorMessage);
}
