using MarkUptv.Services;

namespace MarkUptv.ViewModels;

public partial class EuropeanSportsViewModel : BaseChannelViewModel
{
    /// <summary>
    /// Backend category that curates every free-to-air channel carrying
    /// European football (by country tag, broadcaster or league brand) instead
    /// of the generic world sports pool this screen used to show.
    /// </summary>
    protected override string Category => "europeanfootball";

    public override string NowPlayingText => SelectedChannel != null ? $"⚽ {SelectedChannel.Name}" : "Select a European football channel";

    public EuropeanSportsViewModel(TvApiService tvApi, RecentlyWatchedService recentlyWatched, PaymentService paymentService, ChannelCacheService cache)
        : base(tvApi, recentlyWatched, paymentService, cache) { }
}