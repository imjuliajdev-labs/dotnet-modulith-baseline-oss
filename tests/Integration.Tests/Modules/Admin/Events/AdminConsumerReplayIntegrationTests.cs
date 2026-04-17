using global::Admin.Infrastructure;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Modules;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.Persistence;
using KnowledgeBase.Api;
using KnowledgeBase.PublicContracts.Events;
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

namespace Integration.Tests.Modules.Admin.Events;

public sealed class AdminConsumerReplayIntegrationTests
{
    [Xunit.Fact]
    public async Task AdminConsumerDeduplicatesReplayedSampleAnnouncementsAcrossProviderRestarts()
    {
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync();
        var configuration = PostgresBackedApiApplication.CreateConfiguration(postgres.GetConnectionString());
        var integrationEvent = new SampleAnnouncementPublishedEventV1(
            Guid.Parse("7ca40ee9-2244-472e-a0e1-0c677464ad38"),
            Instant.FromUtc(2026, 4, 4, 12, 0),
            Guid.Parse("1fa0c6f0-7906-42e9-aabd-4a616c4d48cc"),
            "Replay-safe announcement",
            "Admin should only project this once.",
            "admin");

        await using (var firstProvider = await BuildProviderAsync(configuration))
        {
            var dispatcher = firstProvider.GetRequiredService<IIntegrationEventDispatcher>();
            await dispatcher.PublishAsync(integrationEvent, CancellationToken.None);
        }

        await using (var secondProvider = await BuildProviderAsync(configuration))
        {
            var dispatcher = secondProvider.GetRequiredService<IIntegrationEventDispatcher>();
            await dispatcher.PublishAsync(integrationEvent, CancellationToken.None);
        }

        Xunit.Assert.Equal(1, await CountProjectedAnnouncementsAsync(postgres.GetConnectionString(), integrationEvent.AnnouncementId));
    }

    [Xunit.Fact]
    public async Task AdminConsumerDeduplicatesDualPublishedKnowledgeBaseCompatibilityEventsAcrossProviderRestarts()
    {
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync();
        var configuration = PostgresBackedApiApplication.CreateConfiguration(postgres.GetConnectionString());
        var entryId = Guid.Parse("3954c548-f334-4297-8908-dad243d99f2f");
        var publishedAt = Instant.FromUtc(2026, 4, 4, 13, 0);
        var v1Event = new KnowledgeEntryPublishedEventV1(
            Guid.Parse("b8cbbd72-bc63-41d3-8060-7068632713f5"),
            publishedAt,
            entryId,
            "runbook-overview",
            "Runbook overview",
            "Admin should only project this published entry once.",
            "admin");
        var v2Event = new KnowledgeEntryPublishedEventV2(
            Guid.Parse("8d707027-3498-4f93-abf0-ef1bf6033c8c"),
            publishedAt,
            entryId,
            "runbook-overview",
            "Runbook overview",
            "Admin should only project this published entry once.",
            "Operations",
            "admin");

        await using (var firstProvider = await BuildProviderAsync(configuration))
        {
            var dispatcher = firstProvider.GetRequiredService<IIntegrationEventDispatcher>();
            await dispatcher.PublishAsync(v1Event, CancellationToken.None);
        }

        await using (var secondProvider = await BuildProviderAsync(configuration))
        {
            var dispatcher = secondProvider.GetRequiredService<IIntegrationEventDispatcher>();
            await dispatcher.PublishAsync(v2Event, CancellationToken.None);
            await dispatcher.PublishAsync(v1Event, CancellationToken.None);
            await dispatcher.PublishAsync(v2Event, CancellationToken.None);
        }

        Xunit.Assert.Equal(1, await CountProjectedAnnouncementsAsync(postgres.GetConnectionString(), entryId));

        var projected = await ReadProjectedAnnouncementAsync(postgres.GetConnectionString(), entryId);
        Xunit.Assert.NotNull(projected);
        Xunit.Assert.Equal("Runbook overview", projected!.Title);
        Xunit.Assert.Equal("Admin should only project this published entry once.", projected.Body);
        Xunit.Assert.Equal("knowledge-base", projected.SourceModuleKey);
        Xunit.Assert.Equal("runbook-overview", projected.SourceReference);
    }

