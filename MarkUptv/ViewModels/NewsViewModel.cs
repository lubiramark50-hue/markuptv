using CommunityToolkit.Mvvm.Input;
using MarkUptv.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;

namespace MarkUptv.ViewModels;

public partial class NewsViewModel : BaseChannelViewModel
{
    private readonly ILogger<NewsViewModel> _logger;
    private bool _initialized;

    protected override string Category => "news";

    public override string NowPlayingText =>
        SelectedChannel is null
            ? "Select a live news channel"
            : $"📰  {SelectedChannel.Name}";

    public NewsViewModel(
        TvApiService tvApi,
        RecentlyWatchedService recentlyWatched,
        PaymentService paymentService,
        ChannelCacheService cacheService,
        ILogger<NewsViewModel> logger)
        : base(
            tvApi,
            recentlyWatched,
            paymentService,
            cacheService)
    {
        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task InitializeAsync()
    {
        if (_initialized &&
            Channels.Count > 0)
        {
            return;
        }

        try
        {
            _logger.LogInformation(
                "Initializing the news channel page.");

            await LoadChannelsCommand.ExecuteAsync(null);

            _initialized =
                Channels.Count > 0;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "News-page initialization failed.");
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RefreshAsync()
    {
        try
        {
            await LoadChannelsCommand.ExecuteAsync(null);

            _initialized =
                Channels.Count > 0;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "News-channel refresh failed.");
        }
    }

    [RelayCommand]
    private static void OpenFlyout()
    {
        if (Shell.Current is not null)
        {
            Shell.Current.FlyoutIsPresented = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            InitializeCommand.Cancel();
            RefreshCommand.Cancel();

            _logger.LogInformation(
                "NewsViewModel disposed.");
        }

        base.Dispose(disposing);
    }
}