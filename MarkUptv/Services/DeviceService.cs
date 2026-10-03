using System;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using Microsoft.Extensions.Logging;

namespace MarkUptv.Services;

public interface IDeviceService
{
    string GetDeviceId();
}

/// <summary>
/// Securely resolves and caches a unique hardware tracking signature for the device.
/// Standardized for production anti-fraud and trial-tracking constraints.
/// </summary>
public class DeviceService : IDeviceService
{
    private string? _cachedDeviceId;

    // 🛡️ SOLIDIFIED: Thread synchronization lock prevents race conditions during app startup
    private readonly object _deviceLock = new();

    // 🛡️ SOLIDIFIED: Replaced Debug.WriteLine with enterprise telemetry
    private readonly ILogger<DeviceService> _logger;

    private const string FallbackKey = "device_unique_id_fallback";

    public DeviceService(ILogger<DeviceService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns a highly consistent, unique device identifier across all platforms.
    /// Thread-safe and resistant to known hardware cloning bugs.
    /// </summary>
    public string GetDeviceId()
    {
        // Fast-path: Return if already cached
        if (!string.IsNullOrEmpty(_cachedDeviceId))
        {
            return _cachedDeviceId;
        }

        // 🛡️ SOLIDIFIED: Lock prevents multiple services from generating overlapping IDs concurrently
        lock (_deviceLock)
        {
            // Double-check locking pattern
            if (!string.IsNullOrEmpty(_cachedDeviceId))
            {
                return _cachedDeviceId;
            }

            string? deviceId = null;

#if ANDROID
            try
            {
                var context = Platform.AppContext;
                if (context?.ContentResolver != null)
                {
                    deviceId = Android.Provider.Settings.Secure.GetString(
                        context.ContentResolver,
                        Android.Provider.Settings.Secure.AndroidId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve native Android Device ID.");
            }
#elif IOS || MACCATALYST
            try
            {
                var vendorId = UIKit.UIDevice.CurrentDevice.IdentifierForVendor;
                deviceId = vendorId?.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve native Apple Vendor ID.");
            }
#elif WINDOWS
            try
            {
                var systemId = Windows.System.Profile.SystemIdentification.GetSystemIdForPublisher();
                if (systemId?.Id != null && systemId.Id.Length > 0)
                {
                    // 🛡️ SOLIDIFIED: 'using' statement guarantees WinRT memory buffers are safely disposed
                    using var dataReader = Windows.Storage.Streams.DataReader.FromBuffer(systemId.Id);
                    byte[] bytes = new byte[systemId.Id.Length];
                    dataReader.ReadBytes(bytes);
                    
                    deviceId = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve native Windows Hardware ID.");
            }
#endif

            // 🛡️ SOLIDIFIED: Anti-Fraud Check for known bad or cloned hardware IDs
            if (IsInvalidHardwareId(deviceId))
            {
                _logger.LogWarning("Hardware returned a compromised or generic ID '{DeviceId}'. Forcing secure fallback generation.", deviceId);
                deviceId = null;
            }

            // Fallback: Generate and store a secure GUID if hardware APIs fail or return cloned IDs
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                deviceId = Preferences.Default.Get(FallbackKey, string.Empty);

                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    deviceId = Guid.NewGuid().ToString("N");
                    Preferences.Default.Set(FallbackKey, deviceId);
                    _logger.LogInformation("Generated and stored new secure fallback device signature.");
                }
            }

            _cachedDeviceId = deviceId;
            _logger.LogInformation("Device signature resolved and locked into memory cache.");

            return _cachedDeviceId!;
        }
    }

    /// <summary>
    /// 🛡️ SOLIDIFIED: Identifies notorious hardware bugs where thousands of devices return the exact same string.
    /// </summary>
    private bool IsInvalidHardwareId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return true;

        // Known bug: All Android emulators and many rooted Android 8/9 devices return this exact string.
        if (id == "9774d56d682e549c" || id == "0000000000000000")
        {
            return true;
        }

        return false;
    }
}