    [Xunit.Fact]
    public async Task AdminProjectsAnnouncementsDispatchedFromSampleFeatureOutbox()
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
            new { title = "Cross-module announcement", body = "Admin should project this via outbox dispatch." },
            cookies,
            session.HeaderName,
            session.RequestToken,
            "admin-read-cross-module-request-1");

        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        var announcementId = json.RootElement.GetProperty("announcementId").GetGuid();

        using var scope = application.App.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
        var dispatched = await dispatcher.DispatchAvailableAsync(CancellationToken.None);

        Xunit.Assert.Equal(1, dispatched);

        var configuration = application.App.Services.GetRequiredService<IConfiguration>();
        var connectionString = configuration.GetConnectionString("BaselineDatabase")
            ?? throw new InvalidOperationException("Integration test requires BaselineDatabase connection string.");

        Xunit.Assert.Equal(1, await CountProjectedAnnouncementsAsync(connectionString, announcementId));

        var adminReadResponse = await SendAsync(client, HttpMethod.Get, $"/api/v1/admin/announcements/{announcementId}", authCookie);
        Xunit.Assert.Equal(HttpStatusCode.OK, adminReadResponse.StatusCode);

        using var adminReadJson = await ReadJsonAsync(adminReadResponse);
        Xunit.Assert.Equal(announcementId, adminReadJson.RootElement.GetProperty("announcementId").GetGuid());
        Xunit.Assert.Equal("Cross-module announcement", adminReadJson.RootElement.GetProperty("title").GetString());
        Xunit.Assert.Equal("Admin should project this via outbox dispatch.", adminReadJson.RootElement.GetProperty("body").GetString());
        Xunit.Assert.Equal("sample-feature", adminReadJson.RootElement.GetProperty("sourceModuleKey").GetString());

        var adminListResponse = await SendAsync(client, HttpMethod.Get, "/api/v1/admin/announcements?limit=5", authCookie);
        Xunit.Assert.Equal(HttpStatusCode.OK, adminListResponse.StatusCode);

        using var adminListJson = await ReadJsonAsync(adminListResponse);
        var announcements = adminListJson.RootElement.GetProperty("announcements");
        Xunit.Assert.Equal(1, announcements.GetArrayLength());
        Xunit.Assert.Equal(announcementId, announcements[0].GetProperty("announcementId").GetGuid());
        Xunit.Assert.Equal("sample-feature", announcements[0].GetProperty("sourceModuleKey").GetString());
    }

    [Xunit.Fact]
    public async Task AdminProjectsPublishedKnowledgeBaseEntriesDispatchedFromKnowledgeBaseOutbox()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();
        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAsync(client);


        var authCookie = session.Cookie;


        var cookies = session.Cookies;

        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/knowledge-base/manage/entries",
            new CreateKnowledgeEntryRequest(
                Slug: "cross-module-knowledge-entry",
                Title: "Cross-module knowledge entry",
                Body: "Admin should project this knowledge entry via outbox dispatch.",
                Category: "Operations",
                Featured: false,
                SortOrder: 20),
            cookies,
            session.HeaderName,
            session.RequestToken);

        createResponse.EnsureSuccessStatusCode();

        using var createJson = await ReadJsonAsync(createResponse);
        var entryId = createJson.RootElement.GetProperty("entryId").GetGuid();

        var publishResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/knowledge-base/manage/entries/{entryId}/publish",
            new PublishKnowledgeEntryRequest(ExpectedVersion: 1),
            cookies,
            session.HeaderName,
            session.RequestToken,
            "admin-read-cross-module-request-2");

        publishResponse.EnsureSuccessStatusCode();

        using var scope = application.App.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
        var dispatched = await dispatcher.DispatchAvailableAsync(CancellationToken.None);

        Xunit.Assert.Equal(2, dispatched);

        var adminListResponse = await SendAsync(client, HttpMethod.Get, "/api/v1/admin/announcements?limit=5", authCookie);
        Xunit.Assert.Equal(HttpStatusCode.OK, adminListResponse.StatusCode);

        using var adminListJson = await ReadJsonAsync(adminListResponse);
        var projectedAnnouncements = adminListJson.RootElement.GetProperty("announcements")
            .EnumerateArray()
            .Where(candidate =>
                string.Equals(candidate.GetProperty("sourceModuleKey").GetString(), "knowledge-base", StringComparison.Ordinal)
                && string.Equals(candidate.GetProperty("sourceReference").GetString(), "cross-module-knowledge-entry", StringComparison.Ordinal))
            .ToArray();

        Xunit.Assert.Single(projectedAnnouncements);

        var projected = projectedAnnouncements[0];
        Xunit.Assert.Equal(entryId, projected.GetProperty("announcementId").GetGuid());
        Xunit.Assert.Equal("Cross-module knowledge entry", projected.GetProperty("title").GetString());
        Xunit.Assert.Equal("Admin should project this knowledge entry via outbox dispatch.", projected.GetProperty("body").GetString());
        Xunit.Assert.Equal("knowledge-base", projected.GetProperty("sourceModuleKey").GetString());
        Xunit.Assert.Equal("cross-module-knowledge-entry", projected.GetProperty("sourceReference").GetString());

        var projectedAnnouncementId = projected.GetProperty("announcementId").GetGuid();
        var adminReadResponse = await SendAsync(client, HttpMethod.Get, $"/api/v1/admin/announcements/{projectedAnnouncementId}", authCookie);
        Xunit.Assert.Equal(HttpStatusCode.OK, adminReadResponse.StatusCode);

        using var adminReadJson = await ReadJsonAsync(adminReadResponse);
        Xunit.Assert.Equal(projectedAnnouncementId, adminReadJson.RootElement.GetProperty("announcementId").GetGuid());
        Xunit.Assert.Equal("Cross-module knowledge entry", adminReadJson.RootElement.GetProperty("title").GetString());
        Xunit.Assert.Equal("Admin should project this knowledge entry via outbox dispatch.", adminReadJson.RootElement.GetProperty("body").GetString());
        Xunit.Assert.Equal("knowledge-base", adminReadJson.RootElement.GetProperty("sourceModuleKey").GetString());
        Xunit.Assert.Equal("cross-module-knowledge-entry", adminReadJson.RootElement.GetProperty("sourceReference").GetString());
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

    private static async Task<ProjectedAnnouncement?> ReadProjectedAnnouncementAsync(string connectionString, Guid announcementId)
    {
        const string sql = """
            SELECT title, body, source_module_key, source_reference
            FROM admin.announcements
            WHERE announcement_id = @announcementId;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("announcementId", announcementId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new ProjectedAnnouncement(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }
    [Xunit.Fact]
    public async Task AdminConsumerReplayCompletesCorrectlyWhenModuleTransitionsOccurDuringReplay()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();
        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAsync(client);


        var authCookie = session.Cookie;


        var cookies = session.Cookies;

        var publishResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/sample-feature/announcements",
            new { title = "Replay-during-transition announcement", body = "Admin should handle replay correctly across module transitions." },
            cookies,
            session.HeaderName,
            session.RequestToken,
            "admin-replay-transition-1");

        Xunit.Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

        using var publishJson = await ReadJsonAsync(publishResponse);
        var announcementId = publishJson.RootElement.GetProperty("announcementId").GetGuid();

        using (var scope = application.App.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
            var dispatched = await dispatcher.DispatchAvailableAsync(CancellationToken.None);
            Xunit.Assert.Equal(1, dispatched);
        }

        var configuration = application.App.Services.GetRequiredService<IConfiguration>();
        var connectionString = configuration.GetConnectionString("BaselineDatabase")
            ?? throw new InvalidOperationException("Integration test requires BaselineDatabase connection string.");

        Xunit.Assert.Equal(1, await CountProjectedAnnouncementsAsync(connectionString, announcementId));

        var disableResponse = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/admin/disable",
            cookies,
            session.HeaderName,
            session.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

        var enableResponse = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/admin/enable",
            cookies,
            session.HeaderName,
            session.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        Xunit.Assert.Equal(1, await CountProjectedAnnouncementsAsync(connectionString, announcementId));

        var adminReadResponse = await SendAsync(client, HttpMethod.Get, $"/api/v1/admin/announcements/{announcementId}", authCookie);
        Xunit.Assert.Equal(HttpStatusCode.OK, adminReadResponse.StatusCode);

        using var adminReadJson = await ReadJsonAsync(adminReadResponse);
        Xunit.Assert.Equal(announcementId, adminReadJson.RootElement.GetProperty("announcementId").GetGuid());
        Xunit.Assert.Equal("Replay-during-transition announcement", adminReadJson.RootElement.GetProperty("title").GetString());
    }

    private sealed record ProjectedAnnouncement(string Title, string Body, string SourceModuleKey, string? SourceReference);
}
