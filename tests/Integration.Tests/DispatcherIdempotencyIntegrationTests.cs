using System.Diagnostics;
using BuildingBlocks.Application.Actors;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.Dispatching;
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Integration.Tests;

public sealed class DispatcherIdempotencyIntegrationTests
{
    [Xunit.Fact]
    public async Task BuildingBlocksInfrastructureDefaultsRequireDatabaseConfigurationForIdempotentCommands()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new IntegrationInvocationTracker());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddBuildingBlocksInfrastructureDefaults();
        services.AddDispatcher();
        services.AddScoped<ICommandHandler<IntegrationIdempotentCommand, string>, IntegrationIdempotentCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var exception = Xunit.Assert.Throws<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<ICommandIdempotencyStore>());

        Xunit.Assert.Equal(SharedRuntimePersistenceDefaults.MissingConnectionStringMessage, exception.Message);
    }

    [Xunit.Fact]
    public async Task BuildingBlocksInfrastructureDefaultsUsePostgresIdempotencyAndReplayAcrossProviderRestart()
    {
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync("idempotency_" + Guid.NewGuid().ToString("N"));
        var configuration = CreateConfiguration(postgres.GetConnectionString());

        await using (var firstProvider = await BuildProviderAsync(configuration))
        {
            await using var firstScope = firstProvider.CreateAsyncScope();
            Xunit.Assert.IsType<PostgresCommandIdempotencyStore>(firstScope.ServiceProvider.GetRequiredService<ICommandIdempotencyStore>());

            var dispatcher = firstScope.ServiceProvider.GetRequiredService<IDispatcher>();
            var tracker = firstScope.ServiceProvider.GetRequiredService<IntegrationInvocationTracker>();

            var first = await dispatcher.Send(new IntegrationIdempotentCommand("request-2", "alpha"));
            var replay = await dispatcher.Send(new IntegrationIdempotentCommand("request-2", "alpha"));

            Xunit.Assert.True(first.IsSuccess);
            Xunit.Assert.True(replay.IsSuccess);
            Xunit.Assert.Equal("alpha-handled", first.Value);
            Xunit.Assert.Equal(first.Value, replay.Value);
            Xunit.Assert.Equal(1, tracker.HandlerInvocations);
        }

        await using (var secondProvider = await BuildProviderAsync(configuration))
        {
            await using var secondScope = secondProvider.CreateAsyncScope();

            var dispatcher = secondScope.ServiceProvider.GetRequiredService<IDispatcher>();
            var tracker = secondScope.ServiceProvider.GetRequiredService<IntegrationInvocationTracker>();

            var replayAfterRestart = await dispatcher.Send(new IntegrationIdempotentCommand("request-2", "alpha"));

            Xunit.Assert.True(replayAfterRestart.IsSuccess);
            Xunit.Assert.Equal("alpha-handled", replayAfterRestart.Value);
            Xunit.Assert.Equal(0, tracker.HandlerInvocations);
        }
    }

    [Xunit.Fact]
    public async Task DifferentAuthenticatedCallersUseSeparateIdempotencyTracks()
    {
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync("idempotency_callers_" + Guid.NewGuid().ToString("N"));
        var configuration = CreateConfiguration(postgres.GetConnectionString());

        await using (var firstProvider = await BuildProviderAsync(configuration, actorId: "identity:user:alpha"))
        {
            await using var firstScope = firstProvider.CreateAsyncScope();
            var dispatcher = firstScope.ServiceProvider.GetRequiredService<IDispatcher>();
            var tracker = firstScope.ServiceProvider.GetRequiredService<IntegrationInvocationTracker>();

            var first = await dispatcher.Send(new IntegrationIdempotentCommand("request-actor-split", "alpha"));

            Xunit.Assert.True(first.IsSuccess);
            Xunit.Assert.Equal("alpha-handled", first.Value);
            Xunit.Assert.Equal(1, tracker.HandlerInvocations);
        }

        await using (var secondProvider = await BuildProviderAsync(configuration, actorId: "identity:user:bravo"))
        {
            await using var secondScope = secondProvider.CreateAsyncScope();
            var dispatcher = secondScope.ServiceProvider.GetRequiredService<IDispatcher>();
            var tracker = secondScope.ServiceProvider.GetRequiredService<IntegrationInvocationTracker>();

            var second = await dispatcher.Send(new IntegrationIdempotentCommand("request-actor-split", "alpha"));

            Xunit.Assert.True(second.IsSuccess);
            Xunit.Assert.Equal("alpha-handled", second.Value);
            Xunit.Assert.Equal(1, tracker.HandlerInvocations);
        }
    }

    [Xunit.Fact]
    public async Task ExpiredCompletedEntriesArePurgedAndTheSameKeyCanBeReused()
    {
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync("idempotency_retention_" + Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BaselineDatabase"] = postgres.GetConnectionString(),
                ["SharedRuntime:CommandIdempotency:RunningEntryTtl"] = "00:00:01",
                ["SharedRuntime:CommandIdempotency:CompletedEntryTtl"] = "00:00:00.3000000",
                ["SharedRuntime:CommandIdempotency:AbandonedEntryTtl"] = "00:00:00.3000000",
                ["SharedRuntime:CommandIdempotency:CleanupInterval"] = "00:00:00.1000000"
            })
            .Build();

        using var host = await BuildHostAsync(configuration, actorId: "identity:user:alpha");

        IntegrationInvocationTracker tracker;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            tracker = scope.ServiceProvider.GetRequiredService<IntegrationInvocationTracker>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

            var first = await dispatcher.Send(new IntegrationIdempotentCommand("request-retention", "alpha"));

            Xunit.Assert.True(first.IsSuccess);
            Xunit.Assert.Equal("alpha-handled", first.Value);
            Xunit.Assert.Equal(1, tracker.HandlerInvocations);
        }

        await WaitForEntryCountAsync(postgres.GetConnectionString(), expectedCount: 0, timeout: TimeSpan.FromSeconds(5));

        await using (var secondScope = host.Services.CreateAsyncScope())
        {
            var dispatcher = secondScope.ServiceProvider.GetRequiredService<IDispatcher>();

            var replayAfterExpiry = await dispatcher.Send(new IntegrationIdempotentCommand("request-retention", "alpha"));

            Xunit.Assert.True(replayAfterExpiry.IsSuccess);
            Xunit.Assert.Equal("alpha-handled", replayAfterExpiry.Value);
            Xunit.Assert.Equal(2, tracker.HandlerInvocations);
        }
    }

    private static IConfiguration CreateConfiguration(string connectionString)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BaselineDatabase"] = connectionString
            })
            .Build();
    }

    private static async Task<ServiceProvider> BuildProviderAsync(IConfiguration configuration, string? actorId = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton(new IntegrationInvocationTracker());
        services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        services.AddBuildingBlocksInfrastructureDefaults();
        services.AddDispatcher();
        services.AddScoped<ICommandHandler<IntegrationIdempotentCommand, string>, IntegrationIdempotentCommandHandler>();

        if (!string.IsNullOrWhiteSpace(actorId))
        {
            services.AddScoped<ICurrentActorAccessor>(_ => new StubCurrentActorAccessor(actorId));
        }

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IDatabaseMigrationRunner>().ApplyConfiguredMigrationsAsync(CancellationToken.None);
        return provider;
    }

    private static async Task<IHost> BuildHostAsync(IConfiguration configuration, string? actorId = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddLogging();
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
        builder.Services.AddSingleton(new IntegrationInvocationTracker());
        builder.Services.AddSingleton<IModuleStateGuard>(new StubModuleStateGuard(Error.None));
        builder.Services.AddBuildingBlocksInfrastructureDefaults();
        builder.Services.AddDispatcher();
        builder.Services.AddScoped<ICommandHandler<IntegrationIdempotentCommand, string>, IntegrationIdempotentCommandHandler>();

        if (!string.IsNullOrWhiteSpace(actorId))
        {
            builder.Services.AddScoped<ICurrentActorAccessor>(_ => new StubCurrentActorAccessor(actorId));
        }

        var host = builder.Build();
        await host.Services.GetRequiredService<IDatabaseMigrationRunner>().ApplyConfiguredMigrationsAsync(CancellationToken.None);
        await host.StartAsync();
        return host;
    }

    private static async Task WaitForEntryCountAsync(string connectionString, int expectedCount, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (await CountEntriesAsync(connectionString) == expectedCount)
            {
                return;
            }

            await Task.Delay(100);
        }

        Xunit.Assert.Equal(expectedCount, await CountEntriesAsync(connectionString));
    }

    private static async Task<int> CountEntriesAsync(string connectionString)
    {
        const string sql = $$"""
            SELECT COUNT(*)
            FROM {{SharedRuntimePersistenceDefaults.SchemaName}}.{{SharedRuntimePersistenceDefaults.CommandIdempotencyTableName}};
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public sealed record IntegrationIdempotentCommand(string RequestKey, string Value) : IIdempotentCommand<string>;

    private sealed class IntegrationIdempotentCommandHandler : ICommandHandler<IntegrationIdempotentCommand, string>
    {
        private readonly IntegrationInvocationTracker _tracker;

        public IntegrationIdempotentCommandHandler(IntegrationInvocationTracker tracker)
        {
            _tracker = tracker;
        }

        public Task<Result<string>> Handle(IntegrationIdempotentCommand command, CancellationToken cancellationToken)
        {
            _tracker.HandlerInvocations++;
            return Task.FromResult(Result<string>.Success(command.Value + "-handled"));
        }
    }

    private sealed class IntegrationInvocationTracker
    {
        public int HandlerInvocations { get; set; }
    }

    private sealed class StubCurrentActorAccessor : ICurrentActorAccessor
    {
        private readonly string _actorId;

        public StubCurrentActorAccessor(string actorId)
        {
            _actorId = actorId;
        }

        public ValueTask<CurrentActor> GetCurrentAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new CurrentActor(_actorId, isAuthenticated: true));
        }
    }

    private sealed class StubModuleStateGuard : IModuleStateGuard
    {
        private readonly Error _error;

        public StubModuleStateGuard(Error error)
        {
            _error = error;
        }

        public ValueTask<Error> GetFailureOrNoneAsync(string moduleKey, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_error);
        }
    }
}
