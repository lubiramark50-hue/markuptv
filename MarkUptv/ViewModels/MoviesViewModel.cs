using CommunityToolkit.Mvvm.Input;
using MarkUptv.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;

namespace MarkUptv.ViewModels;

/// <summary>
/// Provides movie-channel configuration while the shared
/// BaseChannelViewModel handles loading, searching, filtering,
/// playback, caching, payments and recently watched channels.
/// </summary>
public sealed partial class MoviesViewModel : BaseChannelViewModel
{
    private readonly ILogger<MoviesViewModel> _logger;

    protected override string Category => "movies";

    public override string NowPlayingText =>
        SelectedChannel is null
            ? "Select a movie channel"
            : $"🎬 {SelectedChannel.Name}";

    public MoviesViewModel(
        TvApiService tvApi,
        RecentlyWatchedService recentlyWatched,
        PaymentService paymentService,
        ChannelCacheService cacheService,
        ILogger<MoviesViewModel> logger)
        : base(
            tvApi ?? throw new ArgumentNullException(nameof(tvApi)),
            recentlyWatched ?? throw new ArgumentNullException(nameof(recentlyWatched)),
            paymentService ?? throw new ArgumentNullException(nameof(paymentService)),
            cacheService ?? throw new ArgumentNullException(nameof(cacheService)))
    {
        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation(
            "MoviesViewModel initialized for category {Category}.",
            Category);
    }

    /// <summary>
    /// Opens the application flyout (hamburger) navigation.
    /// </summary>
    [RelayCommand]
    private static void OpenFlyout()
    {
        if (Shell.Current is not null)
        {
            Shell.Current.FlyoutIsPresented = true;
        }
    }

    protected override void Dispose(
        bool disposing)
    {
        if (disposing)
        {
            _logger.LogInformation(
                "MoviesViewModel resources released.");
        }

        base.Dispose(disposing);
    }
}