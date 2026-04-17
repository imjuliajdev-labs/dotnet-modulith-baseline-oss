using Blog.PublicContracts.Events;
using BuildingBlocks.Domain.Events;

namespace Architecture.Tests.Modules.Blog.Events;

public sealed class BlogIntegrationEventContractTests
{
    [Fact]
    public void BlogPostPublishedEventV1LivesInPublicContractsAndCarriesStableMetadata()
    {
        var eventType = typeof(BlogPostPublishedEventV1);

        Assert.Equal("Blog.PublicContracts", eventType.Assembly.GetName().Name);
        Assert.EndsWith("V1", eventType.Name, StringComparison.Ordinal);
        Assert.True(typeof(IIntegrationEvent).IsAssignableFrom(eventType));

        Assert.NotNull(eventType.GetProperty(nameof(BlogPostPublishedEventV1.EventId)));
        Assert.NotNull(eventType.GetProperty(nameof(BlogPostPublishedEventV1.OccurredAt)));
        Assert.NotNull(eventType.GetProperty(nameof(BlogPostPublishedEventV1.PostId)));
        Assert.NotNull(eventType.GetProperty(nameof(BlogPostPublishedEventV1.Slug)));
        Assert.NotNull(eventType.GetProperty(nameof(BlogPostPublishedEventV1.Title)));
        Assert.NotNull(eventType.GetProperty(nameof(BlogPostPublishedEventV1.Summary)));
        Assert.NotNull(eventType.GetProperty(nameof(BlogPostPublishedEventV1.Body)));
        Assert.NotNull(eventType.GetProperty(nameof(BlogPostPublishedEventV1.PublishedByActorId)));
    }
}
