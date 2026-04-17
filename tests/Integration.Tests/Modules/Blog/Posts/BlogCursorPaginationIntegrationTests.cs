using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blog.Api;
using Blog.Application.Posts; // BP-031 dispatch-coverage marker
using Blog.Application.Taxonomy; // BP-031 dispatch-coverage marker
using Identity.Api.Authentication;
using Microsoft.AspNetCore.TestHost;

namespace Integration.Tests.ModuleCoverage.Blog.Posts;

public sealed class BlogCursorPaginationIntegrationTests
{
    [Xunit.Fact]
    public async Task PublicListRoundTripsEveryPublishedPostExactlyOnceThroughTheCursor()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        await EnsureTaxonomyAsync(client, adminSession, "cursor-test", "Cursor Test", ["pagination"]);

        const int TotalPosts = 5;
        var createdSlugs = new List<string>(TotalPosts);
        for (var index = 0; index < TotalPosts; index++)
        {
            var slug = $"cursor-roundtrip-{index:D2}";
            await CreateAndPublishPostAsync(
                client,
                adminSession,
                slug: slug,
                title: $"Cursor roundtrip post {index:D2}",
                category: "cursor-test",
                tag: "pagination");
            createdSlugs.Add(slug);
        }

        const int PageLimit = 2;
        var seenSlugs = new List<string>();
        string? nextCursor = null;
        var pageCount = 0;

        do
        {
            pageCount++;
            Xunit.Assert.True(pageCount <= TotalPosts + 1, "Cursor iteration exceeded expected page count; possible infinite loop.");

            var query = nextCursor is null
                ? $"/api/v1/blog/posts?limit={PageLimit}"
                : $"/api/v1/blog/posts?limit={PageLimit}&after={Uri.EscapeDataString(nextCursor)}";

            var response = await client.GetAsync(query);
            Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var json = await ReadJsonAsync(response);
            var postsElement = json.RootElement.GetProperty("posts");

            foreach (var post in postsElement.EnumerateArray())
            {
                var slug = post.GetProperty("slug").GetString();
                Xunit.Assert.False(string.IsNullOrWhiteSpace(slug));
                seenSlugs.Add(slug!);
            }

            nextCursor = json.RootElement.TryGetProperty("nextCursor", out var nextCursorElement)
                && nextCursorElement.ValueKind == JsonValueKind.String
                ? nextCursorElement.GetString()
                : null;
        }
        while (!string.IsNullOrEmpty(nextCursor));

        var seenSet = seenSlugs.ToHashSet(StringComparer.Ordinal);
        Xunit.Assert.Equal(seenSlugs.Count, seenSet.Count);

        foreach (var slug in createdSlugs)
        {
            Xunit.Assert.Contains(slug, seenSet);
        }

