using System.Collections.Concurrent;
using System.Text.Json;

namespace MarkUptv.Services;

public sealed class MemoryCacheService : ICacheService
{
    private readonly ConcurrentDictionary<string, CacheItem> _items = new();

    public Task<T?> GetAsync<T>(string key)
    {
        if (_items.TryGetValue(key, out var item))
        {
            if (item.ExpiresAt is null || item.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return Task.FromResult(JsonSerializer.Deserialize<T>(item.Json));
            }

            _items.TryRemove(key, out _);
        }

        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        _items[key] = new CacheItem(
            JsonSerializer.Serialize(value),
            expiry.HasValue ? DateTimeOffset.UtcNow.Add(expiry.Value) : null);

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _items.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private sealed record CacheItem(string Json, DateTimeOffset? ExpiresAt);
}
