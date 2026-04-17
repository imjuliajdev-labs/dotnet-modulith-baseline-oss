using Admin.Application.ProcessManagers;
using global::Admin.Infrastructure;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Application.ProcessManagers;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Npgsql;
using SampleFeature.PublicContracts.Events;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests.Modules.Admin.ProcessManagers;

public sealed class AdminAnnouncementProjectionProcessManagerIntegrationTests
{
    [Xunit.Fact]
    public async Task AdminAnnouncementProjectionProcessManagerCompletesCheckpointForPublishedAnnouncement()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();
        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAsync(client);


        var authCookie = session.Cookie;


        var cookies = session.Cookies;

        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/sample-feature/announcements",
            new { title = "Checkpointed announcement", body = "Process manager should complete this workflow." },
            cookies,
            session.HeaderName,
            session.RequestToken,
            "admin-process-manager-request-1");

        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        var announcementId = json.RootElement.GetProperty("announcementId").GetGuid();

        using var scope = application.App.Services.CreateScope();
        var outboxDispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
        var dispatched = await outboxDispatcher.DispatchAvailableAsync(CancellationToken.None);

        Xunit.Assert.Equal(1, dispatched);

        var checkpointStore = scope.ServiceProvider.GetRequiredService<IProcessManagerCheckpointStore>();
        var checkpoint = await checkpointStore.LoadAsync<AdminAnnouncementProjectionProcessState>(
            "admin",
            "AdminAnnouncementProjectionProcessManager",
            announcementId.ToString("N"),
            CancellationToken.None);

        Xunit.Assert.NotNull(checkpoint);
        Xunit.Assert.Equal(ProcessManagerLifecycleState.Completed, checkpoint!.LifecycleState);
        Xunit.Assert.Equal("projection-stored", checkpoint.State.Step);
        Xunit.Assert.True(checkpoint.State.ProjectionStored);
        Xunit.Assert.Null(checkpoint.State.FailureCode);
        Xunit.Assert.Equal("sample-feature", checkpoint.State.SourceModuleKey);
    }

    [Xunit.Fact]
    public async Task AdminAnnouncementProjectionProcessManagerRecoversFailedCheckpointAcrossProviderRestart()
    {
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync();
        var configuration = PostgresBackedApiApplication.CreateConfiguration(postgres.GetConnectionString());
        var announcementId = Guid.Parse("65518287-48d6-4cb7-a580-1d3b8780f0eb");
        var integrationEvent = new SampleAnnouncementPublishedEventV1(
            Guid.Parse("1cb29e2d-7a75-48fa-b5ff-f4bd6f8e1ef2"),
            Instant.FromUtc(2026, 4, 4, 12, 30),
            announcementId,
            "Recovered announcement",
            "A replayed event should resume the failed process manager.",
            "admin");

        await using (var firstProvider = await BuildProviderAsync(configuration))
        {
            var checkpointStore = firstProvider.GetRequiredService<IProcessManagerCheckpointStore>();
            await checkpointStore.SaveAsync(
                new ProcessManagerCheckpoint<AdminAnnouncementProjectionProcessState>(
                    "admin",
                    "AdminAnnouncementProjectionProcessManager",
                    announcementId.ToString("N"),
                    ProcessManagerLifecycleState.Failed,
                    Version: 0,
                    UpdatedAt: integrationEvent.OccurredAt,
                    State: new AdminAnnouncementProjectionProcessState(
                        announcementId,
                        integrationEvent.Title,
                        integrationEvent.Body,
                        integrationEvent.OccurredAt.ToDateTimeOffset(),
                        integrationEvent.PublishedByActorId,
                        "sample-feature",
                        null,
                        Step: "projection-failed",
                        ProjectionStored: false,
                        FailureCode: "admin.announcement_projection_failed"),
                    Failure: new Error("admin.announcement_projection_failed", "Checkpoint seeded to prove recovery.")),
                CancellationToken.None);
        }

        await using (var secondProvider = await BuildProviderAsync(configuration))
        {
            var dispatcher = secondProvider.GetRequiredService<IIntegrationEventDispatcher>();
            await dispatcher.PublishAsync(integrationEvent, CancellationToken.None);
        }

        Xunit.Assert.Equal(1, await CountProjectedAnnouncementsAsync(postgres.GetConnectionString(), announcementId));

        await using (var thirdProvider = await BuildProviderAsync(configuration))
        {
            var checkpointStore = thirdProvider.GetRequiredService<IProcessManagerCheckpointStore>();
            var checkpoint = await checkpointStore.LoadAsync<AdminAnnouncementProjectionProcessState>(
                "admin",
                "AdminAnnouncementProjectionProcessManager",
                announcementId.ToString("N"),
                CancellationToken.None);

            Xunit.Assert.NotNull(checkpoint);
            Xunit.Assert.Equal(ProcessManagerLifecycleState.Completed, checkpoint!.LifecycleState);
            Xunit.Assert.Equal("projection-stored", checkpoint.State.Step);
            Xunit.Assert.True(checkpoint.State.ProjectionStored);
            Xunit.Assert.Equal(2, checkpoint.Version);
            Xunit.Assert.NotNull(checkpoint.CompletedAt);
        }
    }

    private static async Task<ServiceProvider> BuildProviderAsync(IReadOnlyDictionary<string, string?> configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
        services.AddSingleton<IModule>(new global::Admin.Api.AdminModule());
        services.AddBuildingBlocksInfrastructureDefaults();
        services.AddDispatcher(typeof(global::Admin.Application.Consumers.IAdminAnnouncementInbox).Assembly);
        services.AddAdminInfrastructure();

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IDatabaseMigrationRunner>().ApplyConfiguredMigrationsAsync(CancellationToken.None);
        return provider;
    }

    private static async Task<int> CountProjectedAnnouncementsAsync(string connectionString, Guid announcementId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM admin.announcements
            WHERE announcement_id = @announcementId;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("announcementId", announcementId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
