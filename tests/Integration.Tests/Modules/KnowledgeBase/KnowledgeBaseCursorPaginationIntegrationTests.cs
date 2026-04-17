using System.Net;
using System.Text;
using System.Text.Json;
using KnowledgeBase.Application.Entries; // BP-031 dispatch-coverage marker
using Microsoft.AspNetCore.TestHost;

namespace Integration.Tests.Modules.KnowledgeBase;

public sealed class KnowledgeBaseCursorPaginationIntegrationTests
{
    [Xunit.Fact]
    public async Task TamperedCursorReturnsValidationProblemDetailsOnThePublishedListEndpoint()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        // Non-base64 garbage
        var garbage = await client.GetAsync("/api/v1/knowledge-base/entries?limit=2&after=not-a-cursor");
        Xunit.Assert.Equal(HttpStatusCode.BadRequest, garbage.StatusCode);
        using (var garbageJson = await ReadJsonAsync(garbage))
        {
            Xunit.Assert.Equal("knowledge-base.invalid_cursor", garbageJson.RootElement.GetProperty("code").GetString());
        }

        // Valid base64 but no separator
        var noSeparator = Convert.ToBase64String(Encoding.UTF8.GetBytes("no-pipe-in-payload"));
        var separatorless = await client.GetAsync($"/api/v1/knowledge-base/entries?limit=2&after={Uri.EscapeDataString(noSeparator)}");
        Xunit.Assert.Equal(HttpStatusCode.BadRequest, separatorless.StatusCode);

        // Valid base64 and separator but garbage inner payload (unparseable sort order and guid)
        var innerGarbage = Convert.ToBase64String(Encoding.UTF8.GetBytes("not-a-number|not-a-guid"));
        var innerBad = await client.GetAsync($"/api/v1/knowledge-base/entries?limit=2&after={Uri.EscapeDataString(innerGarbage)}");
        Xunit.Assert.Equal(HttpStatusCode.BadRequest, innerBad.StatusCode);
        using (var innerJson = await ReadJsonAsync(innerBad))
        {
            Xunit.Assert.Equal("knowledge-base.invalid_cursor", innerJson.RootElement.GetProperty("code").GetString());
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}
