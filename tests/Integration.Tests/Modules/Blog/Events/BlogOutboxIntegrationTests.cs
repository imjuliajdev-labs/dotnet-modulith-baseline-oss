using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blog.Api;
using Blog.Application.Scheduling;
using Blog.Infrastructure;
using Blog.PublicContracts.Events;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Infrastructure.IntegrationEvents;
using BuildingBlocks.Testing.Time;
using Identity.Api.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using BuildingBlocks.Testing.Http;
using static BuildingBlocks.Testing.Http.IntegrationTestHttpHelpers;

namespace Integration.Tests.Modules.Blog.Events;

public sealed class BlogOutboxIntegrationTests
{
    [Xunit.Fact]
    public void OutboxCapabilityShellAnchorsGovernedRuntimeSeams()
    {
        var outboxRegistrationType = typeof(BlogInfrastructureServiceCollectionExtensions).Assembly
            .GetType("Blog.Infrastructure.Outbox.BlogOutboxRegistration");

        var integrationEvent = new BlogPostPublishedEventV1(
            Guid.NewGuid(),
            Instant.FromUtc(2026, 4, 4, 12, 0),
            Guid.NewGuid(),
            "trust-the-contract",
            "Trust the contract",
            "Refresh generated contracts after backend changes.",
            "Refresh generated contracts after backend changes.",
            "identity:seeded-admin");

        Xunit.Assert.Equal("Blog.PublicContracts", typeof(BlogPostPublishedEventV1).Assembly.GetName().Name);
        Xunit.Assert.Equal("Blog.PublicContracts.Events", typeof(BlogPostPublishedEventV1).Namespace);
        Xunit.Assert.NotNull(outboxRegistrationType);
        Xunit.Assert.Equal("Blog.Infrastructure", outboxRegistrationType!.Assembly.GetName().Name);
        Xunit.Assert.Equal("Blog.Infrastructure.Outbox", outboxRegistrationType.Namespace);
        Xunit.Assert.NotEqual(Guid.Empty, integrationEvent.EventId);
        Xunit.Assert.Equal("trust-the-contract", integrationEvent.Slug);
    }

    [Xunit.Fact]
    public async Task ScheduledPublicationDispatchesBlogPostPublishedEventThroughOutboxAsync()
    {
        var probe = new BlogPublicationDeliveryProbe();
        var clock = new AdjustableClock(SystemClock.Instance.GetCurrentInstant());
        var publishInstant = clock.GetCurrentInstant() + Duration.FromMinutes(5);
        var publishSchedule = ToUtcScheduleInputs(publishInstant);
        await using var postgres = await PostgresBackedApiApplication.StartPostgresAsync();

        await using var application = await PostgresBackedApiApplication.StartAsync(
            postgres.GetConnectionString(),
            configureBuilder: builder =>
            {
                builder.Services.AddSingleton<BuildingBlocks.Domain.Time.IClock>(clock);
                builder.Services.AddSingleton(probe);
                builder.Services.AddDispatcher(typeof(BlogPostPublishedProbeHandler).Assembly);
                builder.Services.AddPostgresIntegrationEventInbox("blog", "blog");
            },
            configureExtraMigrations: static services => services.AddPostgresIntegrationEventInbox("blog", "blog"),
            removeHostedServices: ["BlogPublicationWorker"]);

        var client = application.App.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        var adminSession = await SignInAsync(client);
        await UpdatePreferredTimeZoneAsync(client, adminSession, "Etc/UTC");
        await EnsureTaxonomyAsync(client, adminSession, "delivery", "Delivery", ["platform"]);

        var createResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/blog/manage/posts",
            new CreateBlogPostRequest(
                Slug: "outbox-scheduled-blog-post",
                Title: "Outbox scheduled blog post",
                Summary: "Blog scheduled publication should emit a public event through the outbox.",
                Body: "Dispatch the event after the schedule is due.",
                Featured: false,
                CategorySlug: "delivery",
                TagNames: ["platform"],
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
                PublishLocalDate: publishSchedule.LocalDate,
                PublishLocalTime: publishSchedule.LocalTime,
                UnpublishLocalDate: null,
                UnpublishLocalTime: null),
            adminSession.Cookies,
            adminSession.HeaderName,
            adminSession.RequestToken);

        Xunit.Assert.Equal(HttpStatusCode.OK, scheduleResponse.StatusCode);

        clock.Advance(Duration.FromMinutes(6));

        await using (var scope = application.App.Services.CreateAsyncScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            var result = await dispatcher.Send(new ProcessDueBlogPostSchedulesCommand(), CancellationToken.None);
            Xunit.Assert.True(result.IsSuccess, result.Error?.Code ?? "blog.schedule_dispatch_failed");

            var outboxDispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxDispatcher>();
            var dispatched = await outboxDispatcher.DispatchAvailableAsync(CancellationToken.None);
            Xunit.Assert.Equal(1, dispatched);
        }

        var delivery = Xunit.Assert.Single(probe.Deliveries);
        Xunit.Assert.Equal("outbox-scheduled-blog-post", delivery.Slug);
        Xunit.Assert.Equal("Outbox scheduled blog post", delivery.Title);
        Xunit.Assert.Equal("identity:seeded-admin", delivery.PublishedByActorId);
    }

    private static (string LocalDate, string LocalTime) ToUtcScheduleInputs(Instant instant)
    {
        var localDateTime = instant.InZone(DateTimeZoneProviders.Tzdb["Etc/UTC"]).LocalDateTime;
        return (
            localDateTime.Date.ToString("yyyy-MM-dd", null),
            $"{localDateTime.Hour:D2}:{localDateTime.Minute:D2}");
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

internal sealed class BlogPostPublishedProbeHandler : IModuleScopedIntegrationEventHandler<BlogPostPublishedEventV1>
{
    private readonly BlogPublicationDeliveryProbe _probe;

    public BlogPostPublishedProbeHandler(BlogPublicationDeliveryProbe probe)
    {
        _probe = probe;
    }

    public string ModuleKey => "blog";

    public Task Handle(BlogPostPublishedEventV1 integrationEvent, CancellationToken cancellationToken)
    {
        _probe.Deliveries.Add(integrationEvent);
        return Task.CompletedTask;
    }
}

internal sealed class BlogPublicationDeliveryProbe
{
    public List<BlogPostPublishedEventV1> Deliveries { get; } = [];
}
