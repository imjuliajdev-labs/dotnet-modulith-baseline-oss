using Blog.Application.Posts;
using Blog.Application.Scheduling;
using Blog.Domain.Posts;
using Blog.PublicContracts.Events;
using BuildingBlocks.Application.Results;
using BuildingBlocks.Testing.Time;
using Module.UnitTests.Support;
using NodaTime;

namespace Module.UnitTests.Blog;

public sealed class ScheduleBlogPostLifecycleCommandHandlerTests
{
    [Fact]
    public async Task Handle_builds_the_schedule_from_the_actor_time_zone_and_writes_audit()
    {
        var postId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var current = BlogTestData.CreatePost(postId: postId, status: BlogPostStatus.Draft, version: 3, publicationSchedule: BlogPublicationSchedule.Empty);
        var scheduledPublish = new BlogScheduledPublication(
            new LocalDate(2026, 4, 20),
            new LocalTime(9, 0),
            "Etc/UTC",
            BlogPostLocalTimeResolutions.Exact,
            Instant.FromUtc(2026, 4, 20, 9, 0),
            "admin-1");
        var scheduledPost = BlogTestData.CreatePost(
            postId: postId,
            status: BlogPostStatus.Draft,
            version: 4,
            publicationSchedule: new BlogPublicationSchedule(scheduledPublish, null));

        var store = new RecordingBlogPostStore
        {
            GetByIdResult = Result<BlogPost>.Success(current),
            SchedulePublicationResult = Result<BlogPost>.Success(scheduledPost)
        };
        var timeZoneReader = new StubBlogIdentityTimeZoneReader
        {
            NextResult = Result<string>.Success("Etc/UTC")
        };
        var auditWriter = new RecordingAuditEventWriter();
        var handler = new ScheduleBlogPostLifecycleCommandHandler(
            auditWriter,
            new FakeClock(BlogTestData.FixedNow),
            new StubCurrentActorAccessor(new BuildingBlocks.Application.Actors.CurrentActor("admin-1", isAuthenticated: true, roles: ["Admin"])),
            new InMemoryRequestContextAccessor { Current = new BuildingBlocks.Application.Dispatching.RequestContext("corr-blog-schedule", "req-blog-schedule") },
            store,
            timeZoneReader);

        var result = await handler.Handle(
            new ScheduleBlogPostLifecycleCommand(
                postId,
                3,
                new LocalDate(2026, 4, 20),
                new LocalTime(9, 0),
                null,
                null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(postId, store.LastGetByIdPostId);
        Assert.Equal(["admin-1"], timeZoneReader.ActorIds);
        Assert.Equal(postId, store.LastSchedulePostId);
        Assert.Equal(3, store.LastScheduleExpectedVersion);
        Assert.Equal("admin-1", store.LastScheduleActorId);
        Assert.Equal(BlogTestData.FixedNow, store.LastScheduleNow);
        Assert.NotNull(store.LastPublicationSchedule);
        Assert.NotNull(store.LastPublicationSchedule!.Publish);
        Assert.Equal(Instant.FromUtc(2026, 4, 20, 9, 0), store.LastPublicationSchedule.Publish!.ScheduledForUtc);
        Assert.Null(store.LastPublicationSchedule.Unpublish);

        var audit = Assert.Single(auditWriter.Events);
        Assert.Equal("blog.post.schedule.update", audit.Action);
        Assert.Equal(postId.ToString(), audit.TargetId);
        Assert.Contains("publish:", audit.Outcome, StringComparison.Ordinal);
        Assert.Equal("corr-blog-schedule", audit.CorrelationId);
    }
}

public sealed class ProcessDueBlogPostSchedulesCommandHandlerTests
{
    [Fact]
    public async Task Handle_emits_publish_events_for_publish_transitions_and_audits_every_transition()
    {
        var publishedPost = BlogTestData.CreatePost(
            postId: Guid.Parse("12121212-1212-1212-1212-121212121212"),
            slug: "published-post",
            status: BlogPostStatus.Published,
            version: 5,
            publishedUtc: Instant.FromUtc(2026, 4, 20, 9, 0),
            publishedByActorId: "admin-1");
        var archivedPost = BlogTestData.CreatePost(
            postId: Guid.Parse("34343434-3434-3434-3434-343434343434"),
            slug: "archived-post",
            status: BlogPostStatus.Archived,
            version: 6);

        var store = new RecordingBlogPostStore
        {
            ProcessDueTransitionsResult =
            [
                new ProcessedBlogPostTransition(
                    BlogScheduledTransitionKind.Publish,
                    publishedPost,
                    Instant.FromUtc(2026, 4, 20, 9, 0),
                    "admin-1"),
                new ProcessedBlogPostTransition(
                    BlogScheduledTransitionKind.Unpublish,
                    archivedPost,
                    Instant.FromUtc(2026, 4, 21, 9, 0),
                    "admin-2")
            ]
        };
        var outboxPublisher = new RecordingBlogOutboxPublisher();
        var auditWriter = new RecordingAuditEventWriter();
        var handler = new ProcessDueBlogPostSchedulesCommandHandler(
            auditWriter,
            new FakeClock(BlogTestData.FixedNow),
            outboxPublisher,
            new InMemoryRequestContextAccessor { Current = new BuildingBlocks.Application.Dispatching.RequestContext("corr-blog-process", "req-blog-process") },
            store);

        var result = await handler.Handle(new ProcessDueBlogPostSchedulesCommand(999), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
        Assert.Equal(BlogTestData.FixedNow, store.LastProcessDueNow);
        Assert.Equal(BlogPostSchedulingDefaults.MaxProcessingBatchSize, store.LastProcessDueBatchSize);

        var outbox = Assert.Single(outboxPublisher.Requests);
        var publishedEvent = Assert.IsType<BlogPostPublishedEventV1>(outbox.IntegrationEvent);
        Assert.Equal(publishedPost.PostId, publishedEvent.PostId);
        Assert.Equal("published-post", publishedEvent.Slug);

        Assert.Equal(2, auditWriter.Events.Count);
        Assert.Equal("blog.post.publish.scheduled", auditWriter.Events[0].Action);
        Assert.Equal(BlogPostStatusNames.Published, auditWriter.Events[0].Outcome);
        Assert.Equal("blog.post.unpublish.scheduled", auditWriter.Events[1].Action);
        Assert.Equal(BlogPostStatusNames.Archived, auditWriter.Events[1].Outcome);
    }
}
