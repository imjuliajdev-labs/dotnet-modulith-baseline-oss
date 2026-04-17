using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Api.Authentication;
using KnowledgeBase.Api;
using KnowledgeBase.Application.Entries; // BP-031 dispatch-coverage marker
using Microsoft.AspNetCore.TestHost;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests.Modules.KnowledgeBase;

public sealed class KnowledgeBaseIntegrationTests
{
    [Xunit.Fact]
    public async Task KnowledgeBasePublicEndpointsExposeSeededPublishedEntries()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var listResponse = await client.GetAsync("/api/v1/knowledge-base/entries");
        Xunit.Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using (var listJson = await ReadJsonAsync(listResponse))
        {
            var entries = listJson.RootElement.GetProperty("entries").EnumerateArray().ToArray();
            Xunit.Assert.True(entries.Length >= 2);
            Xunit.Assert.Contains(entries, entry => string.Equals(entry.GetProperty("slug").GetString(), "what-is-this-baseline", StringComparison.Ordinal));
        }

        var detailResponse = await client.GetAsync("/api/v1/knowledge-base/entries/what-is-this-baseline");
        Xunit.Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        using var detailJson = await ReadJsonAsync(detailResponse);
        Xunit.Assert.Equal("What is this baseline?", detailJson.RootElement.GetProperty("title").GetString());
        Xunit.Assert.Equal("published", detailJson.RootElement.GetProperty("status").GetString());
    }

    [Xunit.Fact]
    public async Task KnowledgeBaseOperatorsCanCreateUpdateAndPublishEntries()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");

        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/knowledge-base/manage/entries",
            new CreateKnowledgeEntryRequest(
                Slug: null,
                Title: "How do I trust the frontend contract?",
                Body: "Generated contracts should be refreshed from the backend OpenAPI snapshot.",
                Category: "Contracts",
                Featured: false,
                SortOrder: 30),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        string entryId;
        using (var createJson = await ReadJsonAsync(createResponse))
        {
            entryId = createJson.RootElement.GetProperty("entryId").GetString() ?? string.Empty;
            Xunit.Assert.Equal("draft", createJson.RootElement.GetProperty("status").GetString());
            Xunit.Assert.Equal("how-do-i-trust-the-frontend-contract", createJson.RootElement.GetProperty("slug").GetString());
            Xunit.Assert.Equal(1, createJson.RootElement.GetProperty("version").GetInt32());
        }

        var publicBeforePublish = await client.GetAsync("/api/v1/knowledge-base/entries");
        using (var publicBeforeJson = await ReadJsonAsync(publicBeforePublish))
        {
            var entries = publicBeforeJson.RootElement.GetProperty("entries").EnumerateArray().ToArray();
            Xunit.Assert.DoesNotContain(entries, entry => string.Equals(entry.GetProperty("entryId").GetString(), entryId, StringComparison.Ordinal));
        }

        var updateResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/knowledge-base/manage/entries/{entryId}",
            new UpdateKnowledgeEntryRequest(
                ExpectedVersion: 1,
                Slug: null,
                Title: "How do I trust the frontend contract?",
                Body: "Refresh the generated TypeScript contracts after changing the backend OpenAPI surface.",
                Category: "Contracts",
                Featured: true,
                SortOrder: 30),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var publishResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/knowledge-base/manage/entries/{entryId}/publish",
            new PublishKnowledgeEntryRequest(ExpectedVersion: 2),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken,
            idempotencyKey: "knowledge-base-publish-request-1");

        Xunit.Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

        var publicAfterPublish = await client.GetAsync("/api/v1/knowledge-base/entries");
        Xunit.Assert.Equal(HttpStatusCode.OK, publicAfterPublish.StatusCode);

        using (var publicAfterJson = await ReadJsonAsync(publicAfterPublish))
        {
            var entries = publicAfterJson.RootElement.GetProperty("entries").EnumerateArray().ToArray();
            Xunit.Assert.Contains(entries, entry => string.Equals(entry.GetProperty("entryId").GetString(), entryId, StringComparison.Ordinal));
        }

        var detailResponse = await client.GetAsync("/api/v1/knowledge-base/entries/how-do-i-trust-the-frontend-contract");
        Xunit.Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
    }

    [Xunit.Fact]
    public async Task KnowledgeBaseMutationsRejectStaleVersionUpdates()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/knowledge-base/manage/entries",
            new CreateKnowledgeEntryRequest(
                Slug: "stale-version-entry",
                Title: "Stale version entry",
                Body: "First draft",
                Category: "Testing",
                Featured: false,
                SortOrder: 99),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        using var createJson = await ReadJsonAsync(createResponse);
        var entryId = createJson.RootElement.GetProperty("entryId").GetString() ?? string.Empty;

        var firstUpdate = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/knowledge-base/manage/entries/{entryId}",
            new UpdateKnowledgeEntryRequest(
                ExpectedVersion: 1,
                Slug: "stale-version-entry",
                Title: "Stale version entry",
                Body: "Second draft",
                Category: "Testing",
                Featured: false,
                SortOrder: 99),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, firstUpdate.StatusCode);

        var staleUpdate = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/knowledge-base/manage/entries/{entryId}",
            new UpdateKnowledgeEntryRequest(
                ExpectedVersion: 1,
                Slug: "stale-version-entry",
                Title: "Stale version entry",
                Body: "Stale write",
                Category: "Testing",
                Featured: false,
                SortOrder: 99),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);

        using var staleJson = await ReadJsonAsync(staleUpdate);
        Xunit.Assert.Equal("knowledge-base.entry_version_conflict", staleJson.RootElement.GetProperty("code").GetString());
    }
}
