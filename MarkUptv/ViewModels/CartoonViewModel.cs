using CommunityToolkit.Mvvm.Input;
using MarkUptv.Services;
using Microsoft.Extensions.Logging;

namespace MarkUptv.ViewModels;

/// <summary>
/// ViewModel for the dedicated cartoon streaming page.
/// Search and group filtering are handled by BaseChannelViewModel.
/// </summary>
public partial class CartoonViewModel : BaseChannelViewModel
{
    private readonly ILogger<CartoonViewModel> _logger;
    private bool _disposed;

    protected override string Category => "cartoons";

    public override string NowPlayingText
    {
        get
        {
            if (SelectedChannel is null)
            {
                return "Choose a cartoon and start the adventure!";
            }

            if (!string.IsNullOrWhiteSpace(
                    SelectedChannel.CurrentProgrammeTitle))
            {
                return
                    $"{SelectedChannel.Name}  •  " +
                    SelectedChannel.CurrentProgrammeTitle;
            }

            return $"Now playing: {SelectedChannel.Name}";
        }
    }

    public CartoonViewModel(
        TvApiService tvApi,
        RecentlyWatchedService recentlyWatched,
        PaymentService paymentService,
        ChannelCacheService cacheService,
        ILogger<CartoonViewModel> logger)
        : base(
            tvApi,
            recentlyWatched,
            paymentService,
            cacheService)
    {
        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation(
            "CartoonViewModel initialized.");
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task InitializePageAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (IsLoading)
        {
            return;
        }

        if (Channels.Count > 0)
        {
            ApplyFilter();
            return;
        }

        try
        {
            _logger.LogInformation(
                "Loading cartoon channels.");

            await LoadChannelsAsync(
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug(
                "Cartoon channel loading was cancelled.");
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Cartoon page initialization failed.");
        }
    }

    public void OnPageDisappearing()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogDebug(
            "Cartoon page is disappearing.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(CartoonViewModel));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _disposed = true;

            _logger.LogInformation(
                "CartoonViewModel resources released.");
        }

        base.Dispose(disposing);
    }
}