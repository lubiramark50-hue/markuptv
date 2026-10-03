using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using MarkUptv.Models;

namespace MarkUptv.Services;

public sealed class NewsCacheService : IDisposable
{
    private readonly ILogger<NewsCacheService> _logger;
    private readonly string _cacheDirectory;
    private readonly TimeSpan _defaultTtl = TimeSpan.FromMinutes(30);
    private bool _isDisposed;

    // Type-Isolated Memory Registries to completely eliminate boxing/unboxing overhead
    private readonly ConcurrentDictionary<string, CacheEntry<List<NewsArticle>>> _articleMemoryCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CacheEntry<List<string>>> _categoryMemoryCache = new(StringComparer.Ordinal);

    // Dynamic Disk Trackers to resolve Constructor Race Conditions lazily
    private readonly ConcurrentDictionary<string, bool> _diskLoadedKeys = new(StringComparer.Ordinal);

    // Lock Striping Architecture: Pre-allocated 64 execution pipelines
    private const int StripeCount = 64;
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, StripeCount).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public NewsCacheService(ILogger<NewsCacheService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheDirectory = Path.Combine(FileSystem.AppDataDirectory, "Cache", "NewsV2");

        if (!Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
    }

    /// <summary>
    /// Executes a thread-safe caching transaction for collections of News Articles.
    /// </summary>
    public async Task<List<NewsArticle>?> GetArticlesAsync(
        string rawKey,
        Func<Task<List<NewsArticle>?>> fetchFromApi,
        bool useCache = true,
        TimeSpan? customTtl = null)
    {
        var sanitizedKey = SanitizeKey(rawKey);
        var ttl = customTtl ?? _defaultTtl;
        var now = DateTime.UtcNow;

        if (useCache && _articleMemoryCache.TryGetValue(sanitizedKey, out var memEntry) && !memEntry.IsExpired(ttl, now))
        {
            _logger.LogDebug("Cache HIT [Memory] for Article Payload: '{Key}'", sanitizedKey);
            return memEntry.Data;
        }

        var stripeLock = GetStripeLock(sanitizedKey);
        await stripeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (useCache && _articleMemoryCache.TryGetValue(sanitizedKey, out var lockedEntry) && !lockedEntry.IsExpired(ttl, now))
            {
                return lockedEntry.Data;
            }

            if (useCache && _diskLoadedKeys.TryAdd(sanitizedKey, true))
            {
                var diskEntry = await LoadFromDiskAsync(sanitizedKey, NewsContextContainer.Default.CacheEntryListNewsArticle).ConfigureAwait(false);
                if (diskEntry != null)
                {
                    _articleMemoryCache[sanitizedKey] = diskEntry;
                    if (!diskEntry.IsExpired(ttl, now))
                    {
                        _logger.LogInformation("Cache HIT [Disk Restoration] for Article Payload: '{Key}'", sanitizedKey);
                        return diskEntry.Data;
                    }
                }
            }

            _logger.LogInformation("Cache MISS for Article Payload '{Key}'. Invoking downstream provider...", sanitizedKey);
            var articles = await fetchFromApi().ConfigureAwait(false);

            if (articles != null)
            {
                var cleanEntry = new CacheEntry<List<NewsArticle>> { Data = articles, LastUpdated = now };
                _articleMemoryCache[sanitizedKey] = cleanEntry;
                _ = SaveToDiskBackgroundAsync(sanitizedKey, cleanEntry, NewsContextContainer.Default.CacheEntryListNewsArticle);
                return articles;
            }

            if (_articleMemoryCache.TryGetValue(sanitizedKey, out var expiredFallback))
            {
                _logger.LogWarning("Downstream failure. Providing stale memory fallback for: '{Key}'", sanitizedKey);
                return expiredFallback.Data;
            }

            return null;
        }
        finally
        {
            stripeLock.Release();
        }
    }

    /// <summary>
    /// Executes a thread-safe caching transaction for collection headers (Strings).
    /// </summary>
    public async Task<List<string>?> GetCategoriesAsync(string rawKey, Func<Task<List<string>?>> fetchFromApi, bool useCache = true)
    {
        var sanitizedKey = SanitizeKey(rawKey);
        var now = DateTime.UtcNow;

        if (useCache && _categoryMemoryCache.TryGetValue(sanitizedKey, out var memEntry) && !memEntry.IsExpired(_defaultTtl, now))
        {
            _logger.LogDebug("Cache HIT [Memory] for Category Header: '{Key}'", sanitizedKey);
            return memEntry.Data;
        }

        var stripeLock = GetStripeLock(sanitizedKey);
        await stripeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (useCache && _categoryMemoryCache.TryGetValue(sanitizedKey, out var lockedEntry) && !lockedEntry.IsExpired(_defaultTtl, now))
            {
                return lockedEntry.Data;
            }

            if (useCache && _diskLoadedKeys.TryAdd(sanitizedKey, true))
            {
                var diskEntry = await LoadFromDiskAsync(sanitizedKey, NewsContextContainer.Default.CacheEntryListString).ConfigureAwait(false);
                if (diskEntry != null)
                {
                    _categoryMemoryCache[sanitizedKey] = diskEntry;
                    if (!diskEntry.IsExpired(_defaultTtl, now))
                    {
                        _logger.LogInformation("Cache HIT [Disk Restoration] for Category Header: '{Key}'", sanitizedKey);
                        return diskEntry.Data;
                    }
                }
            }

            _logger.LogInformation("Cache MISS for Category Header '{Key}'. Invoking downstream provider...", sanitizedKey);
            var categories = await fetchFromApi().ConfigureAwait(false);

            if (categories != null)
            {
                var cleanEntry = new CacheEntry<List<string>> { Data = categories, LastUpdated = now };
                _categoryMemoryCache[sanitizedKey] = cleanEntry;
                _ = SaveToDiskBackgroundAsync(sanitizedKey, cleanEntry, NewsContextContainer.Default.CacheEntryListString);
                return categories;
            }

            if (_categoryMemoryCache.TryGetValue(sanitizedKey, out var expiredFallback))
            {
                return expiredFallback.Data;
            }

            return null;
        }
        finally
        {
            stripeLock.Release();
        }
    }

    public void InvalidateKey(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey)) return;
        var sanitizedKey = SanitizeKey(rawKey);

        _articleMemoryCache.TryRemove(sanitizedKey, out _);
        _categoryMemoryCache.TryRemove(sanitizedKey, out _);
        _diskLoadedKeys.TryRemove(sanitizedKey, out _);

        var filePath = Path.Combine(_cacheDirectory, $"{sanitizedKey}.json");
        _ = Task.Run(() => { try { if (File.Exists(filePath)) File.Delete(filePath); } catch { } });
    }

    public async Task ClearAllAsync()
    {
        _articleMemoryCache.Clear();
        _categoryMemoryCache.Clear();
        _diskLoadedKeys.Clear();

        foreach (var stripeLock in _locks) await stripeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                foreach (var file in Directory.GetFiles(_cacheDirectory, "*.json"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        finally
        {
            foreach (var stripeLock in _locks) stripeLock.Release();
        }
    }

    private SemaphoreSlim GetStripeLock(string sanitizedKey)
    {
        uint hash = (uint)sanitizedKey.GetHashCode(StringComparison.Ordinal);
        return _locks[hash % StripeCount];
    }

    private static string SanitizeKey(string rawInput)
    {
        string standard = rawInput.ToLowerInvariant().Trim();
        return Regex.Replace(standard, @"[^a-z0-9_]", "_");
    }

    private async Task<T?> LoadFromDiskAsync<T>(string sanitizedKey, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) where T : class
    {
        var path = Path.Combine(_cacheDirectory, $"{sanitizedKey}.json");
        if (!File.Exists(path)) return null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            return await JsonSerializer.DeserializeAsync(stream, typeInfo).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Disk corruption caught for node path key: '{Key}'", sanitizedKey);
            try { File.Delete(path); } catch { }
            return null;
        }
    }

    private async Task SaveToDiskBackgroundAsync<T>(string sanitizedKey, T entry, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) where T : class
    {
        var stripeLock = GetStripeLock(sanitizedKey);
        await stripeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var path = Path.Combine(_cacheDirectory, $"{sanitizedKey}.json");
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await JsonSerializer.SerializeAsync(stream, entry, typeInfo).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background file saving aborted for node key: '{Key}'", sanitizedKey);
        }
        finally
        {
            stripeLock.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        foreach (var stripeLock in _locks) stripeLock.Dispose();
        _isDisposed = true;
    }
}

public sealed class CacheEntry<T>
{
    [JsonPropertyName("d")] public T? Data { get; init; }
    [JsonPropertyName("u")] public DateTime LastUpdated { get; init; }
    public bool IsExpired(TimeSpan ttl, DateTime referenceTime) => referenceTime - LastUpdated > ttl;
}