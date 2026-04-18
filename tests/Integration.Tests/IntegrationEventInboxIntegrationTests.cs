using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Domain.Events;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.IntegrationEvents;
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Testcontainers.PostgreSql;
using Xunit;

namespace Integration.Tests;

public sealed class IntegrationEventInboxIntegrationTests
{
    [Fact]
    public async Task DuplicateIntegrationDeliveriesAreDeduplicatedAcrossProviderRestarts()
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
        var probe = new DeliveryProbe();
        var integrationEvent = new DocumentIndexedEvent(
            Guid.Parse("8d37819e-1d50-4e44-b98e-4e60fb94f53c"),
            Instant.FromUtc(2026, 4, 3, 12, 0),
            "doc-123");

        await using (var firstProvider = await BuildProviderAsync(configuration, probe))
        {
            var dispatcher = firstProvider.GetRequiredService<IIntegrationEventDispatcher>();
            await dispatcher.PublishAsync(integrationEvent, CancellationToken.None);
        }

        await using (var secondProvider = await BuildProviderAsync(configuration, probe))
        {
            var dispatcher = secondProvider.GetRequiredService<IIntegrationEventDispatcher>();
            await dispatcher.PublishAsync(integrationEvent, CancellationToken.None);
        }

        Assert.Equal(1, probe.ExecutionCount);
    }

    [Fact]
    public async Task DisabledModulesRejectIntegrationDeliveryUntilTheyAreReEnabled()
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
        var probe = new DeliveryProbe();
        var integrationEvent = new DocumentIndexedEvent(
            Guid.Parse("c1df1a24-3677-496d-82d3-909a7f847612"),
            Instant.FromUtc(2026, 4, 3, 12, 30),
            "doc-disabled");

        await using (var disabledProvider = await BuildProviderAsync(configuration, probe, defaultEnabled: false))
        {
            var dispatcher = disabledProvider.GetRequiredService<IIntegrationEventDispatcher>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.PublishAsync(integrationEvent, CancellationToken.None));
        }

        Assert.Equal(0, probe.ExecutionCount);

        await using (var enabledProvider = await BuildProviderAsync(configuration, probe, defaultEnabled: true))
        {
            var dispatcher = enabledProvider.GetRequiredService<IIntegrationEventDispatcher>();
            await dispatcher.PublishAsync(integrationEvent, CancellationToken.None);
        }

        Assert.Equal(1, probe.ExecutionCount);
    }

    private static async Task<ServiceProvider> BuildProviderAsync(
        IReadOnlyDictionary<string, string?> configuration,
        DeliveryProbe probe,
        bool defaultEnabled = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
        services.AddSingleton(probe);
        services.AddSingleton<IModule>(new ReportsModule(defaultEnabled));
        services.AddBuildingBlocksInfrastructureDefaults();
        services.AddPostgresIntegrationEventInbox("reports", "reports");
        services.AddDispatcher(typeof(DocumentIndexedHandler).Assembly);

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IDatabaseMigrationRunner>().ApplyConfiguredMigrationsAsync(CancellationToken.None);
        return provider;
    }

    private sealed record DocumentIndexedEvent(Guid EventId, Instant OccurredAt, string DocumentId) : IIntegrationEvent;

    private sealed class DocumentIndexedHandler : IModuleScopedIntegrationEventHandler<DocumentIndexedEvent>
    {
        private readonly DeliveryProbe _probe;

        public DocumentIndexedHandler(DeliveryProbe probe)
        {
            _probe = probe;
        }

        public string ModuleKey => "reports";

        public Task Handle(DocumentIndexedEvent integrationEvent, CancellationToken cancellationToken)
        {
            _probe.RecordExecution();
            return Task.CompletedTask;
        }
    }

    private sealed class DeliveryProbe
    {
        private int _executionCount;

        public int ExecutionCount => Volatile.Read(ref _executionCount);

        public void RecordExecution()
        {
            Interlocked.Increment(ref _executionCount);
        }
    }

    private sealed class ReportsModule : IModule
    {
        public ReportsModule(bool defaultEnabled)
        {
            Descriptor = new ModuleDescriptor(
                "reports",
                "Reports",
                "/api/reports",
                "reports",
                "reports",
                DefaultEnabled: defaultEnabled,
                CanBeDisabled: true);
        }

        public ModuleDescriptor Descriptor { get; }

        public string Key => "reports";
    }
}
