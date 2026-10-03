using MarkUptv.Services;

namespace MarkUptv.ViewModels;

public partial class GospelViewModel : BaseChannelViewModel
{
    protected override string Category => "religious";
    public override string NowPlayingText => SelectedChannel != null ? $"🙏 {SelectedChannel.Name}" : "Select a gospel channel";

    public GospelViewModel(TvApiService tvApi, RecentlyWatchedService recentlyWatched, PaymentService paymentService, ChannelCacheService cache)
        : base(tvApi, recentlyWatched, paymentService, cache) { }
}