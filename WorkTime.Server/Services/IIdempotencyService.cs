using Microsoft.AspNetCore.Http;

namespace WorkTime.Server.Services;

public interface IIdempotencyService
{
    bool TryGet(string key, out CachedResponse? response);

    void Set(string key, CachedResponse response, TimeSpan? ttl = null);
}

public record CachedResponse(int StatusCode, string ContentType, byte[] Body);
