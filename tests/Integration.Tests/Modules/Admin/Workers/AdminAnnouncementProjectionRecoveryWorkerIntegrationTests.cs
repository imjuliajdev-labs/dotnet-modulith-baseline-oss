using Admin.Application.ProcessManagers;
using Admin.Infrastructure.Inbox;
using Admin.PublicContracts.Queries;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.ProcessManagers;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NodaTime;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests.Modules.Admin.Workers;

public sealed class AdminAnnouncementProjectionRecoveryWorkerIntegrationTests
{
    [Xunit.Fact]
    public async Task AdminRecoveryWorkerCompletesFailedAnnouncementProjectionBeforeOutboxRetry()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync(
            configurationOverrides: CreateWorkerConfiguration(),
            configureBuilder: builder =>
            {
                builder.Services.Replace(ServiceDescriptor.Singleton<global::Admin.Application.Consumers.IAdminAnnouncementInbox, ThrowOnceAdminAnnouncementInbox>());
            });

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAsync(client);


        var authCookie = session.Cookie;


        var cookies = session.Cookies;

        var publishResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/sample-feature/announcements",
            new { title = "Recover this announcement", body = "The Admin worker should recover the failed checkpoint." },
            cookies,
            session.HeaderName,
            session.RequestToken,
            "admin-worker-recovery-1");

        Xunit.Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

        using var publishJson = await ReadJsonAsync(publishResponse);
        var announcementId = publishJson.RootElement.GetProperty("announcementId").GetGuid();

        using (var scope = application.App.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
            var dispatched = await dispatcher.DispatchAvailableAsync(CancellationToken.None);
            Xunit.Assert.Equal(0, dispatched);
        }

        await WaitForConditionAsync(
            async () =>
            {
                using var scope = application.App.Services.CreateScope();
                var checkpointStore = scope.ServiceProvider.GetRequiredService<IProcessManagerCheckpointStore>();
                var checkpoint = await checkpointStore.LoadAsync<AdminAnnouncementProjectionProcessState>(
                    "admin",
                    AdminAnnouncementProjectionProcessManager.ProcessManagerName,
                    announcementId.ToString("N"),
                    CancellationToken.None);

                return checkpoint is { LifecycleState: ProcessManagerLifecycleState.Completed } completed
                    && completed.State.ProjectionStored;
            },
            TimeSpan.FromSeconds(3));

        using (var scope = application.App.Services.CreateScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAdminAnnouncementQueryService>();
            var announcement = await reader.GetAsync(announcementId, CancellationToken.None);

            Xunit.Assert.NotNull(announcement);
            Xunit.Assert.Equal("Recover this announcement", announcement!.Title);
        }
    }

    [Xunit.Fact]
    public async Task AdminRecoveryWorkerWaitsUntilModuleIsReEnabledBeforeRecovering()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync(configurationOverrides: CreateWorkerConfiguration());
        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAsync(client);


        var authCookie = session.Cookie;


        var cookies = session.Cookies;

        var disableResponse = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/admin/disable",
            cookies,
            session.HeaderName,
            session.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

        var announcementId = Guid.Parse("4ec99668-c29a-42f5-90dd-5821285f4fb8");
        await SeedFailedCheckpointAsync(application.App.Services, announcementId);

        await Task.Delay(TimeSpan.FromMilliseconds(500));

        using (var scope = application.App.Services.CreateScope())
        {
            var checkpointStore = scope.ServiceProvider.GetRequiredService<IProcessManagerCheckpointStore>();
            var checkpoint = await checkpointStore.LoadAsync<AdminAnnouncementProjectionProcessState>(
                "admin",
                AdminAnnouncementProjectionProcessManager.ProcessManagerName,
                announcementId.ToString("N"),
                CancellationToken.None);

            Xunit.Assert.NotNull(checkpoint);
            Xunit.Assert.Equal(ProcessManagerLifecycleState.Failed, checkpoint!.LifecycleState);

            var reader = scope.ServiceProvider.GetRequiredService<IAdminAnnouncementQueryService>();
            Xunit.Assert.Null(await reader.GetAsync(announcementId, CancellationToken.None));
        }

        var enableResponse = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/admin/enable",
            cookies,
            session.HeaderName,
            session.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        await WaitForConditionAsync(
            async () =>
            {
                using var scope = application.App.Services.CreateScope();
                var checkpointStore = scope.ServiceProvider.GetRequiredService<IProcessManagerCheckpointStore>();
                var checkpoint = await checkpointStore.LoadAsync<AdminAnnouncementProjectionProcessState>(
                    "admin",
                    AdminAnnouncementProjectionProcessManager.ProcessManagerName,
                    announcementId.ToString("N"),
                    CancellationToken.None);

                return checkpoint is { LifecycleState: ProcessManagerLifecycleState.Completed } completed
                    && completed.State.ProjectionStored;
            },
            TimeSpan.FromSeconds(3));
    }

    private static IReadOnlyDictionary<string, string?> CreateWorkerConfiguration()
    {
        return new Dictionary<string, string?>
        {
            ["Modules:Admin:ProjectionRecoveryWorker:PollIntervalMilliseconds"] = "100",
            ["Modules:Admin:ProjectionRecoveryWorker:StaleAfterMilliseconds"] = "100",
            ["Modules:Admin:ProjectionRecoveryWorker:BatchSize"] = "10"
        };
    }

    private static async Task SeedFailedCheckpointAsync(IServiceProvider services, Guid announcementId)
    {
        using var scope = services.CreateScope();
        var checkpointStore = scope.ServiceProvider.GetRequiredService<IProcessManagerCheckpointStore>();
        // Fixed sentinel in the past: the worker's StaleAfterMilliseconds guard treats anything
        // older than a few milliseconds as stale, so a hard-coded past Instant is sufficient and
        // keeps the test independent of wall-clock time.
        var occurredAt = Instant.FromUtc(2024, 1, 1, 0, 0);

        await checkpointStore.SaveAsync(
            new ProcessManagerCheckpoint<AdminAnnouncementProjectionProcessState>(
                "admin",
                AdminAnnouncementProjectionProcessManager.ProcessManagerName,
                announcementId.ToString("N"),
                ProcessManagerLifecycleState.Failed,
                Version: 0,
                UpdatedAt: occurredAt,
                State: new AdminAnnouncementProjectionProcessState(
                    announcementId,
                    "Seeded recovery candidate",
                    "The worker should recover this after Admin is re-enabled.",
                    occurredAt.ToDateTimeOffset(),
                    "admin",
                    "sample-feature",
                    null,
                    Step: "projection-failed",
                    ProjectionStored: false,
                    FailureCode: "admin.announcement_projection_failed"),
                Failure: new BuildingBlocks.Application.Results.Error(
                    "admin.announcement_projection_failed",
                    "Seeded to prove worker recovery.")),
            CancellationToken.None);
    }

    private static async Task WaitForConditionAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        while (!deadline.Token.IsCancellationRequested)
        {
            if (await condition())
            {
                return;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), deadline.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Xunit.Assert.Fail($"Condition was not satisfied within {timeout}.");
    }
    private static JsonElement GetModule(JsonDocument document, string moduleKey)
    {
        return document.RootElement
            .GetProperty("modules")
            .EnumerateArray()
            .Single(element => string.Equals(element.GetProperty("key").GetString(), moduleKey, StringComparison.Ordinal));
    }
    [Xunit.Fact]
    public async Task AdminRecoveryWorkerLetsInFlightRecoveryDrainBeforeDisableFinalizes()
    {
        var coordinator = new BlockingAdminAnnouncementInboxCoordinator();

        await using var application = await PostgresBackedApiApplication.StartAsync(
            configurationOverrides: CreateWorkerConfiguration(),
            configureBuilder: builder =>
            {
                builder.Services.AddSingleton(coordinator);
                builder.Services.Replace(ServiceDescriptor.Singleton<global::Admin.Application.Consumers.IAdminAnnouncementInbox, BlockingAdminAnnouncementInbox>());
            });

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAsync(client);


        var authCookie = session.Cookie;


        var cookies = session.Cookies;

        var announcementId = Guid.Parse("b7e2a5c1-3f84-4d19-9c72-6e8a1b3d4f5e");
        await SeedFailedCheckpointAsync(application.App.Services, announcementId);

        await coordinator.WaitForStoreStartedAsync().WaitAsync(TimeSpan.FromSeconds(10));

        var disableTask = SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/admin/disable",
            cookies,
            session.HeaderName,
            session.RequestToken);

        await WaitForConditionAsync(
            async () =>
            {
                var modulesResponse = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/modules");
                if (modulesResponse.StatusCode != HttpStatusCode.OK)
                {
                    return false;
                }

                using var modulesJson = await ReadJsonAsync(modulesResponse);
                var admin = GetModule(modulesJson, "admin");
                return string.Equals(admin.GetProperty("desiredState").GetString(), "disabled", StringComparison.Ordinal)
                    && string.Equals(admin.GetProperty("runtimeState").GetString(), "disabling", StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));

        Xunit.Assert.False(disableTask.IsCompleted, "Disable should wait for the in-flight recovery lease to drain.");

        using (var scope = application.App.Services.CreateScope())
        {
            var checkpointStore = scope.ServiceProvider.GetRequiredService<IProcessManagerCheckpointStore>();
            var checkpoint = await checkpointStore.LoadAsync<AdminAnnouncementProjectionProcessState>(
                "admin",
                AdminAnnouncementProjectionProcessManager.ProcessManagerName,
                announcementId.ToString("N"),
                CancellationToken.None);

            Xunit.Assert.NotNull(checkpoint);
            Xunit.Assert.Equal(ProcessManagerLifecycleState.Failed, checkpoint!.LifecycleState);
        }

        coordinator.ReleaseStore();

        var disableResponse = await disableTask.WaitAsync(TimeSpan.FromSeconds(10));
        Xunit.Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

        await WaitForConditionAsync(
            async () =>
            {
                using var scope = application.App.Services.CreateScope();
                var checkpointStore = scope.ServiceProvider.GetRequiredService<IProcessManagerCheckpointStore>();
                var checkpoint = await checkpointStore.LoadAsync<AdminAnnouncementProjectionProcessState>(
                    "admin",
                    AdminAnnouncementProjectionProcessManager.ProcessManagerName,
                    announcementId.ToString("N"),
                    CancellationToken.None);

                return checkpoint is { LifecycleState: ProcessManagerLifecycleState.Completed } completed
                    && completed.State.ProjectionStored;
            },
            TimeSpan.FromSeconds(5));

        using (var scope = application.App.Services.CreateScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAdminAnnouncementQueryService>();
            var announcement = await reader.GetAsync(announcementId, CancellationToken.None);

            Xunit.Assert.NotNull(announcement);
            Xunit.Assert.Equal("Seeded recovery candidate", announcement!.Title);
        }
    }

    private sealed class BlockingAdminAnnouncementInboxCoordinator
    {
        private readonly TaskCompletionSource _releaseStore = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _storeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitForStoreStartedAsync()
        {
            return _storeStarted.Task;
        }

        public void MarkStoreStarted()
        {
            _storeStarted.TrySetResult();
        }

        public void ReleaseStore()
        {
            _releaseStore.TrySetResult();
        }

        public Task WaitForReleaseAsync(CancellationToken cancellationToken)
        {
            return _releaseStore.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class BlockingAdminAnnouncementInbox : global::Admin.Application.Consumers.IAdminAnnouncementInbox
    {
        private readonly BlockingAdminAnnouncementInboxCoordinator _coordinator;
        private readonly string _connectionString;
        private int _remainingBlocks = 1;

        public BlockingAdminAnnouncementInbox(IConfiguration configuration, BlockingAdminAnnouncementInboxCoordinator coordinator)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _connectionString = configuration.GetConnectionString("BaselineDatabase")
                ?? throw new InvalidOperationException("Admin worker recovery test requires ConnectionStrings:BaselineDatabase.");
        }

        public async ValueTask StoreAsync(global::Admin.Application.Consumers.AdminAnnouncementProjection announcement, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _remainingBlocks, 0) == 1)
            {
                _coordinator.MarkStoreStarted();
                await _coordinator.WaitForReleaseAsync(cancellationToken);
            }

            await InsertAnnouncementAsync(_connectionString, announcement, cancellationToken);
        }
    }

    private sealed class ThrowOnceAdminAnnouncementInbox : global::Admin.Application.Consumers.IAdminAnnouncementInbox
    {
        private readonly string _connectionString;
        private int _remainingFailures = 1;

        public ThrowOnceAdminAnnouncementInbox(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            _connectionString = configuration.GetConnectionString("BaselineDatabase")
                ?? throw new InvalidOperationException("Admin worker recovery test requires ConnectionStrings:BaselineDatabase.");
        }

        public async ValueTask StoreAsync(global::Admin.Application.Consumers.AdminAnnouncementProjection announcement, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _remainingFailures, 0) == 1)
            {
                throw new InvalidOperationException("Synthetic Admin projection failure.");
            }

            await InsertAnnouncementAsync(_connectionString, announcement, cancellationToken);
        }
    }

    private static async Task InsertAnnouncementAsync(
        string connectionString,
        global::Admin.Application.Consumers.AdminAnnouncementProjection announcement,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO admin.announcements (announcement_id, title, body, published_utc, published_by_actor_id, source_module_key, source_reference)
            VALUES (@announcementId, @title, @body, @publishedUtc, @publishedByActorId, @sourceModuleKey, @sourceReference)
            ON CONFLICT (announcement_id) DO NOTHING;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("announcementId", announcement.AnnouncementId);
        command.Parameters.AddWithValue("title", announcement.Title);
        command.Parameters.AddWithValue("body", announcement.Body);
        command.Parameters.AddWithValue("publishedUtc", announcement.PublishedUtc);
        command.Parameters.AddWithValue("publishedByActorId", announcement.PublishedByActorId);
        command.Parameters.AddWithValue("sourceModuleKey", announcement.SourceModuleKey);
        command.Parameters.AddWithValue("sourceReference", (object?)announcement.SourceReference ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
