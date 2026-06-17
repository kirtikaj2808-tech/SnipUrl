using StackExchange.Redis;

namespace UrlService.Infrastructure.Cache;

// This class is the only place in the app that talks to Redis.
// Everything else uses this class — they never touch Redis directly.
public class RedisCacheService
{
    private readonly IDatabase _db;

    // TTL = Time To Live — how long a cached value stays in Redis
    // After 24 hours Redis automatically deletes it
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    public RedisCacheService(IConnectionMultiplexer redis)
    {
        // IConnectionMultiplexer is the StackExchange.Redis connection
        // .GetDatabase() gives us the actual database to query
        _db = redis.GetDatabase();
    }

    // Try to get a URL from Redis by its short code
    // Returns null if not found (cache miss)
    public async Task<string?> GetUrlAsync(string shortCode)
    {
        // Redis key format: "url:abc123"
        // Prefixing with "url:" avoids key collisions if we cache other things later
        var key = $"url:{shortCode}";

        var value = await _db.StringGetAsync(key);

        // RedisValue has implicit null/empty check
        return value.HasValue ? value.ToString() : null;
    }

    // Save a URL to Redis with an expiry time
    public async Task SetUrlAsync(string shortCode, string originalUrl)
    {
        var key = $"url:{shortCode}";

        // StringSetAsync stores a simple string value
        // The TimeSpan sets the TTL — Redis auto-deletes after 24 hours
        await _db.StringSetAsync(key, originalUrl, DefaultTtl);
    }

    // Remove a URL from cache — needed when a link is deleted or deactivated
    public async Task RemoveUrlAsync(string shortCode)
    {
        var key = $"url:{shortCode}";
        await _db.KeyDeleteAsync(key);
    }
}