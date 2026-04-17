using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Domain.Time;
using Identity.Api.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using IClock = BuildingBlocks.Domain.Time.IClock;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests;

public sealed class IdentityRecentAuthenticationIntegrationTests
{
    [Xunit.Fact]
    public async Task StepUpRefreshesRecentAuthenticationAndAllowsSensitiveIdentityAndPlatformMutations()
    {
        var clock = new AdjustableClock(SystemClock.Instance.GetCurrentInstant());

        await using var application = await PostgresBackedApiApplication.StartAsync(
            configureBuilder: builder => builder.Services.AddSingleton<IClock>(clock));

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        clock.Advance(Duration.FromMinutes(6));

        var staleMachineCreate = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/machine-clients",
            new { ClientName = "stale-before-step-up", Roles = new[] { BuildingBlocks.Application.Authorization.IdentityRoles.Machine } },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        await AssertProblemAsync(staleMachineCreate, HttpStatusCode.Unauthorized, "auth.recent_auth_required");

        var stepUp = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/step-up",
            new StepUpCurrentActorRequest("LocalOnly!123"),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, stepUp.StatusCode);

        var refreshedAuthCookie = GetCookie(stepUp, "__Host-dotnet-modulith-baseline");
        var refreshedAntiforgery = await GetAntiforgeryAsync(client, refreshedAuthCookie);
        var refreshedCookies = CombineCookies(refreshedAuthCookie, refreshedAntiforgery.Cookie);

        var machineCreate = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/machine-clients",
            new { ClientName = "fresh-after-step-up", Roles = new[] { BuildingBlocks.Application.Authorization.IdentityRoles.Machine } },
            refreshedCookies,
            refreshedAntiforgery.HeaderName,
            refreshedAntiforgery.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, machineCreate.StatusCode);

        var optionalModuleKey = await GetOptionalModuleKeyAsync(client);
        if (optionalModuleKey is not null)
        {
            var disableOptionalModule = await SendAsync(
                client,
                HttpMethod.Post,
                $"/api/v1/platform/modules/{optionalModuleKey}/disable",
                refreshedCookies,
                refreshedAntiforgery.HeaderName,
                refreshedAntiforgery.RequestToken);

            Xunit.Assert.Equal(HttpStatusCode.OK, disableOptionalModule.StatusCode);
        }
    }

    [Xunit.Fact]
    public async Task StepUpRejectsInvalidPasswordAndDoesNotRefreshRecentAuthentication()
    {
        var clock = new AdjustableClock(SystemClock.Instance.GetCurrentInstant());

        await using var application = await PostgresBackedApiApplication.StartAsync(
            configureBuilder: builder => builder.Services.AddSingleton<IClock>(clock));

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        clock.Advance(Duration.FromMinutes(6));

        var stepUp = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/step-up",
            new StepUpCurrentActorRequest("wrong-password"),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        await AssertProblemAsync(stepUp, HttpStatusCode.Unauthorized, "identity.invalid_credentials");

        var optionalModuleKey = await GetOptionalModuleKeyAsync(client);
        if (optionalModuleKey is not null)
        {
            var stalePlatformMutation = await SendAsync(
                client,
                HttpMethod.Post,
                $"/api/v1/platform/modules/{optionalModuleKey}/disable",
                adminSession.Cookies,
                adminSession.HeaderName,
                adminSession.RequestToken);

            await AssertProblemAsync(stalePlatformMutation, HttpStatusCode.Unauthorized, "auth.recent_auth_required");
        }
    }
    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedCode)
    {
        Xunit.Assert.Equal(expectedStatus, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Xunit.Assert.Equal(expectedCode, json.RootElement.GetProperty("code").GetString());
    }
    private static async Task<string?> GetOptionalModuleKeyAsync(HttpClient client)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/modules");
        response.EnsureSuccessStatusCode();

        using var json = await ReadJsonAsync(response);
        foreach (var module in json.RootElement.GetProperty("modules").EnumerateArray())
        {
            if (module.GetProperty("canBeDisabled").GetBoolean())
            {
                return module.GetProperty("key").GetString();
            }
        }

        return null;
    }
}
