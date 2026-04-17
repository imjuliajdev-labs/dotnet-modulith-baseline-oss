using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Api.Authentication;
using Identity.Application.Authentication; // BP-031 dispatch-coverage marker
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Npgsql;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests;

public sealed class IdentityAuthenticationIntegrationTests
{
    [Xunit.Fact]
    public async Task IdentityLoginAndCurrentUserFlowRoundTripThroughCookieAuthentication()
    {
        await using var application = await CreateApplicationAsync(new Dictionary<string, string?>
        {
            ["Modules:Identity:SeededAdmin:Password"] = "LocalOnly!123"
        });

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var antiforgery = await GetAntiforgeryAsync(client);
        Xunit.Assert.Contains("; secure", antiforgery.SetCookieHeader, StringComparison.OrdinalIgnoreCase);
        Xunit.Assert.Contains("; httponly", antiforgery.SetCookieHeader, StringComparison.OrdinalIgnoreCase);

        var loginResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new PasswordSignInRequest("admin", "LocalOnly!123"),
            antiforgery.Cookie,
            antiforgery.HeaderName,
            antiforgery.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var authCookie = GetCookie(loginResponse, "__Host-dotnet-modulith-baseline");

        using (var loginJson = await ReadJsonAsync(loginResponse))
        {
            Xunit.Assert.Equal("admin", loginJson.RootElement.GetProperty("userName").GetString());
            Xunit.Assert.Equal("Baseline Admin", loginJson.RootElement.GetProperty("displayName").GetString());
        }

        var meResponse = await SendAsync(client, HttpMethod.Get, "/api/v1/identity/me", authCookie);

        Xunit.Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        using var meJson = await ReadJsonAsync(meResponse);
        Xunit.Assert.Equal("admin", meJson.RootElement.GetProperty("userName").GetString());
        Xunit.Assert.Equal("Baseline Admin", meJson.RootElement.GetProperty("displayName").GetString());

        var roles = meJson.RootElement.GetProperty("roles").EnumerateArray();
        Xunit.Assert.Contains(roles, element => string.Equals(element.GetString(), BuildingBlocks.Application.Authorization.IdentityRoles.Admin, StringComparison.Ordinal));
    }

    [Xunit.Fact]
    public async Task IdentityLoginRejectsInvalidCredentialsWithCanonicalProblemDetails()
    {
        await using var application = await CreateApplicationAsync(new Dictionary<string, string?>
        {
            ["Modules:Identity:SeededAdmin:Password"] = "LocalOnly!123"
        });

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var antiforgery = await GetAntiforgeryAsync(client);
        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new PasswordSignInRequest("admin", "wrong-password"),
            antiforgery.Cookie,
            antiforgery.HeaderName,
            antiforgery.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Xunit.Assert.Equal("identity.invalid_credentials", json.RootElement.GetProperty("code").GetString());
        Xunit.Assert.Equal("Unauthorized", json.RootElement.GetProperty("title").GetString());
    }

    [Xunit.Fact]
    public async Task IdentityTreatsSeededAdminPasswordsWithLeadingAndTrailingWhitespaceAsExactCredentials()
    {
        const string exactPassword = "  LocalOnly!123  ";

        await using var application = await CreateApplicationAsync(new Dictionary<string, string?>
        {
            ["Modules:Identity:SeededAdmin:Password"] = exactPassword
        });

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAsync(client, "admin", exactPassword);

        var trimmedStepUp = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/step-up",
            new StepUpCurrentActorRequest(exactPassword.Trim()),
            session.Cookies,
            session.HeaderName,
            session.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, trimmedStepUp.StatusCode);
        using (var trimmedStepUpJson = await ReadJsonAsync(trimmedStepUp))
        {
            Xunit.Assert.Equal("identity.invalid_credentials", trimmedStepUpJson.RootElement.GetProperty("code").GetString());
        }

