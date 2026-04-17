using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blog.Api;
using Blog.Application.Posts; // BP-031 dispatch-coverage marker
using Blog.Application.Scheduling;
using Blog.Application.Taxonomy; // BP-031 dispatch-coverage marker
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Testing.Time;
using Identity.Api.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Npgsql;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests.ModuleCoverage.Blog.Posts;

public sealed class BlogPostsIntegrationTests
{
    [Xunit.Fact]
    public async Task BlogPostsAcceptMaximumSlugAndTitleLengths()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        await EnsureTaxonomyAsync(client, adminSession, "max-length", "Max Length", ["migrations"]);

        var slug = new string('s', 200);
        var title = new string('T', 256);

        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/blog/manage/posts",
            new CreateBlogPostRequest(
                Slug: slug,
                Title: title,
                Summary: "Maximum slug and title length coverage for the canonical blog columns.",
                Body: "The canonical snake_case columns must accept the documented upper bounds for slug and title.",
                Featured: false,
                CategorySlug: "max-length",
                TagNames: ["migrations"],
                ShareTargets: ["linkedin"],
                SeoMetadata: new BlogSeoMetadataRequest(null, null, null)),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        using var createJson = await ReadJsonAsync(createResponse);
        Xunit.Assert.Equal(slug, createJson.RootElement.GetProperty("slug").GetString());
        Xunit.Assert.Equal(title, createJson.RootElement.GetProperty("title").GetString());
    }

    [Xunit.Fact]
    public async Task OperatorsCanCreatePublishAndExposeBlogPostsOnThePublicSurface()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        await EnsureTaxonomyAsync(client, adminSession, "platform-strategy", "Platform Strategy", ["architecture", "publishing"]);

        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/blog/manage/posts",
            new CreateBlogPostRequest(
                Slug: null,
                Title: "First governed blog post",
                Summary: "A first real post in the governed Blog module.",
                Body: "This post proves the Blog module can create drafts, publish them, and expose rich metadata.",
                Featured: true,
                CategorySlug: "platform-strategy",
                TagNames: ["architecture", "publishing"],
                ShareTargets: ["LinkedIn", "Email Newsletter"],
                SeoMetadata: new BlogSeoMetadataRequest(
                    Title: "Governed blog SEO title",
                    Description: "Governed blog SEO description",
                    Keywords: "governed, blog")),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        string postId;
        using (var createJson = await ReadJsonAsync(createResponse))
        {
            postId = createJson.RootElement.GetProperty("postId").GetString() ?? string.Empty;
            Xunit.Assert.Equal("first-governed-blog-post", createJson.RootElement.GetProperty("slug").GetString());
            Xunit.Assert.Equal("draft", createJson.RootElement.GetProperty("status").GetString());
            Xunit.Assert.Equal(1, createJson.RootElement.GetProperty("version").GetInt32());
            Xunit.Assert.Equal(1, createJson.RootElement.GetProperty("revisionNumber").GetInt32());
            Xunit.Assert.Equal(0, createJson.RootElement.GetProperty("viewCount").GetInt64());
            Xunit.Assert.Equal("platform-strategy", createJson.RootElement.GetProperty("categorySlug").GetString());
        }

        var publicBeforePublish = await client.GetAsync("/api/v1/blog/posts");
        Xunit.Assert.Equal(HttpStatusCode.OK, publicBeforePublish.StatusCode);

        using (var publicBeforeJson = await ReadJsonAsync(publicBeforePublish))
        {
            var posts = publicBeforeJson.RootElement.GetProperty("posts").EnumerateArray().ToArray();
            Xunit.Assert.Empty(posts);
        }

        var publishResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/blog/manage/posts/{postId}/publish",
            new PublishBlogPostRequest(ExpectedVersion: 1),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken,
            idempotencyKey: "blog-publish-request-1");

        Xunit.Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

        using (var publishJson = await ReadJsonAsync(publishResponse))
        {
            Xunit.Assert.Equal("published", publishJson.RootElement.GetProperty("status").GetString());
            Xunit.Assert.Equal(2, publishJson.RootElement.GetProperty("version").GetInt32());
            Xunit.Assert.NotNull(publishJson.RootElement.GetProperty("publishedUtc").GetString());
            Xunit.Assert.Equal(1, publishJson.RootElement.GetProperty("revisionNumber").GetInt32());
        }

        var publicAfterPublish = await client.GetAsync("/api/v1/blog/posts");
        Xunit.Assert.Equal(HttpStatusCode.OK, publicAfterPublish.StatusCode);

        using (var publicAfterJson = await ReadJsonAsync(publicAfterPublish))
        {
            var posts = publicAfterJson.RootElement.GetProperty("posts").EnumerateArray().ToArray();
            Xunit.Assert.Contains(posts, post => string.Equals(post.GetProperty("postId").GetString(), postId, StringComparison.Ordinal));
        }

        var detailResponse = await client.GetAsync("/api/v1/blog/posts/first-governed-blog-post");
        Xunit.Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        using (var detailJson = await ReadJsonAsync(detailResponse))
        {
            Xunit.Assert.Equal("First governed blog post", detailJson.RootElement.GetProperty("title").GetString());
            Xunit.Assert.Equal("published", detailJson.RootElement.GetProperty("status").GetString());
            Xunit.Assert.Equal(0, detailJson.RootElement.GetProperty("viewCount").GetInt64());
            Xunit.Assert.Equal("platform-strategy", detailJson.RootElement.GetProperty("categorySlug").GetString());
        }

        var publicAntiforgery = await GetAntiforgeryAsync(client);
        var registerViewResponse = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/blog/posts/first-governed-blog-post/views",
            publicAntiforgery.Cookie,
            publicAntiforgery.HeaderName,
            publicAntiforgery.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.NoContent, registerViewResponse.StatusCode);

        var secondDetailResponse = await client.GetAsync("/api/v1/blog/posts/first-governed-blog-post");
        secondDetailResponse.EnsureSuccessStatusCode();

        using var secondDetailJson = await ReadJsonAsync(secondDetailResponse);
        Xunit.Assert.Equal(1, secondDetailJson.RootElement.GetProperty("viewCount").GetInt64());
    }

    [Xunit.Fact]
    public async Task PublishEndpointReplaysTheOriginalResponseForTheSameIdempotencyKey()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        await EnsureTaxonomyAsync(client, adminSession, "delivery", "Delivery", ["operations"]);

        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/blog/manage/posts",
            new CreateBlogPostRequest(
                Slug: "replayed-publish",
                Title: "Replayed publish",
                Summary: "The same idempotency key should replay the stored publish response.",
                Body: "Publishing should remain replay-safe for the same idempotency key.",
                Featured: false,
                CategorySlug: "delivery",
                TagNames: ["operations"],
                ShareTargets: ["linkedin"],
                SeoMetadata: new BlogSeoMetadataRequest(null, null, null)),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        using var createJson = await ReadJsonAsync(createResponse);
        var postId = createJson.RootElement.GetProperty("postId").GetString() ?? string.Empty;

        var firstPublish = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/blog/manage/posts/{postId}/publish",
            new PublishBlogPostRequest(ExpectedVersion: 1),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken,
            idempotencyKey: "blog-publish-replay");
        var secondPublish = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/blog/manage/posts/{postId}/publish",
            new PublishBlogPostRequest(ExpectedVersion: 1),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken,
            idempotencyKey: "blog-publish-replay");

        Xunit.Assert.Equal(HttpStatusCode.OK, firstPublish.StatusCode);
        Xunit.Assert.Equal(HttpStatusCode.OK, secondPublish.StatusCode);

        using var firstPublishJson = await ReadJsonAsync(firstPublish);
        using var secondPublishJson = await ReadJsonAsync(secondPublish);

        Xunit.Assert.Equal(
            firstPublishJson.RootElement.GetProperty("version").GetInt32(),
            secondPublishJson.RootElement.GetProperty("version").GetInt32());
        Xunit.Assert.Equal(
            firstPublishJson.RootElement.GetProperty("publishedUtc").GetString(),
            secondPublishJson.RootElement.GetProperty("publishedUtc").GetString());
    }

    [Xunit.Fact]
    public async Task PublishEndpointRejectsStaleVersionsWithConflict()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        await EnsureTaxonomyAsync(client, adminSession, "delivery", "Delivery", ["operations"]);

        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/blog/manage/posts",
            new CreateBlogPostRequest(
                Slug: "stale-version-blog-post",
                Title: "Stale version blog post",
                Summary: "The second publish should fail with a concurrency conflict.",
                Body: "Concurrency protection must reject stale publish attempts.",
                Featured: false,
                CategorySlug: "delivery",
                TagNames: ["operations"],
                ShareTargets: ["linkedin"],
                SeoMetadata: new BlogSeoMetadataRequest(null, null, null)),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        using var createJson = await ReadJsonAsync(createResponse);
        var postId = createJson.RootElement.GetProperty("postId").GetString() ?? string.Empty;

        var firstPublish = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/blog/manage/posts/{postId}/publish",
            new PublishBlogPostRequest(ExpectedVersion: 1),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken,
            idempotencyKey: "blog-publish-stale-first");
        Xunit.Assert.Equal(HttpStatusCode.OK, firstPublish.StatusCode);

        var stalePublish = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/blog/manage/posts/{postId}/publish",
            new PublishBlogPostRequest(ExpectedVersion: 1),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken,
            idempotencyKey: "blog-publish-stale-second");

        Xunit.Assert.Equal(HttpStatusCode.Conflict, stalePublish.StatusCode);

        using var staleJson = await ReadJsonAsync(stalePublish);
        Xunit.Assert.Equal("blog.post_version_conflict", staleJson.RootElement.GetProperty("code").GetString());
    }

    [Xunit.Fact]
    public async Task BlogMutationsRejectStaleVersionUpdates()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        await EnsureTaxonomyAsync(client, adminSession, "delivery", "Delivery", ["operations", "reliability"]);

        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/blog/manage/posts",
            new CreateBlogPostRequest(
                Slug: "stale-blog-post",
                Title: "Stale blog post",
                Summary: "Version conflict coverage.",
                Body: "First draft",
                Featured: false,
                CategorySlug: "delivery",
                TagNames: ["operations"],
                ShareTargets: ["linkedin"],
                SeoMetadata: new BlogSeoMetadataRequest(null, null, null)),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        using var createConflictJson = await ReadJsonAsync(createResponse);
        var postId = createConflictJson.RootElement.GetProperty("postId").GetString() ?? string.Empty;

        var firstUpdate = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/blog/manage/posts/{postId}",
            new UpdateBlogPostRequest(
                ExpectedVersion: 1,
                Slug: "stale-blog-post",
                Title: "Stale blog post",
                Summary: "Version conflict coverage.",
                Body: "Second draft",
                Featured: false,
                CategorySlug: "delivery",
                TagNames: ["operations", "reliability"],
                ShareTargets: ["email newsletter"],
                SeoMetadata: new BlogSeoMetadataRequest("SEO", "Description", "keywords")),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, firstUpdate.StatusCode);

        var staleUpdate = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/blog/manage/posts/{postId}",
            new UpdateBlogPostRequest(
                ExpectedVersion: 1,
                Slug: "stale-blog-post",
                Title: "Stale blog post",
                Summary: "Version conflict coverage.",
                Body: "Stale write",
                Featured: false,
                CategorySlug: "delivery",
                TagNames: ["operations"],
                ShareTargets: ["linkedin"],
                SeoMetadata: new BlogSeoMetadataRequest(null, null, null)),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);

        using var staleUpdateJson = await ReadJsonAsync(staleUpdate);
        Xunit.Assert.Equal("blog.post_version_conflict", staleUpdateJson.RootElement.GetProperty("code").GetString());
    }

    [Xunit.Fact]
    public async Task BlogTaxonomyEndpointsManageEditorialCategoriesAndTags()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");

        var createCategory = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/blog/manage/categories",
            new CreateBlogCategoryRequest(Slug: null, Name: "Platform Strategy", Description: "Long-range platform direction."),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.True(createCategory.IsSuccessStatusCode, await createCategory.Content.ReadAsStringAsync());

        using var createdCategoryJson = await ReadJsonAsync(createCategory);
        Xunit.Assert.Equal("platform-strategy", createdCategoryJson.RootElement.GetProperty("slug").GetString());
        Xunit.Assert.Equal(1, createdCategoryJson.RootElement.GetProperty("version").GetInt32());

        var createTag = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/blog/manage/tags",
            new CreateBlogTagRequest(Slug: null, DisplayName: "Architecture", Description: "Architecture-focused writing."),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, createTag.StatusCode);

        using var createdTagJson = await ReadJsonAsync(createTag);
        Xunit.Assert.Equal("architecture", createdTagJson.RootElement.GetProperty("slug").GetString());
        Xunit.Assert.Equal("Architecture", createdTagJson.RootElement.GetProperty("displayName").GetString());

        var updateCategory = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/blog/manage/categories/platform-strategy",
            new UpdateBlogCategoryRequest(ExpectedVersion: 1, Name: "Platform Strategy", Description: "Updated editorial description."),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, updateCategory.StatusCode);

        var updateTag = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/api/v1/blog/manage/tags/architecture",
            new UpdateBlogTagRequest(ExpectedVersion: 1, DisplayName: "Architecture", Description: "Updated tag description."),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, updateTag.StatusCode);

        var categoriesResponse = await SendAsync(client, HttpMethod.Get, "/api/v1/blog/manage/categories", adminSession.Cookie);
        Xunit.Assert.Equal(HttpStatusCode.OK, categoriesResponse.StatusCode);

        using (var categoriesJson = await ReadJsonAsync(categoriesResponse))
        {
            var category = categoriesJson.RootElement.GetProperty("categories").EnumerateArray().Single();
            Xunit.Assert.Equal("platform-strategy", category.GetProperty("slug").GetString());
            Xunit.Assert.Equal("Updated editorial description.", category.GetProperty("description").GetString());
            Xunit.Assert.Equal(2, category.GetProperty("version").GetInt32());
        }

        var tagsResponse = await SendAsync(client, HttpMethod.Get, "/api/v1/blog/manage/tags", adminSession.Cookie);
        Xunit.Assert.Equal(HttpStatusCode.OK, tagsResponse.StatusCode);

        using var tagsJson = await ReadJsonAsync(tagsResponse);
        var tag = tagsJson.RootElement.GetProperty("tags").EnumerateArray().Single();
        Xunit.Assert.Equal("architecture", tag.GetProperty("slug").GetString());
        Xunit.Assert.Equal("Updated tag description.", tag.GetProperty("description").GetString());
        Xunit.Assert.Equal(2, tag.GetProperty("version").GetInt32());
    }

    [Xunit.Fact]
    public async Task BlogSchedulesPublishAndUnpublishPostsUsingPreferredTimeZone()
    {
        var clock = new AdjustableClock(SystemClock.Instance.GetCurrentInstant());
        var publishInstant = clock.GetCurrentInstant() + Duration.FromMinutes(15);
        var unpublishInstant = clock.GetCurrentInstant() + Duration.FromMinutes(30);
        var publishSchedule = ToUtcScheduleInputs(publishInstant);
        var unpublishSchedule = ToUtcScheduleInputs(unpublishInstant);
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync();

        await using var application = await PostgresBackedApiApplication.StartAsync(
            postgres.GetConnectionString(),
            configureBuilder: builder => builder.Services.AddSingleton<BuildingBlocks.Domain.Time.IClock>(clock),
            removeHostedServices: ["BlogPublicationWorker"]);

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        await UpdatePreferredTimeZoneAsync(client, adminSession, "Etc/UTC");
        await EnsureTaxonomyAsync(client, adminSession, "platform-strategy", "Platform Strategy", ["architecture"]);

        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/blog/manage/posts",
            new CreateBlogPostRequest(
                Slug: "scheduled-blog-post",
                Title: "Scheduled blog post",
                Summary: "Schedule publish and unpublish without waiting on wall time.",
                Body: "This post is scheduled through the current actor time zone.",
                Featured: false,
                CategorySlug: "platform-strategy",
                TagNames: ["architecture"],
                ShareTargets: ["linkedin"],
                SeoMetadata: new BlogSeoMetadataRequest(null, null, null)),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        using var createScheduleJson = await ReadJsonAsync(createResponse);
        var postId = createScheduleJson.RootElement.GetProperty("postId").GetString() ?? string.Empty;

        var scheduleResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/blog/manage/posts/{postId}/schedule",
            new ScheduleBlogPostLifecycleRequest(
                ExpectedVersion: 1,
                PublishLocalDate: publishSchedule.LocalDate,
                PublishLocalTime: publishSchedule.LocalTime,
                UnpublishLocalDate: unpublishSchedule.LocalDate,
                UnpublishLocalTime: unpublishSchedule.LocalTime),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, scheduleResponse.StatusCode);

        using (var scheduleJson = await ReadJsonAsync(scheduleResponse))
        {
            Xunit.Assert.Equal("draft", scheduleJson.RootElement.GetProperty("status").GetString());
            Xunit.Assert.Equal(publishSchedule.LocalDate, scheduleJson.RootElement.GetProperty("scheduledPublish").GetProperty("scheduledLocalDate").GetString());
            Xunit.Assert.Equal(publishSchedule.LocalTimeWithSeconds, scheduleJson.RootElement.GetProperty("scheduledPublish").GetProperty("scheduledLocalTime").GetString());
            Xunit.Assert.Equal("Etc/UTC", scheduleJson.RootElement.GetProperty("scheduledPublish").GetProperty("timeZoneId").GetString());
            Xunit.Assert.Equal(unpublishSchedule.LocalDate, scheduleJson.RootElement.GetProperty("scheduledUnpublish").GetProperty("scheduledLocalDate").GetString());
        }

        var draftPublicResponse = await client.GetAsync("/api/v1/blog/posts/scheduled-blog-post");
        Xunit.Assert.Equal(HttpStatusCode.NotFound, draftPublicResponse.StatusCode);

        clock.Advance(Duration.FromMinutes(16));
        await ProcessDueSchedulesAsync(application);

        var publishedResponse = await client.GetAsync("/api/v1/blog/posts/scheduled-blog-post");
        Xunit.Assert.Equal(HttpStatusCode.OK, publishedResponse.StatusCode);

        using (var publishedJson = await ReadJsonAsync(publishedResponse))
        {
            Xunit.Assert.Equal("published", publishedJson.RootElement.GetProperty("status").GetString());
            Xunit.Assert.Equal(publishSchedule.ScheduledForUtc.ToDateTimeOffset(), publishedJson.RootElement.GetProperty("publishedUtc").GetDateTimeOffset());
            Xunit.Assert.True(publishedJson.RootElement.GetProperty("scheduledPublish").ValueKind == JsonValueKind.Null);
            Xunit.Assert.True(publishedJson.RootElement.GetProperty("scheduledUnpublish").ValueKind == JsonValueKind.Object);
        }

        clock.Advance(Duration.FromMinutes(15));
        await ProcessDueSchedulesAsync(application);

        var unpublishedResponse = await client.GetAsync("/api/v1/blog/posts/scheduled-blog-post");
        Xunit.Assert.Equal(HttpStatusCode.NotFound, unpublishedResponse.StatusCode);

        var managementResponse = await SendAsync(client, HttpMethod.Get, "/api/v1/blog/manage/posts", adminSession.Cookie);
        Xunit.Assert.Equal(HttpStatusCode.OK, managementResponse.StatusCode);

        using var managementJson = await ReadJsonAsync(managementResponse);
        var scheduledPost = managementJson.RootElement
            .GetProperty("posts")
            .EnumerateArray()
            .Single(post => post.GetProperty("postId").GetString() == postId);

        Xunit.Assert.Equal("archived", scheduledPost.GetProperty("status").GetString());
        Xunit.Assert.True(scheduledPost.GetProperty("scheduledPublish").ValueKind == JsonValueKind.Null);
        Xunit.Assert.True(scheduledPost.GetProperty("scheduledUnpublish").ValueKind == JsonValueKind.Null);
    }

    [Xunit.Fact]
    public async Task BlogSchedulingReturnsValidationFailureWhenIdentityTimeZoneIsInvalid()
    {
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync();
        var connectionString = postgres.GetConnectionString();

        await using var application = await PostgresBackedApiApplication.StartAsync(connectionString);

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        await EnsureTaxonomyAsync(client, adminSession, "resilience", "Resilience", ["operations"]);

        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/blog/manage/posts",
            new CreateBlogPostRequest(
                Slug: "invalid-time-zone-blog-post",
                Title: "Invalid time zone coverage",
                Summary: "Defensive validation for stale Identity time zone values.",
                Body: "Scheduling should fail cleanly when Identity returns a stale zone id.",
                Featured: false,
                CategorySlug: "resilience",
                TagNames: ["operations"],
                ShareTargets: ["linkedin"],
                SeoMetadata: new BlogSeoMetadataRequest(null, null, null)),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        createResponse.EnsureSuccessStatusCode();

        using var createInvalidZoneJson = await ReadJsonAsync(createResponse);
        var postId = createInvalidZoneJson.RootElement.GetProperty("postId").GetString() ?? string.Empty;
        await SetIdentityPreferredTimeZoneDirectlyAsync(connectionString, "admin", "Mars/OlympusMons");

        var scheduleResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/blog/manage/posts/{postId}/schedule",
            new ScheduleBlogPostLifecycleRequest(
                ExpectedVersion: 1,
                PublishLocalDate: "2026-04-09",
                PublishLocalTime: "12:15",
                UnpublishLocalDate: null,
                UnpublishLocalTime: null),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.BadRequest, scheduleResponse.StatusCode);

        using var scheduleJson = await ReadJsonAsync(scheduleResponse);
        Xunit.Assert.Equal("blog.schedule_time_zone_invalid", scheduleJson.RootElement.GetProperty("code").GetString());
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

        Xunit.Assert.True(createCategory.IsSuccessStatusCode, await createCategory.Content.ReadAsStringAsync());

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

            Xunit.Assert.True(createTag.IsSuccessStatusCode, await createTag.Content.ReadAsStringAsync());
        }
    }

    private static async Task<int> ProcessDueSchedulesAsync(PostgresBackedApiApplication application)
    {
        await using var scope = application.App.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var result = await dispatcher.Send(new ProcessDueBlogPostSchedulesCommand(), CancellationToken.None);
        Xunit.Assert.True(result.IsSuccess, result.Error?.Code ?? "blog.schedule_dispatch_failed");
        return result.Value;
    }

    private static (string LocalDate, string LocalTime, string LocalTimeWithSeconds, Instant ScheduledForUtc) ToUtcScheduleInputs(Instant instant)
    {
        var localDateTime = instant.InZone(DateTimeZoneProviders.Tzdb["Etc/UTC"]).LocalDateTime;
        var truncatedLocalDateTime = new LocalDateTime(
            localDateTime.Year,
            localDateTime.Month,
            localDateTime.Day,
            localDateTime.Hour,
            localDateTime.Minute);
        return (
            truncatedLocalDateTime.Date.ToString("yyyy-MM-dd", null),
            $"{truncatedLocalDateTime.Hour:D2}:{truncatedLocalDateTime.Minute:D2}",
            $"{truncatedLocalDateTime.Hour:D2}:{truncatedLocalDateTime.Minute:D2}:00",
            truncatedLocalDateTime.InZoneLeniently(DateTimeZoneProviders.Tzdb["Etc/UTC"]).ToInstant());
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

    private static async Task SetIdentityPreferredTimeZoneDirectlyAsync(string connectionString, string userName, string preferredTimeZoneId)
    {
        const string sql = """
            UPDATE identity.accounts
            SET preferred_time_zone_id = @preferredTimeZoneId
            WHERE user_name = @userName;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("preferredTimeZoneId", preferredTimeZoneId);
        command.Parameters.AddWithValue("userName", userName);

        var updated = await command.ExecuteNonQueryAsync();
        Xunit.Assert.Equal(1, updated);
    }
}
