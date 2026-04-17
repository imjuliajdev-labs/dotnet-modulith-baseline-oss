using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ApiHost;
using BuildingBlocks.Infrastructure.Authorization;
using BuildingBlocks.Infrastructure.Persistence;
using Identity.Api.Authentication;
using Identity.Application.Authentication;
using Identity.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests;

public sealed class IdentityBrowserCookieCompatibilityIntegrationTests
{
    [Xunit.Fact]
    public async Task DefaultComposedRuntimePersistsDataProtectionKeysAndSharesCookiesAcrossHosts()
    {
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync("baseline-cookie-shared");
        var connectionString = postgres.GetConnectionString();

        await using var firstApplication = await PostgresBackedApiApplication.StartAsync(connectionString, applyMigrations: true);
        var firstClient = firstApplication.App.GetTestClient();
        firstClient.BaseAddress = new Uri("https://localhost");

        var antiforgery = await GetAntiforgeryAsync(firstClient);
        var loginResponse = await SendJsonAsync(
            firstClient,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new PasswordSignInRequest("admin", "LocalOnly!123"),
            antiforgery.Cookie,
            antiforgery.HeaderName,
            antiforgery.RequestToken);

        loginResponse.EnsureSuccessStatusCode();
        var authCookie = GetCookie(loginResponse, "__Host-dotnet-modulith-baseline");

        var persistedKeyCount = await GetPersistedDataProtectionKeyCountAsync(connectionString);
        Xunit.Assert.True(persistedKeyCount > 0, "Expected the shared runtime data protection key store to contain at least one key after issuing an auth cookie.");

        await using var secondApplication = await PostgresBackedApiApplication.StartAsync(connectionString, applyMigrations: false);
        var secondClient = secondApplication.App.GetTestClient();
        secondClient.BaseAddress = new Uri("https://localhost");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/me");
        request.Headers.Add("Cookie", authCookie);
        var meResponse = await secondClient.SendAsync(request);

        Xunit.Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        using var json = await ReadJsonAsync(meResponse);
        Xunit.Assert.Equal("admin", json.RootElement.GetProperty("userName").GetString());
        Xunit.Assert.Equal("Baseline Admin", json.RootElement.GetProperty("displayName").GetString());
    }

    [Xunit.Fact]
    public async Task ExplicitSessionRevocationInvalidatesSharedCookiesAcrossHosts()
    {
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync("baseline-cookie-revocation");
        var connectionString = postgres.GetConnectionString();

        await using var firstApplication = await PostgresBackedApiApplication.StartAsync(connectionString, applyMigrations: true);
        await using var secondApplication = await PostgresBackedApiApplication.StartAsync(connectionString, applyMigrations: false);

        var firstClient = firstApplication.App.GetTestClient();
        firstClient.BaseAddress = new Uri("https://localhost");

        var secondClient = secondApplication.App.GetTestClient();
        secondClient.BaseAddress = new Uri("https://localhost");

        var antiforgery = await GetAntiforgeryAsync(firstClient);
        var loginResponse = await SendJsonAsync(
            firstClient,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new PasswordSignInRequest("admin", "LocalOnly!123"),
            antiforgery.Cookie,
            antiforgery.HeaderName,
            antiforgery.RequestToken);

        loginResponse.EnsureSuccessStatusCode();
        var authCookie = GetCookie(loginResponse, "__Host-dotnet-modulith-baseline");

        var meBefore = await SendAsync(secondClient, HttpMethod.Get, "/api/v1/identity/me", authCookie);
        Xunit.Assert.Equal(HttpStatusCode.OK, meBefore.StatusCode);

        var authenticatedAntiforgery = await GetAntiforgeryAsync(firstClient, authCookie);
        var revokeResponse = await SendJsonAsync(
            firstClient,
            HttpMethod.Post,
            "/api/v1/identity/users/identity:seeded-admin/revoke-sessions",
            new { },
            $"{authCookie}; {authenticatedAntiforgery.Cookie}",
            authenticatedAntiforgery.HeaderName,
            authenticatedAntiforgery.RequestToken);
        revokeResponse.EnsureSuccessStatusCode();

        var meAfter = await SendAsync(secondClient, HttpMethod.Get, "/api/v1/identity/me", authCookie);
        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, meAfter.StatusCode);

