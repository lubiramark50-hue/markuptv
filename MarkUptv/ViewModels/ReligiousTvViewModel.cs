using MarkUptv.Services;

namespace MarkUptv.ViewModels;

public partial class ReligiousTvViewModel : BaseChannelViewModel
{
    protected override string Category => "religious";
    public override string NowPlayingText => SelectedChannel != null ? $"⛪ {SelectedChannel.Name}" : "Select a religious channel";

    public ReligiousTvViewModel(
        TvApiService tvApi,
        RecentlyWatchedService recentlyWatched,
        PaymentService paymentService,
        ChannelCacheService cacheService)
        : base(tvApi, recentlyWatched, paymentService, cacheService) { }
}