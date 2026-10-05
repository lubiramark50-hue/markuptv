using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Models;
using MarkUptv.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace MarkUptv.ViewModels;

/// <summary>
/// The Movies screen: a real film catalogue (not channels) with a search bar,
/// newest/popular shelves, translated-language filters and live download
/// state for every card.
/// </summary>
public partial class MovieCatalogViewModel : BaseViewModel
{
    private const string ShelfNewest = "Newest";
    private const string ShelfPopular = "Popular";
    private const string ShelfTranslated = "Translated";
    private const string AllLanguages = "All languages";
    private const int PageSize = 30;

    private readonly MovieCatalogService _catalog;
    private readonly MovieDownloadService _downloads;
    private readonly PaymentService _payments;
    private readonly ILogger<MovieCatalogViewModel> _logger;

    private CancellationTokenSource? _searchDebounce;
    private int _page = 1;
    private bool _hasMore;
    private bool _initialized;
    private string _languageQuery = string.Empty;
    private string _activeQuery = string.Empty;

    public MovieCatalogViewModel(
        MovieCatalogService catalog,
        MovieDownloadService downloads,
        PaymentService payments,
        ILogger<MovieCatalogViewModel> logger)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _downloads.ProgressChanged += OnDownloadProgress;
        _downloads.LibraryChanged += OnLibraryChanged;

