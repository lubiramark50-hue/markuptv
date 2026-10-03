using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using MarkUptv.Models;

namespace MarkUptv.Services;

/// <summary>
/// Enterprise-grade Caching Service.
/// Features: Memory + Disk persistence, Cache Stampede prevention (per-key locking), 
/// Time-To-Live (TTL) validation, and graceful offline degradation.
/// </summary>
public class ChannelCacheService
{
    private readonly TvApiService _tvApi;
    private readonly ILogger<ChannelCacheService> _logger;

    // Defines how long cached data remains valid before forcing a background refresh
    private readonly TimeSpan _cacheTtl = TimeSpan.FromHours(12);

    // Memory Cache
    private readonly ConcurrentDictionary<string, CacheEntry<CategoryResponse>> _memoryCache = new();

    // Concurrency Locks: Prevents "Cache Stampede" (multiple simultaneous API calls for the same category)
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _categoryLocks = new();

    // Global lock for disk I/O to prevent file corruption
    private readonly SemaphoreSlim _diskIoLock = new(1, 1);

    private readonly string _cacheFilePath;

    // The one-shot startup load, awaited by the stale-while-revalidate path so a
    // cold start can still paint the last known line-up.
    private Task _diskLoadTask = Task.CompletedTask;

    public ChannelCacheService(TvApiService tvApi, ILogger<ChannelCacheService> logger)
    {
        _tvApi = tvApi ?? throw new ArgumentNullException(nameof(tvApi));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Set up secure local storage path for persistence
        _cacheFilePath = Path.Combine(FileSystem.AppDataDirectory, "markup_channels_cache.json");

        // Fire-and-forget background load from disk into memory on startup.
        // Task.Run matters: this constructor is reached from the UI thread, and
        // without it the await inside would resume on the UI thread and parse
        // the multi-megabyte cache there — freezing the first frame.
        _diskLoadTask = Task.Run(LoadCacheFromDiskAsync);
    }

    /// <summary>
    /// Preloads multiple categories asynchronously, processing them in parallel safely.
    /// </summary>
    public async Task PreloadCategoriesAsync(IEnumerable<string> categories)
    {
        if (categories == null || !categories.Any()) return;

        _logger.LogInformation("Starting parallel background preload for {Count} categories.", categories.Count());

        // Process all categories in parallel, but safely restricted by our per-key locks
        var tasks = categories.Select(cat => GetChannelsAsync(cat, useCache: true));

        await Task.WhenAll(tasks);

        _logger.LogInformation("Background preload complete.");
    }

    /// <summary>
    /// Core data fetcher. Tries Memory -> Disk -> API -> Fallback to expired Memory/Disk.
    /// </summary>
    public async Task<CategoryResponse?> GetChannelsAsync(string category, bool useCache = true)
    {
        if (string.IsNullOrWhiteSpace(category)) return null;

        category = category.ToLowerInvariant().Trim();

        // 1. FAST PATH: Check memory cache first
        if (useCache && _memoryCache.TryGetValue(category, out var memEntry))
        {
            if (!memEntry.IsExpired(_cacheTtl))
            {
                _logger.LogDebug("Cache HIT (Memory) for category: {Category}", category);
                return memEntry.Data;
            }
        }

        // 2. STALE-WHILE-REVALIDATE: never block the screen on a slow backend.
        //    A cold category fetch can take tens of seconds (the server resolves
        //    stream candidates on demand), which used to leave the page sitting on
        //    a spinner - or worse, on "nothing here" - while the request was in
        //    flight. If anything usable is already cached, hand it over now and
        //    refresh in the background.
        if (useCache)
        {
            CategoryResponse? cached = await TryGetStaleResponseAsync(category);

            if (cached?.Channels is { Count: > 0 })
            {
                _logger.LogDebug(
                    "Serving cached line-up for {Category} while refreshing.",
                    category);

                _ = RefreshCategoryInBackgroundAsync(category);

                return cached;
            }
        }

        // 3. CONCURRENCY CONTROL: Lock specifically for this category to prevent stampedes
        var categoryLock = _categoryLocks.GetOrAdd(category, _ => new SemaphoreSlim(1, 1));
        await categoryLock.WaitAsync();

        try
        {
            // Double-check memory cache inside the lock in case another thread just finished fetching it
            if (useCache && _memoryCache.TryGetValue(category, out var lockedMemEntry) && !lockedMemEntry.IsExpired(_cacheTtl))
            {
                return lockedMemEntry.Data;
            }

            _logger.LogInformation("Cache MISS or EXPIRED for {Category}. Fetching from API...", category);

            // 3. API FETCH
            CategoryResponse? apiResponse = null;
            try
            {
                apiResponse = await _tvApi.GetChannelsAsync(category);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API Call failed for category: {Category}", category);
            }

            // 4. PROCESS RESULTS & FALLBACKS
            if (apiResponse != null && apiResponse.Channels != null && apiResponse.Channels.Any())
            {
                // Success: Update Cache
                var newEntry = new CacheEntry<CategoryResponse>
                {
                    Data = apiResponse,
                    LastUpdated = DateTime.UtcNow
                };

                _memoryCache[category] = newEntry;

                // Fire and forget save to disk so we don't block the UI thread waiting for I/O
                _ = SaveCacheToDiskAsync();

                return apiResponse;
            }
            else
            {
                // Fallback: API failed or returned empty. If we have EXPIRED cached data, return that instead of nothing.
                if (_memoryCache.TryGetValue(category, out var expiredEntry))
                {
                    _logger.LogWarning("Using EXPIRED fallback cache for {Category} due to API failure.", category);
                    return expiredEntry.Data;
                }

                return null;
            }
        }
        finally
        {
            categoryLock.Release();
        }
    }

