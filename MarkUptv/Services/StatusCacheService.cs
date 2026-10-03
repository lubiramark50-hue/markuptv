using MarkUptv.Models;
using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace MarkUptv.Services;

/// <summary>
/// A thread-safe, high-performance in-memory cache for managing device authorization status.
/// </summary>
public class StatusCacheService : IDisposable
{
    private DeviceStatusResponse? _cachedStatus;
    private DateTime _cacheTime;

    // 🛡️ SOLIDIFIED: Thread synchronization primitive to block read/write race conditions
    private readonly ReaderWriterLockSlim _cacheLock = new();

    // 🛡️ SOLIDIFIED: Added telemetry tracking to monitor cache hits vs network drops
    private readonly ILogger<StatusCacheService> _logger;

    // 🛡️ SOLIDIFIED: Extracted duration to a clear constant
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

    public StatusCacheService(ILogger<StatusCacheService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retrieves the status if it exists and has not expired.
    /// </summary>
    public DeviceStatusResponse? GetCachedStatus()
    {
        // 🛡️ SOLIDIFIED: ReadLock allows multiple threads to read safely, but blocks if a write is occurring
        _cacheLock.EnterReadLock();
        try
        {
            if (_cachedStatus != null && DateTime.UtcNow - _cacheTime < _cacheDuration)
            {
                _logger.LogDebug("StatusCacheService: Cache HIT.");
                return _cachedStatus;
            }

            return null;
        }
        finally
        {
            _cacheLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Modern C# TryGet pattern for cleaner implementation in calling services.
    /// </summary>
    public bool TryGet(out DeviceStatusResponse? cachedStatus)
    {
        cachedStatus = GetCachedStatus();
        return cachedStatus != null;
    }

    /// <summary>
    /// Safely writes the new status to memory, resetting the expiration timer.
    /// </summary>
    public void SetCachedStatus(DeviceStatusResponse? status)
    {
        // 🛡️ SOLIDIFIED: Defensive null check prevents accidentally caching a broken state
        if (status == null)
        {
            _logger.LogWarning("StatusCacheService: Attempted to cache a null status. Triggering invalidation instead.");
            Invalidate();
            return;
        }

        // 🛡️ SOLIDIFIED: WriteLock blocks ALL reads and other writes until this update completes
        _cacheLock.EnterWriteLock();
        try
        {
            _cachedStatus = status;
            _cacheTime = DateTime.UtcNow;
            _logger.LogInformation("StatusCacheService: Cache UPDATED securely. Valid for {Minutes} minutes.", _cacheDuration.TotalMinutes);
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Instantly wipes the cache. Required immediately after a successful payment to force a live check.
    /// </summary>
    public void Invalidate()
    {
        _cacheLock.EnterWriteLock();
        try
        {
            _cachedStatus = null;
            _cacheTime = DateTime.MinValue; // 🛡️ SOLIDIFIED: Reset the clock to guarantee immediate expiration
            _logger.LogInformation("StatusCacheService: Memory cache explicitly INVALIDATED.");
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 🛡️ SOLIDIFIED: Standard IDisposable implementation to free memory from the Lock mechanism when app closes
    /// </summary>
    public void Dispose()
    {
        _cacheLock?.Dispose();
        GC.SuppressFinalize(this);
    }
}