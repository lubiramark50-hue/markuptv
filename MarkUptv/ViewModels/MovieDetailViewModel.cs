using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Models;
using MarkUptv.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace MarkUptv.ViewModels;

/// <summary>
/// One film: stream it now, save it for offline watching (inside the app),
/// play the saved copy, read its subtitle tracks, or jump to the source page.
/// </summary>
public partial class MovieDetailViewModel : BaseViewModel, IQueryAttributable
{
    private readonly MovieCatalogService _catalog;
    private readonly MovieDownloadService _downloads;
    private readonly PaymentService _payments;
    private readonly ILogger<MovieDetailViewModel> _logger;

    private CancellationTokenSource? _downloadCts;
    private string _movieId = string.Empty;

    public MovieDetailViewModel(
        MovieCatalogService catalog,
        MovieDownloadService downloads,
        PaymentService payments,
        ILogger<MovieDetailViewModel> logger)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _downloads.ProgressChanged += OnDownloadProgress;
    }

    // ── Day pass ────────────────────────────────────────────────────────

    /// <summary>Server said this device has no pass, so the links were withheld.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionsVisible))]
    private bool _passLocked;

    /// <summary>
    /// Play/save only make sense once the pass exists. Computed in the view
    /// model so the page needs no converter during construction — an early
    /// resource lookup there can leave the whole detail page blank.
    /// </summary>
    public bool ActionsVisible => !PassLocked;

    [ObservableProperty]
    private bool _passActive;

    [ObservableProperty]
    private string _passExpiryText = string.Empty;

    [ObservableProperty]
    private string _passPriceText = "1,000 UGX";

    [ObservableProperty]
    private string _passDurationText = "24 hours";

    [ObservableProperty]
    private string _passEmail = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBuyingPass))]
    private bool _isBuyingPass;

    public bool IsNotBuyingPass => !IsBuyingPass;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPassMessage))]
    private string _passMessage = string.Empty;

    public bool HasPassMessage => PassMessage.Length > 0;

    /// <summary>
    /// Starts a real checkout for the daily pass. The server owns the amount
    /// (the client only names the product), and the gateway handles the money,
    /// so nothing here can be tampered with from the app side.
    /// </summary>
    [RelayCommand]
    private async Task BuyPassAsync()
    {
        if (IsBuyingPass)
        {
            return;
        }

        PassMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(PassEmail) || !PassEmail.Contains('@'))
        {
            PassMessage = "Enter an email address for the receipt.";
            return;
        }

        IsBuyingPass = true;

        try
        {
            PaymentInitiationResult result = await _payments
                .InitiatePaymentAsync(new PaymentDetails
                {
                    Email = PassEmail.Trim(),
                    Product = PaymentDetails.DayPassProduct
                })
                .ConfigureAwait(true);

            if (!result.Success || string.IsNullOrWhiteSpace(result.RedirectUrl))
            {
                PassMessage = result.Error ?? "The payment gateway could not be reached.";
                return;
            }

            // Coming back from the gateway: the callback page fires only after
            // the server has verified the money, so a fresh load then shows the
            // film as playable.
            PaymentWebViewCallbackHolder.SuccessCallback = async () =>
            {
                PassMessage = "Payment received — unlocking…";
                await LoadAsync().ConfigureAwait(true);
            };

            PaymentWebViewCallbackHolder.FailureCallback = () =>
            {
                PassMessage = "That payment did not complete. You have not been charged.";
                return Task.CompletedTask;
            };

            await Shell.Current.GoToAsync(
                $"PaymentWebViewPage?url={Uri.EscapeDataString(result.RedirectUrl)}");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Day pass checkout could not be started.");
            PassMessage = "Checkout could not be started. Please try again.";
        }
        finally
        {
            IsBuyingPass = false;
        }
    }

    // ── State ───────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMovie))]
    private MovieDetail? _movie;

    [ObservableProperty]
    private string _title = "Loading…";

    [ObservableProperty]
    private string _metaLine = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _posterUrl = string.Empty;

    [ObservableProperty]
    private bool _hasPoster;

    /// <summary>Bound straight into the MediaElement.</summary>
    [ObservableProperty]
    private MediaSource? _playerSource;

    [ObservableProperty]
    private bool _isPlayerVisible;

    [ObservableProperty]
    private string _playLabel = "Play now";

    [ObservableProperty]
    private string _downloadLabel = "Save offline";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadBusy))]
    private MovieDownloadState _downloadState = MovieDownloadState.None;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _downloadStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSizeText))]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRuntimeText))]
    private string _runtimeText = string.Empty;

    [ObservableProperty]
    private string _languageText = string.Empty;

    [ObservableProperty]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _isSavedOffline;

    [ObservableProperty]
    private string _subtitleSummary = string.Empty;

    public bool HasMovie => Movie is not null;

    public bool IsDownloadBusy =>
        DownloadState is MovieDownloadState.Downloading or MovieDownloadState.Queued;

    public bool HasErrorText => ErrorText.Length > 0;

    /// <summary>Badges disappear rather than showing an empty pill.</summary>
    public bool HasSizeText => SizeText.Length > 0;

    public bool HasRuntimeText => RuntimeText.Length > 0;

    // ── Loading ─────────────────────────────────────────────────────────

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out object? value))
        {
            _movieId = Uri.UnescapeDataString(value?.ToString() ?? string.Empty);
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(_movieId))
        {
            ErrorText = "This film could not be identified.";
            return;
        }

        IsLoading = true;
        ErrorText = string.Empty;

        try
        {
            MovieDetail? movie = await _catalog.GetMovieAsync(_movieId).ConfigureAwait(true);

            if (movie is null)
            {
                ErrorText = "This film is no longer available from the free catalogue.";
                return;
            }

            Movie = movie;
            Title = movie.Title;
            Description = string.IsNullOrWhiteSpace(movie.Synopsis)
                ? "No synopsis published for this print."
                : movie.Description;
            PosterUrl = movie.PosterUrl;
            HasPoster = movie.HasPoster;
            SizeText = movie.SizeText;
            RuntimeText = movie.RuntimeLabel;
            LanguageText = string.IsNullOrWhiteSpace(movie.Language) ? "Language not tagged" : movie.Language;
            SourceText = $"{movie.Source} · {movie.AddedText}";
            SubtitleSummary = movie.HasSubtitlesList
                ? $"{movie.Subtitles.Count} subtitle track(s) available offline"
                : "No subtitle track published";

            MetaLine = string.Join(
                "  ·  ",
                new[]
                {
                    movie.YearText,
                    movie.RuntimeLabel,
                    movie.HasLanguageBadge ? movie.Language : null,
                    movie.SizeText,
                    movie.TranslationLabel
                }.Where(part => !string.IsNullOrWhiteSpace(part)));

            // A withheld film is an offer, not an error: show the price the
            // server will charge and hide every control that would fail.
            PassLocked = movie.RequiresPass;
            PassPriceText = movie.PassPriceText;
            PassDurationText = movie.PassDurationText;
            PassActive = !movie.RequiresPass;
            PassExpiryText = await DescribePassAsync().ConfigureAwait(true);

            if (PassLocked)
            {
                PlayerSource = null;
                IsPlayerVisible = false;
                PlayLabel = "Pass required";
                DownloadLabel = "Pass required";
            }

            ApplyDownloadState();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Movie {Id} could not be opened.", _movieId);
            ErrorText = "This film could not be loaded. Check the connection and try again.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Countdown for the active-pass strip. Read from the device status rather
    /// than guessed, so the figure matches what the server enforces.
    /// </summary>
    private async Task<string> DescribePassAsync()
    {
        try
        {
            DeviceStatusResponse? status = await _payments.GetFreshStatusAsync().ConfigureAwait(true);
            return status?.PassExpiryText ?? string.Empty;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Pass status could not be read for the countdown.");
            return string.Empty;
        }
    }

    // ── Play ────────────────────────────────────────────────────────────

    /// <summary>
    /// Plays the film. When an offline copy exists it always wins, so the
    /// saved library is what actually gets used on a plane or with no data.
    /// </summary>
    [RelayCommand]
    private void Play()
    {
        string? localPath = _downloads.GetLocalPath(_movieId);

        if (!string.IsNullOrWhiteSpace(localPath) && System.IO.File.Exists(localPath))
        {
            // Music and video always play one at a time: replacing the source
            // stops whatever was playing before.
            PlayerSource = MediaSource.FromFile(localPath);
            IsPlayerVisible = true;
            PlayLabel = "Playing saved copy";
            DownloadStatus = "Playing your offline copy";
            return;
        }

        if (Movie is null || !Movie.CanPlay)
        {
            ErrorText = "No playable file was published for this film.";
            return;
        }

        PlayerSource = MediaSource.FromUri(Movie.StreamUrl);
        IsPlayerVisible = true;
        PlayLabel = "Playing online";
        DownloadStatus = "Streaming from the free catalogue";
    }

    [RelayCommand]
    private void StopPlayback()
    {
        PlayerSource = null;
        IsPlayerVisible = false;
        PlayLabel = _downloads.IsDownloaded(_movieId) ? "Play saved copy" : "Play now";
    }

    // ── Download / offline library ──────────────────────────────────────

    [RelayCommand]
    private async Task ToggleDownloadAsync()
    {
        if (Movie is null)
        {
            return;
        }

        if (_downloads.IsDownloaded(_movieId))
        {
            await OpenDownloadsAsync().ConfigureAwait(true);
            return;
        }

        if (IsDownloadBusy)
        {
            _downloadCts?.Cancel();
            return;
        }

        if (!Movie.CanDownload)
        {
            ErrorText = "This film has no downloadable file.";
            return;
        }

        _downloadCts = new CancellationTokenSource();
        DownloadState = MovieDownloadState.Queued;
        DownloadStatus = "Starting download…";

        MovieDownloadRecord? record = await _downloads
            .DownloadAsync(Movie, _downloadCts.Token)
            .ConfigureAwait(true);

        DownloadState = record is null ? MovieDownloadState.Failed : MovieDownloadState.Completed;

        if (record is null)
        {
            DownloadStatus = "Download did not finish. Tap to retry.";
        }

        ApplyDownloadState();
    }

    [RelayCommand]
    private static async Task OpenDownloadsAsync()
        => await Shell.Current.GoToAsync("MovieDownloadsPage");

    [RelayCommand]
    private static async Task GoBackAsync()
    {
        // Leaving the page must not leave a film playing in the background.
        if (Shell.Current?.CurrentPage?.BindingContext is MovieDetailViewModel viewModel)
        {
            viewModel.StopPlayback();
        }

        await Shell.Current!.GoToAsync("..");
    }

    [RelayCommand]
    private static async Task OpenSourcePageAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(url);
        }
        catch
        {
            // No browser available — the attribution text still identifies the source.
        }
    }

    private void OnDownloadProgress(object? sender, MovieDownloadProgress progress)
    {
        if (!string.Equals(progress.MovieId, _movieId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            DownloadState = progress.State;
            DownloadProgress = progress.Fraction;

            if (progress.Message.Length > 0)
            {
                DownloadStatus = progress.Message;
            }

            UpdateDownloadLabel();
        });
    }

    private void ApplyDownloadState()
    {
        DownloadState = _downloads.GetState(_movieId);
        DownloadProgress = _downloads.GetFraction(_movieId);
        IsSavedOffline = DownloadState == MovieDownloadState.Completed;

        if (IsSavedOffline)
        {
            DownloadStatus = "Saved in the app — plays without internet";
        }

        UpdateDownloadLabel();
    }

    private void UpdateDownloadLabel()
    {
        DownloadLabel = DownloadState switch
        {
            MovieDownloadState.Downloading => $"Cancel · {DownloadProgress:P0}",
            MovieDownloadState.Queued => "Cancel download",
            MovieDownloadState.Completed => "Open offline library",
            MovieDownloadState.Failed => "Retry download",
            _ => "Save offline"
        };

        PlayLabel = IsSavedOffline ? "Play saved copy" : "Play now";
    }
}
