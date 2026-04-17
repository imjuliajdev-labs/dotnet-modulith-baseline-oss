using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Domain.Time;

namespace BuildingBlocks.Infrastructure.IntegrationEvents;

public sealed class IntegrationEventOutboxPublisher : IIntegrationEventOutboxPublisher
{
    private readonly IClock _clock;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IReadOnlyDictionary<string, IIntegrationEventOutboxStore> _stores;

    public IntegrationEventOutboxPublisher(
        IEnumerable<IIntegrationEventOutboxStore> stores,
        IClock clock,
        IRequestContextAccessor requestContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(stores);

        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _requestContextAccessor = requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));

        var duplicates = stores
            .GroupBy(static store => NormalizeModuleKey(store.ModuleKey), StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate integration-event outbox registrations were found for: {string.Join(", ", duplicates.OrderBy(static key => key, StringComparer.Ordinal))}.");
        }

        _stores = stores.ToDictionary(static store => NormalizeModuleKey(store.ModuleKey), StringComparer.Ordinal);
    }

    public ValueTask PublishAsync(IntegrationEventOutboxPublishRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.IntegrationEvent);

        var moduleKey = NormalizeModuleKey(request.ModuleKey);
        if (!_stores.TryGetValue(moduleKey, out var store))
        {
            throw new InvalidOperationException($"No integration-event outbox is registered for module '{moduleKey}'.");
        }

        var headers = request.Headers is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(request.Headers, StringComparer.Ordinal);

        var requestContext = _requestContextAccessor.Current;
        if (requestContext is not null)
        {
            if (!headers.ContainsKey("correlationId") && !string.IsNullOrWhiteSpace(requestContext.CorrelationId))
            {
                headers["correlationId"] = requestContext.CorrelationId;
            }

            if (!headers.ContainsKey("requestId") && !string.IsNullOrWhiteSpace(requestContext.RequestId))
            {
                headers["requestId"] = requestContext.RequestId;
            }
        }

        var publishRequest = request with
        {
            ModuleKey = moduleKey,
            AvailableAt = request.AvailableAt ?? _clock.GetCurrentInstant(),
            Headers = headers
        };

        return store.EnqueueAsync(publishRequest, cancellationToken);
    }

    private static string NormalizeModuleKey(string moduleKey)
    {
        return string.IsNullOrWhiteSpace(moduleKey)
            ? string.Empty
            : moduleKey.Trim();
    }
}
