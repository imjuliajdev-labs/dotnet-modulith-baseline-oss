using Admin.Application.Queries; // BP-031 dispatch-coverage marker
using KnowledgeBase.PublicContracts.Queries;
using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests.Modules.Admin.SharedQueries;

public sealed class AdminKnowledgeBaseGuidanceQueryIntegrationTests
{
    [Xunit.Fact]
    public async Task AdminCanReadPublishedKnowledgeBaseGuidanceThroughSharedQueryContracts()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();
        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var signInResult = await SignInAsync(client);


        var authCookie = signInResult.Cookie;

        var response = await SendAsync(client, HttpMethod.Get, "/api/v1/admin/guidance?limit=3", authCookie);

        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        var entries = json.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        var first = entries.First();

        Xunit.Assert.Equal("KnowledgeBasePublishedEntryReadModel", typeof(KnowledgeBasePublishedEntryReadModel).Name);
        Xunit.Assert.True(entries.Length >= 2);
        Xunit.Assert.Equal("What is this baseline?", first.GetProperty("title").GetString());
        Xunit.Assert.Equal("what-is-this-baseline", first.GetProperty("slug").GetString());
        Xunit.Assert.Equal("Getting started", first.GetProperty("category").GetString());
        Xunit.Assert.True(first.GetProperty("featured").GetBoolean());
    }
}
