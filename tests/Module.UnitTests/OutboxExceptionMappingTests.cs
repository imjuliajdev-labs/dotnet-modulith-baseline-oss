using BuildingBlocks.Application.Results;
using BuildingBlocks.Infrastructure.IntegrationEvents;
using BuildingBlocks.Infrastructure.ProblemDetails;

namespace Module.UnitTests;

public sealed class OutboxExceptionMappingTests
{
    [Fact]
    public void UnresolvableTypeExceptionMapsToTypeUnresolvableErrorCode()
    {
        var mapper = new DefaultExceptionToErrorMapper();
        var exception = new UnresolvableIntegrationEventTypeException(
            messageId: 42,
            moduleKey: "chaos",
            eventType: "Missing.Type.Name");

        var error = mapper.Map(exception);

        Assert.Equal("outbox.type_unresolvable", error.Code);
        Assert.Equal(ErrorKind.Failure, error.Kind);
        Assert.Equal(42, exception.MessageId);
        Assert.Equal("chaos", exception.ModuleKey);
        Assert.Equal("Missing.Type.Name", exception.EventType);
    }

    [Fact]
    public void DeserializationExceptionMapsToDeserializeFailedErrorCode()
    {
        var mapper = new DefaultExceptionToErrorMapper();
        var exception = new IntegrationEventDeserializationException(
            messageId: 43,
            moduleKey: "chaos",
            eventType: "Chaos.Event");

        var error = mapper.Map(exception);

        Assert.Equal("outbox.deserialize_failed", error.Code);
        Assert.Equal(ErrorKind.Failure, error.Kind);
    }

    [Fact]
    public void IdMismatchExceptionMapsToEventIdMismatchErrorCodeAndCarriesBothIds()
    {
        var mapper = new DefaultExceptionToErrorMapper();
        var stored = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var deserialized = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var exception = new IntegrationEventIdMismatchException(
            messageId: 44,
            moduleKey: "chaos",
            eventType: "Chaos.Event",
            storedEventId: stored,
            deserializedEventId: deserialized);

        var error = mapper.Map(exception);

        Assert.Equal("outbox.event_id_mismatch", error.Code);
        Assert.Equal(ErrorKind.Failure, error.Kind);
        Assert.Equal(stored, exception.StoredEventId);
        Assert.Equal(deserialized, exception.DeserializedEventId);
    }

    [Fact]
    public void OutboxDispatchExceptionHierarchyAllowsSingleCatchToIdentifyDeterministicFailures()
    {
        IntegrationEventOutboxDispatchException[] deterministicFailures =
        [
            new UnresolvableIntegrationEventTypeException(1, "chaos", "x"),
            new IntegrationEventDeserializationException(2, "chaos", "x"),
            new IntegrationEventIdMismatchException(3, "chaos", "x", Guid.Empty, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
        ];

        foreach (var failure in deterministicFailures)
        {
            Assert.IsAssignableFrom<IntegrationEventOutboxDispatchException>(failure);
        }
    }
}
