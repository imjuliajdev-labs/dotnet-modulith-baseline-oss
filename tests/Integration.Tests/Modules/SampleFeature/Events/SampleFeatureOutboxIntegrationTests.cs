using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.IntegrationEvents;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SampleFeature.Application.Publishing; // BP-031 dispatch-coverage marker
using SampleFeature.PublicContracts.Events;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests.Modules.SampleFeature.Events;

public sealed class SampleFeatureOutboxIntegrationTests
{
    [Xunit.Fact]
    public async Task SampleFeaturePublishCommandReplaysAndSuppressesDuplicateWritesForSameIdempotencyKey()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAsync(client);


        var authCookie = session.Cookie;


        var cookies = session.Cookies;

        const string requestKey = "sample-announcement-request-1";

        var first = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/sample-feature/announcements",
            new { title = "Replay-safe announcement", body = "The first write should be replayed." },
            cookies,
            session.HeaderName,
            session.RequestToken,
            requestKey);

        var second = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/sample-feature/announcements",
            new { title = "Replay-safe announcement", body = "The first write should be replayed." },
            cookies,
            session.HeaderName,
            session.RequestToken,
            requestKey);

        Xunit.Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Xunit.Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var firstJson = await ReadJsonAsync(first);
        using var secondJson = await ReadJsonAsync(second);

        var announcementId = firstJson.RootElement.GetProperty("announcementId").GetGuid();
        Xunit.Assert.Equal(announcementId, secondJson.RootElement.GetProperty("announcementId").GetGuid());

        var configuration = application.App.Services.GetRequiredService<IConfiguration>();
        var connectionString = configuration.GetConnectionString("BaselineDatabase")
            ?? throw new InvalidOperationException("Integration test requires BaselineDatabase connection string.");

        Xunit.Assert.Equal(1, await CountAnnouncementsAsync(connectionString));
        Xunit.Assert.Equal(1, await CountOutboxMessagesAsync(connectionString));
    }

    [Xunit.Fact]
    public async Task SampleFeaturePublishCommandRejectsConflictingPayloadForSameIdempotencyKey()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAsync(client);


        var authCookie = session.Cookie;


        var cookies = session.Cookies;

        const string requestKey = "sample-announcement-request-2";

        var first = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/sample-feature/announcements",
            new { title = "Conflict announcement", body = "Baseline body." },
            cookies,
            session.HeaderName,
            session.RequestToken,
            requestKey);

        var conflict = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/sample-feature/announcements",
            new { title = "Conflict announcement", body = "Changed body." },
            cookies,
            session.HeaderName,
            session.RequestToken,
            requestKey);

        Xunit.Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Xunit.Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        using var conflictJson = await ReadJsonAsync(conflict);
        Xunit.Assert.Equal("idempotency.request_conflict", conflictJson.RootElement.GetProperty("code").GetString());
    }

    [Xunit.Fact]
    public async Task SampleFeaturePublishCommandPersistsAnnouncementAndDispatchesItsPublicEvent()
    {
        var probe = new AnnouncementDeliveryProbe();

        await using var application = await PostgresBackedApiApplication.StartAsync(
            configureBuilder: builder =>
            {
                builder.Services.AddSingleton(probe);
                builder.Services.AddDispatcher(typeof(SampleFeatureAnnouncementHandler).Assembly);
                builder.Services.AddPostgresIntegrationEventInbox("sample-feature", "sample_feature");
            },
            configureExtraMigrations: static services => services.AddPostgresIntegrationEventInbox("sample-feature", "sample_feature"));

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAsync(client);


        var authCookie = session.Cookie;


        var cookies = session.Cookies;

        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/sample-feature/announcements",
            new { title = "Baseline announcement", body = "Outbox-backed publication from SampleFeature." },
            cookies,
            session.HeaderName,
            session.RequestToken,
            "sample-announcement-request-0");

        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        var announcementId = json.RootElement.GetProperty("announcementId").GetGuid();

        Xunit.Assert.True(await AnnouncementExistsAsync(application, announcementId));

        using var scope = application.App.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
        var dispatched = await dispatcher.DispatchAvailableAsync(CancellationToken.None);

        Xunit.Assert.Equal(1, dispatched);
        Xunit.Assert.Single(probe.Deliveries);
        Xunit.Assert.Equal(announcementId, probe.Deliveries[0].AnnouncementId);
        Xunit.Assert.Equal("Baseline announcement", probe.Deliveries[0].Title);
        Xunit.Assert.Equal("Outbox-backed publication from SampleFeature.", probe.Deliveries[0].Body);
    }

    private static async Task<bool> AnnouncementExistsAsync(PostgresBackedApiApplication application, Guid announcementId)
    {
        var configuration = application.App.Services.GetRequiredService<IConfiguration>();
        var connectionString = configuration.GetConnectionString("BaselineDatabase")
            ?? throw new InvalidOperationException("Integration test requires BaselineDatabase connection string.");

        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM sample_feature.announcements
                WHERE announcement_id = @announcementId);
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("announcementId", announcementId);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<int> CountAnnouncementsAsync(string connectionString)
    {
        const string sql = "SELECT COUNT(*) FROM sample_feature.announcements;";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountOutboxMessagesAsync(string connectionString)
    {
        const string sql = "SELECT COUNT(*) FROM sample_feature.integration_outbox;";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}

internal sealed class SampleFeatureAnnouncementHandler : IModuleScopedIntegrationEventHandler<SampleAnnouncementPublishedEventV1>
{
    private readonly AnnouncementDeliveryProbe _probe;

    public SampleFeatureAnnouncementHandler(AnnouncementDeliveryProbe probe)
    {
        _probe = probe;
    }

    public string ModuleKey => "sample-feature";

    public Task Handle(SampleAnnouncementPublishedEventV1 integrationEvent, CancellationToken cancellationToken)
    {
        _probe.Deliveries.Add(integrationEvent);
        return Task.CompletedTask;
    }
}

internal sealed class AnnouncementDeliveryProbe
{
    public List<SampleAnnouncementPublishedEventV1> Deliveries { get; } = [];
}
