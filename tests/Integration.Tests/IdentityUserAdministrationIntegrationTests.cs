using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Application.Authorization;
using Identity.Api.Administration;
using Identity.Api.Authentication;
using Identity.Application.Administration; // BP-031 dispatch-coverage marker
using Microsoft.AspNetCore.TestHost;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests;

public sealed class IdentityUserAdministrationIntegrationTests
{
    private const string SeededAdminPassword = "LocalOnly!123";
    private const string SeededAdminUserName = "admin";
    private const string SeededAdminActorId = "identity:seeded-admin";

    [Xunit.Fact]
    public async Task IdentityUserAdministrationCanListAndCreateUsersAndNewUsersCanSignIn()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAndStepUpAsync(client, SeededAdminUserName, SeededAdminPassword);
        var listBefore = await SendAsync(client, HttpMethod.Get, "/api/v1/identity/users", adminSession.Cookie);
        Xunit.Assert.Equal(HttpStatusCode.OK, listBefore.StatusCode);

        using (var beforeJson = await ReadJsonAsync(listBefore))
        {
            var users = beforeJson.RootElement.GetProperty("users").EnumerateArray().ToArray();
            Xunit.Assert.Single(users);
            Xunit.Assert.Equal(SeededAdminUserName, users[0].GetProperty("userName").GetString());
        }

        var create = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/users",
            new CreateIdentityUserRequest
            {
                UserName = "operator",
                DisplayName = "Baseline Operator",
                Password = "Operator!Passw0rd",
                Roles = [IdentityRoles.User],
                Enabled = true
            },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using (var createJson = await ReadJsonAsync(create))
        {
            Xunit.Assert.Equal("operator", createJson.RootElement.GetProperty("userName").GetString());
            Xunit.Assert.True(createJson.RootElement.GetProperty("enabled").GetBoolean());

            var createRoles = createJson.RootElement.GetProperty("roles").EnumerateArray()
                .Select(static value => value.GetString())
                .ToArray();
            Xunit.Assert.Contains(IdentityRoles.User, createRoles);
        }

        var listAfter = await SendAsync(client, HttpMethod.Get, "/api/v1/identity/users", adminSession.Cookie);
        Xunit.Assert.Equal(HttpStatusCode.OK, listAfter.StatusCode);

        using (var afterJson = await ReadJsonAsync(listAfter))
        {
            var users = afterJson.RootElement.GetProperty("users").EnumerateArray().ToArray();
            Xunit.Assert.Equal(2, users.Length);
            Xunit.Assert.Contains(users, user => string.Equals(user.GetProperty("userName").GetString(), "operator", StringComparison.Ordinal));
        }

