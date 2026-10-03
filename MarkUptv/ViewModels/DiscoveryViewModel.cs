using MarkUptv.Services;

namespace MarkUptv.ViewModels;

public partial class DiscoveryViewModel : BaseChannelViewModel
{
    // "general" is the undifferentiated dumping ground (2,400+ channels);
    // loading it here made a screen called Discovery open on an arbitrary,
    // alphabetically-sorted slice of every channel the platform carries.
    // Documentary is the pool that actually matches the promise of the page.
    protected override string Category => "documentary";
    public override string NowPlayingText => SelectedChannel != null ? $"🔍 {SelectedChannel.Name}" : "Select a discovery channel";

    public DiscoveryViewModel(TvApiService tvApi, RecentlyWatchedService recentlyWatched, PaymentService paymentService, ChannelCacheService cache)
        : base(tvApi, recentlyWatched, paymentService, cache) { }
}