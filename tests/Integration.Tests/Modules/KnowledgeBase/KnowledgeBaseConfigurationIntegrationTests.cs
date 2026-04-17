using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Api.Authentication;
using KnowledgeBase.Api;
using KnowledgeBase.Application.Settings; // BP-031 dispatch-coverage marker
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NodaTime;
using IClock = BuildingBlocks.Domain.Time.IClock;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests.Modules.KnowledgeBase;

public sealed class KnowledgeBaseConfigurationIntegrationTests
{
    [Xunit.Fact]
    public async Task KnowledgeBaseSettingsExposeDefaultsAndPersistAuditedUpdates()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var publicBefore = await client.GetAsync("/api/v1/knowledge-base/settings");
        Xunit.Assert.Equal(HttpStatusCode.OK, publicBefore.StatusCode);

        using (var publicBeforeJson = await ReadJsonAsync(publicBefore))
        {
            Xunit.Assert.Equal("Knowledge Base", publicBeforeJson.RootElement.GetProperty("publicExperienceTitle").GetString());
            Xunit.Assert.True(publicBeforeJson.RootElement.GetProperty("searchEnabled").GetBoolean());
        }

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");

        var managementBefore = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/v1/knowledge-base/manage/settings",
            adminSession.Cookies);
        managementBefore.EnsureSuccessStatusCode();

        var updatedTitle = "Operator-curated knowledge hub";
        using var managementBeforeJson = await ReadJsonAsync(managementBefore);
        var updateResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/knowledge-base/manage/settings",
            new UpdateKnowledgeBaseSettingsRequest(
                ExpectedVersion: managementBeforeJson.RootElement.GetProperty("version").GetInt32(),
                PublicExperienceTitle: updatedTitle,
                PublicExperienceBlurb: "Live module settings now flow through a persisted, audited KnowledgeBase reference slice.",
                SearchPlaceholder: "Search governed guidance",
                SearchEnabled: false,
                ManagementPreviewLimit: 5),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        updateResponse.EnsureSuccessStatusCode();

        using (var updateJson = await ReadJsonAsync(updateResponse))
        {
            Xunit.Assert.Equal(updatedTitle, updateJson.RootElement.GetProperty("publicExperienceTitle").GetString());
            Xunit.Assert.Equal(2, updateJson.RootElement.GetProperty("version").GetInt32());
            Xunit.Assert.False(updateJson.RootElement.GetProperty("searchEnabled").GetBoolean());
            Xunit.Assert.Equal(5, updateJson.RootElement.GetProperty("managementPreviewLimit").GetInt32());
        }

        var publicAfter = await client.GetAsync("/api/v1/knowledge-base/settings");
        publicAfter.EnsureSuccessStatusCode();

        using (var publicAfterJson = await ReadJsonAsync(publicAfter))
        {
            Xunit.Assert.Equal(updatedTitle, publicAfterJson.RootElement.GetProperty("publicExperienceTitle").GetString());
            Xunit.Assert.False(publicAfterJson.RootElement.GetProperty("searchEnabled").GetBoolean());
        }

        var auditResponse = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/audit-events?limit=20", adminSession.Cookies);
        auditResponse.EnsureSuccessStatusCode();

        using var auditJson = await ReadJsonAsync(auditResponse);
        var events = auditJson.RootElement.GetProperty("events").EnumerateArray().ToArray();
        Xunit.Assert.Contains(events, entry =>
            string.Equals(entry.GetProperty("action").GetString(), "knowledge-base.settings.update", StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("targetId").GetString(), "default", StringComparison.Ordinal));
    }

    [Xunit.Fact]
    public async Task KnowledgeBaseSettingsUpdatesRequireRecentAuthentication()
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
            "/api/v1/knowledge-base/manage/settings",
            new UpdateKnowledgeBaseSettingsRequest(
                ExpectedVersion: 1,
                PublicExperienceTitle: "Late update",
                PublicExperienceBlurb: "This write should require recent auth.",
                SearchPlaceholder: "Search governed guidance",
                SearchEnabled: true,
                ManagementPreviewLimit: 4),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Xunit.Assert.Equal("auth.recent_auth_required", json.RootElement.GetProperty("code").GetString());
    }

    [Xunit.Fact]
    public async Task KnowledgeBaseSettingsRejectStaleVersionWithConflictError()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");

        var managementBefore = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/v1/knowledge-base/manage/settings",
            adminSession.Cookies);
        managementBefore.EnsureSuccessStatusCode();

        using var managementBeforeJson = await ReadJsonAsync(managementBefore);
        var initialVersion = managementBeforeJson.RootElement.GetProperty("version").GetInt32();
        Xunit.Assert.Equal(1, initialVersion);

        var firstUpdate = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/knowledge-base/manage/settings",
            new UpdateKnowledgeBaseSettingsRequest(
                ExpectedVersion: 1,
                PublicExperienceTitle: "First update",
                PublicExperienceBlurb: "First update blurb.",
                SearchPlaceholder: "Search governed guidance",
                SearchEnabled: true,
                ManagementPreviewLimit: 5),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        firstUpdate.EnsureSuccessStatusCode();

        using (var firstUpdateJson = await ReadJsonAsync(firstUpdate))
        {
            Xunit.Assert.Equal(2, firstUpdateJson.RootElement.GetProperty("version").GetInt32());
        }

        var staleUpdate = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/knowledge-base/manage/settings",
            new UpdateKnowledgeBaseSettingsRequest(
                ExpectedVersion: 1,
                PublicExperienceTitle: "Stale update",
                PublicExperienceBlurb: "Stale update blurb.",
                SearchPlaceholder: "Search governed guidance",
                SearchEnabled: true,
                ManagementPreviewLimit: 5),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);

        using var staleJson = await ReadJsonAsync(staleUpdate);
        Xunit.Assert.Equal("knowledge-base.settings_version_conflict", staleJson.RootElement.GetProperty("code").GetString());
    }

    [Xunit.Fact]
    public async Task KnowledgeBaseSettingsAuditTrailRecordsActorAndCorrelationId()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");

        var updateResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/knowledge-base/manage/settings",
            new UpdateKnowledgeBaseSettingsRequest(
                ExpectedVersion: 1,
                PublicExperienceTitle: "Audit trail test",
                PublicExperienceBlurb: "Verifying audit metadata.",
                SearchPlaceholder: "Search governed guidance",
                SearchEnabled: true,
                ManagementPreviewLimit: 4),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        updateResponse.EnsureSuccessStatusCode();

        var auditResponse = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/audit-events?limit=20", adminSession.Cookies);
        auditResponse.EnsureSuccessStatusCode();

        using var auditJson = await ReadJsonAsync(auditResponse);
        var events = auditJson.RootElement.GetProperty("events").EnumerateArray().ToArray();
        var settingsEvent = events.FirstOrDefault(entry =>
            string.Equals(entry.GetProperty("action").GetString(), "knowledge-base.settings.update", StringComparison.Ordinal));

        Xunit.Assert.NotEqual(default, settingsEvent);
        Xunit.Assert.Equal("identity:seeded-admin", settingsEvent.GetProperty("actorId").GetString());
        Xunit.Assert.False(string.IsNullOrWhiteSpace(settingsEvent.GetProperty("correlationId").GetString()));

        var occurredUtcString = settingsEvent.GetProperty("occurredUtc").GetString();
        Xunit.Assert.NotNull(occurredUtcString);
        Xunit.Assert.True(DateTimeOffset.TryParse(occurredUtcString, out _));
    }

    [Xunit.Fact]
    public async Task KnowledgeBaseSettingsRejectInvalidManagementPreviewLimitAtRuntime()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");

        var zeroLimitResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/knowledge-base/manage/settings",
            new UpdateKnowledgeBaseSettingsRequest(
                ExpectedVersion: 1,
                PublicExperienceTitle: "Valid title",
                PublicExperienceBlurb: "Valid blurb.",
                SearchPlaceholder: "Search governed guidance",
                SearchEnabled: true,
                ManagementPreviewLimit: 0),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.BadRequest, zeroLimitResponse.StatusCode);

        using (var zeroLimitJson = await ReadJsonAsync(zeroLimitResponse))
        {
            Xunit.Assert.Equal("knowledge-base.settings_preview_limit_invalid", zeroLimitJson.RootElement.GetProperty("code").GetString());
        }

        var negativeLimitResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/knowledge-base/manage/settings",
            new UpdateKnowledgeBaseSettingsRequest(
                ExpectedVersion: 1,
                PublicExperienceTitle: "Valid title",
                PublicExperienceBlurb: "Valid blurb.",
                SearchPlaceholder: "Search governed guidance",
                SearchEnabled: true,
                ManagementPreviewLimit: -1),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.BadRequest, negativeLimitResponse.StatusCode);

        using (var negativeLimitJson = await ReadJsonAsync(negativeLimitResponse))
        {
            Xunit.Assert.Equal("knowledge-base.settings_preview_limit_invalid", negativeLimitJson.RootElement.GetProperty("code").GetString());
        }
    }

    [Xunit.Fact]
    public async Task KnowledgeBaseSettingsRejectEmptyPublicExperienceTitle()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");

        var emptyTitleResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/knowledge-base/manage/settings",
            new UpdateKnowledgeBaseSettingsRequest(
                ExpectedVersion: 1,
                PublicExperienceTitle: "",
                PublicExperienceBlurb: "Valid blurb.",
                SearchPlaceholder: "Search governed guidance",
                SearchEnabled: true,
                ManagementPreviewLimit: 4),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.BadRequest, emptyTitleResponse.StatusCode);

        using (var emptyTitleJson = await ReadJsonAsync(emptyTitleResponse))
        {
            Xunit.Assert.Equal("knowledge-base.settings_title_required", emptyTitleJson.RootElement.GetProperty("code").GetString());
        }

        var whitespaceTitleResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/knowledge-base/manage/settings",
            new UpdateKnowledgeBaseSettingsRequest(
                ExpectedVersion: 1,
                PublicExperienceTitle: "   ",
                PublicExperienceBlurb: "Valid blurb.",
                SearchPlaceholder: "Search governed guidance",
                SearchEnabled: true,
                ManagementPreviewLimit: 4),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.BadRequest, whitespaceTitleResponse.StatusCode);

        using (var whitespaceTitleJson = await ReadJsonAsync(whitespaceTitleResponse))
        {
            Xunit.Assert.Equal("knowledge-base.settings_title_required", whitespaceTitleJson.RootElement.GetProperty("code").GetString());
        }
    }

    [Xunit.Fact]
    public async Task KnowledgeBaseInfrastructureOptionsFailFastWhenSettingsDefaultsAreInvalid()
    {
        await Xunit.Assert.ThrowsAnyAsync<OptionsValidationException>(async () =>
        {
            await using var application = await PostgresBackedApiApplication.StartAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["Modules:KnowledgeBase:Settings:ManagementPreviewLimit"] = "0"
                });
        });
    }
}
