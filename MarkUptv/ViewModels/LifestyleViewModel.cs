using MarkUptv.Services;

namespace MarkUptv.ViewModels;

public partial class LifestyleViewModel : BaseChannelViewModel
{
    // A dedicated lifestyle pool exists in the channel table, so there is no
    // reason to browse the much larger, unrelated entertainment pool instead.
    protected override string Category => "lifestyle";
    public override string NowPlayingText => SelectedChannel != null ? $"🌿 {SelectedChannel.Name}" : "Select a lifestyle channel";

    public LifestyleViewModel(TvApiService tvApi, RecentlyWatchedService recentlyWatched, PaymentService paymentService, ChannelCacheService cache)
        : base(tvApi, recentlyWatched, paymentService, cache) { }
}