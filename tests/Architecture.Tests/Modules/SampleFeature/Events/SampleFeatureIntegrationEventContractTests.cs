using BuildingBlocks.Domain.Events;
using SampleFeature.PublicContracts.Events;

namespace Architecture.Tests.Modules.SampleFeature.Events;

public sealed class SampleFeatureIntegrationEventContractTests
{
    [Fact]
    public void SampleAnnouncementPublishedEventV1LivesInPublicContractsAndCarriesStableMetadata()
    {
        var eventType = typeof(SampleAnnouncementPublishedEventV1);

        Assert.Equal("SampleFeature.PublicContracts", eventType.Assembly.GetName().Name);
        Assert.EndsWith("V1", eventType.Name, StringComparison.Ordinal);
        Assert.True(typeof(IIntegrationEvent).IsAssignableFrom(eventType));

        Assert.NotNull(eventType.GetProperty(nameof(SampleAnnouncementPublishedEventV1.EventId)));
        Assert.NotNull(eventType.GetProperty(nameof(SampleAnnouncementPublishedEventV1.OccurredAt)));
        Assert.NotNull(eventType.GetProperty(nameof(SampleAnnouncementPublishedEventV1.AnnouncementId)));
        Assert.NotNull(eventType.GetProperty(nameof(SampleAnnouncementPublishedEventV1.Title)));
        Assert.NotNull(eventType.GetProperty(nameof(SampleAnnouncementPublishedEventV1.Body)));
        Assert.NotNull(eventType.GetProperty(nameof(SampleAnnouncementPublishedEventV1.PublishedByActorId)));
    }
}
