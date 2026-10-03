using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;

namespace MarkUptv.Services;

/// <summary>
/// An active, event-driven network monitoring service.
/// Safely tracks internet status and connection profiles for a streaming-heavy environment.
/// </summary>
public class ConnectivityService : IDisposable
{
    private readonly IConnectivity _connectivity;
    private readonly ILogger<ConnectivityService> _logger;

    // 🛡️ SOLIDIFIED: Live event stream so the UI can instantly show an "Offline" banner when the network drops
    public event EventHandler<bool>? NetworkStateChanged;

    public ConnectivityService(IConnectivity connectivity, ILogger<ConnectivityService> logger)
    {
        // 🛡️ SOLIDIFIED: Dependency Injection instead of static `Connectivity.Current` allows for unit testing
        _connectivity = connectivity ?? throw new ArgumentNullException(nameof(connectivity));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Hook into the operating system's live network change broadcaster
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    /// <summary>
    /// Checks if the device currently has an active route to the internet.
    /// </summary>
    public bool IsConnected => _connectivity.NetworkAccess == NetworkAccess.Internet;

    /// <summary>
    /// 🛡️ SOLIDIFIED: Streaming optimization. Checks if the user is on Mobile Data.
    /// You can use this to ask: "You are on Cellular Data. Do you still want to stream this video?"
    /// </summary>
    public bool IsMeteredConnection =>
        _connectivity.ConnectionProfiles.Contains(ConnectionProfile.Cellular);

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        bool hasInternet = e.NetworkAccess == NetworkAccess.Internet;

        if (hasInternet)
        {
            // Logs exactly what kind of connection was restored (WiFi, Ethernet, Cellular)
            _logger.LogInformation("Network Restored: Handshake established via {Profiles}", string.Join(", ", e.ConnectionProfiles));
        }
        else
        {
            _logger.LogWarning("Network Dropped: Device lost routing access to the internet.");
        }

        // Fire the event to notify any ViewModels that are listening
        NetworkStateChanged?.Invoke(this, hasInternet);
    }

    /// <summary>
    /// 🛡️ SOLIDIFIED: Unhooks the OS-level event to prevent memory leaks when the app shuts down.
    /// </summary>
    public void Dispose()
    {
        if (_connectivity != null)
        {
            _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        }
        GC.SuppressFinalize(this);
    }
}