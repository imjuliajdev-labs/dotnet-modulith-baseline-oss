using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Api.Authentication;
using Identity.Application.Administration; // BP-031 dispatch-coverage marker
using Microsoft.AspNetCore.TestHost;
using Platform.Application.Bootstrap; // BP-031 dispatch-coverage marker
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using IClock = BuildingBlocks.Domain.Time.IClock;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests;

public sealed class MachineAuthenticationIntegrationTests
{
    [Xunit.Fact]
    public async Task MachineBootstrapEndpointRequiresMachineAuthenticationScheme()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var anonymousResponse = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/machine/bootstrap");
        await AssertProblemAsync(anonymousResponse, HttpStatusCode.Unauthorized, "auth.unauthorized");

        var antiforgery = await GetAntiforgeryAsync(client);
        var login = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new PasswordSignInRequest("admin", "LocalOnly!123"),
            antiforgery.Cookie,
            antiforgery.HeaderName,
            antiforgery.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = GetCookie(login, "__Host-dotnet-modulith-baseline");

        var cookieOnlyResponse = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/machine/bootstrap", cookie);
        await AssertProblemAsync(cookieOnlyResponse, HttpStatusCode.Unauthorized, "auth.unauthorized");
    }

    [Xunit.Fact]
    public async Task MachineBootstrapEndpointAcceptsValidMachineCredential()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var validMachineResponse = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/v1/platform/machine/bootstrap",
            machineApiKey: "MachineOnly!123");

        Xunit.Assert.Equal(HttpStatusCode.OK, validMachineResponse.StatusCode);
        using var validJson = await ReadJsonAsync(validMachineResponse);
        var modules = validJson.RootElement.GetProperty("modules");
        Xunit.Assert.True(modules.GetArrayLength() >= 4);
    }

    [Xunit.Fact]
    public async Task MachineBootstrapEndpointTreatsSeededMachineKeysAsExactCredentials()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var paddedMachineKeyResponse = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/v1/platform/machine/bootstrap",
            machineApiKey: " MachineOnly!123 ");

        await AssertProblemAsync(paddedMachineKeyResponse, HttpStatusCode.Unauthorized, "auth.unauthorized");

        var exactMachineKeyResponse = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/v1/platform/machine/bootstrap",
            machineApiKey: "MachineOnly!123");

        Xunit.Assert.Equal(HttpStatusCode.OK, exactMachineKeyResponse.StatusCode);
    }

    [Xunit.Fact]
    public async Task MachineClientLifecycleCreateGetDisableRotateRevokeFlowThroughPersistedStore()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsAdminAsync(client);

        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/machine-clients",
            new { ClientName = "test-ci-client", Roles = new[] { BuildingBlocks.Application.Authorization.IdentityRoles.Machine } },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        string clientId;
        string plaintextSecret;
        using (var createJson = await ReadJsonAsync(createResponse))
        {
            clientId = createJson.RootElement.GetProperty("clientId").GetString()!;
            plaintextSecret = createJson.RootElement.GetProperty("plaintextSecret").GetString()!;
            Xunit.Assert.StartsWith("machine:client:", clientId);
            Xunit.Assert.False(string.IsNullOrWhiteSpace(plaintextSecret));
        }

        var listResponse = await SendAsync(client, HttpMethod.Get, "/api/v1/identity/machine-clients", adminSession.Cookies);
        Xunit.Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using (var listJson = await ReadJsonAsync(listResponse))
        {
            var clients = listJson.RootElement.GetProperty("clients").EnumerateArray().ToArray();
            Xunit.Assert.Contains(clients, entry =>
                string.Equals(entry.GetProperty("clientId").GetString(), clientId, StringComparison.Ordinal)
                && string.Equals(entry.GetProperty("clientName").GetString(), "test-ci-client", StringComparison.Ordinal));
        }

        var getResponse = await SendAsync(client, HttpMethod.Get, $"/api/v1/identity/machine-clients/{clientId}", adminSession.Cookies);
        Xunit.Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using (var getJson = await ReadJsonAsync(getResponse))
        {
            Xunit.Assert.Equal(clientId, getJson.RootElement.GetProperty("clientId").GetString());
            Xunit.Assert.True(getJson.RootElement.GetProperty("isActive").GetBoolean());
            Xunit.Assert.False(getJson.RootElement.GetProperty("isRevoked").GetBoolean());
        }

        var machineKey = $"{clientId}:{plaintextSecret}";
        var bootstrapResponse = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/v1/platform/machine/bootstrap",
            machineApiKey: machineKey);
        Xunit.Assert.Equal(HttpStatusCode.OK, bootstrapResponse.StatusCode);

        var deactivateResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/identity/machine-clients/{clientId}/status",
            new { IsActive = false },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);

        var inactiveKeyResponse = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/v1/platform/machine/bootstrap",
            machineApiKey: machineKey);
        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, inactiveKeyResponse.StatusCode);

        var reactivateResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/identity/machine-clients/{clientId}/status",
            new { IsActive = true },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.OK, reactivateResponse.StatusCode);

        var reactivatedKeyResponse = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/v1/platform/machine/bootstrap",
            machineApiKey: machineKey);
        Xunit.Assert.Equal(HttpStatusCode.OK, reactivatedKeyResponse.StatusCode);

        var rotateResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/identity/machine-clients/{clientId}/rotate-secret",
            new { },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.OK, rotateResponse.StatusCode);

        string newPlaintextSecret;
        using (var rotateJson = await ReadJsonAsync(rotateResponse))
        {
            newPlaintextSecret = rotateJson.RootElement.GetProperty("plaintextSecret").GetString()!;
            Xunit.Assert.False(string.IsNullOrWhiteSpace(newPlaintextSecret));
            Xunit.Assert.NotEqual(plaintextSecret, newPlaintextSecret);
        }

        var oldKeyResponse = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/v1/platform/machine/bootstrap",
            machineApiKey: machineKey);
        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, oldKeyResponse.StatusCode);

        var newMachineKey = $"{clientId}:{newPlaintextSecret}";
        var newKeyResponse = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/v1/platform/machine/bootstrap",
            machineApiKey: newMachineKey);
        Xunit.Assert.Equal(HttpStatusCode.OK, newKeyResponse.StatusCode);

        var revokeResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/identity/machine-clients/{clientId}/revoke",
            new { },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        var revokedKeyResponse = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/v1/platform/machine/bootstrap",
            machineApiKey: newMachineKey);
        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, revokedKeyResponse.StatusCode);
    }

    [Xunit.Fact]
    public async Task RevokedMachineClientsCannotBeReactivatedAndAuthenticationIsAuditedAsRevoked()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsAdminAsync(client);

        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/machine-clients",
            new { ClientName = "revoked-client", Roles = new[] { BuildingBlocks.Application.Authorization.IdentityRoles.Machine } },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        string clientId;
        string plaintextSecret;
        using (var json = await ReadJsonAsync(createResponse))
        {
            clientId = json.RootElement.GetProperty("clientId").GetString()!;
            plaintextSecret = json.RootElement.GetProperty("plaintextSecret").GetString()!;
        }

        var revokeResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/identity/machine-clients/{clientId}/revoke",
            new { },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        var reactivateResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/identity/machine-clients/{clientId}/status",
            new { IsActive = true },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);
        await AssertProblemAsync(reactivateResponse, HttpStatusCode.BadRequest, "identity.machine_client_already_revoked");

        var revokedKeyResponse = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/v1/platform/machine/bootstrap",
            machineApiKey: $"{clientId}:{plaintextSecret}");
        await AssertProblemAsync(revokedKeyResponse, HttpStatusCode.Unauthorized, "auth.unauthorized");

        var auditResponse = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/audit-events?limit=20", adminSession.Cookies);
        auditResponse.EnsureSuccessStatusCode();

        using var auditJson = await ReadJsonAsync(auditResponse);
        var events = auditJson.RootElement.GetProperty("events").EnumerateArray().ToArray();
        Xunit.Assert.Contains(events, entry =>
            string.Equals(entry.GetProperty("action").GetString(), "identity.machine_auth.authenticate", StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("targetId").GetString(), clientId, StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("outcome").GetString(), "failed_revoked", StringComparison.Ordinal));
    }

    [Xunit.Fact]
    public async Task InactiveMachineClientsFailAuthenticationWithoutBeingTreatedAsRevoked()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsAdminAsync(client);

        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/machine-clients",
            new { ClientName = "inactive-client", Roles = new[] { BuildingBlocks.Application.Authorization.IdentityRoles.Machine } },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        string clientId;
        string plaintextSecret;
        using (var json = await ReadJsonAsync(createResponse))
        {
            clientId = json.RootElement.GetProperty("clientId").GetString()!;
            plaintextSecret = json.RootElement.GetProperty("plaintextSecret").GetString()!;
        }

        var deactivateResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/identity/machine-clients/{clientId}/status",
            new { IsActive = false },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);

        var getResponse = await SendAsync(client, HttpMethod.Get, $"/api/v1/identity/machine-clients/{clientId}", adminSession.Cookies);
        getResponse.EnsureSuccessStatusCode();
        using (var getJson = await ReadJsonAsync(getResponse))
        {
            Xunit.Assert.False(getJson.RootElement.GetProperty("isActive").GetBoolean());
            Xunit.Assert.False(getJson.RootElement.GetProperty("isRevoked").GetBoolean());
        }

        var inactiveKeyResponse = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/v1/platform/machine/bootstrap",
            machineApiKey: $"{clientId}:{plaintextSecret}");
        await AssertProblemAsync(inactiveKeyResponse, HttpStatusCode.Unauthorized, "auth.unauthorized");

        var auditResponse = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/audit-events?limit=20", adminSession.Cookies);
        auditResponse.EnsureSuccessStatusCode();

        using var auditJson = await ReadJsonAsync(auditResponse);
        var events = auditJson.RootElement.GetProperty("events").EnumerateArray().ToArray();
        Xunit.Assert.Contains(events, entry =>
            string.Equals(entry.GetProperty("action").GetString(), "identity.machine_auth.authenticate", StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("targetId").GetString(), clientId, StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("outcome").GetString(), "failed_inactive", StringComparison.Ordinal));
    }

    [Xunit.Fact]
    public async Task SeededMachineAuthenticationContinuesToWorkAlongsidePersistedClients()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsAdminAsync(client);

        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/machine-clients",
            new { ClientName = "coexistence-test-client", Roles = new[] { BuildingBlocks.Application.Authorization.IdentityRoles.Machine } },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        string clientId;
        string plaintextSecret;
        using (var json = await ReadJsonAsync(createResponse))
        {
            clientId = json.RootElement.GetProperty("clientId").GetString()!;
            plaintextSecret = json.RootElement.GetProperty("plaintextSecret").GetString()!;
        }

        var seededResponse = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/v1/platform/machine/bootstrap",
            machineApiKey: "MachineOnly!123");
        Xunit.Assert.Equal(HttpStatusCode.OK, seededResponse.StatusCode);

        var persistedResponse = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/v1/platform/machine/bootstrap",
            machineApiKey: $"{clientId}:{plaintextSecret}");
        Xunit.Assert.Equal(HttpStatusCode.OK, persistedResponse.StatusCode);
    }

    [Xunit.Fact]
    public async Task MachineClientCreateRequiresAdminRole()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        // Anonymous calls must be rejected.
        var anonymousAntiforgery = await GetAntiforgeryAsync(client);
        var anonymousCreate = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/machine-clients",
            new { ClientName = "permission-check", Roles = new[] { BuildingBlocks.Application.Authorization.IdentityRoles.Machine } },
            anonymousAntiforgery.Cookie,
            anonymousAntiforgery.HeaderName,
            anonymousAntiforgery.RequestToken);

        await AssertProblemAsync(anonymousCreate, HttpStatusCode.Unauthorized, "auth.unauthorized");
    }

    [Xunit.Fact]
    public async Task MachineClientCreateRequiresRecentAuthentication()
    {
        var clock = new AdjustableClock(SystemClock.Instance.GetCurrentInstant());

        await using var application = await PostgresBackedApiApplication.StartAsync(
            configureBuilder: builder => builder.Services.AddSingleton<IClock>(clock));

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsAdminAsync(client);
        clock.Advance(Duration.FromMinutes(6));

        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/machine-clients",
            new { ClientName = "stale-session", Roles = new[] { BuildingBlocks.Application.Authorization.IdentityRoles.Machine } },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        await AssertProblemAsync(createResponse, HttpStatusCode.Unauthorized, "auth.recent_auth_required");
    }

    [Xunit.Fact]
    public async Task MachineAuthenticationAndPrivilegedMachineActionsAreAudited()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var invalidResponse = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/v1/platform/machine/bootstrap",
            machineApiKey: "MachineOnly!123-invalid");
        await AssertProblemAsync(invalidResponse, HttpStatusCode.Unauthorized, "auth.unauthorized");

        var bootstrapResponse = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/v1/platform/machine/bootstrap",
            machineApiKey: "MachineOnly!123");
        Xunit.Assert.Equal(HttpStatusCode.OK, bootstrapResponse.StatusCode);

        var adminModulePresent = await HasModuleAsync(client, "admin");
        if (adminModulePresent)
        {
            var adminResponse = await SendAsync(
                client,
                HttpMethod.Get,
                "/api/v1/admin/machine/announcements?limit=5",
                machineApiKey: "MachineOnly!123");
            Xunit.Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        }

        var adminSession = await SignInAsAdminAsync(client);
        var auditResponse = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/audit-events?limit=20", adminSession.Cookies);
        Xunit.Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);

        using var auditJson = await ReadJsonAsync(auditResponse);
        var events = auditJson.RootElement.GetProperty("events").EnumerateArray().ToArray();

        Xunit.Assert.Contains(events, entry =>
            string.Equals(entry.GetProperty("action").GetString(), "identity.machine_auth.authenticate", StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("targetId").GetString(), "unknown", StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("outcome").GetString(), "failed_invalid_credentials", StringComparison.Ordinal));

        Xunit.Assert.Contains(events, entry =>
            string.Equals(entry.GetProperty("action").GetString(), "identity.machine_auth.authenticate", StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("targetId").GetString(), "baseline-machine-client", StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("actorId").GetString(), "machine:seeded-client", StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("outcome").GetString(), "succeeded_seeded", StringComparison.Ordinal));

        Xunit.Assert.Contains(events, entry =>
            string.Equals(entry.GetProperty("action").GetString(), "platform.machine_bootstrap.read", StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("actorId").GetString(), "machine:seeded-client", StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("outcome").GetString(), "succeeded", StringComparison.Ordinal));

        if (adminModulePresent)
        {
            Xunit.Assert.Contains(events, entry =>
                string.Equals(entry.GetProperty("action").GetString(), "admin.machine_announcements.read", StringComparison.Ordinal)
                && string.Equals(entry.GetProperty("actorId").GetString(), "machine:seeded-client", StringComparison.Ordinal)
                && string.Equals(entry.GetProperty("outcome").GetString(), "succeeded", StringComparison.Ordinal));
        }
    }

    private static async Task<bool> HasModuleAsync(HttpClient client, string moduleKey)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/bootstrap");
        response.EnsureSuccessStatusCode();

        using var json = await ReadJsonAsync(response);
        return json.RootElement
            .GetProperty("modules")
            .EnumerateArray()
            .Any(module => string.Equals(module.GetProperty("key").GetString(), moduleKey, StringComparison.Ordinal));
    }

    private static async Task<AuthenticatedSession> SignInAsAdminAsync(HttpClient client)
    {
        var antiforgery = await GetAntiforgeryAsync(client);
        var login = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new PasswordSignInRequest("admin", "LocalOnly!123"),
            antiforgery.Cookie,
            antiforgery.HeaderName,
            antiforgery.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var sessionCookie = GetCookie(login, "__Host-dotnet-modulith-baseline");
        var authenticatedAntiforgery = await GetAntiforgeryAsync(client, sessionCookie);

        return new AuthenticatedSession(
            $"{sessionCookie}; {authenticatedAntiforgery.Cookie}",
            authenticatedAntiforgery.HeaderName,
            authenticatedAntiforgery.RequestToken);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedCode)
    {
        Xunit.Assert.Equal(expectedStatus, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Xunit.Assert.Equal(expectedCode, json.RootElement.GetProperty("code").GetString());
    }
    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? cookies = null,
        string? machineApiKey = null,
        string? antiforgeryHeaderName = null,
        string? antiforgeryToken = null)
    {
        using var request = new HttpRequestMessage(method, path);

        if (!string.IsNullOrWhiteSpace(cookies))
        {
            request.Headers.Add("Cookie", cookies);
        }

        if (!string.IsNullOrWhiteSpace(machineApiKey))
        {
            request.Headers.Add("X-Machine-Key", machineApiKey);
        }

        if (!string.IsNullOrWhiteSpace(antiforgeryHeaderName) && !string.IsNullOrWhiteSpace(antiforgeryToken))
        {
            request.Headers.Add(antiforgeryHeaderName, antiforgeryToken);
        }

        return await client.SendAsync(request);
    }
    private sealed record AuthenticatedSession(string Cookies, string HeaderName, string RequestToken);
}
