using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blog.Api;
using Blog.Application.Settings; // BP-031 dispatch-coverage marker
using Identity.Api.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using IClock = BuildingBlocks.Domain.Time.IClock;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests.ModuleCoverage.Blog.Configuration;

public sealed class BlogSettingsIntegrationTests
{
    [Xunit.Fact]
    public async Task BlogSettingsPersistAuditedUpdatesAcrossApplicationRestart()
    {
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync("blog_settings_" + Guid.NewGuid().ToString("N"));
        var connectionString = postgres.GetConnectionString();

        await using (var application = await PostgresBackedApiApplication.StartAsync(connectionString))
        {
            var client = application.App.GetTestClient();
            client.BaseAddress = new Uri("https://localhost");

            var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");

            var settingsBefore = await SendAsync(client, HttpMethod.Get, "/api/v1/blog/settings", adminSession.Cookies);
            settingsBefore.EnsureSuccessStatusCode();

            using var beforeJson = await ReadJsonAsync(settingsBefore);
            Xunit.Assert.Equal("Editorial publishing surface for the Blog module.", beforeJson.RootElement.GetProperty("operatorSummary").GetString());
            Xunit.Assert.Equal(10, beforeJson.RootElement.GetProperty("previewLimit").GetInt32());
            Xunit.Assert.Equal(1, beforeJson.RootElement.GetProperty("version").GetInt32());

            var updateResponse = await SendJsonAsync(
                client,
                HttpMethod.Put,
                "/api/v1/blog/settings",
                new UpdateBlogSettingsRequest(
                    ExpectedVersion: 1,
                    OperatorSummary: "Persisted Blog operator summary",
                    PreviewLimit: 6),
                adminSession.Cookies,
                adminSession.HeaderName,
                adminSession.RequestToken);

            updateResponse.EnsureSuccessStatusCode();

            using (var updateJson = await ReadJsonAsync(updateResponse))
            {
                Xunit.Assert.Equal("Persisted Blog operator summary", updateJson.RootElement.GetProperty("operatorSummary").GetString());
                Xunit.Assert.Equal(6, updateJson.RootElement.GetProperty("previewLimit").GetInt32());
                Xunit.Assert.Equal(2, updateJson.RootElement.GetProperty("version").GetInt32());
            }
        }

        await using (var restartedApplication = await PostgresBackedApiApplication.StartAsync(connectionString, applyMigrations: false))
        {
            var restartedClient = restartedApplication.App.GetTestClient();
            restartedClient.BaseAddress = new Uri("https://localhost");

            var adminSession = await SignInAsync(restartedClient, "admin", "LocalOnly!123");
            var settingsAfterRestart = await SendAsync(restartedClient, HttpMethod.Get, "/api/v1/blog/settings", adminSession.Cookies);
            settingsAfterRestart.EnsureSuccessStatusCode();

            using (var restartJson = await ReadJsonAsync(settingsAfterRestart))
            {
                Xunit.Assert.Equal("Persisted Blog operator summary", restartJson.RootElement.GetProperty("operatorSummary").GetString());
                Xunit.Assert.Equal(6, restartJson.RootElement.GetProperty("previewLimit").GetInt32());
                Xunit.Assert.Equal(2, restartJson.RootElement.GetProperty("version").GetInt32());
            }

            var auditResponse = await SendAsync(restartedClient, HttpMethod.Get, "/api/v1/platform/audit-events?limit=20", adminSession.Cookies);
            auditResponse.EnsureSuccessStatusCode();

            using var auditJson = await ReadJsonAsync(auditResponse);
            var events = auditJson.RootElement.GetProperty("events").EnumerateArray().ToArray();
            Xunit.Assert.Contains(events, entry =>
                string.Equals(entry.GetProperty("action").GetString(), "blog.settings.update", StringComparison.Ordinal)
                && string.Equals(entry.GetProperty("targetId").GetString(), "default", StringComparison.Ordinal)
                && string.Equals(entry.GetProperty("actorId").GetString(), "identity:seeded-admin", StringComparison.Ordinal));
        }
    }

    [Xunit.Fact]
    public async Task BlogSettingsUpdatesRequireRecentAuthentication()
    {
        var clock = new AdjustableClock(SystemClock.Instance.GetCurrentInstant());

        await using var application = await PostgresBackedApiApplication.StartAsync(
            configureBuilder: builder => builder.Services.AddSingleton<IClock>(clock));

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        clock.Advance(Duration.FromMinutes(6));

        var response = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/blog/settings",
            new UpdateBlogSettingsRequest(1, "Late update", 4),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Xunit.Assert.Equal("auth.recent_auth_required", json.RootElement.GetProperty("code").GetString());
    }

    [Xunit.Fact]
    public async Task BlogSettingsRejectStaleVersionWithConflictError()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");

        var firstUpdate = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/blog/settings",
            new UpdateBlogSettingsRequest(1, "First update", 7),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        firstUpdate.EnsureSuccessStatusCode();

        var staleUpdate = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/blog/settings",
            new UpdateBlogSettingsRequest(1, "Stale update", 8),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);

        using var staleJson = await ReadJsonAsync(staleUpdate);
        Xunit.Assert.Equal("blog.settings_version_conflict", staleJson.RootElement.GetProperty("code").GetString());
    }
}
