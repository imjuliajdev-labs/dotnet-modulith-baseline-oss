using BuildingBlocks.Application.ProcessManagers;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.Persistence;
using BuildingBlocks.Infrastructure.ProcessManagers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Testcontainers.PostgreSql;
using Xunit;

namespace Integration.Tests;

public sealed class ProcessManagerCheckpointIntegrationTests
{
    [Fact]
    public async Task CheckpointsPersistAcrossRestartsAndSupportCompensationTransitions()
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
        var startedAt = Instant.FromUtc(2026, 4, 3, 12, 15);
        var failedAt = Instant.FromUtc(2026, 4, 3, 12, 20);
        var compensatedAt = Instant.FromUtc(2026, 4, 3, 12, 25);
        const string processManagerName = "AccountProvisioningProcessManager";
        const string processId = "account-123";

        await using (var firstProvider = await BuildProviderAsync(configuration))
        {
            var store = firstProvider.GetRequiredService<IProcessManagerCheckpointStore>();
            var initialCheckpoint = new ProcessManagerCheckpoint<ProvisioningState>(
                "accounts",
                processManagerName,
                processId,
                ProcessManagerLifecycleState.Running,
                Version: 0,
                startedAt,
                new ProvisioningState("reserve-inventory", startedAt, IsCompensated: false, FailureCode: null));

            var saved = await store.SaveAsync(initialCheckpoint, CancellationToken.None);

            Assert.Equal(1, saved.Version);
        }

        await using (var secondProvider = await BuildProviderAsync(configuration))
        {
            var store = secondProvider.GetRequiredService<IProcessManagerCheckpointStore>();
            var checkpoint = await store.LoadAsync<ProvisioningState>("accounts", processManagerName, processId, CancellationToken.None);

            Assert.NotNull(checkpoint);
            Assert.Equal(startedAt, checkpoint!.State.StartedAt);
            Assert.Equal("reserve-inventory", checkpoint.State.Step);

            var failedCheckpoint = checkpoint with
            {
                LifecycleState = ProcessManagerLifecycleState.Failed,
                UpdatedAt = failedAt,
                State = checkpoint.State with { FailureCode = "inventory.timeout" },
                Failure = new Error("inventory.timeout", "Inventory reservation timed out.")
            };

            var savedFailure = await store.SaveAsync(failedCheckpoint, CancellationToken.None);
            Assert.Equal(2, savedFailure.Version);
        }

        await using (var thirdProvider = await BuildProviderAsync(configuration))
        {
            var store = thirdProvider.GetRequiredService<IProcessManagerCheckpointStore>();
            var failedCheckpoint = await store.LoadAsync<ProvisioningState>("accounts", processManagerName, processId, CancellationToken.None);

            Assert.NotNull(failedCheckpoint);
            Assert.Equal(ProcessManagerLifecycleState.Failed, failedCheckpoint!.LifecycleState);
            Assert.Equal("inventory.timeout", failedCheckpoint.State.FailureCode);

            var compensatedCheckpoint = failedCheckpoint with
            {
                LifecycleState = ProcessManagerLifecycleState.Compensated,
                UpdatedAt = compensatedAt,
                CompletedAt = compensatedAt,
                State = failedCheckpoint.State with
                {
                    Step = "release-reservation",
                    IsCompensated = true,
                    FailureCode = null
                },
                Failure = null
            };

            var savedCompensation = await store.SaveAsync(compensatedCheckpoint, CancellationToken.None);
            Assert.Equal(3, savedCompensation.Version);

            var reloaded = await store.LoadAsync<ProvisioningState>("accounts", processManagerName, processId, CancellationToken.None);

            Assert.NotNull(reloaded);
            Assert.Equal(ProcessManagerLifecycleState.Compensated, reloaded!.LifecycleState);
            Assert.Equal(compensatedAt, reloaded.CompletedAt);
            Assert.True(reloaded.State.IsCompensated);
            Assert.Equal("release-reservation", reloaded.State.Step);
            Assert.Equal(3, reloaded.Version);
        }
    }

    [Fact]
    public async Task CheckpointListingFiltersLifecycleStatesAndOrdersOldestFirst()
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
        const string processManagerName = "AccountProvisioningProcessManager";

        await using (var provider = await BuildProviderAsync(configuration))
        {
            var store = provider.GetRequiredService<IProcessManagerCheckpointStore>();

            await store.SaveAsync(
                new ProcessManagerCheckpoint<ProvisioningState>(
                    "accounts",
                    processManagerName,
                    "alpha",
                    ProcessManagerLifecycleState.Failed,
                    Version: 0,
                    UpdatedAt: Instant.FromUtc(2026, 4, 4, 13, 0),
                    State: new ProvisioningState("alpha", Instant.FromUtc(2026, 4, 4, 12, 55), false, "inventory.timeout"),
                    Failure: new Error("inventory.timeout", "Alpha failed.")),
                CancellationToken.None);

            await store.SaveAsync(
                new ProcessManagerCheckpoint<ProvisioningState>(
                    "accounts",
                    processManagerName,
                    "bravo",
                    ProcessManagerLifecycleState.Running,
                    Version: 0,
                    UpdatedAt: Instant.FromUtc(2026, 4, 4, 12, 50),
                    State: new ProvisioningState("bravo", Instant.FromUtc(2026, 4, 4, 12, 45), false, null)),
                CancellationToken.None);

            await store.SaveAsync(
                new ProcessManagerCheckpoint<ProvisioningState>(
                    "accounts",
                    processManagerName,
                    "charlie",
                    ProcessManagerLifecycleState.Completed,
                    Version: 0,
                    UpdatedAt: Instant.FromUtc(2026, 4, 4, 13, 5),
                    State: new ProvisioningState("charlie", Instant.FromUtc(2026, 4, 4, 12, 40), false, null),
                    CompletedAt: Instant.FromUtc(2026, 4, 4, 13, 5)),
                CancellationToken.None);

            var checkpoints = await store.ListAsync<ProvisioningState>(
                "accounts",
                processManagerName,
                [ProcessManagerLifecycleState.Running, ProcessManagerLifecycleState.Failed],
                updatedBefore: Instant.FromUtc(2026, 4, 4, 13, 1),
                limit: 10,
                CancellationToken.None);

            Assert.Equal(2, checkpoints.Count);
            Assert.Collection(
                checkpoints,
                checkpoint =>
                {
                    Assert.Equal("bravo", checkpoint.ProcessId);
                    Assert.Equal(ProcessManagerLifecycleState.Running, checkpoint.LifecycleState);
                },
                checkpoint =>
                {
                    Assert.Equal("alpha", checkpoint.ProcessId);
                    Assert.Equal(ProcessManagerLifecycleState.Failed, checkpoint.LifecycleState);
                });
        }
    }

    private static async Task<ServiceProvider> BuildProviderAsync(IReadOnlyDictionary<string, string?> configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
        services.AddBuildingBlocksInfrastructureDefaults();
        services.AddPostgresProcessManagerCheckpoints("accounts", "accounts");

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IDatabaseMigrationRunner>().ApplyConfiguredMigrationsAsync(CancellationToken.None);
        return provider;
    }

    private sealed record ProvisioningState(
        string Step,
        Instant StartedAt,
        bool IsCompensated,
        string? FailureCode);
}
