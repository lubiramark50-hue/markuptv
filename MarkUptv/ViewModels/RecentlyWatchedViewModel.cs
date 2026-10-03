using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Models;
using MarkUptv.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MarkUptv.ViewModels;

public partial class RecentlyWatchedViewModel : ObservableObject, IDisposable
{
    private readonly RecentlyWatchedService _recentlyWatchedService;
    private readonly ILogger<RecentlyWatchedViewModel> _logger;
    private bool _isDisposed;

    [ObservableProperty] private string _streamUrl = string.Empty;
    [ObservableProperty] private string _currentChannelName = string.Empty;
    [ObservableProperty] private string _currentChannelGroup = string.Empty;
    [ObservableProperty] private string _currentChannelLogo = string.Empty;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isBuffering;
    [ObservableProperty] private bool _isPlayerVisible;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private TvChannel? _currentChannel;

    // --- FIXES FOR THE XAML BINDING WARNINGS ---
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ObservableCollection<string> _groups = new();

    public ObservableCollection<TvChannel> RecentlyWatched { get; } = new();

    public RecentlyWatchedViewModel(RecentlyWatchedService recentlyWatchedService, ILogger<RecentlyWatchedViewModel> logger)
    {
        _recentlyWatchedService = recentlyWatchedService ?? throw new ArgumentNullException(nameof(recentlyWatchedService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _recentlyWatchedService.RecentlyWatchedChanged += OnRecentlyWatchedChanged;
        LoadRecentlyWatched();

        // Initialize an empty placeholder collection for groups to prevent UI null reference issues
        Groups = new ObservableCollection<string> { "All" };
    }

    /// <summary>
    /// Called when the page receives a navigation parameter (via IQueryAttributable).
    /// </summary>
    public void Initialize(TvChannel channel)
    {
        if (channel == null)
        {
            _logger.LogWarning("Initialize called with null channel.");
            ErrorMessage = "No channel provided.";
            HasError = true;
            IsLoading = false;
            return;
        }

        _logger.LogInformation("Initializing playback for channel: {ChannelName}", channel.Name);

        CurrentChannel = channel;
        CurrentChannelName = channel.Name;
        CurrentChannelGroup = channel.Group ?? string.Empty;
        CurrentChannelLogo = channel.Logo ?? string.Empty;
        StreamUrl = channel.Url;
        HasError = false;
        ErrorMessage = string.Empty;
        IsLoading = false;
        IsPlayerVisible = true;

        // Add to recent history
        _recentlyWatchedService.Add(channel);
    }

    private void OnRecentlyWatchedChanged()
    {
        MainThread.BeginInvokeOnMainThread(LoadRecentlyWatched);
    }

    private void LoadRecentlyWatched()
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(LoadRecentlyWatched);
            return;
        }

        try
        {
            var recent = _recentlyWatchedService.GetRecent();
            RecentlyWatched.Clear();
            if (recent != null)
            {
                // Optimized processing to avoid duplicate rendering calculations
                var uniqueItems = recent.GroupBy(x => x.Url).Select(g => g.First());
                foreach (var ch in uniqueItems)
                {
                    RecentlyWatched.Add(ch);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load recently watched list.");
        }
    }

    [RelayCommand]
    private async Task PlayRecentAsync(TvChannel channel)
    {
        if (channel == null) return;

        // Switch to the selected channel
        _logger.LogInformation("Switching to recently watched channel: {ChannelName}", channel.Name);
        Initialize(channel);

        // Refresh the recently watched list (the service will add this new one)
        LoadRecentlyWatched();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RefreshStreamAsync()
    {
        if (CurrentChannel != null)
        {
            _logger.LogInformation("Manually refreshing stream for {ChannelName}", CurrentChannel.Name);
            // Toggle player visibility to force reload
            IsPlayerVisible = false;
            await Task.Delay(200);

            // Re-set the stream URL to trigger reload
            var url = StreamUrl;
            StreamUrl = string.Empty;
            await Task.Delay(100);
            StreamUrl = url;

            IsPlayerVisible = true;
            HasError = false;
            ErrorMessage = string.Empty;
        }
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        await Shell.Current.GoToAsync("..");
    }

    public void MediaOpened()
    {
        IsLoading = false;
        IsBuffering = false;
        IsPlayerVisible = true;
        HasError = false;
        ErrorMessage = string.Empty;
        _logger.LogInformation("Media opened successfully for {ChannelName}", CurrentChannelName);
    }

    public void MediaFailed()
    {
        IsLoading = false;
        IsBuffering = false;
        IsPlayerVisible = false;
        HasError = true;
        ErrorMessage = "Stream failed to load. Please try another channel.";
        _logger.LogWarning("Media failed for {ChannelName}", CurrentChannelName);
    }

    // --- IMMACULATE MEMORY DISPOSAL ---
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed) return;

        if (disposing)
        {
            if (_recentlyWatchedService != null)
            {
                _recentlyWatchedService.RecentlyWatchedChanged -= OnRecentlyWatchedChanged;
            }
            _logger.LogInformation("RecentlyWatchedViewModel completely released from memory context.");
        }

        _isDisposed = true;
    }
}