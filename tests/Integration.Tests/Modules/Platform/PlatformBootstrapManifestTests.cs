using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Platform.Application.Bootstrap; // BP-031 dispatch-coverage marker

namespace Integration.Tests.ModuleCoverage.Platform;

public sealed class PlatformBootstrapManifestTests
{
    [Xunit.Fact]
    public async Task PlatformBootstrapManifestIncludesTheScaffoldedModule()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var response = await client.GetAsync("/api/v1/platform/bootstrap");

        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var modules = document.RootElement.GetProperty("modules").EnumerateArray().ToArray();

        Xunit.Assert.Contains(modules, element =>
            string.Equals(element.GetProperty("key").GetString(), "platform", StringComparison.Ordinal)
            && string.Equals(element.GetProperty("displayName").GetString(), "Platform", StringComparison.Ordinal));
    }
}
