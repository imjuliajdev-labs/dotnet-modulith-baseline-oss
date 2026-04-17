using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Application.Authorization;
using Identity.Api.Administration;
using Identity.Api.Authentication;
using Identity.Application.Authentication; // BP-031 dispatch-coverage marker
using Microsoft.AspNetCore.TestHost;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests;

public sealed class IdentityPasswordChangeIntegrationTests
{
    private const string SeededAdminUserName = "admin";
    private const string SeededAdminPassword = "LocalOnly!123";
    private const string NewAdminPassword = "NewStrongP@ssw0rd1!";

    [Xunit.Fact]
    public async Task AdminCanChangeTheirOwnPasswordAndSignInWithTheNewCredentials()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAndStepUpAsync(client, SeededAdminUserName, SeededAdminPassword);

        var changeResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/password",
            new ChangeCurrentActorPasswordRequest(SeededAdminPassword, NewAdminPassword),
            session.Cookies,
            session.HeaderName,
            session.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);
        using (var changeJson = await ReadJsonAsync(changeResponse))
        {
            Xunit.Assert.Equal(SeededAdminUserName, changeJson.RootElement.GetProperty("userName").GetString());
        }

        // Sign out by abandoning the cookie; the old password should be rejected.
        var oldPasswordAntiforgery = await GetAntiforgeryAsync(client);
        var oldPasswordLogin = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new PasswordSignInRequest(SeededAdminUserName, SeededAdminPassword),
            oldPasswordAntiforgery.Cookie,
            oldPasswordAntiforgery.HeaderName,
            oldPasswordAntiforgery.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        var newPasswordAntiforgery = await GetAntiforgeryAsync(client);
        var newPasswordLogin = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new PasswordSignInRequest(SeededAdminUserName, NewAdminPassword),
            newPasswordAntiforgery.Cookie,
            newPasswordAntiforgery.HeaderName,
            newPasswordAntiforgery.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, newPasswordLogin.StatusCode);
    }

    [Xunit.Fact]
    public async Task PasswordChangeCanSetALeadingAndTrailingWhitespaceSensitivePassword()
    {
        const string exactPassword = "  NewStrongP@ssw0rd1!  ";

        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAndStepUpAsync(client, SeededAdminUserName, SeededAdminPassword);

        var changeResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/password",
            new ChangeCurrentActorPasswordRequest(SeededAdminPassword, exactPassword),
            session.Cookies,
            session.HeaderName,
            session.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);

        var oldPasswordAntiforgery = await GetAntiforgeryAsync(client);
        var oldPasswordLogin = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new PasswordSignInRequest(SeededAdminUserName, SeededAdminPassword),
            oldPasswordAntiforgery.Cookie,
            oldPasswordAntiforgery.HeaderName,
            oldPasswordAntiforgery.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        var trimmedPasswordAntiforgery = await GetAntiforgeryAsync(client);
        var trimmedPasswordLogin = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new PasswordSignInRequest(SeededAdminUserName, exactPassword.Trim()),
            trimmedPasswordAntiforgery.Cookie,
            trimmedPasswordAntiforgery.HeaderName,
            trimmedPasswordAntiforgery.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, trimmedPasswordLogin.StatusCode);

        var exactPasswordAntiforgery = await GetAntiforgeryAsync(client);
        var exactPasswordLogin = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new PasswordSignInRequest(SeededAdminUserName, exactPassword),
            exactPasswordAntiforgery.Cookie,
            exactPasswordAntiforgery.HeaderName,
            exactPasswordAntiforgery.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, exactPasswordLogin.StatusCode);
    }

    [Xunit.Fact]
    public async Task PasswordChangeRejectsWrongCurrentPassword()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAndStepUpAsync(client, SeededAdminUserName, SeededAdminPassword);

        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/password",
            new ChangeCurrentActorPasswordRequest("Not-the-right-one-1!", NewAdminPassword),
            session.Cookies,
            session.HeaderName,
            session.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        Xunit.Assert.Equal("identity.current_password_invalid", json.RootElement.GetProperty("code").GetString());
    }

    [Xunit.Fact]
    public async Task PasswordChangeRejectsWeakNewPassword()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var session = await SignInAndStepUpAsync(client, SeededAdminUserName, SeededAdminPassword);

        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/password",
            new ChangeCurrentActorPasswordRequest(SeededAdminPassword, "short"),
            session.Cookies,
            session.HeaderName,
            session.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        var code = json.RootElement.GetProperty("code").GetString();
        Xunit.Assert.True(
            code == "identity.password_policy_not_met" || code == "identity.password_too_short",
            $"Unexpected error code '{code}' for weak-password rejection.");
    }

    [Xunit.Fact]
    public async Task PasswordChangeEndpointIsRateLimitedPerAuthenticatedUser()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAndStepUpAsync(client, SeededAdminUserName, SeededAdminPassword);
        await CreateOperatorAsync(client, adminSession, "password-rate-limit-operator", "Operator!Passw0rd");

        var operatorSession = await SignInAndStepUpAsync(client, "password-rate-limit-operator", "Operator!Passw0rd");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var invalid = await SendJsonAsync(
                client,
                HttpMethod.Post,
                "/api/v1/identity/session/password",
                new ChangeCurrentActorPasswordRequest("Wrong!Passw0rd", NewAdminPassword),
                adminSession.Cookies,
                adminSession.HeaderName,
                adminSession.RequestToken);

            Xunit.Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
            using var json = await ReadJsonAsync(invalid);
            Xunit.Assert.Equal("identity.current_password_invalid", json.RootElement.GetProperty("code").GetString());
        }

        var rateLimited = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/password",
            new ChangeCurrentActorPasswordRequest("Wrong!Passw0rd", NewAdminPassword),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.TooManyRequests, rateLimited.StatusCode);

        var otherUserAttempt = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/password",
            new ChangeCurrentActorPasswordRequest("Wrong!Passw0rd", "AnotherStrongP@ssw0rd1!"),
            operatorSession.Cookies,
            operatorSession.HeaderName,
            operatorSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, otherUserAttempt.StatusCode);
        using var otherUserJson = await ReadJsonAsync(otherUserAttempt);
        Xunit.Assert.Equal("identity.current_password_invalid", otherUserJson.RootElement.GetProperty("code").GetString());
    }

    // NOTE: A recent-auth expiry companion test is intentionally omitted.
    // The ChangeCurrentActorPasswordCommand uses IRequireRecentAuthentication with a
    // wall-clock window enforced by the RecentAuthenticationAuthorizer. Faking the
    // IClock to advance past the window requires reconstructing the host with a custom
    // clock, signing in (which captures AuthenticatedAt from that same clock), then
    // advancing the clock. The existing IdentityRecentAuthenticationIntegrationTests
    // already exercises this for other admin commands via the same pipeline, so we rely
    // on that coverage here rather than duplicating the harness.

    private static async Task CreateOperatorAsync(HttpClient client, AuthenticatedSession adminSession, string userName, string password)
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
                Roles = [IdentityRoles.User],
                Enabled = true
            },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        create.EnsureSuccessStatusCode();
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

        // The step-up refreshes the auth cookie. Re-read it so subsequent calls carry the new cookie.
        var refreshedAuthCookie = GetCookie(stepUp, "__Host-dotnet-modulith-baseline");
        var refreshedAntiforgery = await GetAntiforgeryAsync(client, refreshedAuthCookie);
        return new AuthenticatedSession(
            refreshedAntiforgery.HeaderName,
            refreshedAntiforgery.RequestToken,
            refreshedAuthCookie,
            CombineCookies(refreshedAuthCookie, refreshedAntiforgery.Cookie));
    }
}