        using var json = await ReadJsonAsync(meAfter);
        Xunit.Assert.Equal("auth.unauthorized", json.RootElement.GetProperty("code").GetString());
    }

    [Xunit.Fact]
    public async Task CurrentHostAcceptsLegacyBrowserCookiesAndRenewsThem()
    {
        var keyDirectory = Path.Combine(Path.GetTempPath(), $"baseline-cookie-compat-{Guid.NewGuid():n}");
        Directory.CreateDirectory(keyDirectory);

        try
        {
            await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync("baseline-cookie-compat");
            await using var application = await StartCurrentHostWithFileSystemKeysAsync(keyDirectory, postgres.GetConnectionString());

            IdentityActorSession session;
            using (var scope = application.Services.CreateScope())
            {
                var reader = scope.ServiceProvider.GetRequiredService<IIdentityCurrentActorReader>();
                var result = await reader.GetCurrentAsync("identity:seeded-admin", CancellationToken.None);
                Xunit.Assert.True(result.IsSuccess);
                session = result.Value!;
            }

            await using var legacyHost = await StartLegacyCookieHostAsync(keyDirectory, session);
            var legacyClient = legacyHost.GetTestClient();
            legacyClient.BaseAddress = new Uri("https://localhost");

            var legacyLogin = await legacyClient.PostAsync("/legacy-login", content: null);
            legacyLogin.EnsureSuccessStatusCode();
            var legacyCookie = GetCookie(legacyLogin, "__Host-dotnet-modulith-baseline");

            var client = application.GetTestClient();
            client.BaseAddress = new Uri("https://localhost");

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/me");
            request.Headers.Add("Cookie", legacyCookie);
            var meResponse = await client.SendAsync(request);

            Xunit.Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
            Xunit.Assert.Contains(
                meResponse.Headers.GetValues("Set-Cookie"),
                header => header.StartsWith("__Host-dotnet-modulith-baseline", StringComparison.OrdinalIgnoreCase));

            using var json = await ReadJsonAsync(meResponse);
            Xunit.Assert.Equal("admin", json.RootElement.GetProperty("userName").GetString());
            Xunit.Assert.Equal("Baseline Admin", json.RootElement.GetProperty("displayName").GetString());
        }
        finally
        {
            if (Directory.Exists(keyDirectory))
            {
                Directory.Delete(keyDirectory, recursive: true);
            }
        }
    }

    private static async Task<WebApplication> StartCurrentHostWithFileSystemKeysAsync(string keyDirectory, string connectionString)
    {
        var configuration = PostgresBackedApiApplication.CreateConfiguration(connectionString);
        await PostgresBackedApiApplication.RunDbMigratorAsync(configuration);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });

        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(configuration);
        builder.AddBaselineApiHostServices();
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
            .SetApplicationName("baseline-cookie-compat");

        var app = builder.Build();
        app.MapBaselineApiHost();
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartLegacyCookieHostAsync(string keyDirectory, IdentityActorSession session)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });

        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
            .SetApplicationName("baseline-cookie-compat");
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.Name = "__Host-dotnet-modulith-baseline";
                options.Cookie.Path = "/";
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
            });

        var app = builder.Build();
        app.UseAuthentication();
        app.MapPost(
            "/legacy-login",
            async (HttpContext httpContext) =>
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, session.ActorId),
                    new(ClaimTypes.Name, session.UserName),
                    new(IdentityAuthenticationClaimTypes.DisplayName, session.DisplayName),
                    new(
                        HttpContextCurrentActorAccessor.AuthenticationInstantClaimType,
                        NodaTime.SystemClock.Instance.GetCurrentInstant().ToUnixTimeTicks().ToString(CultureInfo.InvariantCulture))
                };

                claims.Add(new Claim(ClaimTypes.Role, BuildingBlocks.Application.Authorization.IdentityRoles.Admin));

                var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
                var issuedUtc = NodaTime.SystemClock.Instance.GetCurrentInstant().ToDateTimeOffset();
                await httpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        AllowRefresh = true,
                        IsPersistent = true,
                        IssuedUtc = issuedUtc,
                        ExpiresUtc = issuedUtc.AddHours(8)
                    });

                return Results.Ok();
            });

        await app.StartAsync();
        return app;
    }

    private static async Task<int> GetPersistedDataProtectionKeyCountAsync(string connectionString)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM starter_runtime.building_blocks_data_protection_keys;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
