using MarkUptv.Services;

namespace MarkUptv.ViewModels;

public partial class FashionViewModel : BaseChannelViewModel
{
    // The lifestyle pool is where the channel table actually keeps fashion
    // broadcasters (FashionTV, Code Fashion, FashionBox and friends). Loading
    // "entertainment" meant this screen never showed a single fashion channel.
    protected override string Category => "lifestyle";
    public override string NowPlayingText => SelectedChannel != null ? $"👗 {SelectedChannel.Name}" : "Select a fashion channel";

    public FashionViewModel(TvApiService tvApi, RecentlyWatchedService recentlyWatched, PaymentService paymentService, ChannelCacheService cache)
        : base(tvApi, recentlyWatched, paymentService, cache) { }
}