        var operatorSession = await SignInAsync(client, "operator", "Operator!Passw0rd");
        var me = await SendAsync(client, HttpMethod.Get, "/api/v1/identity/me", operatorSession.Cookie);
        Xunit.Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        using var meJson = await ReadJsonAsync(me);
        Xunit.Assert.Equal("operator", meJson.RootElement.GetProperty("userName").GetString());
        var roles = meJson.RootElement.GetProperty("roles").EnumerateArray()
            .Select(static value => value.GetString())
            .ToArray();
        Xunit.Assert.Contains(IdentityRoles.User, roles);
    }

    [Xunit.Fact]
    public async Task IdentityUserAdministrationCanDisableUsersAndInvalidateTheirSessions()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAndStepUpAsync(client, SeededAdminUserName, SeededAdminPassword);
        var actorId = await CreateOperatorAsync(client, adminSession, "operator", "Operator!Passw0rd");

        var operatorSession = await SignInAsync(client, "operator", "Operator!Passw0rd");

        var disable = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/identity/users/{actorId}/status",
            new UpdateIdentityUserStatusRequest(Enabled: false),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        var me = await SendAsync(client, HttpMethod.Get, "/api/v1/identity/me", operatorSession.Cookie);
        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        var failedLoginAntiforgery = await GetAntiforgeryAsync(client);
        var failedLogin = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new PasswordSignInRequest("operator", "Operator!Passw0rd"),
            failedLoginAntiforgery.Cookie,
            failedLoginAntiforgery.HeaderName,
            failedLoginAntiforgery.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, failedLogin.StatusCode);

        var enable = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/identity/users/{actorId}/status",
            new UpdateIdentityUserStatusRequest(Enabled: true),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, enable.StatusCode);

        var restoredSession = await SignInAsync(client, "operator", "Operator!Passw0rd");
        var restoredMe = await SendAsync(client, HttpMethod.Get, "/api/v1/identity/me", restoredSession.Cookie);
        Xunit.Assert.Equal(HttpStatusCode.OK, restoredMe.StatusCode);
    }

    [Xunit.Fact]
    public async Task IdentityUserAdministrationCanResetPasswordsAndInvalidateExistingSessions()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAndStepUpAsync(client, SeededAdminUserName, SeededAdminPassword);
        var actorId = await CreateOperatorAsync(client, adminSession, "operator", "Operator!Passw0rd");

        var operatorSession = await SignInAsync(client, "operator", "Operator!Passw0rd");

        var reset = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/identity/users/{actorId}/password",
            new ResetIdentityUserPasswordRequest("Refreshed!Passw0rd"),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var staleMe = await SendAsync(client, HttpMethod.Get, "/api/v1/identity/me", operatorSession.Cookie);
        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, staleMe.StatusCode);

        var oldPasswordAntiforgery = await GetAntiforgeryAsync(client);
        var oldPasswordLogin = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new PasswordSignInRequest("operator", "Operator!Passw0rd"),
            oldPasswordAntiforgery.Cookie,
            oldPasswordAntiforgery.HeaderName,
            oldPasswordAntiforgery.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        var newPasswordSession = await SignInAsync(client, "operator", "Refreshed!Passw0rd");
        var me = await SendAsync(client, HttpMethod.Get, "/api/v1/identity/me", newPasswordSession.Cookie);
        Xunit.Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Xunit.Fact]
    public async Task IdentityUserAdministrationCanRevokeSessionsWithoutChangingPasswords()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAndStepUpAsync(client, SeededAdminUserName, SeededAdminPassword);
        var actorId = await CreateOperatorAsync(client, adminSession, "session-operator", "Operator!Passw0rd");

        var operatorSession = await SignInAsync(client, "session-operator", "Operator!Passw0rd");

        var revoke = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/identity/users/{actorId}/revoke-sessions",
            new { },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        var staleMe = await SendAsync(client, HttpMethod.Get, "/api/v1/identity/me", operatorSession.Cookie);
        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, staleMe.StatusCode);

        var restoredSession = await SignInAsync(client, "session-operator", "Operator!Passw0rd");
        var restoredMe = await SendAsync(client, HttpMethod.Get, "/api/v1/identity/me", restoredSession.Cookie);
        Xunit.Assert.Equal(HttpStatusCode.OK, restoredMe.StatusCode);
    }

    [Xunit.Fact]
    public async Task IdentityUserAdministrationPasswordResetPreservesLeadingAndTrailingWhitespace()
    {
        const string exactPassword = "  Refreshed!Passw0rd  ";

        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAndStepUpAsync(client, SeededAdminUserName, SeededAdminPassword);
        var actorId = await CreateOperatorAsync(client, adminSession, "whitespace-reset-operator", "Operator!Passw0rd");

        var reset = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/identity/users/{actorId}/password",
            new ResetIdentityUserPasswordRequest(exactPassword),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var trimmedPasswordAntiforgery = await GetAntiforgeryAsync(client);
        var trimmedPasswordLogin = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new PasswordSignInRequest("whitespace-reset-operator", exactPassword.Trim()),
            trimmedPasswordAntiforgery.Cookie,
            trimmedPasswordAntiforgery.HeaderName,
            trimmedPasswordAntiforgery.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, trimmedPasswordLogin.StatusCode);

        var exactPasswordSession = await SignInAsync(client, "whitespace-reset-operator", exactPassword);
        var me = await SendAsync(client, HttpMethod.Get, "/api/v1/identity/me", exactPasswordSession.Cookie);
        Xunit.Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Xunit.Fact]
    public async Task IdentitySuccessfulSignInResetsFailedAccessCountBeforeTheLockoutThreshold()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, SeededAdminUserName, SeededAdminPassword);
        await CreateOperatorAsync(client, adminSession, "lockout-operator", "Operator!Passw0rd");

        async Task<HttpResponseMessage> AttemptLoginAsync(string candidatePassword)
        {
            var antiforgery = await GetAntiforgeryAsync(client);
            return await SendJsonAsync(
                client,
                HttpMethod.Post,
                "/api/v1/identity/session/login",
                new PasswordSignInRequest("lockout-operator", candidatePassword),
                antiforgery.Cookie,
                antiforgery.HeaderName,
                antiforgery.RequestToken);
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var invalid = await AttemptLoginAsync("Wrong!Passw0rd");
            Xunit.Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
            using var json = await ReadJsonAsync(invalid);
            Xunit.Assert.Equal("identity.invalid_credentials", json.RootElement.GetProperty("code").GetString());
        }

        var recovered = await AttemptLoginAsync("Operator!Passw0rd");
        Xunit.Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var invalid = await AttemptLoginAsync("Wrong!Passw0rd");
            Xunit.Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
            using var json = await ReadJsonAsync(invalid);
            Xunit.Assert.Equal("identity.invalid_credentials", json.RootElement.GetProperty("code").GetString());
        }
    }

    [Xunit.Fact]
    public async Task IdentityAdminPasswordResetClearsAnExistingLockout()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, SeededAdminUserName, SeededAdminPassword);
        var actorId = await CreateOperatorAsync(client, adminSession, "lockout-operator", "Operator!Passw0rd");

        async Task<HttpResponseMessage> AttemptLoginAsync(string candidatePassword)
        {
            var antiforgery = await GetAntiforgeryAsync(client);
            return await SendJsonAsync(
                client,
                HttpMethod.Post,
                "/api/v1/identity/session/login",
                new PasswordSignInRequest("lockout-operator", candidatePassword),
                antiforgery.Cookie,
                antiforgery.HeaderName,
                antiforgery.RequestToken);
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var invalid = await AttemptLoginAsync("Wrong!Passw0rd");
            Xunit.Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
            using var json = await ReadJsonAsync(invalid);
            Xunit.Assert.Equal("identity.invalid_credentials", json.RootElement.GetProperty("code").GetString());
        }

        var lockedOut = await AttemptLoginAsync("Wrong!Passw0rd");
        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, lockedOut.StatusCode);
        using (var lockedOutJson = await ReadJsonAsync(lockedOut))
        {
            Xunit.Assert.Equal("identity.account_locked", lockedOutJson.RootElement.GetProperty("code").GetString());
        }

        var lockedOutValid = await AttemptLoginAsync("Operator!Passw0rd");
        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, lockedOutValid.StatusCode);
        using (var lockedOutValidJson = await ReadJsonAsync(lockedOutValid))
        {
            Xunit.Assert.Equal("identity.account_locked", lockedOutValidJson.RootElement.GetProperty("code").GetString());
        }

        var reset = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/identity/users/{actorId}/password",
            new ResetIdentityUserPasswordRequest("Refreshed!Passw0rd"),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var restored = await AttemptLoginAsync("Refreshed!Passw0rd");
        Xunit.Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }

    [Xunit.Fact]
    public async Task AdminCanUnlockUserAfterRepeatedFailedLoginAttempts()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAndStepUpAsync(client, SeededAdminUserName, SeededAdminPassword);
        var actorId = await CreateOperatorAsync(client, adminSession, "unlock-operator", "Operator!Passw0rd");

        async Task<HttpResponseMessage> AttemptLoginAsync(string candidatePassword)
        {
            var antiforgery = await GetAntiforgeryAsync(client);
            return await SendJsonAsync(
                client,
                HttpMethod.Post,
                "/api/v1/identity/session/login",
                new PasswordSignInRequest("unlock-operator", candidatePassword),
                antiforgery.Cookie,
                antiforgery.HeaderName,
                antiforgery.RequestToken);
        }

        // Exceed max failed attempts (5) to trigger lockout.
        for (var attempt = 0; attempt < 6; attempt++)
        {
            await AttemptLoginAsync("Wrong!Passw0rd");
        }

        var lockedOutResult = await AttemptLoginAsync("Operator!Passw0rd");
        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, lockedOutResult.StatusCode);
        using (var lockedJson = await ReadJsonAsync(lockedOutResult))
        {
            Xunit.Assert.Equal("identity.account_locked", lockedJson.RootElement.GetProperty("code").GetString());
        }

        var unlock = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/identity/users/{actorId}/unlock",
            new { },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, unlock.StatusCode);

        var restored = await AttemptLoginAsync("Operator!Passw0rd");
        Xunit.Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }

    [Xunit.Fact]
    public async Task AdminCannotRemoveTheirOwnAdminRole()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAndStepUpAsync(client, SeededAdminUserName, SeededAdminPassword);

        var response = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/identity/users/{SeededAdminActorId}/roles",
            new UpdateIdentityUserRolesRequest(new[] { IdentityRoles.User }),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Xunit.Assert.Equal("identity.cannot_remove_own_admin_role", json.RootElement.GetProperty("code").GetString());
    }

    [Xunit.Fact]
    public async Task AdminCanRemoveAdminRoleFromAnotherAdminWithoutSelfDemotion()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAndStepUpAsync(client, SeededAdminUserName, SeededAdminPassword);
        var secondAdminId = await CreateOperatorAsync(
            client,
            adminSession,
            "second-admin",
            "Operator!Passw0rd",
            new[] { IdentityRoles.Admin });

        var response = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/identity/users/{secondAdminId}/roles",
            new UpdateIdentityUserRolesRequest(new[] { IdentityRoles.User }),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        var roles = json.RootElement.GetProperty("roles").EnumerateArray()
            .Select(static value => value.GetString())
            .ToArray();
        Xunit.Assert.Contains(IdentityRoles.User, roles);
        Xunit.Assert.DoesNotContain(IdentityRoles.Admin, roles);
    }

    [Xunit.Fact]
    public async Task IdentityUserLifecycleActionsAreAudited()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAndStepUpAsync(client, SeededAdminUserName, SeededAdminPassword);
        var actorId = await CreateOperatorAsync(client, adminSession, "audited-operator", "Operator!Passw0rd");

        var disable = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/identity/users/{actorId}/status",
            new UpdateIdentityUserStatusRequest(Enabled: false),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        var enable = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/identity/users/{actorId}/status",
            new UpdateIdentityUserStatusRequest(Enabled: true),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.OK, enable.StatusCode);

        var reset = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/identity/users/{actorId}/password",
            new ResetIdentityUserPasswordRequest("Refreshed!Passw0rd"),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var revoke = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/identity/users/{actorId}/revoke-sessions",
            new { },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);
        Xunit.Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        var audit = await SendAsync(client, HttpMethod.Get, "/api/v1/platform/audit-events?limit=20", adminSession.Cookie);
        audit.EnsureSuccessStatusCode();

        using var auditJson = await ReadJsonAsync(audit);
        var events = auditJson.RootElement.GetProperty("events").EnumerateArray().ToArray();

        Xunit.Assert.Contains(events, entry =>
            string.Equals(entry.GetProperty("action").GetString(), "identity.user.create", StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("targetId").GetString(), actorId, StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("outcome").GetString(), "enabled", StringComparison.Ordinal));

        Xunit.Assert.Contains(events, entry =>
            string.Equals(entry.GetProperty("action").GetString(), "identity.user.status.update", StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("targetId").GetString(), actorId, StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("outcome").GetString(), "disabled", StringComparison.Ordinal));

        Xunit.Assert.Contains(events, entry =>
            string.Equals(entry.GetProperty("action").GetString(), "identity.user.status.update", StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("targetId").GetString(), actorId, StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("outcome").GetString(), "enabled", StringComparison.Ordinal));

        Xunit.Assert.Contains(events, entry =>
            string.Equals(entry.GetProperty("action").GetString(), "identity.user.password.reset", StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("targetId").GetString(), actorId, StringComparison.Ordinal));

        Xunit.Assert.Contains(events, entry =>
            string.Equals(entry.GetProperty("action").GetString(), "identity.user.sessions.revoke", StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("targetId").GetString(), actorId, StringComparison.Ordinal)
            && string.Equals(entry.GetProperty("outcome").GetString(), "revoked", StringComparison.Ordinal));
    }

    private static async Task<string> CreateOperatorAsync(
        HttpClient client,
        AuthenticatedSession adminSession,
        string userName,
        string password,
        IReadOnlyCollection<string>? roles = null)
    {
        var create = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/users",
            new CreateIdentityUserRequest
            {
                UserName = userName,
                DisplayName = $"{userName} display",
                Password = password,
                Roles = (roles ?? new[] { IdentityRoles.User }).ToArray(),
                Enabled = true
            },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        create.EnsureSuccessStatusCode();

        using var createJson = await ReadJsonAsync(create);
        return createJson.RootElement.GetProperty("actorId").GetString() ?? throw new InvalidOperationException("Missing actorId in create response.");
    }
    private static async Task<AuthenticatedSession> SignInAndStepUpAsync(HttpClient client, string userName, string password)
    {
        var session = await SignInAsync(client, userName, password);

        var stepUp = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/step-up",
            new StepUpCurrentActorRequest(password),
            session.Cookies,
            session.HeaderName,
            session.RequestToken);

        stepUp.EnsureSuccessStatusCode();
        return session;
    }
}