        var exactStepUp = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/step-up",
            new StepUpCurrentActorRequest(exactPassword),
            session.Cookies,
            session.HeaderName,
            session.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, exactStepUp.StatusCode);

        var trimmedLoginAntiforgery = await GetAntiforgeryAsync(client);
        var trimmedLogin = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new PasswordSignInRequest("admin", exactPassword.Trim()),
            trimmedLoginAntiforgery.Cookie,
            trimmedLoginAntiforgery.HeaderName,
            trimmedLoginAntiforgery.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, trimmedLogin.StatusCode);
        using var trimmedLoginJson = await ReadJsonAsync(trimmedLogin);
        Xunit.Assert.Equal("identity.invalid_credentials", trimmedLoginJson.RootElement.GetProperty("code").GetString());
    }

    [Xunit.Fact]
    public async Task IdentityLoginEndpointReturnsTooManyRequestsAfterTenAttemptsWithinTheRateLimitWindow()
    {
        await using var application = await CreateApplicationAsync(new Dictionary<string, string?>
        {
            ["Modules:Identity:SeededAdmin:Password"] = "LocalOnly!123"
        });

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        async Task<HttpResponseMessage> AttemptLoginAsync()
        {
            var antiforgery = await GetAntiforgeryAsync(client);
            return await SendJsonAsync(
                client,
                HttpMethod.Post,
                "/api/v1/identity/session/login",
                new PasswordSignInRequest("missing-user", "Wrong!Passw0rd"),
                antiforgery.Cookie,
                antiforgery.HeaderName,
                antiforgery.RequestToken);
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var response = await AttemptLoginAsync();
            Xunit.Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var rateLimited = await AttemptLoginAsync();
        Xunit.Assert.Equal(HttpStatusCode.TooManyRequests, rateLimited.StatusCode);
    }

    [Xunit.Fact]
    public async Task IdentityStepUpEndpointSharesThePerIpAuthenticationAttemptBudget()
    {
        await using var application = await CreateApplicationAsync(new Dictionary<string, string?>
        {
            ["Modules:Identity:SeededAdmin:Password"] = "LocalOnly!123"
        });

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAsync(client, "admin", "LocalOnly!123");

        for (var attempt = 0; attempt < 9; attempt++)
        {
            var response = await SendJsonAsync(
                client,
                HttpMethod.Post,
                "/api/v1/identity/session/step-up",
                new StepUpCurrentActorRequest("LocalOnly!123"),
                session.Cookies,
                session.HeaderName,
                session.RequestToken);

            Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var rateLimited = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/step-up",
            new StepUpCurrentActorRequest("LocalOnly!123"),
            session.Cookies,
            session.HeaderName,
            session.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.TooManyRequests, rateLimited.StatusCode);
    }

    [Xunit.Fact]
    public async Task IdentityLogoutRequiresAntiforgeryAndUsesCanonicalProblemDetailsWhenMissing()
    {
        await using var application = await CreateApplicationAsync(new Dictionary<string, string?>
        {
            ["Modules:Identity:SeededAdmin:Password"] = "LocalOnly!123"
        });

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var antiforgery = await GetAntiforgeryAsync(client);
        var loginResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new PasswordSignInRequest("admin", "LocalOnly!123"),
            antiforgery.Cookie,
            antiforgery.HeaderName,
            antiforgery.RequestToken);

        var authCookie = GetCookie(loginResponse, "__Host-dotnet-modulith-baseline");
        var logoutResponse = await SendAsync(client, HttpMethod.Post, "/api/v1/identity/session/logout", authCookie);

        Xunit.Assert.Equal(HttpStatusCode.BadRequest, logoutResponse.StatusCode);

        using var json = await ReadJsonAsync(logoutResponse);
        Xunit.Assert.Equal("security.antiforgery_invalid", json.RootElement.GetProperty("code").GetString());
        Xunit.Assert.Equal("Validation failed", json.RootElement.GetProperty("title").GetString());
    }

    [Xunit.Fact]
    public async Task IdentityRejectsStaleCookiesWhenTheSecurityStampChanges()
    {
        await using var application = await CreateApplicationAsync(new Dictionary<string, string?>
        {
            ["Modules:Identity:SeededAdmin:Password"] = "LocalOnly!123"
        });

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var antiforgery = await GetAntiforgeryAsync(client);
        var loginResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new PasswordSignInRequest("admin", "LocalOnly!123"),
            antiforgery.Cookie,
            antiforgery.HeaderName,
            antiforgery.RequestToken);

        var authCookie = GetCookie(loginResponse, "__Host-dotnet-modulith-baseline");
        var connectionString = application.App.Configuration.GetConnectionString("BaselineDatabase")
            ?? throw new InvalidOperationException("Identity authentication integration tests require ConnectionStrings:BaselineDatabase.");

        await UpdateSecurityStampAsync(connectionString, "identity:seeded-admin", Guid.NewGuid().ToString("n"));

        var meResponse = await SendAsync(client, HttpMethod.Get, "/api/v1/identity/me", authCookie);

        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);

        using var json = await ReadJsonAsync(meResponse);
        Xunit.Assert.Equal("auth.unauthorized", json.RootElement.GetProperty("code").GetString());
    }

    private static Task<PostgresBackedApiApplication> CreateApplicationAsync(IReadOnlyDictionary<string, string?> configuration)
    {
        return PostgresBackedApiApplication.StartAsync(configurationOverrides: configuration);
    }
    private static async Task UpdateSecurityStampAsync(string connectionString, string actorId, string securityStamp)
    {
        const string sql = """
            UPDATE identity.accounts
            SET security_stamp = @securityStamp
            WHERE actor_id = @actorId;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("actorId", actorId);
        command.Parameters.AddWithValue("securityStamp", securityStamp);

        var updated = await command.ExecuteNonQueryAsync();
        Xunit.Assert.Equal(1, updated);
    }
}
