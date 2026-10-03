using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Core.Primitives;
using CommunityToolkit.Maui.Views;
using MarkUptv.Helpers;
using MarkUptv.Models;
using MarkUptv.ViewModels;

namespace MarkUptv.Pages;

/// <summary>
/// Live player page.
///
/// One instance is reused for the whole app lifetime, so navigating to the
/// player does not build a new page and a new media pipeline on every visit.
/// The instance is created after the first window exists (see AppShell), and
/// event wiring is attached on appear and torn down on disappear.
/// </summary>
public partial class PlayerPage : ContentPage
{
    // Keep this below the 10-second "video must start" target so that a dead
    // primary source still fails over to a working backup inside the budget.
    private static readonly TimeSpan StartWatchdogTimeout = TimeSpan.FromSeconds(7);

    private readonly PlayerViewModel _viewModel;
    private IDispatcherTimer? _watchdog;
    private bool _attached;

    public PlayerPage(PlayerViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));

        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        Attach();

        try
        {
            _viewModel.PrepareForNewSession();
            await _viewModel.StartAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"[PlayerPage] start failed: {exception}");
        }
    }

    protected override void OnDisappearing()
    {
        Detach();
        base.OnDisappearing();
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    private void Attach()
    {
        if (_attached)
        {
            return;
        }

        _viewModel.PlayRequested += OnPlayRequested;
        _viewModel.BrowseRequested += OnBrowseRequested;

        ChannelPlayer.MediaOpened += OnMediaOpened;
        ChannelPlayer.MediaFailed += OnMediaFailed;
        ChannelPlayer.StateChanged += OnStateChanged;

        PlaybackCoordinator.Register(ChannelPlayer);

        _attached = true;
    }

    private void Detach()
    {
        if (!_attached)
        {
            return;
        }

        StopWatchdog();

        try
        {
            _viewModel.PlayRequested -= OnPlayRequested;
            _viewModel.BrowseRequested -= OnBrowseRequested;

            ChannelPlayer.MediaOpened -= OnMediaOpened;
            ChannelPlayer.MediaFailed -= OnMediaFailed;
            ChannelPlayer.StateChanged -= OnStateChanged;

            ChannelPlayer.Stop();
            ChannelPlayer.Source = null;
            OfficialView.Source = null;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"[PlayerPage] detach failed: {exception}");
        }

        PlaybackCoordinator.Release(ChannelPlayer);

        _attached = false;
    }

    // ------------------------------------------------------------------
    // View-model requests
    // ------------------------------------------------------------------

    private void OnPlayRequested(object? sender, StreamCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.ResolvedUrl))
        {
            _viewModel.ReportFailed("source had no URL");
            return;
        }

        StartWatchdog();

        try
        {
            ChannelPlayer.Source = candidate.ResolvedUrl;
        }
        catch (Exception exception)
        {
            StopWatchdog();
            _viewModel.ReportFailed(exception.Message);
        }
    }

    private void OnBrowseRequested(object? sender, StreamCandidate candidate)
    {
        StopWatchdog();

        if (string.IsNullOrWhiteSpace(candidate.ResolvedUrl))
        {
            return;
        }

        try
        {
            // YouTube embeds get a clean full-screen iframe instead of the
            // messy mobile web page with cookie banners and desktop chrome.
            if (Helpers.YouTubeEmbedHelper.IsYouTubeEmbedUrl(candidate.ResolvedUrl))
            {
                OfficialView.Source =
                    Helpers.YouTubeEmbedHelper.BuildFromEmbedUrl(candidate.ResolvedUrl);
            }
            else if (Helpers.YouTubeEmbedHelper.IsYouTubeUrl(candidate.ResolvedUrl) &&
                     candidate.Mode == "embed")
            {
                // Generic YouTube URL in embed mode — wrap it.
                OfficialView.Source =
                    Helpers.YouTubeEmbedHelper.BuildFromEmbedUrl(candidate.ResolvedUrl);
            }
            else
            {
                OfficialView.Source = candidate.ResolvedUrl;
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"[PlayerPage] browse failed: {exception}");
        }
    }

    // ------------------------------------------------------------------
    // Media surface callbacks
    // ------------------------------------------------------------------

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        StopWatchdog();
        _viewModel.ReportOpened();
    }

    private void OnMediaFailed(object? sender, MediaFailedEventArgs e)
    {
        StopWatchdog();
        _viewModel.ReportFailed(
            string.IsNullOrWhiteSpace(e.ErrorMessage) ? "playback error" : e.ErrorMessage);
    }

    private void OnStateChanged(object? sender, MediaStateChangedEventArgs e)
    {
        if (e.NewState == MediaElementState.Buffering)
        {
            _viewModel.ReportBuffering();
        }
    }

    /// <summary>Tap on a tile in the sources strip switches to that source.</summary>
    private void OnSourceTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject bindable &&
            bindable.BindingContext is StreamCandidate candidate)
        {
            _viewModel.PlayCandidateCommand.Execute(candidate);
        }
    }

    // ------------------------------------------------------------------
    // Stall watchdog: if nothing paints in time, fail over.
    // ------------------------------------------------------------------

    private void StartWatchdog()
    {
        StopWatchdog();

        _watchdog = Dispatcher.CreateTimer();
        _watchdog.Interval = StartWatchdogTimeout;
        _watchdog.IsRepeating = false;
        _watchdog.Tick += OnWatchdogTick;
        _watchdog.Start();
    }

    private void StopWatchdog()
    {
        if (_watchdog is null)
        {
            return;
        }

        _watchdog.Stop();
        _watchdog.Tick -= OnWatchdogTick;
        _watchdog = null;
    }

    private void OnWatchdogTick(object? sender, EventArgs e)
    {
        StopWatchdog();
        _viewModel.ReportFailed("timed out waiting for video");
    }
}
