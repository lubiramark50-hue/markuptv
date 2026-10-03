using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Models;
using MarkUptv.Pages;
using MarkUptv.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Networking;

namespace MarkUptv.ViewModels;

public enum SearchTab
{
    All,
    Images,
    Videos,
    Articles,
    Web
}

/// <summary>
/// Controls global search, result classification, filtering,
/// cancellation and navigation.
/// </summary>
public partial class SearchViewModel :
    BaseViewModel,
    IDisposable
{
    private static readonly TimeSpan SearchDelay =
        TimeSpan.FromMilliseconds(450);

    private readonly SearchApiService _searchApi;
    private readonly ILogger<SearchViewModel> _logger;
    private readonly IConnectivity _connectivity;

    private readonly List<SearchResult> _allSearchResults = [];

    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _debounceCancellation;

    private long _searchVersion;
    private bool _suppressDebounce;
    private bool _disposed;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllSelected))]
    [NotifyPropertyChangedFor(nameof(IsImagesSelected))]
    [NotifyPropertyChangedFor(nameof(IsVideosSelected))]
    [NotifyPropertyChangedFor(nameof(IsArticlesSelected))]
    [NotifyPropertyChangedFor(nameof(IsWebSelected))]
    [NotifyPropertyChangedFor(nameof(SelectedTabTitle))]
    [NotifyPropertyChangedFor(nameof(SearchSummary))]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    private SearchTab _selectedTab = SearchTab.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWelcome))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(SearchSummary))]
    private bool _hasSearched;

    public ObservableCollection<SearchResult> DisplayedResults { get; } = [];

    public bool IsAllSelected =>
        SelectedTab == SearchTab.All;

    public bool IsImagesSelected =>
        SelectedTab == SearchTab.Images;

    public bool IsVideosSelected =>
        SelectedTab == SearchTab.Videos;

    public bool IsArticlesSelected =>
        SelectedTab == SearchTab.Articles;

    public bool IsWebSelected =>
        SelectedTab == SearchTab.Web;

    public bool ShowWelcome =>
        !HasSearched &&
        !IsLoading &&
        !HasError;

    public bool HasResults =>
        DisplayedResults.Count > 0;

    public bool IsEmpty =>
        HasSearched &&
        !IsLoading &&
        !HasError &&
        DisplayedResults.Count == 0;

    public int ResultCount =>
        DisplayedResults.Count;

    public int AllCount =>
        _allSearchResults.Count;

    public int ImageCount =>
        _allSearchResults.Count(IsImage);

    public int VideoCount =>
        _allSearchResults.Count(IsVideo);

    public int ArticleCount =>
        _allSearchResults.Count(IsArticle);

    public int WebCount =>
        _allSearchResults.Count(IsWeb);

    public string SelectedTabTitle =>
        SelectedTab switch
        {
            SearchTab.Images => "Image results",
            SearchTab.Videos => "Video results",
            SearchTab.Articles => "News and articles",
            SearchTab.Web => "Web results",
            _ => "Top results"
        };

    public string SearchSummary
    {
        get
        {
            if (!HasSearched)
            {
                return "Search channels, movies, music, news, images and the web.";
            }

            string resultWord =
                ResultCount == 1
                    ? "result"
                    : "results";

            string query =
                string.IsNullOrWhiteSpace(SearchText)
                    ? string.Empty
                    : $" for “{SearchText.Trim()}”";

            return $"{ResultCount} {resultWord}{query}";
        }
    }

    public string EmptyMessage =>
        SelectedTab switch
        {
            SearchTab.Images =>
                "No matching images were found.",

            SearchTab.Videos =>
                "No matching videos or playable streams were found.",

            SearchTab.Articles =>
                "No matching news stories or articles were found.",

            SearchTab.Web =>
                "No matching web pages were found.",

            _ =>
                "Try different words or make your search more general."
        };

    public SearchViewModel(
        SearchApiService searchApi,
        ILogger<SearchViewModel> logger,
        IConnectivity connectivity)
    {
        _searchApi = searchApi
            ?? throw new ArgumentNullException(nameof(searchApi));

        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        _connectivity = connectivity
            ?? throw new ArgumentNullException(nameof(connectivity));

        PropertyChanged += OnViewModelPropertyChanged;
        DisplayedResults.CollectionChanged += OnDisplayedResultsChanged;
    }

    // ============================================================
    // SHELL QUERY
    // ============================================================

    public void ApplyQueryAttributes(
        IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!query.TryGetValue(
                "query",
                out object? queryValue))
        {
            return;
        }

        string value =
            DecodeRouteValue(
                queryValue?.ToString());

        Initialize(value);
    }

    public void Initialize(
        string? query)
    {
        ThrowIfDisposed();

        string normalizedQuery =
            query?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return;
        }

        CancelDebounce();

        _suppressDebounce = true;

        try
        {
            SearchText = normalizedQuery;
        }
        finally
        {
            _suppressDebounce = false;
        }

        _ = ExecuteInitialSearchSafelyAsync(
            normalizedQuery);
    }

    private async Task ExecuteInitialSearchSafelyAsync(
        string query)
    {
        try
        {
            await ExecuteSearchAsync(
                query,
                CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            // Page was closed before the initial search started.
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Initial Shell search failed.");
        }
    }

    // ============================================================
    // LIVE SEARCH
    // ============================================================

    partial void OnSearchTextChanged(
        string value)
    {
        if (_disposed ||
            _suppressDebounce)
        {
            return;
        }

        CancelDebounce();

        string query =
            value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(query))
        {
            CancelActiveSearch();

            _ = RunOnMainThreadAsync(
                ClearSearchState);

            return;
        }

        if (query.Length < 2)
        {
            return;
        }

        var cancellation =
            new CancellationTokenSource();

        _debounceCancellation =
            cancellation;

        _ = ExecuteDebouncedSearchAsync(
            query,
            cancellation.Token);
    }

    private async Task ExecuteDebouncedSearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(
                SearchDelay,
                cancellationToken);

            await ExecuteSearchAsync(
                query,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Replaced by a newer search.
        }
        catch (ObjectDisposedException)
        {
            // Page was disposed while waiting.
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Debounced search failed.");
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SearchAsync(
        CancellationToken cancellationToken)
    {
        string query =
            SearchText?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(query))
        {
            SetError("Enter something to search for.");
            return;
        }

        CancelDebounce();

        await ExecuteSearchAsync(
            query,
            cancellationToken);
    }

    private async Task ExecuteSearchAsync(
        string query,
        CancellationToken commandCancellationToken)
    {
        ThrowIfDisposed();

        string normalizedQuery =
            query.Trim();

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return;
        }

        if (_connectivity.NetworkAccess !=
            NetworkAccess.Internet)
        {
            await SetSearchErrorAsync(
                "No internet connection. Check your network and try again.");

            return;
        }

        CancelActiveSearch();

        var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                commandCancellationToken);

        _searchCancellation =
            cancellation;

        CancellationToken token =
            cancellation.Token;

        long searchVersion =
            Interlocked.Increment(
                ref _searchVersion);

        await RunOnMainThreadAsync(
            () =>
            {
                IsLoading = true;
                HasSearched = true;

                ClearError();
                NotifySearchStateChanged();
            });

        try
        {
            _logger.LogInformation(
                "Searching MarkUpTV for {Query}.",
                normalizedQuery);

            IEnumerable<SearchResult>? response =
                await _searchApi.SearchAsync(
                    normalizedQuery,
                    token);

            token.ThrowIfCancellationRequested();

            if (searchVersion !=
                Volatile.Read(ref _searchVersion))
            {
                return;
            }

            List<SearchResult> results =
                response?
                    .Where(result => result is not null)
                    .Where(HasUsefulResultData)
                    .DistinctBy(
                        CreateResultKey,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList()
                ?? [];

            await RunOnMainThreadAsync(
                () =>
                {
                    if (searchVersion !=
                        Volatile.Read(ref _searchVersion))
                    {
                        return;
                    }

                    _allSearchResults.Clear();
                    _allSearchResults.AddRange(results);

                    ApplySelectedTab();

                    IsLoading = false;

                    NotifyResultCounts();
                    NotifySearchStateChanged();
                });

            _logger.LogInformation(
                "Search completed. Query: {Query}; Results: {Count}",
                normalizedQuery,
                results.Count);
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Search for {Query} was cancelled.",
                normalizedQuery);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "Search backend could not be reached.");

            if (searchVersion ==
                Volatile.Read(ref _searchVersion))
            {
                await SetSearchErrorAsync(
                    "The search service could not be reached. Try again shortly.");
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unexpected search failure.");

            if (searchVersion ==
                Volatile.Read(ref _searchVersion))
            {
                await SetSearchErrorAsync(
                    "Search could not be completed. Please try again.");
            }
        }
        finally
        {
            if (searchVersion ==
                Volatile.Read(ref _searchVersion))
            {
                await RunOnMainThreadAsync(
                    () =>
                    {
                        IsLoading = false;
                        NotifySearchStateChanged();
                    });
            }

            if (ReferenceEquals(
                    _searchCancellation,
                    cancellation))
            {
                _searchCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    // ============================================================
    // FILTERING
    // ============================================================

    [RelayCommand]
    private void SelectTab(
        string? tabName)
    {
        if (string.IsNullOrWhiteSpace(tabName))
        {
            return;
        }

        if (!Enum.TryParse(
                tabName,
                ignoreCase: true,
                out SearchTab tab))
        {
            return;
        }

        SelectedTab = tab;
    }

    partial void OnSelectedTabChanged(
        SearchTab value)
    {
        ApplySelectedTab();
    }

    private void ApplySelectedTab()
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(
                ApplySelectedTab);

            return;
        }

        IEnumerable<SearchResult> filtered =
            SelectedTab switch
            {
                SearchTab.Images =>
                    _allSearchResults.Where(IsImage),

                SearchTab.Videos =>
                    _allSearchResults.Where(IsVideo),

                SearchTab.Articles =>
                    _allSearchResults.Where(IsArticle),

                SearchTab.Web =>
                    _allSearchResults.Where(IsWeb),

                _ =>
                    _allSearchResults
            };

        DisplayedResults.Clear();

        foreach (SearchResult result in filtered)
        {
            DisplayedResults.Add(result);
        }

        NotifySearchStateChanged();
    }

    // ============================================================
    // RESULT NAVIGATION
    // ============================================================

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenResultAsync(
        SearchResult? result,
        CancellationToken cancellationToken)
    {
        if (result is null)
        {
            return;
        }

        string url =
            ResolveResultUrl(result);

        if (!IsSupportedUrl(url))
        {
            SetError(
                "This result does not contain a valid web address.");

            return;
        }

        string title =
            string.IsNullOrWhiteSpace(result.Title)
                ? "MarkUpTV result"
                : result.Title.Trim();

        try
        {
            if (result.Type ==
                SearchResultType.Article)
            {
                await Browser.Default.OpenAsync(
                    new Uri(url),
                    BrowserLaunchMode.SystemPreferred);

                return;
            }

            if (result.Type ==
                    SearchResultType.Channel ||
                IsDirectMediaResult(result, url))
            {
                var mediaParameters =
                    new Dictionary<string, object>
                    {
                        ["streamUrl"] = url,
                        ["title"] = title
                    };

                await SafeNavigateAsync(
                    "//PlayerPage",
                    mediaParameters,
                    cancellationToken);

                return;
            }

            var browserParameters =
                new Dictionary<string, object>
                {
                    ["url"] = url,
                    ["pageTitle"] = title,
                    ["resultType"] = result.Type.ToString()
                };

            await SafeNavigateAsync(
                nameof(WebViewPage),
                browserParameters,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Opening a search result was cancelled.");
        }
        catch (Exception exception)
        {
            SetError(
                "The selected result could not be opened.");

            _logger.LogError(
                exception,
                "Search-result navigation failed. URL: {Url}",
                url);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task GoBackAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await SafeNavigateAsync(
                "..",
                parameters: null,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Normal cancellation.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Search page could not navigate backward.");
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        CancelDebounce();
        CancelActiveSearch();

        _suppressDebounce = true;

        try
        {
            SearchText = string.Empty;
        }
        finally
        {
            _suppressDebounce = false;
        }

        ClearSearchState();
    }

    // ============================================================
    // RESULT CLASSIFICATION
    // ============================================================

    private static bool IsImage(
        SearchResult result)
    {
        return result.Type ==
               SearchResultType.Image;
    }

    private static bool IsVideo(
        SearchResult result)
    {
        return result.Type is
            SearchResultType.Video or
            SearchResultType.Podcast or
            SearchResultType.Channel;
    }

    private static bool IsArticle(
        SearchResult result)
    {
        return result.Type ==
               SearchResultType.Article;
    }

    private static bool IsWeb(
        SearchResult result)
    {
        return result.Type is
            SearchResultType.Web or
            SearchResultType.Link;
    }

    private static bool HasUsefulResultData(
        SearchResult result)
    {
        return
            !string.IsNullOrWhiteSpace(result.Title) ||
            !string.IsNullOrWhiteSpace(result.Description) ||
            !string.IsNullOrWhiteSpace(result.Url) ||
            !string.IsNullOrWhiteSpace(result.ThumbnailUrl);
    }

    private static string CreateResultKey(
        SearchResult result)
    {
        return
            $"{result.Type}|" +
            $"{result.Url?.Trim()}|" +
            $"{result.Title?.Trim()}";
    }

    private static string ResolveResultUrl(
        SearchResult result)
    {
        string url =
            result.Url?.Trim() ?? string.Empty;

        if (IsSupportedUrl(url))
        {
            return url;
        }

        if (result.Type ==
                SearchResultType.Image &&
            IsSupportedUrl(result.ThumbnailUrl))
        {
            return result.ThumbnailUrl!.Trim();
        }

        return string.Empty;
    }

    private static bool IsSupportedUrl(
        string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(
                url.Trim(),
                UriKind.Absolute,
                out Uri? uri))
        {
            return false;
        }

        return
            string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirectMediaResult(
        SearchResult result,
        string url)
    {
        if (result.Type ==
            SearchResultType.Podcast)
        {
            return true;
        }

        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out Uri? uri))
        {
            return false;
        }

        string path =
            uri.AbsolutePath;

        return
            path.EndsWith(
                ".m3u8",
                StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(
                ".mp4",
                StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(
                ".webm",
                StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(
                ".mp3",
                StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(
                ".m4a",
                StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(
                ".aac",
                StringComparison.OrdinalIgnoreCase);
    }

    // ============================================================
    // UI STATE
    // ============================================================

    private async Task SetSearchErrorAsync(
        string message)
    {
        await RunOnMainThreadAsync(
            () =>
            {
                SetError(message);

                IsLoading = false;
                HasSearched = true;

                _allSearchResults.Clear();
                DisplayedResults.Clear();

                NotifyResultCounts();
                NotifySearchStateChanged();
            });
    }

    private void ClearSearchState()
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(
                ClearSearchState);

            return;
        }

        ClearError();

        IsLoading = false;
        HasSearched = false;
        SelectedTab = SearchTab.All;

        _allSearchResults.Clear();
        DisplayedResults.Clear();

        NotifyResultCounts();
        NotifySearchStateChanged();
    }

    private void NotifySearchStateChanged()
    {
        OnPropertyChanged(nameof(ShowWelcome));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ResultCount));
        OnPropertyChanged(nameof(SearchSummary));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private void NotifyResultCounts()
    {
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(ImageCount));
        OnPropertyChanged(nameof(VideoCount));
        OnPropertyChanged(nameof(ArticleCount));
        OnPropertyChanged(nameof(WebCount));
    }

    private void OnDisplayedResultsChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        NotifySearchStateChanged();
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (string.Equals(
                e.PropertyName,
                nameof(IsLoading),
                StringComparison.Ordinal) ||
            string.Equals(
                e.PropertyName,
                nameof(ErrorMessage),
                StringComparison.Ordinal))
        {
            NotifySearchStateChanged();
        }
    }

    // ============================================================
    // LIFECYCLE
    // ============================================================

    public void OnPageDisappearing()
    {
        CancelDebounce();
        CancelActiveSearch();
    }

    private void CancelDebounce()
    {
        CancellationTokenSource? cancellation =
            Interlocked.Exchange(
                ref _debounceCancellation,
                null);

        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void CancelActiveSearch()
    {
        Interlocked.Increment(
            ref _searchVersion);

        CancellationTokenSource? cancellation =
            Interlocked.Exchange(
                ref _searchCancellation,
                null);

        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    private static string DecodeRouteValue(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static Task RunOnMainThreadAsync(
        Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (MainThread.IsMainThread)
        {
            action();
            return Task.CompletedTask;
        }

        return MainThread.InvokeOnMainThreadAsync(
            action);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        CancelDebounce();
        CancelActiveSearch();

        PropertyChanged -= OnViewModelPropertyChanged;
        DisplayedResults.CollectionChanged -= OnDisplayedResultsChanged;

        GC.SuppressFinalize(this);
    }
}