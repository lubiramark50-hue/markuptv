using MarkUptv.Services;

namespace MarkUptv.ViewModels;

public partial class WildLifeViewModel : BaseChannelViewModel
{
    protected override string Category => "wildlife";
    public override string NowPlayingText => SelectedChannel != null ? $"🦁 {SelectedChannel.Name}" : "Select a wildlife channel";

    public WildLifeViewModel(
        TvApiService tvApi,
        RecentlyWatchedService recentlyWatched,
        PaymentService paymentService,
        ChannelCacheService cacheService)
        : base(tvApi, recentlyWatched, paymentService, cacheService) { }
}