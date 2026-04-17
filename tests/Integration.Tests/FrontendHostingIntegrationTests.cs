using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Integration.Tests;

public sealed class FrontendHostingIntegrationTests
{
    [Fact]
    public async Task BuiltFrontendAssetsAreServedFromRootAndClientRoutes()
    {
        var webRoot = Directory.CreateTempSubdirectory("dotnet-modulith-baseline-webroot-");

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(webRoot.FullName, "index.html"),
                "<!doctype html><html><body><div id=\"root\">baseline frontend shell</div></body></html>");

            await using var application = await PostgresBackedApiApplication.StartAsync(webRootPath: webRoot.FullName);

            var client = application.App.GetTestClient();
            client.BaseAddress = new Uri("https://localhost");

            var rootRequest = new HttpRequestMessage(HttpMethod.Get, "/");
            rootRequest.Headers.Accept.ParseAdd("text/html");
            var rootResponse = await client.SendAsync(rootRequest);
            Assert.Equal(HttpStatusCode.OK, rootResponse.StatusCode);
            Assert.Equal("text/html", rootResponse.Content.Headers.ContentType?.MediaType);
            Assert.Contains("baseline frontend shell", await rootResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var bootstrapResponse = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, bootstrapResponse.StatusCode);
            Assert.Equal("application/json; charset=utf-8", bootstrapResponse.Content.Headers.ContentType?.ToString());
            Assert.Contains("bootstrap", await bootstrapResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var featureRouteResponse = await client.GetAsync("/platform");
            Assert.Equal(HttpStatusCode.OK, featureRouteResponse.StatusCode);
            Assert.Equal("text/html", featureRouteResponse.Content.Headers.ContentType?.MediaType);
            Assert.Contains("baseline frontend shell", await featureRouteResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var apiResponse = await client.GetAsync("/api/v1/platform/bootstrap");
            Assert.Equal(HttpStatusCode.OK, apiResponse.StatusCode);
            Assert.Equal("application/json; charset=utf-8", apiResponse.Content.Headers.ContentType?.ToString());
        }
        finally
        {
            webRoot.Delete(recursive: true);
        }
    }
}