        Xunit.Assert.Equal(TotalPosts, seenSet.Intersect(createdSlugs, StringComparer.Ordinal).Count());
    }

    [Xunit.Fact]
    public async Task FinalPageReturnsNullNextCursorWhenNoMorePostsRemain()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        await EnsureTaxonomyAsync(client, adminSession, "cursor-terminal", "Cursor Terminal", ["pagination"]);

        const int TotalPosts = 3;
        for (var index = 0; index < TotalPosts; index++)
        {
            await CreateAndPublishPostAsync(
                client,
                adminSession,
                slug: $"cursor-terminal-{index:D2}",
                title: $"Cursor terminal post {index:D2}",
                category: "cursor-terminal",
                tag: "pagination");
        }

        var firstPage = await client.GetAsync($"/api/v1/blog/posts?limit={TotalPosts}");
        Xunit.Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);

        using var firstJson = await ReadJsonAsync(firstPage);
        Xunit.Assert.Equal(TotalPosts, firstJson.RootElement.GetProperty("posts").GetArrayLength());

        var firstNextCursor = firstJson.RootElement.TryGetProperty("nextCursor", out var firstCursorElement)
            && firstCursorElement.ValueKind == JsonValueKind.String
            ? firstCursorElement.GetString()
            : null;

        Xunit.Assert.True(
            string.IsNullOrEmpty(firstNextCursor),
            $"A page that returns every remaining post must not advertise a next cursor, but got '{firstNextCursor}'.");
    }

    [Xunit.Fact]
    public async Task TamperedCursorReturnsValidationProblemDetailsRatherThanSilentlyRestartingFromTheFirstPage()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        await EnsureTaxonomyAsync(client, adminSession, "cursor-tampered", "Cursor Tampered", ["pagination"]);

        await CreateAndPublishPostAsync(
            client,
            adminSession,
            slug: "cursor-tampered-seed",
            title: "Cursor tampered seed",
            category: "cursor-tampered",
            tag: "pagination");

        // Non-base64 garbage
        var garbage = await client.GetAsync("/api/v1/blog/posts?limit=2&after=not-a-cursor");
        Xunit.Assert.Equal(HttpStatusCode.BadRequest, garbage.StatusCode);
        using (var garbageJson = await ReadJsonAsync(garbage))
        {
            Xunit.Assert.Equal("blog.invalid_cursor", garbageJson.RootElement.GetProperty("code").GetString());
        }

        // Valid base64 but no separator
        var noSeparator = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("no-pipe-in-payload"));
        var separatorless = await client.GetAsync($"/api/v1/blog/posts?limit=2&after={Uri.EscapeDataString(noSeparator)}");
        Xunit.Assert.Equal(HttpStatusCode.BadRequest, separatorless.StatusCode);

        // Valid base64 and separator but garbage inner payload (unparseable date and guid)
        var innerGarbage = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("not-a-date|not-a-guid"));
        var innerBad = await client.GetAsync($"/api/v1/blog/posts?limit=2&after={Uri.EscapeDataString(innerGarbage)}");
        Xunit.Assert.Equal(HttpStatusCode.BadRequest, innerBad.StatusCode);
        using (var innerJson = await ReadJsonAsync(innerBad))
        {
            Xunit.Assert.Equal("blog.invalid_cursor", innerJson.RootElement.GetProperty("code").GetString());
        }
    }

    [Xunit.Fact]
    public async Task CursorOrderingIsDeterministicAcrossClockAdjacentPublications()
    {
        await using var application = await PostgresBackedApiApplication.StartAsync();

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client, "admin", "LocalOnly!123");
        await EnsureTaxonomyAsync(client, adminSession, "cursor-stable", "Cursor Stable", ["pagination"]);

        const int TotalPosts = 4;
        for (var index = 0; index < TotalPosts; index++)
        {
            await CreateAndPublishPostAsync(
                client,
                adminSession,
                slug: $"cursor-stable-{index:D2}",
                title: $"Cursor stable post {index:D2}",
                category: "cursor-stable",
                tag: "pagination");
        }

        static async Task<IReadOnlyList<string>> PageAllAsync(HttpClient client)
        {
            var order = new List<string>();
            string? cursor = null;
            do
            {
                var query = cursor is null
                    ? "/api/v1/blog/posts?limit=2"
                    : $"/api/v1/blog/posts?limit=2&after={Uri.EscapeDataString(cursor)}";
                var response = await client.GetAsync(query);
                Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = await ReadJsonAsync(response);
                foreach (var post in json.RootElement.GetProperty("posts").EnumerateArray())
                {
                    order.Add(post.GetProperty("slug").GetString() ?? string.Empty);
                }

                cursor = json.RootElement.TryGetProperty("nextCursor", out var cursorElement)
                    && cursorElement.ValueKind == JsonValueKind.String
                    ? cursorElement.GetString()
                    : null;
            }
            while (!string.IsNullOrEmpty(cursor));

            return order;
        }

        var firstTraversal = await PageAllAsync(client);
        var secondTraversal = await PageAllAsync(client);

        Xunit.Assert.Equal(firstTraversal.Count, secondTraversal.Count);
        Xunit.Assert.True(
            firstTraversal.SequenceEqual(secondTraversal, StringComparer.Ordinal),
            $"Cursor pagination ordering is not deterministic. First traversal: [{string.Join(", ", firstTraversal)}], second traversal: [{string.Join(", ", secondTraversal)}].");
    }

    private static async Task CreateAndPublishPostAsync(
        HttpClient client,
        AuthenticatedSession adminSession,
        string slug,
        string title,
        string category,
        string tag)
    {
        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/blog/manage/posts",
            new CreateBlogPostRequest(
                Slug: slug,
                Title: title,
                Summary: $"Cursor pagination fixture summary for {slug}.",
                Body: $"Cursor pagination fixture body for {slug}. Long enough to pass minimum body validation.",
                Featured: false,
                CategorySlug: category,
                TagNames: [tag],
                ShareTargets: [],
                SeoMetadata: new BlogSeoMetadataRequest(null, null, null)),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.True(
            createResponse.IsSuccessStatusCode,
            $"Create failed for {slug}: {await createResponse.Content.ReadAsStringAsync()}");

        string postId;
        using (var createJson = await ReadJsonAsync(createResponse))
        {
            postId = createJson.RootElement.GetProperty("postId").GetString() ?? string.Empty;
        }

        Xunit.Assert.False(string.IsNullOrWhiteSpace(postId));

        var publishResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/blog/manage/posts/{postId}/publish",
            new PublishBlogPostRequest(ExpectedVersion: 1),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken,
            idempotencyKey: $"blog-cursor-publish-{postId}");

        Xunit.Assert.True(
            publishResponse.IsSuccessStatusCode,
            $"Publish failed for {slug}: {await publishResponse.Content.ReadAsStringAsync()}");
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

        Xunit.Assert.True(
            createCategory.IsSuccessStatusCode,
            $"Create category failed: {await createCategory.Content.ReadAsStringAsync()}");

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

            Xunit.Assert.True(
                createTag.IsSuccessStatusCode,
                $"Create tag failed: {await createTag.Content.ReadAsStringAsync()}");
        }
    }

    private static async Task<AuthenticatedSession> SignInAsync(HttpClient client, string userName, string password)
    {
        var antiforgery = await GetAntiforgeryAsync(client);
        var login = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/identity/session/login",
            new PasswordSignInRequest(userName, password),
            antiforgery.Cookie,
            antiforgery.HeaderName,
            antiforgery.RequestToken);

        login.EnsureSuccessStatusCode();
        var authCookie = GetCookie(login, "__Host-dotnet-modulith-baseline");

        var authenticatedAntiforgery = await GetAntiforgeryAsync(client, authCookie);
        return new AuthenticatedSession(
            authenticatedAntiforgery.HeaderName,
            authenticatedAntiforgery.RequestToken,
            authCookie,
            CombineCookies(authCookie, authenticatedAntiforgery.Cookie));
    }

    private static async Task<AntiforgeryContext> GetAntiforgeryAsync(HttpClient client, string? cookies = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/antiforgery");
        if (!string.IsNullOrWhiteSpace(cookies))
        {
            request.Headers.Add("Cookie", cookies);
        }

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var json = await ReadJsonAsync(response);
        return new AntiforgeryContext(
            json.RootElement.GetProperty("headerName").GetString() ?? string.Empty,
            json.RootElement.GetProperty("requestToken").GetString() ?? string.Empty,
            GetCookie(response, ".AspNetCore.Antiforgery"));
    }

    private static async Task<HttpResponseMessage> SendJsonAsync<TBody>(
        HttpClient client,
        HttpMethod method,
        string path,
        TBody body,
        string? cookies = null,
        string? antiforgeryHeaderName = null,
        string? antiforgeryToken = null,
        string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body)
        };

        if (!string.IsNullOrWhiteSpace(cookies))
        {
            request.Headers.Add("Cookie", cookies);
        }

        if (!string.IsNullOrWhiteSpace(antiforgeryHeaderName) && !string.IsNullOrWhiteSpace(antiforgeryToken))
        {
            request.Headers.Add(antiforgeryHeaderName, antiforgeryToken);
        }

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static string GetCookie(HttpResponseMessage response, string cookieNamePrefix)
    {
        var cookie = response.Headers
            .GetValues("Set-Cookie")
            .Single(header => header.StartsWith(cookieNamePrefix, StringComparison.OrdinalIgnoreCase));

        return cookie.Split(';', 2)[0];
    }

    private static string CombineCookies(params string?[] cookies)
    {
        return string.Join(
            "; ",
            cookies
                .Where(static cookie => !string.IsNullOrWhiteSpace(cookie))
                .Select(static cookie => cookie!));
    }

    private sealed record AntiforgeryContext(string HeaderName, string RequestToken, string Cookie);

    private sealed record AuthenticatedSession(string HeaderName, string RequestToken, string Cookie, string Cookies);
}