        SyncShelfSelection();
    }

    // ── State ───────────────────────────────────────────────────────────

    public ObservableCollection<MovieCatalogItem> Movies { get; } = [];

    public ObservableCollection<MovieLanguage> Languages { get; } = [];

    public ObservableCollection<ShelfOption> Shelves { get; } =
    [
        new ShelfOption(ShelfNewest, "Newest"),
        new ShelfOption(ShelfPopular, "Most watched"),
        new ShelfOption(ShelfTranslated, "Translated films")
    ];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedShelf = ShelfNewest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllLanguages))]
    private string _selectedLanguage = AllLanguages;

    /// <summary>Highlights the "All languages" chip when nothing is filtered.</summary>
    public bool IsAllLanguages => SelectedLanguage == AllLanguages;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMovies))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isLoadingMore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusText = string.Empty;

    public bool HasStatus => StatusText.Length > 0;

    [ObservableProperty]
    private string _headerTitle = "Movies";

    [ObservableProperty]
    private string _headerSubtitle = "A day pass unlocks every film — stream or save offline";

    [ObservableProperty]
    private int _downloadCount;

    [ObservableProperty]
    private string _storageText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMovies))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private int _movieCount;

    public bool HasMovies => MovieCount > 0;

    /// <summary>Only show the empty state once a load has finished.</summary>
    public bool ShowEmptyState => IsEmpty && !IsLoading;

    public bool HasTranslations => Languages.Any(language => language.Translated);

    // ── Day pass (what buys the films and the 18+ shelf) ────────────────

    /// <summary>True while the viewer's pass is running — films stream and save.</summary>
    [ObservableProperty]
    private bool _passActive;

    [ObservableProperty]
    private string _passPriceText = "1,000 UGX";

    [ObservableProperty]
    private string _passDurationText = "24 hours";

    [ObservableProperty]
    private string _passExpiryText = string.Empty;

    [ObservableProperty]
    private string _passEmail = string.Empty;

    [ObservableProperty]
    private bool _isBuyingPass;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPassMessage))]
    private string _passMessage = string.Empty;

    public bool HasPassMessage => PassMessage.Length > 0;

    /// <summary>
    /// Refreshes the pass banner from the server. Every figure shown (price,
    /// length, expiry) comes from here, so the films page can never advertise
    /// an amount the gateway will not charge.
    /// </summary>
    private async Task RefreshPassAsync()
    {
        try
        {
            DeviceStatusResponse? status = await _payments.GetFreshStatusAsync().ConfigureAwait(true);

            PassActive = status?.PassActive ?? false;
            PassPriceText = status?.PassPriceText ?? PassPriceText;
            PassDurationText = status?.PassDurationText ?? PassDurationText;
            PassExpiryText = status?.PassExpiryText ?? string.Empty;

            // The shelf header must not promise free films while the offer
            // card right below it says otherwise.
            HeaderSubtitle = PassActive
                ? "All films unlocked — stream now or save for offline"
                : "A day pass unlocks every film — stream or save offline";
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Pass state could not be read for the films banner.");

            // Unknown entitlement reads as locked: the shelf behind it is
            // enforced server-side anyway, so showing the offer is honest.
            PassActive = false;
        }
    }

    /// <summary>
    /// Starts a real checkout for the daily pass. The server owns the price
    /// (the client only names the product), and the gateway takes the money.
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

            PaymentWebViewCallbackHolder.SuccessCallback = async () =>
            {
                PassMessage = "Payment received — unlocking…";

                // The callback page only renders once the server has verified
                // the money, so this refresh sees the pass in force.
                await RefreshPassAsync().ConfigureAwait(true);
                await RefreshAsync().ConfigureAwait(true);
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

    // ── Lifecycle ───────────────────────────────────────────────────────

    [RelayCommand]
    private async Task InitializeAsync()
    {
        RefreshLibraryState();

        if (_initialized)
        {
            return;
        }

        _initialized = true;

        await RefreshPassAsync().ConfigureAwait(true);
        await LoadLanguagesAsync().ConfigureAwait(true);
        await LoadShelfAsync(reset: true).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        RefreshLibraryState();
        await RefreshPassAsync().ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(_activeQuery))
        {
            await LoadShelfAsync(reset: true).ConfigureAwait(true);
        }
        else
        {
            await RunSearchAsync(_activeQuery, reset: true).ConfigureAwait(true);
        }
    }

    private async Task LoadLanguagesAsync()
    {
        var languages = await _catalog.GetLanguagesAsync().ConfigureAwait(true);

        Languages.Clear();

        foreach (MovieLanguage language in languages)
        {
            Languages.Add(language);
        }
    }

    // ── Search ──────────────────────────────────────────────────────────

    /// <summary>
    /// Debounced search: typing fires one request after a short pause instead
    /// of one per keystroke.
    /// </summary>
    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();

        var cts = new CancellationTokenSource();
        _searchDebounce = cts;

        _ = DebouncedSearchAsync(value, cts.Token);
    }

    private async Task DebouncedSearchAsync(string value, CancellationToken ct)
    {
        try
        {
            await Task.Delay(450, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ct.IsCancellationRequested)
        {
            return;
        }

        string query = value?.Trim() ?? string.Empty;
        _activeQuery = query;

        if (query.Length == 0)
        {
            await LoadShelfAsync(reset: true).ConfigureAwait(true);
            return;
        }

        await RunSearchAsync(query, reset: true).ConfigureAwait(true);
    }

    /// <summary>
    /// Turns raw transport errors into a sentence that tells the viewer what
    /// to do next; "Search: server replied 502" is a log line, not copy.
    /// </summary>
    private static string FriendlyError(string technical) => technical.ToLowerInvariant() switch
    {
        var t when t.Contains("502") || t.Contains("503") || t.Contains("504")
            => "The film catalogue is warming up — try again in a moment.",
        var t when t.Contains("401") || t.Contains("403")
            => "This shelf needs an active day pass — get one above.",
        var t when t.Contains("timed out") || t.Contains("timeout") || t.Contains("network")
            => "The network dropped. Check your connection and pull to refresh.",
        _ => "Something went wrong loading films. Pull down to refresh."
    };

    private async Task RunSearchAsync(string query, bool reset)
    {
        if (reset)
        {
            _page = 1;
            IsLoading = true;
        }
        else
        {
            IsLoadingMore = true;
        }

        try
        {
            MovieSearchResult page = await _catalog
                .SearchAsync(query, CurrentLanguageQuery(), "relevance", _page, PageSize)
                .ConfigureAwait(true);

            ApplyPage(page, reset);

            HeaderTitle = "Movies";
            HeaderSubtitle = page.TotalCount > 0
                ? $"{page.TotalCount:N0} films match \"{query}\""
                : $"No film matches \"{query}\"";

            StatusText = Movies.Count == 0 && _catalog.LastError.Length > 0
                ? FriendlyError(_catalog.LastError)
                : string.Empty;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Movie search failed for {Query}.", query);
            StatusText = "Search is unavailable right now. Try again in a moment.";
        }
        finally
        {
            IsLoading = false;
            IsLoadingMore = false;
            RefreshLibraryState();
        }
    }

    // ── Shelves ─────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SelectShelfAsync(string? shelf)
    {
        if (string.IsNullOrWhiteSpace(shelf))
        {
            return;
        }

        if (string.Equals(SelectedShelf, shelf, StringComparison.Ordinal))
        {
            await LoadShelfAsync(reset: true).ConfigureAwait(true);
            return;
        }

        SelectedShelf = shelf;
        SyncShelfSelection();
        _activeQuery = string.Empty;
        SearchText = string.Empty;

        await LoadShelfAsync(reset: true).ConfigureAwait(true);
    }

    private void SyncShelfSelection()
    {
        foreach (ShelfOption option in Shelves)
        {
            option.IsSelected = string.Equals(option.Name, SelectedShelf, StringComparison.Ordinal);
        }
    }

    [RelayCommand]
    private async Task SelectLanguageAsync(MovieLanguage? language)
    {
        if (language is null)
        {
            SelectedLanguage = AllLanguages;
            _languageQuery = string.Empty;
        }
        else
        {
            SelectedLanguage = language.Name;
            _languageQuery = language.Query;
        }

        foreach (MovieLanguage option in Languages)
        {
            option.IsSelected = language is not null &&
                string.Equals(option.Name, language.Name, StringComparison.Ordinal);
        }

        _page = 1;

        if (string.IsNullOrWhiteSpace(_activeQuery))
        {
            await LoadShelfAsync(reset: true).ConfigureAwait(true);
        }
        else
        {
            await RunSearchAsync(_activeQuery, reset: true).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        _languageQuery = string.Empty;
        SelectedLanguage = AllLanguages;
        _activeQuery = string.Empty;
        SearchText = string.Empty;
        SelectedShelf = ShelfNewest;
        SyncShelfSelection();

        foreach (MovieLanguage option in Languages)
        {
            option.IsSelected = false;
        }

        await LoadShelfAsync(reset: true).ConfigureAwait(true);
    }

    private async Task LoadShelfAsync(bool reset)
    {
        if (reset)
        {
            _page = 1;
            IsLoading = true;
        }
        else
        {
            IsLoadingMore = true;
        }

        try
        {
            MovieSearchResult page = SelectedShelf switch
            {
                ShelfPopular => await _catalog
                    .GetPopularAsync(CurrentLanguageQuery(), _page, PageSize)
                    .ConfigureAwait(true),

                ShelfTranslated => await _catalog
                    .GetTranslatedAsync(CurrentLanguageQuery(), _page, PageSize)
                    .ConfigureAwait(true),

                _ => await _catalog
                    .GetLatestAsync(CurrentLanguageQuery(), _page, PageSize)
                    .ConfigureAwait(true)
            };

            ApplyPage(page, reset);

            if (Movies.Count == 0 && _catalog.LastError.Length > 0)
            {
                StatusText = FriendlyError(_catalog.LastError);
            }
            else
            {
                StatusText = string.Empty;
            }

            string languageLabel = SelectedLanguage == AllLanguages ? string.Empty : $" · {SelectedLanguage}";

            HeaderTitle = SelectedShelf switch
            {
                ShelfPopular => "Most watched",
                ShelfTranslated => "Translated films",
                _ => "Newest films"
            };

            HeaderSubtitle =
                $"{Math.Max(page.TotalCount, Movies.Count):N0} films{languageLabel} — {(PassActive ? "stream now or save for offline" : "get the day pass to stream or save")}";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Movie shelf {Shelf} failed.", SelectedShelf);
            StatusText = "The catalogue is unavailable right now. Try again in a moment.";
        }
        finally
        {
            IsLoading = false;
            IsLoadingMore = false;
            RefreshLibraryState();
        }
    }

    [RelayCommand]
    private Task LoadMoreAsync() => _hasMore
        ? (string.IsNullOrWhiteSpace(_activeQuery) ? LoadShelfAsync(reset: false) : RunSearchAsync(_activeQuery, reset: false))
        : Task.CompletedTask;

    private void ApplyPage(MovieSearchResult page, bool reset)
    {
        if (reset)
        {
            Movies.Clear();
        }

        foreach (MovieCatalogItem item in page.Items)
        {
            _downloads.ApplyState(item);
            Movies.Add(item);
        }

        _hasMore = page.HasMore;
        _page++;

        MovieCount = Movies.Count;
        IsEmpty = Movies.Count == 0;
    }

    // ── Navigation ──────────────────────────────────────────────────────

    [RelayCommand]
    private static async Task OpenMovieAsync(MovieCatalogItem? movie)
    {
        if (movie is null || string.IsNullOrWhiteSpace(movie.Id))
        {
            return;
        }

        await Shell.Current.GoToAsync(
            $"MovieDetailPage?id={Uri.EscapeDataString(movie.Id)}");
    }

    [RelayCommand]
    private static async Task OpenDownloadsAsync()
        => await Shell.Current.GoToAsync("MovieDownloadsPage");

    [RelayCommand]
    private static void OpenFlyout()
    {
        if (Shell.Current is not null)
        {
            Shell.Current.FlyoutIsPresented = true;
        }
    }

    // ── Download state plumbing ─────────────────────────────────────────

    private string? CurrentLanguageQuery() =>
        string.IsNullOrWhiteSpace(_languageQuery) ? null : _languageQuery;

    /// <summary>
    /// Shelf browsing wants "newest" or "most watched"; typing a title wants
    /// relevance, otherwise a classic with millions of plays outranks the film
    /// the user actually asked for.
    /// </summary>
    private string CurrentSort() => SelectedShelf switch
    {
        ShelfPopular => "popular",
        ShelfNewest => "newest",
        ShelfTranslated => "newest",
        _ => "relevance"
    };

    private void RefreshLibraryState()
    {
        foreach (MovieCatalogItem movie in Movies)
        {
            _downloads.ApplyState(movie);
        }

        DownloadCount = _downloads.DownloadCount;
        StorageText = DownloadCount > 0
            ? $"{DownloadCount} saved · {MovieCatalogItem.FormatSize(_downloads.TotalBytesUsed())}"
            : "Nothing saved yet";
    }

    private void OnDownloadProgress(object? sender, MovieDownloadProgress progress)
        => MainThread.BeginInvokeOnMainThread(() =>
        {
            MovieCatalogItem? movie = Movies.FirstOrDefault(item =>
                string.Equals(item.Id, progress.MovieId, StringComparison.OrdinalIgnoreCase));

            if (movie is not null)
            {
                movie.DownloadState = progress.State;
                movie.DownloadProgress = progress.Fraction;

                // Cards bound to the item need a nudge; the collection view
                // reuses rows, so replace the item to force a rebind.
                int index = Movies.IndexOf(movie);
                if (index >= 0)
                {
                    Movies[index] = movie;
                }
            }

            RefreshLibraryState();
        });

    private void OnLibraryChanged(object? sender, EventArgs e)
        => MainThread.BeginInvokeOnMainThread(RefreshLibraryState);
}