    /// <summary>
    /// Returns whatever we already hold for a category - even an expired entry -
    /// after giving the one-shot disk load a chance to finish. Used to paint the
    /// previous line-up while a fresh fetch is on its way.
    /// </summary>
    private async Task<CategoryResponse?> TryGetStaleResponseAsync(string category)
    {
        if (_memoryCache.TryGetValue(category, out var memoryEntry))
        {
            return memoryEntry.Data;
        }

        try
        {
            await _diskLoadTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Disk cache was not ready while serving {Category}.",
                category);
        }

        return _memoryCache.TryGetValue(category, out var diskEntry)
            ? diskEntry.Data
            : null;
    }

    /// <summary>
    /// Fetches a category without blocking the caller and replaces the cached
    /// entry when something better arrives.
    /// </summary>
    private Task RefreshCategoryInBackgroundAsync(string category)
    {
        return Task.Run(async () =>
        {
            try
            {
                CategoryResponse? fresh = await _tvApi.GetChannelsAsync(category);

                if (fresh?.Channels is not { Count: > 0 })
                {
                    return;
                }

                _memoryCache[category] = new CacheEntry<CategoryResponse>
                {
                    Data = fresh,
                    LastUpdated = DateTime.UtcNow
                };

                _ = SaveCacheToDiskAsync();

                _logger.LogInformation(
                    "Background refresh completed for {Category} with {Count} channels.",
                    category,
                    fresh.Channels.Count);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(
                    exception,
                    "Background refresh failed for {Category}.",
                    category);
            }
        });
    }

    /// <summary>
    /// Gets synchronous access to available data instantly (useful for rapid UI binding).
    /// Returns null if not cached, requiring an async call.
    /// </summary>
    public List<TvChannel>? GetCachedChannels(string category)
    {
        category = category?.ToLowerInvariant().Trim() ?? string.Empty;

        if (_memoryCache.TryGetValue(category, out var entry))
        {
            return entry.Data?.Channels;
        }
        return null;
    }

    /// <summary>
    /// Purges cache for a specific category and deletes it from disk.
    /// </summary>
    public void InvalidateCategory(string category)
    {
        category = category?.ToLowerInvariant().Trim() ?? string.Empty;

        if (_memoryCache.TryRemove(category, out _))
        {
            _ = SaveCacheToDiskAsync();
        }
    }

    /// <summary>
    /// Nuclear option: Wipes all memory and disk caches completely.
    /// </summary>
    public async Task ClearAllAsync()
    {
        _memoryCache.Clear();

        await _diskIoLock.WaitAsync();
        try
        {
            if (File.Exists(_cacheFilePath))
            {
                File.Delete(_cacheFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete disk cache file.");
        }
        finally
        {
            _diskIoLock.Release();
        }
    }

    // ========================================================================
    // PRIVATE DISK I/O SUB-SYSTEM
    // ========================================================================

    private async Task SaveCacheToDiskAsync()
    {
        await _diskIoLock.WaitAsync();
        try
        {
            // Snapshot the dictionary to safely serialize it
            var snapshot = _memoryCache.ToDictionary(k => k.Key, v => v.Value);

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = false });
            await File.WriteAllTextAsync(_cacheFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize and save cache to disk.");
        }
        finally
        {
            _diskIoLock.Release();
        }
    }

    private async Task LoadCacheFromDiskAsync()
    {
        await _diskIoLock.WaitAsync();
        try
        {
            if (!File.Exists(_cacheFilePath)) return;

            var json = await File.ReadAllTextAsync(_cacheFilePath);
            if (string.IsNullOrWhiteSpace(json)) return;

            var diskCache = JsonSerializer.Deserialize<Dictionary<string, CacheEntry<CategoryResponse>>>(json);

            if (diskCache != null)
            {
                foreach (var kvp in diskCache)
                {
                    _memoryCache[kvp.Key] = kvp.Value;
                }
                _logger.LogInformation("Successfully restored {Count} categories from disk cache.", diskCache.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load cache from disk. Starting fresh.");
            // If the file is corrupted, wipe it.
            if (File.Exists(_cacheFilePath)) File.Delete(_cacheFilePath);
        }
        finally
        {
            _diskIoLock.Release();
        }
    }

    // ========================================================================
    // DATA STRUCTURES
    // ========================================================================

    /// <summary>
    /// Wrapper class to track the age of cached data.
    /// </summary>
    private class CacheEntry<T>
    {
        public T? Data { get; set; }
        public DateTime LastUpdated { get; set; }

        public bool IsExpired(TimeSpan ttl)
        {
            return DateTime.UtcNow - LastUpdated > ttl;
        }
    }
}