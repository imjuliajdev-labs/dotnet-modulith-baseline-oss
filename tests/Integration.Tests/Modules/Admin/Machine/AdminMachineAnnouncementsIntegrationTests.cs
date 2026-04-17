using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Admin.Application.Queries; // BP-031 dispatch-coverage marker
using BuildingBlocks.Application.Dispatching;
using Identity.Infrastructure.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests.Modules.Admin.Machine;

public sealed class AdminMachineAnnouncementsIntegrationTests
{
    [Xunit.Fact]
    public async Task AdminMachineAnnouncementsRequireMachineAuthentication()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();
        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var response = await client.GetAsync("/api/v1/admin/machine/announcements");

        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Xunit.Fact]
    public async Task AdminMachineAnnouncementsExposeProjectedFeedToAuthorizedMachineClients()
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
            new { title = "Machine feed announcement", body = "Machine clients should read this from Admin." },
            cookies,
            session.HeaderName,
            session.RequestToken,
            "admin-machine-request-1");

        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        var announcementId = json.RootElement.GetProperty("announcementId").GetGuid();

        using var scope = application.App.Services.CreateScope();
        var outboxDispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
        var dispatched = await outboxDispatcher.DispatchAvailableAsync(CancellationToken.None);

        Xunit.Assert.Equal(1, dispatched);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/machine/announcements");
        request.Headers.Add(IdentityMachineAuthenticationDefaults.ApiKeyHeaderName, "MachineOnly!123");

        var machineResponse = await client.SendAsync(request);
        Xunit.Assert.Equal(HttpStatusCode.OK, machineResponse.StatusCode);

        using var machineJson = await ReadJsonAsync(machineResponse);
        Xunit.Assert.Equal("v1", machineJson.RootElement.GetProperty("contractVersion").GetString());
        var announcements = machineJson.RootElement.GetProperty("announcements");
        Xunit.Assert.Equal(1, announcements.GetArrayLength());
        Xunit.Assert.Equal(announcementId, announcements[0].GetProperty("announcementId").GetGuid());
        Xunit.Assert.Equal("Machine feed announcement", announcements[0].GetProperty("title").GetString());
        Xunit.Assert.Equal("sample-feature", announcements[0].GetProperty("sourceModuleKey").GetString());
    }
}
