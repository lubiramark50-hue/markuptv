using MarkUptv.Services;

namespace MarkUptv.ViewModels;

public partial class LocalViewModel : BaseChannelViewModel
{
    protected override string Category => "general";
    public override string NowPlayingText => SelectedChannel != null ? $"📺 {SelectedChannel.Name}" : "Select a local channel";
    public string CountryName => "Local TV";

    public LocalViewModel(TvApiService tvApi, RecentlyWatchedService recentlyWatched, PaymentService paymentService, ChannelCacheService cache)
        : base(tvApi, recentlyWatched, paymentService, cache) { }
}