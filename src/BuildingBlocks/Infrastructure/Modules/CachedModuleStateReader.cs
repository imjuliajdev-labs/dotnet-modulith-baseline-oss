using BuildingBlocks.Application.Modules;
using Microsoft.Extensions.Caching.Memory;

namespace BuildingBlocks.Infrastructure.Modules;

public sealed class CachedModuleStateReader : IModuleStateReader
{
    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromSeconds(30);

    private readonly IModuleStateReader _inner;
    private readonly IMemoryCache _cache;

    public CachedModuleStateReader(IModuleStateReader inner, IMemoryCache cache)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async ValueTask<ModuleStateSnapshot> GetRequiredStateAsync(string moduleKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);

        var cacheKey = $"module:state:{moduleKey}";

        if (_cache.TryGetValue(cacheKey, out ModuleStateSnapshot? cached) && cached is not null)
        {
            return cached;
        }

        var state = await _inner.GetRequiredStateAsync(moduleKey, cancellationToken);

        _cache.Set(cacheKey, state, new MemoryCacheEntryOptions
        {
            SlidingExpiration = SlidingExpiration,
        });

        return state;
    }

    public static void InvalidateModuleState(IMemoryCache cache, string moduleKey)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);

        cache.Remove($"module:state:{moduleKey}");
    }
}
