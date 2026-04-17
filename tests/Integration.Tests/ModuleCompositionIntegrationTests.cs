using System.Net;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;
using ApiHost;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure;
using BuildingBlocks.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StarterOpenApiServiceCollectionExtensions = BuildingBlocks.Infrastructure.OpenApi.OpenApiServiceCollectionExtensions;

namespace Integration.Tests;

public sealed class ModuleCompositionIntegrationTests
{
    [Xunit.Fact]
    public async Task PlatformBootstrapEndpointReturnsTheModuleManifest()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var response = await client.GetAsync("/api/v1/platform/bootstrap");
        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Xunit.Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());

        using var json = await ReadJsonAsync(response);
        var modules = json.RootElement.GetProperty("modules");
        var registeredModules = application.App.Services.GetServices<IApiModule>().ToArray();

        Xunit.Assert.Equal(registeredModules.Length, modules.GetArrayLength());
        Xunit.Assert.Contains(modules.EnumerateArray(), element =>
            string.Equals(element.GetProperty("key").GetString(), "platform", StringComparison.Ordinal) &&
            string.Equals(element.GetProperty("moduleNamespace").GetString(), "platform", StringComparison.Ordinal));

        foreach (var element in modules.EnumerateArray())
        {
            var key = element.GetProperty("key").GetString();

            Xunit.Assert.False(
                element.TryGetProperty("permissionNamespace", out _),
                $"Module '{key}' must not expose the legacy permissionNamespace field.");
            Xunit.Assert.True(
                element.TryGetProperty("moduleNamespace", out var moduleNamespaceElement),
                $"Module '{key}' must expose the moduleNamespace field.");

            Xunit.Assert.False(
                string.IsNullOrWhiteSpace(moduleNamespaceElement.GetString()),
                $"Module '{key}' must provide a non-empty moduleNamespace value.");
        }
    }

    [Xunit.Fact]
    public async Task OpenApiDocumentIncludesBrowserFacingModuleEndpointsAndProblemResponses()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var response = await client.GetAsync("/openapi/v1.json");

        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Xunit.Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());

        using var json = await ReadJsonAsync(response);
        Xunit.Assert.Equal("v1", json.RootElement.GetProperty(StarterOpenApiServiceCollectionExtensions.VersionSetExtensionName).GetString());
        var paths = json.RootElement.GetProperty("paths");

        Xunit.Assert.False(paths.TryGetProperty(ApiHostComposition.HostStatusPath, out _));

        var bootstrapGet = paths.GetProperty("/api/v1/platform/bootstrap").GetProperty("get");
        Xunit.Assert.Equal(
            "Get the module bootstrap manifest for the frontend shell.",
            bootstrapGet.GetProperty("summary").GetString());
        Xunit.Assert.Equal("v1", bootstrapGet.GetProperty(StarterOpenApiServiceCollectionExtensions.VersionSetExtensionName).GetString());
        Xunit.Assert.True(
            bootstrapGet.GetProperty("responses")
                .GetProperty("400")
                .GetProperty("content")
                .TryGetProperty("application/problem+json", out _));

        var loginPost = paths.GetProperty("/api/v1/identity/session/login").GetProperty("post");
        Xunit.Assert.Equal(
            "Authenticate a browser session with username and password.",
            loginPost.GetProperty("summary").GetString());
        Xunit.Assert.Equal("v1", loginPost.GetProperty(StarterOpenApiServiceCollectionExtensions.VersionSetExtensionName).GetString());
        Xunit.Assert.True(
            loginPost.GetProperty("responses")
                .GetProperty("401")
                .GetProperty("content")
                .TryGetProperty("application/problem+json", out _));
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}
