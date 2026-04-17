using Microsoft.AspNetCore.Builder;

namespace BuildingBlocks.Infrastructure.Http;

public sealed record NonCursorListEndpointMetadata(string Reason);

public static class NonCursorListEndpointConventionBuilder
{
    public static TBuilder WithNonCursorListEndpoint<TBuilder>(this TBuilder builder, string reason)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return builder.WithMetadata(new NonCursorListEndpointMetadata(reason));
    }
}
