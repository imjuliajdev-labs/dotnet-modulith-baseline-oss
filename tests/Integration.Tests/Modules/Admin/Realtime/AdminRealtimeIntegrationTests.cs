using Admin.Application.Realtime;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.Realtime;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests.Modules.Admin.Realtime;

public sealed class AdminRealtimeIntegrationTests
{
    [Xunit.Fact]
    public async Task AdminAnnouncementProjectionNotificationsArePublishedToAuthorizedRealtimeClients()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAsync(client);


        var authCookie = session.Cookie;


        var cookies = session.Cookies;

        var messageReceived = new TaskCompletionSource<(string Channel, JsonElement Payload)>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = CreateConnection(application, cookies);
        connection.On<string, JsonElement>(BrowserRealtimeMethods.Message, (channel, payload) =>
        {
            if (channel == AdminRealtimeChannels.AnnouncementProjected)
            {
                messageReceived.TrySetResult((channel, payload));
            }
        });

        await connection.StartAsync();

        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/sample-feature/announcements",
            new { title = "Realtime announcement", body = "Admin realtime clients should see this." },
            cookies,
            session.HeaderName,
            session.RequestToken,
            "admin-realtime-request-1");

        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        var announcementId = json.RootElement.GetProperty("announcementId").GetGuid();

        using var scope = application.App.Services.CreateScope();
        var outboxDispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
        var dispatched = await outboxDispatcher.DispatchAvailableAsync(CancellationToken.None);

        Xunit.Assert.Equal(1, dispatched);

        var (channel, payload) = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Xunit.Assert.Equal(AdminRealtimeChannels.AnnouncementProjected, channel);
        Xunit.Assert.Equal(announcementId, payload.GetProperty("announcementId").GetGuid());
        Xunit.Assert.Equal("Realtime announcement", payload.GetProperty("title").GetString());
        Xunit.Assert.Equal("Admin realtime clients should see this.", payload.GetProperty("body").GetString());
        Xunit.Assert.Equal("sample-feature", payload.GetProperty("sourceModuleKey").GetString());
    }

    private static HubConnection CreateConnection(PostgresBackedApiApplication application, string cookies)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(cookies);

        return new HubConnectionBuilder()
            .WithUrl($"https://localhost{BrowserRealtimeDefaults.Path}", options =>
            {
                options.Headers.Add("Cookie", cookies);
                options.HttpMessageHandlerFactory = _ => application.App.GetTestServer().CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
    }
}
