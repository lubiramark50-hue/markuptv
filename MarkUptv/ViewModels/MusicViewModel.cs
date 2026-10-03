using CommunityToolkit.Mvvm.Input;
using MarkUptv.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;

namespace MarkUptv.ViewModels;

public sealed partial class MusicViewModel : BaseChannelViewModel
{
    protected override string Category => "music";

    public override string NowPlayingText =>
        SelectedChannel is null
            ? "Select a music channel to begin"
            : $"🎵 Streaming: {SelectedChannel.Name}";

    public MusicViewModel(
        TvApiService tvApi,
        RecentlyWatchedService recentlyWatched,
        PaymentService paymentService,
        ChannelCacheService cacheService,
        ILogger<MusicViewModel> logger)
        : base(
            tvApi,
            recentlyWatched,
            paymentService,
            cacheService)
    {
        ArgumentNullException.ThrowIfNull(logger);

        logger.LogInformation(
            "MusicViewModel initialized.");
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
}