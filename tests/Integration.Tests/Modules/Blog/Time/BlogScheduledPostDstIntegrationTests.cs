using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blog.Api;
using Blog.Application.Scheduling;
using BuildingBlocks.Testing.Time;
using Identity.Api.Authentication;
using Microsoft.AspNetCore.TestHost;
using NodaTime;
using NodaTime.TimeZones;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests.Modules.Blog.Time;

public sealed class BlogScheduledPostDstIntegrationTests
{
    [Xunit.Theory]
    [Xunit.MemberData(nameof(DstSchedulingCases))]
    public async Task SchedulingEndpointResolvesDstBoundaryCasesThroughPreferredTimeZoneAsync(
        string caseName,
        LocalDateTime localDateTime,
        string expectedResolution,
        Instant expectedScheduledForUtc)
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client);
        await UpdatePreferredTimeZoneAsync(client, adminSession, DstRegressionCases.EasternTimeZoneId);
        await EnsureTaxonomyAsync(client, adminSession, "time", "Time", ["dst"]);

        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/blog/manage/posts",
            new CreateBlogPostRequest(
                Slug: $"dst-{caseName}",
                Title: $"DST {caseName}",
                Summary: "Validate DST schedule resolution for Blog posts.",
                Body: "DST coverage for blog scheduling.",
                Featured: false,
                CategorySlug: "time",
                TagNames: ["dst"],
                ShareTargets: ["linkedin"],
                SeoMetadata: new BlogSeoMetadataRequest(null, null, null)),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        createResponse.EnsureSuccessStatusCode();

        var postId = string.Empty;
        using (var createJson = await ReadJsonAsync(createResponse))
        {
            postId = createJson.RootElement.GetProperty("postId").GetString() ?? string.Empty;
        }

        var scheduleResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/blog/manage/posts/{postId}/schedule",
            new ScheduleBlogPostLifecycleRequest(
                ExpectedVersion: 1,
                PublishLocalDate: localDateTime.Date.ToString("yyyy-MM-dd", null),
                PublishLocalTime: $"{localDateTime.Hour:D2}:{localDateTime.Minute:D2}",
                UnpublishLocalDate: null,
                UnpublishLocalTime: null),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, scheduleResponse.StatusCode);

        using var scheduleJson = await ReadJsonAsync(scheduleResponse);
        var scheduledPublish = scheduleJson.RootElement.GetProperty("scheduledPublish");

        Xunit.Assert.Equal(DstRegressionCases.EasternTimeZoneId, scheduledPublish.GetProperty("timeZoneId").GetString());
        Xunit.Assert.Equal(expectedResolution, scheduledPublish.GetProperty("localTimeResolution").GetString());
        Xunit.Assert.Equal(expectedScheduledForUtc.ToDateTimeOffset(), scheduledPublish.GetProperty("scheduledForUtc").GetDateTimeOffset());
    }

    public static IEnumerable<object[]> DstSchedulingCases()
    {
        var springForwardGap = new LocalDateTime(2027, 3, 14, 2, 30);
        var fallBackAmbiguous = new LocalDateTime(2027, 11, 7, 1, 30);

        yield return [
            "spring-forward-gap",
            springForwardGap,
            BlogPostLocalTimeResolutions.SkippedForward,
            ResolveSpringForwardGapLater(springForwardGap)];

        yield return [
            "fall-back-ambiguous-earlier",
            fallBackAmbiguous,
            BlogPostLocalTimeResolutions.AmbiguousEarlier,
            ResolveFallBackEarlier(fallBackAmbiguous)];
    }

    private static Instant ResolveSpringForwardGapLater(LocalDateTime localDateTime)
    {
        var zone = DateTimeZoneProviders.Tzdb[DstRegressionCases.EasternTimeZoneId];
        return zone.AtLeniently(localDateTime).ToInstant();
    }

    private static Instant ResolveFallBackEarlier(LocalDateTime localDateTime)
    {
        var zone = DateTimeZoneProviders.Tzdb[DstRegressionCases.EasternTimeZoneId];
        return zone.ResolveLocal(
            localDateTime,
            Resolvers.CreateMappingResolver(Resolvers.ReturnEarlier, Resolvers.ThrowWhenSkipped)).ToInstant();
    }

    private static async Task EnsureTaxonomyAsync(
        HttpClient client,
        AuthenticatedSession adminSession,
        string categorySlug,
        string categoryName,
        IReadOnlyCollection<string> tagSlugs)
    {
        var createCategory = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/blog/manage/categories",
            new CreateBlogCategoryRequest(categorySlug, categoryName, null),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        createCategory.EnsureSuccessStatusCode();

        foreach (var tagSlug in tagSlugs)
        {
            var createTag = await SendJsonAsync(
                client,
                HttpMethod.Post,
                "/api/v1/blog/manage/tags",
                new CreateBlogTagRequest(tagSlug, tagSlug.Replace('-', ' '), null),
                adminSession.Cookies,
                adminSession.HeaderName,
                adminSession.RequestToken);

            createTag.EnsureSuccessStatusCode();
        }
    }

    private static async Task UpdatePreferredTimeZoneAsync(HttpClient client, AuthenticatedSession adminSession, string preferredTimeZoneId)
    {
        var response = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/identity/me/preferences/time-zone",
            new { preferredTimeZoneId },
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        response.EnsureSuccessStatusCode();
    }
}
