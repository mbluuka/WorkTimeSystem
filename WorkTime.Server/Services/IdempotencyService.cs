using Microsoft.Extensions.Caching.Memory;

namespace WorkTime.Server.Services;

public class IdempotencyService : IIdempotencyService
{
    private readonly IMemoryCache _cache;

    public IdempotencyService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool TryGet(string key, out CachedResponse? response)
    {
        return _cache.TryGetValue(key, out response);
    }

    public void Set(string key, CachedResponse response, TimeSpan? ttl = null)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl ?? TimeSpan.FromMinutes(10)
        };

        _cache.Set(key, response, options);
    }
}
