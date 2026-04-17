using BuildingBlocks.Domain.Events;
using KnowledgeBase.PublicContracts.Events;

namespace Architecture.Tests.Modules.KnowledgeBase.Events;

public sealed class KnowledgeBaseIntegrationEventContractTests
{
    [Fact]
    public void KnowledgeEntryPublishedEventV1LivesInPublicContractsAndCarriesStableMetadata()
    {
        var eventType = typeof(KnowledgeEntryPublishedEventV1);

        Assert.Equal("KnowledgeBase.PublicContracts", eventType.Assembly.GetName().Name);
        Assert.EndsWith("V1", eventType.Name, StringComparison.Ordinal);
        Assert.True(typeof(IIntegrationEvent).IsAssignableFrom(eventType));

        Assert.NotNull(eventType.GetProperty(nameof(KnowledgeEntryPublishedEventV1.EventId)));
        Assert.NotNull(eventType.GetProperty(nameof(KnowledgeEntryPublishedEventV1.OccurredAt)));
        Assert.NotNull(eventType.GetProperty(nameof(KnowledgeEntryPublishedEventV1.EntryId)));
        Assert.NotNull(eventType.GetProperty(nameof(KnowledgeEntryPublishedEventV1.Slug)));
        Assert.NotNull(eventType.GetProperty(nameof(KnowledgeEntryPublishedEventV1.Title)));
        Assert.NotNull(eventType.GetProperty(nameof(KnowledgeEntryPublishedEventV1.Body)));
        Assert.NotNull(eventType.GetProperty(nameof(KnowledgeEntryPublishedEventV1.PublishedByActorId)));
    }

    [Fact]
    public void KnowledgeEntryPublishedEventV2IsAnAdditiveEvolutionOfV1()
    {
        var v1Properties = typeof(KnowledgeEntryPublishedEventV1)
            .GetProperties()
            .Select(static property => property.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        var v2Type = typeof(KnowledgeEntryPublishedEventV2);
        var v2Properties = v2Type
            .GetProperties()
            .Select(static property => property.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal("KnowledgeBase.PublicContracts", v2Type.Assembly.GetName().Name);
        Assert.EndsWith("V2", v2Type.Name, StringComparison.Ordinal);
        Assert.True(typeof(IIntegrationEvent).IsAssignableFrom(v2Type));

        foreach (var propertyName in v1Properties)
        {
            Assert.Contains(propertyName, v2Properties, StringComparer.Ordinal);
        }

        Assert.Contains(nameof(KnowledgeEntryPublishedEventV2.Category), v2Properties, StringComparer.Ordinal);
    }
}
