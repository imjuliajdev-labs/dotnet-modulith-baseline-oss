using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Modules;
using BuildingBlocks.Infrastructure.Realtime;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Xunit;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests;

public sealed class PlatformRealtimeIntegrationTests
{
    [Fact]
    public async Task BrowserRealtimeHubNegotiationRequiresAuthenticatedBrowserSession()
    {
        await using var application = await CreateApplicationAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        using var anonymousRequest = new HttpRequestMessage(HttpMethod.Post, $"{BrowserRealtimeDefaults.Path}/negotiate?negotiateVersion=1")
        {
            Content = JsonContent.Create(new { })
        };

        var anonymousResponse = await client.SendAsync(anonymousRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        var signInResult = await SignInAsync(client);


        var authCookie = signInResult.Cookie;

        using var authenticatedRequest = new HttpRequestMessage(HttpMethod.Post, $"{BrowserRealtimeDefaults.Path}/negotiate?negotiateVersion=1")
        {
            Content = JsonContent.Create(new { })
        };
        authenticatedRequest.Headers.Add("Cookie", authCookie);

        var authenticatedResponse = await client.SendAsync(authenticatedRequest);
        Assert.Equal(HttpStatusCode.OK, authenticatedResponse.StatusCode);
    }

    [Fact]
    public async Task PlatformModuleStateChangesArePublishedToAuthenticatedRealtimeClients()
    {
        await using var application = await CreateApplicationAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAsync(client);


        var authCookie = session.Cookie;


        var cookies = session.Cookies;

        var messageReceived = new TaskCompletionSource<(string Channel, JsonElement Payload)>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = CreateConnection(application, cookies);
        connection.On<string, JsonElement>(BrowserRealtimeMethods.Message, (channel, payload) =>
        {
            messageReceived.TrySetResult((channel, payload));
        });

        await connection.StartAsync();

        var enableResponse = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/platform/modules/reports/enable",
            cookies,
            session.HeaderName,
            session.RequestToken);

        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        var (channel, payload) = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("platform.module-state.changed", channel);
        Assert.Equal("reports", payload.GetProperty("moduleKey").GetString());
        Assert.Equal("enabled", payload.GetProperty("desiredState").GetString());
        Assert.Equal("enabled", payload.GetProperty("runtimeState").GetString());
        Assert.Equal(2, payload.GetProperty("version").GetInt64());
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

    private static Task<PostgresBackedApiApplication> CreateApplicationAsync()
    {
        return PostgresBackedApiApplication.StartAsync(configureBuilder: builder =>
        {
            builder.Services.AddApiModule<PlatformModuleStateIntegrationTests.ReportsModule>();
        });
    }
    private static string ReadSetCookieHeader(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join("; ", values.Select(static value => value.Split(';', 2)[0]))
            : throw new InvalidOperationException("Expected a Set-Cookie header in the response.");
    }
}
