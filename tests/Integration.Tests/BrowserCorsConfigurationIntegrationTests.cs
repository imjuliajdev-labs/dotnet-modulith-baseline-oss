using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ApiHost;

namespace Integration.Tests;

public sealed class BrowserCorsConfigurationIntegrationTests
{
    private const string AllowedOrigin = "https://localhost:3000";
    private const string DisallowedOrigin = "https://evil.example";

    [Xunit.Fact]
    public async Task SameOriginDefaultDoesNotEmitCorsHeaders()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var response = await SendCorsRequestAsync(client, AllowedOrigin);

        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Xunit.Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Xunit.Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Xunit.Fact]
    public async Task ConfiguredCorsPolicyAllowsOnlyNamedOrigins()
    {
        var configurationOverrides = new Dictionary<string, string?>
        {
            ["Frontend:AllowedOrigins:0"] = AllowedOrigin
        };

        await using var application = await PostgresBackedApiApplication.StartAsync(configurationOverrides: configurationOverrides);

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var allowedResponse = await SendCorsRequestAsync(client, AllowedOrigin);
        Xunit.Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        AssertHeaderEquals(allowedResponse, "Access-Control-Allow-Origin", AllowedOrigin);
        AssertHeaderEquals(allowedResponse, "Access-Control-Allow-Credentials", "true");

        var disallowedResponse = await SendCorsRequestAsync(client, DisallowedOrigin);
        Xunit.Assert.Equal(HttpStatusCode.OK, disallowedResponse.StatusCode);
        Xunit.Assert.False(disallowedResponse.Headers.Contains("Access-Control-Allow-Origin"));
        Xunit.Assert.False(disallowedResponse.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Xunit.Fact]
    public async Task DevelopmentProfileAllowsOnlyTheCheckedInViteOrigin()
    {
        var developmentSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json");
        Xunit.Assert.True(File.Exists(developmentSettingsPath), "Expected appsettings.Development.json to be present in the integration test output.");

        await using var application = await PostgresBackedApiApplication.StartAsync(
            configureBuilder: builder => builder.Configuration.AddJsonFile(developmentSettingsPath, optional: false, reloadOnChange: false));

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var allowedResponse = await SendCorsRequestAsync(client, AllowedOrigin);
        Xunit.Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        AssertHeaderEquals(allowedResponse, "Access-Control-Allow-Origin", AllowedOrigin);
        AssertHeaderEquals(allowedResponse, "Access-Control-Allow-Credentials", "true");

        var disallowedResponse = await SendCorsRequestAsync(client, DisallowedOrigin);
        Xunit.Assert.Equal(HttpStatusCode.OK, disallowedResponse.StatusCode);
        Xunit.Assert.False(disallowedResponse.Headers.Contains("Access-Control-Allow-Origin"));
        Xunit.Assert.False(disallowedResponse.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    private static void AssertHeaderEquals(HttpResponseMessage response, string headerName, string expectedValue)
    {
        Xunit.Assert.True(response.Headers.TryGetValues(headerName, out var values), $"Expected header '{headerName}' to be present.");
        Xunit.Assert.Equal(expectedValue, Xunit.Assert.Single(values));
    }

    [Xunit.Fact]
    public async Task PreflightRejectsMethodsOutsideTheAllowList()
    {
        // The W3C CORS spec and the ASP.NET Core CORS middleware treat origin and method as
        // independent checks. When the Origin is on the allow-list but the requested method is
        // not, the middleware still echoes `Access-Control-Allow-Origin` — what it omits is the
        // `Access-Control-Allow-Methods` header, and the browser then refuses the real request
        // client-side. We assert on that exact shape: allowed preflights advertise the allowed
        // methods, and forbidden preflights never name the forbidden method in Allow-Methods.
        var configurationOverrides = new Dictionary<string, string?>
        {
            ["Frontend:AllowedOrigins:0"] = AllowedOrigin
        };

        await using var application = await PostgresBackedApiApplication.StartAsync(configurationOverrides: configurationOverrides);

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var allowedPreflight = await SendPreflightAsync(client, AllowedOrigin, requestedMethod: "POST", requestedHeaders: "content-type,x-csrf-token");
        AssertHeaderEquals(allowedPreflight, "Access-Control-Allow-Origin", AllowedOrigin);
        AssertHeaderEquals(allowedPreflight, "Access-Control-Allow-Credentials", "true");
        Xunit.Assert.Contains("POST", ReadHeaderAsJoinedString(allowedPreflight, "Access-Control-Allow-Methods"), StringComparison.OrdinalIgnoreCase);

        var forbiddenMethodPreflight = await SendPreflightAsync(client, AllowedOrigin, requestedMethod: "TRACE", requestedHeaders: null);
        var forbiddenAllowMethods = ReadHeaderAsJoinedString(forbiddenMethodPreflight, "Access-Control-Allow-Methods");
        Xunit.Assert.DoesNotContain("TRACE", forbiddenAllowMethods, StringComparison.OrdinalIgnoreCase);
    }

    [Xunit.Fact]
    public async Task PreflightRejectsHeadersOutsideTheAllowList()
    {
        // Same independence as the method check: with an allowed origin but a disallowed
        // requested header, the middleware echoes `Access-Control-Allow-Origin` and simply
        // never names the forbidden header in `Access-Control-Allow-Headers`.
        var configurationOverrides = new Dictionary<string, string?>
        {
            ["Frontend:AllowedOrigins:0"] = AllowedOrigin
        };

        await using var application = await PostgresBackedApiApplication.StartAsync(configurationOverrides: configurationOverrides);

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var allowedHeaderPreflight = await SendPreflightAsync(client, AllowedOrigin, requestedMethod: "POST", requestedHeaders: "content-type,x-csrf-token");
        var allowedHeaders = ReadHeaderAsJoinedString(allowedHeaderPreflight, "Access-Control-Allow-Headers");
        Xunit.Assert.Contains("X-CSRF-TOKEN", allowedHeaders, StringComparison.OrdinalIgnoreCase);

        var forbiddenHeaderPreflight = await SendPreflightAsync(client, AllowedOrigin, requestedMethod: "POST", requestedHeaders: "x-forwarded-for");
        var forbiddenAllowHeaders = ReadHeaderAsJoinedString(forbiddenHeaderPreflight, "Access-Control-Allow-Headers");
        Xunit.Assert.DoesNotContain("x-forwarded-for", forbiddenAllowHeaders, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadHeaderAsJoinedString(HttpResponseMessage response, string headerName)
    {
        return response.Headers.TryGetValues(headerName, out var values) ? string.Join(",", values) : string.Empty;
    }

    [Xunit.Fact]
    public async Task ConfiguredPolicyExposesExplicitAllowListsWithoutWildcards()
    {
        var configurationOverrides = new Dictionary<string, string?>
        {
            ["Frontend:AllowedOrigins:0"] = AllowedOrigin
        };

        await using var application = await PostgresBackedApiApplication.StartAsync(configurationOverrides: configurationOverrides);

        var options = application.App.Services.GetRequiredService<IOptions<FrontendCorsOptions>>().Value;

        Xunit.Assert.Contains("GET", options.AllowedMethods);
        Xunit.Assert.DoesNotContain("*", options.AllowedMethods);
        Xunit.Assert.Contains("X-CSRF-TOKEN", options.AllowedHeaders);
        Xunit.Assert.DoesNotContain("*", options.AllowedHeaders);
        Xunit.Assert.All(options.AllowedOrigins, static origin => Xunit.Assert.DoesNotContain("*", origin));
    }

    [Xunit.Fact]
    public async Task BootFailsWhenAllowedOriginsContainWildcard()
    {
        var configurationOverrides = new Dictionary<string, string?>
        {
            ["Frontend:AllowedOrigins:0"] = "https://*.evil.example"
        };

        await Xunit.Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var application = await PostgresBackedApiApplication.StartAsync(configurationOverrides: configurationOverrides);
        });
    }

    private static Task<HttpResponseMessage> SendCorsRequestAsync(HttpClient client, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/bootstrap");
        request.Headers.Add("Origin", origin);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendPreflightAsync(HttpClient client, string origin, string requestedMethod, string? requestedHeaders)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/platform/bootstrap");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", requestedMethod);
        if (!string.IsNullOrWhiteSpace(requestedHeaders))
        {
            request.Headers.Add("Access-Control-Request-Headers", requestedHeaders);
        }

        return client.SendAsync(request);
    }
}
