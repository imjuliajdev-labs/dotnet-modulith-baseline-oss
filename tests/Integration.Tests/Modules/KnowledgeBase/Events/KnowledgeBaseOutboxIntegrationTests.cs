using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.IntegrationEvents;
using KnowledgeBase.Api;
using KnowledgeBase.PublicContracts.Events;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests.Modules.KnowledgeBase.Events;

public sealed class KnowledgeBaseOutboxIntegrationTests
{
    [Xunit.Fact]
    public async Task KnowledgeBasePublishCommandReplaysAndSuppressesDuplicateWritesForSameIdempotencyKey()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        var entryId = await CreateDraftAsync(client, adminSession, "Replay-safe knowledge entry", "repeatable-kb-entry");

        const string requestKey = "knowledge-base-publish-request-2";

        var first = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/knowledge-base/manage/entries/{entryId}/publish",
            new PublishKnowledgeEntryRequest(ExpectedVersion: 1),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken,
            requestKey);

        var second = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/knowledge-base/manage/entries/{entryId}/publish",
            new PublishKnowledgeEntryRequest(ExpectedVersion: 1),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken,
            requestKey);

        Xunit.Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Xunit.Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var firstJson = await ReadJsonAsync(first);
        using var secondJson = await ReadJsonAsync(second);
        Xunit.Assert.Equal(
            firstJson.RootElement.GetProperty("entryId").GetGuid(),
            secondJson.RootElement.GetProperty("entryId").GetGuid());
        Xunit.Assert.Equal("published", firstJson.RootElement.GetProperty("status").GetString());

        var configuration = application.App.Services.GetRequiredService<IConfiguration>();
        var connectionString = configuration.GetConnectionString("BaselineDatabase")
            ?? throw new InvalidOperationException("Integration test requires BaselineDatabase connection string.");

        Xunit.Assert.Equal(2, await CountKnowledgeEntryOutboxMessagesAsync(connectionString, entryId));
        Xunit.Assert.Equal(new[] { 1, 2 }, await ReadKnowledgeEntryOutboxVersionsAsync(connectionString, entryId));
    }

    [Xunit.Fact]
    public async Task KnowledgeBasePublishCommandRejectsConflictingPayloadForSameIdempotencyKey()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        var entryId = await CreateDraftAsync(client, adminSession, "Conflict knowledge entry", "conflict-kb-entry");

        const string requestKey = "knowledge-base-publish-request-3";

        var first = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/knowledge-base/manage/entries/{entryId}/publish",
            new PublishKnowledgeEntryRequest(ExpectedVersion: 1),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken,
            requestKey);

        var conflict = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/knowledge-base/manage/entries/{entryId}/publish",
            new PublishKnowledgeEntryRequest(ExpectedVersion: 2),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken,
            requestKey);

        Xunit.Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Xunit.Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        using var conflictJson = await ReadJsonAsync(conflict);
        Xunit.Assert.Equal("idempotency.request_conflict", conflictJson.RootElement.GetProperty("code").GetString());
    }

    [Xunit.Fact]
    public async Task KnowledgeBasePublishCommandDualPublishesCompatibilityEventsForTheSameBusinessPublication()
    {
        var probe = new KnowledgeBaseDeliveryProbe();

        await using var application = await PostgresBackedApiApplication.StartAsync(
            configureBuilder: builder =>
            {
                builder.Services.AddSingleton(probe);
                builder.Services.AddDispatcher(typeof(KnowledgeBasePublishedEventHandler).Assembly);
                builder.Services.AddPostgresIntegrationEventInbox("knowledge-base", "knowledge_base");
            },
            configureExtraMigrations: static services => services.AddPostgresIntegrationEventInbox("knowledge-base", "knowledge_base"));

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        var entryId = await CreateDraftAsync(client, adminSession, "Baseline knowledge entry", "baseline-knowledge-entry");

        var response = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/knowledge-base/manage/entries/{entryId}/publish",
            new PublishKnowledgeEntryRequest(ExpectedVersion: 1),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken,
            "knowledge-base-publish-request-4");

        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Xunit.Assert.Equal(entryId, json.RootElement.GetProperty("entryId").GetGuid());

        var configuration = application.App.Services.GetRequiredService<IConfiguration>();
        var connectionString = configuration.GetConnectionString("BaselineDatabase")
            ?? throw new InvalidOperationException("Integration test requires BaselineDatabase connection string.");

        Xunit.Assert.Equal(2, await CountKnowledgeEntryOutboxMessagesAsync(connectionString, entryId));
        Xunit.Assert.Equal(new[] { 1, 2 }, await ReadKnowledgeEntryOutboxVersionsAsync(connectionString, entryId));

        using var scope = application.App.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
        var dispatched = await dispatcher.DispatchAvailableAsync(CancellationToken.None);

        Xunit.Assert.Equal(2, dispatched);
        Xunit.Assert.Single(probe.V1Deliveries);
        Xunit.Assert.Single(probe.V2Deliveries);
        Xunit.Assert.Equal(entryId, probe.V1Deliveries[0].EntryId);
        Xunit.Assert.Equal(entryId, probe.V2Deliveries[0].EntryId);
        Xunit.Assert.Equal(probe.V1Deliveries[0].EntryId, probe.V2Deliveries[0].EntryId);
        Xunit.Assert.NotEqual(probe.V1Deliveries[0].EventId, probe.V2Deliveries[0].EventId);
        Xunit.Assert.Equal("baseline-knowledge-entry", probe.V1Deliveries[0].Slug);
        Xunit.Assert.Equal("baseline-knowledge-entry", probe.V2Deliveries[0].Slug);
        Xunit.Assert.Equal("Baseline knowledge entry", probe.V1Deliveries[0].Title);
        Xunit.Assert.Equal("Baseline knowledge entry", probe.V2Deliveries[0].Title);
        Xunit.Assert.Equal("Contracts", probe.V2Deliveries[0].Category);
    }

    private static async Task<Guid> CreateDraftAsync(HttpClient client, AuthenticatedSession session, string title, string slug)
    {
        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/knowledge-base/manage/entries",
            new CreateKnowledgeEntryRequest(
                Slug: slug,
                Title: title,
                Body: "Generated contracts should be refreshed from the backend OpenAPI snapshot.",
                Category: "Contracts",
                Featured: false,
                SortOrder: 20),
            session.Cookies,
            session.HeaderName,
            session.RequestToken);

        createResponse.EnsureSuccessStatusCode();

        using var createJson = await ReadJsonAsync(createResponse);
        return createJson.RootElement.GetProperty("entryId").GetGuid();
    }

    private static async Task<int> CountKnowledgeEntryOutboxMessagesAsync(string connectionString, Guid entryId)
    {
        const string sql = "SELECT COUNT(*) FROM knowledge_base.integration_outbox WHERE payload ->> 'entryId' = @entryId;";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("entryId", entryId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int[]> ReadKnowledgeEntryOutboxVersionsAsync(string connectionString, Guid entryId)
    {
        const string sql = "SELECT event_version FROM knowledge_base.integration_outbox WHERE payload ->> 'entryId' = @entryId ORDER BY event_version;";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("entryId", entryId.ToString());
        await using var reader = await command.ExecuteReaderAsync();

        var versions = new List<int>();
        while (await reader.ReadAsync())
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions.ToArray();
    }
}

internal sealed class KnowledgeBasePublishedEventHandler : IModuleScopedIntegrationEventHandler<KnowledgeEntryPublishedEventV1>
{
    private readonly KnowledgeBaseDeliveryProbe _probe;

    public KnowledgeBasePublishedEventHandler(KnowledgeBaseDeliveryProbe probe)
    {
        _probe = probe;
    }

    public string ModuleKey => "knowledge-base";

    public Task Handle(KnowledgeEntryPublishedEventV1 integrationEvent, CancellationToken cancellationToken)
    {
        _probe.V1Deliveries.Add(integrationEvent);
        return Task.CompletedTask;
    }
}

internal sealed class KnowledgeBasePublishedEventV2Handler : IModuleScopedIntegrationEventHandler<KnowledgeEntryPublishedEventV2>
{
    private readonly KnowledgeBaseDeliveryProbe _probe;

    public KnowledgeBasePublishedEventV2Handler(KnowledgeBaseDeliveryProbe probe)
    {
        _probe = probe;
    }

    public string ModuleKey => "knowledge-base";

    public Task Handle(KnowledgeEntryPublishedEventV2 integrationEvent, CancellationToken cancellationToken)
    {
        _probe.V2Deliveries.Add(integrationEvent);
        return Task.CompletedTask;
    }
}

internal sealed class KnowledgeBaseDeliveryProbe
{
    public List<KnowledgeEntryPublishedEventV1> V1Deliveries { get; } = [];

    public List<KnowledgeEntryPublishedEventV2> V2Deliveries { get; } = [];
}
