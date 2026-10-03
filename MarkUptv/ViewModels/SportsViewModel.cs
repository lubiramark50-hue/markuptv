using CommunityToolkit.Mvvm.Input;
using MarkUptv.Services;
using Microsoft.Maui.Controls;

namespace MarkUptv.ViewModels;

public partial class SportsViewModel : BaseChannelViewModel
{
    protected override string Category => "sports";
    public override string NowPlayingText => SelectedChannel != null ? $"⚽ {SelectedChannel.Name}" : "Select a sports channel";

    public SportsViewModel(
        TvApiService tvApi,
        RecentlyWatchedService recentlyWatched,
        PaymentService paymentService,
        ChannelCacheService cacheService)
        : base(tvApi, recentlyWatched, paymentService, cacheService) { }

    [RelayCommand]
    private static void OpenFlyout()
    {
        if (Shell.Current is not null)
        {
            Shell.Current.FlyoutIsPresented = true;
        }
    }
}