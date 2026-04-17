using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Identity.Api.Authentication;
using Microsoft.AspNetCore.TestHost;
using Platform.Application.Auditing; // BP-031 dispatch-coverage marker
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests;

public sealed class PlatformAuditCursorPaginationIntegrationTests
{
    [Xunit.Fact]
    public async Task TamperedCursorReturnsValidationProblemDetailsOnThePlatformAuditEventsEndpoint()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");

        // Non-base64 garbage
        var garbage = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/audit-events?limit=5&after=not-a-cursor", adminSession.Cookies);
        Xunit.Assert.Equal(HttpStatusCode.BadRequest, garbage.StatusCode);
        using (var garbageJson = await ReadJsonAsync(garbage))
        {
            Xunit.Assert.Equal("platform.invalid_cursor", garbageJson.RootElement.GetProperty("code").GetString());
        }

        // Valid base64 but no separator
        var noSeparator = Convert.ToBase64String(Encoding.UTF8.GetBytes("no-pipe-in-payload"));
        var separatorless = await SendAsync(client, HttpMethod.Get, $"/api/v1/platform/audit-events?limit=5&after={Uri.EscapeDataString(noSeparator)}", adminSession.Cookies);
        Xunit.Assert.Equal(HttpStatusCode.BadRequest, separatorless.StatusCode);

        // Valid base64 and separator but garbage inner payload (unparseable tick count and id)
        var innerGarbage = Convert.ToBase64String(Encoding.UTF8.GetBytes("not-a-number|also-not-a-number"));
        var innerBad = await SendAsync(client, HttpMethod.Get, $"/api/v1/platform/audit-events?limit=5&after={Uri.EscapeDataString(innerGarbage)}", adminSession.Cookies);
        Xunit.Assert.Equal(HttpStatusCode.BadRequest, innerBad.StatusCode);
        using (var innerJson = await ReadJsonAsync(innerBad))
        {
            Xunit.Assert.Equal("platform.invalid_cursor", innerJson.RootElement.GetProperty("code").GetString());
        }
    }
}
