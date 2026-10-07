using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Models;
using MarkUptv.Pages;
using MarkUptv.Serialization;
using MarkUptv.Services;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;

namespace MarkUptv.ViewModels;

/// <summary>
/// ViewModel for the main MarkUpTV dashboard.
/// Handles dashboard loading, refreshing, pagination,
/// navigation, channel playback and page state.
/// </summary>
public partial class MainPageViewModel :
    BaseViewModel,
    IDisposable
{
    private const int NewsPageSize = 10;

    // Drip-fill staging: items land here first and are moved into the visible
    // collection one per dispatcher pass. Realizing a bound card is real UI
    // work, so staging keeps every pass well under Android's input watchdog.
    private readonly Queue<DashboardFeature> _pendingCategories = new();
    private readonly Queue<NewsArticle> _pendingNews = new();
    private bool _dripFillActive;

    private static readonly TimeSpan DashboardFreshness =
        TimeSpan.FromMinutes(3);

    private readonly TvApiService _tvApi;
    private readonly NewsApiService _newsApi;
    private readonly SocialService _socialService;
    private readonly FootballApiService _footballApi;
    private readonly LiveResolutionService _liveResolver;
    private readonly LiveSessionService _liveSession;
    private readonly RecentlyWatchedService _recentlyWatched;
    private readonly IAiTvService _aiTvService;
    private readonly ILogger<MainPageViewModel> _logger;

    private readonly SemaphoreSlim _initializationGate =
        new(1, 1);

    private readonly SemaphoreSlim _refreshGate =
        new(1, 1);

    private readonly SemaphoreSlim _newsGate =
        new(1, 1);

    private readonly Timer _clockTimer;

    private TvChannel? _featuredChannel;

    private LiveMatch? _currentLiveMatch;

    private int _newsPage = 1;
    private bool _initialized;
    private bool _disposed;

    private DateTimeOffset _lastDashboardLoadUtc =
        DateTimeOffset.MinValue;

    // ============================================================
    // COLLECTIONS
    // ============================================================

    public ObservableCollection<TvChannel>
        RecentlyWatchedChannels
    { get; } = [];

    public ObservableCollection<TvChannel>
        TrendingChannels
    { get; } = [];

    public ObservableCollection<NewsArticle>
        NewsItems
    { get; } = [];

    public ObservableCollection<SocialPost>
        SocialPostsPreview
    { get; } = [];

    public ObservableCollection<DashboardFeature>
        PrimaryFeatures
    { get; } = [];

    public ObservableCollection<DashboardFeature>
        CategoryFeatures
    { get; } = [];

    // ============================================================
    // HEADER
    // ============================================================

    [ObservableProperty]
    private string _currentTime =
        DateTime.Now.ToString("HH:mm");

    [ObservableProperty]
    private string _currentDate =
        DateTime.Now.ToString("dddd, MMMM d");

    [ObservableProperty]
    private string _greetingMessage =
        CreateGreeting();

    [ObservableProperty]
    private bool _hasUnreadNotifications;

    // ============================================================
    // FEATURED HERO
    // ============================================================

    [ObservableProperty]
    private string _featuredTitle =
        "Live television from around the world";

    [ObservableProperty]
    private string _featuredSubtitle =
        "Sports, news, movies, music and entertainment in one place.";

    [ObservableProperty]
    private string _featuredImageUrl =
        string.Empty;

    [ObservableProperty]
    private string _featuredCategory =
        "FEATURED";

    [ObservableProperty]
    private bool _hasFeaturedContent = true;

    // ============================================================
    // LIVE MATCH
    // ============================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveMatchTitle))]
    private string _homeTeamName =
        string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveMatchTitle))]
    private string _awayTeamName =
        string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveMatchTitle))]
    private string _homeTeamScore =
        "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveMatchTitle))]
    private string _awayTeamScore =
        "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveMatchSubtitle))]
    private string _liveMatchMinute =
        "LIVE";

    [ObservableProperty]
    private bool _hasLiveMatch;

    public string LiveMatchSubtitle =>
        string.IsNullOrWhiteSpace(LiveMatchMinute)
            ? "LIVE NOW"
            : LiveMatchMinute;

    public string LiveMatchTitle
    {
        get
        {
            if (string.IsNullOrWhiteSpace(HomeTeamName) ||
                string.IsNullOrWhiteSpace(AwayTeamName))
            {
                return "Live football is available now";
            }

            return
                $"{HomeTeamName} {HomeTeamScore}  –  " +
                $"{AwayTeamScore} {AwayTeamName}";
        }
    }

    // ============================================================
    // CHANNEL STATE
    // ============================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChannelsVisible))]
    private bool _isChannelsLoading;

    public bool IsChannelsVisible =>
        !IsChannelsLoading &&
        TrendingChannels.Count > 0;

    [ObservableProperty]
    private bool _hasRecentlyWatched;

    [ObservableProperty]
    private bool _hasSocialPreview;

    // ============================================================
    // NEWS STATE
    // ============================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewsVisible))]
    [NotifyPropertyChangedFor(nameof(CanLoadMoreNews))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreNewsCommand))]
    private bool _isNewsLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewsVisible))]
    [NotifyPropertyChangedFor(nameof(CanLoadMoreNews))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreNewsCommand))]
    private bool _hasNewsError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewsVisible))]
    [NotifyPropertyChangedFor(nameof(CanLoadMoreNews))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreNewsCommand))]
    private bool _isNewsEmpty;

    [ObservableProperty]
    private string _newsErrorMessage =
        "News could not be loaded.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMoreNews))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreNewsCommand))]
    private bool _isLoadingMore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMoreNews))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreNewsCommand))]
    private bool _hasMoreNews = true;

    public bool IsNewsVisible =>
        !IsNewsLoading &&
        !HasNewsError &&
        NewsItems.Count > 0;

    public bool CanLoadMoreNews =>
        HasMoreNews &&
        !IsLoadingMore &&
        !IsNewsLoading &&
        !HasNewsError &&
        NewsItems.Count > 0;

    // ============================================================
    // PAGE STATE
    // ============================================================

    [ObservableProperty]
    private bool _isPageRefreshing;

    [ObservableProperty]
    private string _searchQueryText =
        string.Empty;

    // ============================================================
    // CONSTRUCTOR
    // ============================================================

    public MainPageViewModel(
        TvApiService tvApi,
        NewsApiService newsApi,
        SocialService socialService,
        FootballApiService footballApi,
        LiveResolutionService liveResolver,
        LiveSessionService liveSession,
        RecentlyWatchedService recentlyWatched,
        IAiTvService aiTvService,
        ILogger<MainPageViewModel> logger)
    {
        _tvApi = tvApi
            ?? throw new ArgumentNullException(
                nameof(tvApi));

        _newsApi = newsApi
            ?? throw new ArgumentNullException(
                nameof(newsApi));

        _socialService = socialService
            ?? throw new ArgumentNullException(
                nameof(socialService));

        _footballApi = footballApi
            ?? throw new ArgumentNullException(
                nameof(footballApi));

        _liveResolver = liveResolver
            ?? throw new ArgumentNullException(
                nameof(liveResolver));

        _liveSession = liveSession
            ?? throw new ArgumentNullException(
                nameof(liveSession));

        _recentlyWatched = recentlyWatched
            ?? throw new ArgumentNullException(
                nameof(recentlyWatched));

        _aiTvService = aiTvService
            ?? throw new ArgumentNullException(
                nameof(aiTvService));

        _logger = logger
            ?? throw new ArgumentNullException(
                nameof(logger));

        Services.StartupTrace.Mark("MainPageViewModel ctor: building dashboard");

        BuildDashboardFeatures();
        StartDripFill();
        InitializeAi();

        _clockTimer = new Timer(
            UpdateClock,
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(30));

        Services.StartupTrace.Mark("MainPageViewModel ctor done");
    }

    // ============================================================
    // INITIALIZATION
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private async Task InitializeAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        await _initializationGate.WaitAsync(
            cancellationToken);

        try
        {
            bool dashboardIsFresh =
                _initialized &&
                DateTimeOffset.UtcNow -
                _lastDashboardLoadUtc <
                DashboardFreshness;

            if (dashboardIsFresh)
            {
                await LoadRecentlyWatchedAsync(
                    cancellationToken);

                return;
            }

            await RefreshDashboardCoreAsync(
                cancellationToken);

            _initialized = true;

            _lastDashboardLoadUtc =
                DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Main dashboard initialization was cancelled.");
        }
        catch (Exception exception)
        {
            SetError(
                "The dashboard could not be loaded.");

            _logger.LogError(
                exception,
                "Main dashboard initialization failed.");
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    // ============================================================
    // PULL TO REFRESH
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private async Task PullToRefreshAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        bool entered =
            await _refreshGate.WaitAsync(
                0,
                cancellationToken);

        if (!entered)
        {
            return;
        }

        IsPageRefreshing = true;
        ClearError();

        try
        {
            await RefreshDashboardCoreAsync(
                cancellationToken);

            _initialized = true;

            _lastDashboardLoadUtc =
                DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Dashboard refresh was cancelled.");
        }
        catch (Exception exception)
        {
            SetError(
                "The dashboard could not be refreshed.");

            _logger.LogError(
                exception,
                "Dashboard refresh failed.");
        }
        finally
        {
            IsPageRefreshing = false;

            _refreshGate.Release();
        }
    }

    private async Task RefreshDashboardCoreAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Task.WhenAll(
            LoadTrendingChannelsAsync(
                cancellationToken),

            LoadRecentlyWatchedAsync(
                cancellationToken),

            LoadNewsPageAsync(
                reset: true,
                cancellationToken),

            LoadSocialPreviewAsync(
                cancellationToken),

            LoadLiveMatchAsync(
                cancellationToken));

        await RunOnMainThreadAsync(
            RefreshAiHighlightsAndRecommendation);
    }

    // ============================================================
    // TRENDING CHANNELS
    // ============================================================

    private async Task LoadTrendingChannelsAsync(
        CancellationToken cancellationToken)
    {
        await RunOnMainThreadAsync(
            () => IsChannelsLoading = true);

        try
        {
            var response =
                await _tvApi.GetTrendingAsync(
                    limit: 15);

            cancellationToken.ThrowIfCancellationRequested();

            List<TvChannel> channels =
                response?
                    .OfType<TvChannel>()
                    .Where(channel =>
                        !string.IsNullOrWhiteSpace(
                            GetPlayableStreamUrl(channel)))
                    .ToList()
                ?? [];

            await RunOnMainThreadAsync(
                () =>
                {
                    TrendingChannels.Clear();

                    foreach (TvChannel channel
                             in channels)
                    {
                        TrendingChannels.Add(
                            channel);
                    }

                    SetFeaturedChannel(
                        channels.FirstOrDefault());

                    OnPropertyChanged(
                        nameof(IsChannelsVisible));
                });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Trending channels could not be loaded.");
        }
        finally
        {
            await RunOnMainThreadAsync(
                () =>
                {
                    IsChannelsLoading = false;

                    OnPropertyChanged(
                        nameof(IsChannelsVisible));
                });
        }
    }

    private static string? GetPlayableStreamUrl(
        TvChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (!string.IsNullOrWhiteSpace(
                channel.StreamUrl))
        {
            return channel.StreamUrl.Trim();
        }

        if (!string.IsNullOrWhiteSpace(
                channel.Url))
        {
            return channel.Url.Trim();
        }

        return null;
    }

    // ============================================================
    // RECENTLY WATCHED
    // ============================================================

    private async Task LoadRecentlyWatchedAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            List<TvChannel> recent =
                _recentlyWatched
                    .GetRecent(limit: 8)
                    .OfType<TvChannel>()
                    .Where(channel =>
                        !string.IsNullOrWhiteSpace(
                            GetPlayableStreamUrl(channel)))
                    .ToList();

            cancellationToken.ThrowIfCancellationRequested();

            await RunOnMainThreadAsync(
                () =>
                {
                    RecentlyWatchedChannels.Clear();

                    foreach (TvChannel channel
                             in recent)
                    {
                        RecentlyWatchedChannels.Add(
                            channel);
                    }

                    HasRecentlyWatched =
                        RecentlyWatchedChannels.Count > 0;
                });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Recently watched channels could not be loaded.");
        }
    }

    // ============================================================
    // NEWS
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private Task RefreshNewsAsync(
        CancellationToken cancellationToken)
    {
        return LoadNewsPageAsync(
            reset: true,
            cancellationToken);
    }

    [RelayCommand(
        AllowConcurrentExecutions = false,
        CanExecute = nameof(CanLoadMoreNews))]
    private Task LoadMoreNewsAsync(
        CancellationToken cancellationToken)
    {
        return LoadNewsPageAsync(
            reset: false,
            cancellationToken);
    }

    private async Task LoadNewsPageAsync(
        bool reset,
        CancellationToken cancellationToken)
    {
        await _newsGate.WaitAsync(
            cancellationToken);

        try
        {
            if (reset)
            {
                await RunOnMainThreadAsync(
                    () =>
                    {
                        IsNewsLoading = true;
                        IsLoadingMore = false;

                        HasNewsError = false;
                        IsNewsEmpty = false;

                        NewsErrorMessage =
                            string.Empty;

                        _newsPage = 1;
                        HasMoreNews = true;

                        NewsItems.Clear();
                        _pendingNews.Clear();

                        NotifyNewsStateChanged();
                    });
            }
            else
            {
                if (!CanLoadMoreNews)
                {
                    return;
                }

                await RunOnMainThreadAsync(
                    () => IsLoadingMore = true);
            }

            int requestedPage =
                reset
                    ? 1
                    : _newsPage;

            var response =
                await _newsApi.GetNewsAsync(
                    page: requestedPage,
                    pageSize: NewsPageSize);

            cancellationToken.ThrowIfCancellationRequested();

            List<NewsArticle> articles =
                response?
                    .OfType<NewsArticle>()
                    .ToList()
                ?? [];

            await RunOnMainThreadAsync(
                () =>
                {
                    if (reset)
                    {
                        NewsItems.Clear();
                        _pendingNews.Clear();
                    }

                    // Stage instead of adding: one card realizes per dispatcher
                    // pass, so no single UI-thread block grows with the page.
                    foreach (NewsArticle article in articles)
                    {
                        if (ContainsNewsArticle(article) ||
                            _pendingNews.Any(pending =>
                                string.Equals(
                                    pending.Url,
                                    article.Url,
                                    StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        _pendingNews.Enqueue(article);
                    }

                    StartDripFill();

                    HasMoreNews =
                        articles.Count >=
                        NewsPageSize;

                    if (articles.Count > 0)
                    {
                        _newsPage =
                            requestedPage + 1;
                    }

                    IsNewsEmpty =
                        NewsItems.Count == 0;

                    HasNewsError = false;

                    NotifyNewsStateChanged();
                });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RunOnMainThreadAsync(
                () =>
                {
                    if (reset)
                    {
                        NewsItems.Clear();

                        HasNewsError = true;
                        IsNewsEmpty = false;
                        HasMoreNews = false;

                        NewsErrorMessage =
                            "Headlines could not be loaded. " +
                            "Check your connection and try again.";
                    }
                    else
                    {
                        SetError(
                            "More news could not be loaded.");
                    }

                    NotifyNewsStateChanged();
                });

            _logger.LogError(
                exception,
                "News loading failed. Reset: {Reset}",
                reset);
        }
        finally
        {
            await RunOnMainThreadAsync(
                () =>
                {
                    IsNewsLoading = false;
                    IsLoadingMore = false;

                    NotifyNewsStateChanged();
                });

            _newsGate.Release();
        }
    }

    private bool ContainsNewsArticle(
        NewsArticle candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (!string.IsNullOrWhiteSpace(
                candidate.Url))
        {
            return NewsItems.Any(existing =>
                string.Equals(
                    existing.Url,
                    candidate.Url,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(
                candidate.Title))
        {
            return NewsItems.Any(existing =>
                string.Equals(
                    existing.Title,
                    candidate.Title,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    existing.Source,
                    candidate.Source,
                    StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private void NotifyNewsStateChanged()
    {
        OnPropertyChanged(
            nameof(IsNewsVisible));

        OnPropertyChanged(
            nameof(CanLoadMoreNews));

        LoadMoreNewsCommand
            .NotifyCanExecuteChanged();
    }

    // ============================================================
    // SOCIAL PREVIEW
    // ============================================================

    private async Task LoadSocialPreviewAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var response =
                await _socialService.GetPostsAsync(
                    filter: "Trending",
                    page: 1,
                    pageSize: 4);

            cancellationToken.ThrowIfCancellationRequested();

            List<SocialPost> posts =
                response?
                    .OfType<SocialPost>()
                    .Take(4)
                    .ToList()
                ?? [];

            await RunOnMainThreadAsync(
                () =>
                {
                    SocialPostsPreview.Clear();

                    foreach (SocialPost post
                             in posts)
                    {
                        SocialPostsPreview.Add(
                            post);
                    }

                    HasSocialPreview =
                        SocialPostsPreview.Count > 0;
                });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RunOnMainThreadAsync(
                () =>
                {
                    SocialPostsPreview.Clear();
                    HasSocialPreview = false;
                });

            _logger.LogError(
                exception,
                "Social preview could not be loaded.");
        }
    }

    // ============================================================
    // LIVE MATCH
    // ============================================================

    private async Task LoadLiveMatchAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            LiveMatch? match =
                await _footballApi
                    .GetCurrentLiveMatchAsync();

            _currentLiveMatch = match;

            cancellationToken.ThrowIfCancellationRequested();

            if (match is null)
            {
                await RunOnMainThreadAsync(
                    ClearLiveMatch);

                return;
            }

            string[] scores =
                (match.Score ?? "0-0")
                    .Split(
                        '-',
                        2,
                        StringSplitOptions.TrimEntries);

            await RunOnMainThreadAsync(
                () =>
                {
                    HomeTeamName =
                        match.HomeTeam ??
                        string.Empty;

                    AwayTeamName =
                        match.AwayTeam ??
                        string.Empty;

                    HomeTeamScore =
                        scores.Length > 0 &&
                        !string.IsNullOrWhiteSpace(
                            scores[0])
                            ? scores[0]
                            : "0";

                    AwayTeamScore =
                        scores.Length > 1 &&
                        !string.IsNullOrWhiteSpace(
                            scores[1])
                            ? scores[1]
                            : "0";

                    LiveMatchMinute =
                        string.IsNullOrWhiteSpace(
                            match.Minute)
                            ? "LIVE NOW"
                            : match.Minute.Trim();

                    HasLiveMatch =
                        !string.IsNullOrWhiteSpace(
                            HomeTeamName) &&
                        !string.IsNullOrWhiteSpace(
                            AwayTeamName);
                });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RunOnMainThreadAsync(
                ClearLiveMatch);

            _logger.LogError(
                exception,
                "Live football information could not be loaded.");
        }
    }

    private void ClearLiveMatch()
    {
        HasLiveMatch = false;

        HomeTeamName = string.Empty;
        AwayTeamName = string.Empty;

        HomeTeamScore = "0";
        AwayTeamScore = "0";

        LiveMatchMinute = "LIVE";
    }

    // ============================================================
    // SEARCH
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private async Task SearchAsync(
        CancellationToken cancellationToken)
    {
        string query =
            SearchQueryText.Trim();

        if (string.IsNullOrWhiteSpace(query))
        {
            await OpenSearchAsync(
                cancellationToken);

            return;
        }

        var parameters =
            new Dictionary<string, object>
            {
                ["query"] = query
            };

        await NavigateWithDiagnosticsAsync(
            $"///{nameof(SearchPage)}",
            parameters,
            cancellationToken);

        SearchQueryText =
            string.Empty;
    }

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private Task OpenSearchAsync(
        CancellationToken cancellationToken)
    {
        return NavigateWithDiagnosticsAsync(
            $"///{nameof(SearchPage)}",
            parameters: null,
            cancellationToken);
    }

    // ============================================================
    // HEADER NAVIGATION
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private Task OpenNotificationsAsync(
        CancellationToken cancellationToken)
    {
        return SafeNavigateAsync(
            nameof(NotificationsPage),
            parameters: null,
            cancellationToken);
    }

    // ============================================================
    // FEATURED AND LIVE ACTIONS
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private async Task GoToFeaturedAsync(
        CancellationToken cancellationToken)
    {
        if (_featuredChannel is not null)
        {
            await PlayChannelCoreAsync(
                _featuredChannel,
                cancellationToken);

            return;
        }

        await NavigateCategoryAsync(
            "general",
            "Live TV",
            "#00D6C9",
            cancellationToken);
    }

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private async Task GoToLiveMatchAsync(
        CancellationToken cancellationToken)
    {
        LiveMatch? match = _currentLiveMatch;

        if (match is null)
        {
            // Nothing in play right now - open the football board instead.
            await NavigateRootAsync(
                nameof(FootballPage),
                cancellationToken);

            return;
        }

        try
        {
            FixtureStreamRequest request = new()
            {
                HomeTeam = match.HomeTeam,
                AwayTeam = match.AwayTeam,
                League = match.League,
                KickoffIso = match.KickoffUtc == default
                    ? null
                    : match.KickoffUtc.ToString("O"),
                BackendStreamUrl = match.StreamUrl,
                BackendStreamKind = match.StreamKind,
                BackendStreamEmbedUrl = match.StreamEmbedUrl,
                BackendStreamLabel = match.StreamLabel
            };

            LiveResolution resolution = await _liveResolver
                .ResolveAsync(request, cancellationToken);

            string title = $"{match.HomeTeam} vs {match.AwayTeam}";

            _liveSession.SetPending(new PendingLivePlayback
            {
                Title = title,
                League = match.League,
                Candidates = resolution.Candidates
            });

            await MainThread.InvokeOnMainThreadAsync(
                () => Shell.Current!.GoToAsync(nameof(PlayerPage)));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Opening the live match failed; opening the football board instead.");

            await NavigateRootAsync(
                nameof(FootballPage),
                cancellationToken);
        }
    }

    // ============================================================
    // PRIMARY NAVIGATION
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private Task GoToAllAsync(
        CancellationToken cancellationToken)
    {
        return NavigateCategoryAsync(
            "general",
            "Live TV",
            "#00D6C9",
            cancellationToken);
    }

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private Task GoToSportsAsync(
        CancellationToken cancellationToken)
    {
        return NavigateRootAsync(
            "SportsPage",
            cancellationToken);
    }

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private Task GoToNewsAsync(
        CancellationToken cancellationToken)
    {
        return NavigateRootAsync(
            "NewsPage",
            cancellationToken);
    }

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private Task GoToMoviesAsync(
        CancellationToken cancellationToken)
    {
        return NavigateRootAsync(
            nameof(MoviesPage),
            cancellationToken);
    }

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private Task GoToMusicAsync(
        CancellationToken cancellationToken)
    {
        return NavigateRootAsync(
            nameof(MusicPage),
            cancellationToken);
    }

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private Task GoToSocialAsync(
        CancellationToken cancellationToken)
    {
        return NavigateRootAsync(
            nameof(SocialPage),
            cancellationToken);
    }

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private Task GoToDonationAsync(
        CancellationToken cancellationToken)
    {
        return NavigateRootAsync(
            nameof(DonationPage),
            cancellationToken);
    }

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private Task GoToRecentlyWatchedAsync(
        CancellationToken cancellationToken)
    {
        return NavigateRootAsync(
            nameof(RecentlyWatchedPage),
            cancellationToken);
    }

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private Task GoToAllChannelsAsync(
        CancellationToken cancellationToken)
    {
        return NavigateCategoryAsync(
            "general",
            "Live TV",
            "#00D6C9",
            cancellationToken);
    }

    // ============================================================
    // FEATURE CARDS
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private async Task OpenDashboardFeatureAsync(
        DashboardFeature? feature,
        CancellationToken cancellationToken)
    {
        if (feature is null)
        {
            return;
        }

        if (feature.IsCategory ||
            string.Equals(
                feature.Kind,
                "category",
                StringComparison.OrdinalIgnoreCase))
        {
            await NavigateCategoryAsync(
                feature.Category,
                feature.Title,
                feature.Accent,
                cancellationToken);

            return;
        }

        if (string.IsNullOrWhiteSpace(
                feature.Route))
        {
            return;
        }

        await NavigateRootAsync(
            feature.Route,
            cancellationToken);
    }

    // ============================================================
    // CHANNEL PLAYBACK
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private Task PlayChannelAsync(
        TvChannel? channel,
        CancellationToken cancellationToken)
    {
        return PlayChannelCoreAsync(
            channel,
            cancellationToken);
    }

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private Task PlayRecentAsync(
        TvChannel? channel,
        CancellationToken cancellationToken)
    {
        return PlayChannelCoreAsync(
            channel,
            cancellationToken);
    }

    private async Task PlayChannelCoreAsync(
        TvChannel? channel,
        CancellationToken cancellationToken)
    {
        if (channel is null)
        {
            SetError(
                "The selected channel is unavailable.");

            return;
        }

        string? streamUrl =
            GetPlayableStreamUrl(
                channel);

        if (string.IsNullOrWhiteSpace(
                streamUrl))
        {
            SetError(
                "This channel does not contain a playable stream.");

            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        ClearError();

        _recentlyWatched.AddOrUpdate(
            channel);

        var parameters =
            new Dictionary<string, object>
            {
                ["streamUrl"] = streamUrl,

                ["title"] =
                    string.IsNullOrWhiteSpace(
                        channel.Name)
                        ? "Live Channel"
                        : channel.Name.Trim()
            };

        await NavigateWithDiagnosticsAsync(
            $"///{nameof(PlayerPage)}",
            parameters,
            cancellationToken);
    }

    // ============================================================
    // NEWS NAVIGATION
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private async Task OpenNewsItemAsync(
        NewsArticle? item,
        CancellationToken cancellationToken)
    {
        if (item is null ||
            string.IsNullOrWhiteSpace(
                item.Url))
        {
            SetError(
                "This news article does not have a valid address.");

            return;
        }

        try
        {
            var url = item.Url.Trim();

            await Browser.Default.OpenAsync(
                new Uri(url),
                BrowserLaunchMode.SystemPreferred);

            _logger.LogInformation(
                "Opened news article in the system browser: {Url}",
                url);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // The user navigated away before the browser opened.
        }
        catch (Exception exception)
        {
            SetError(
                "The article could not be opened in your browser.");

            _logger.LogError(
                exception,
                "Failed to open a news article in the browser.");
        }
    }

    // ============================================================
    // SOCIAL NAVIGATION
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private Task OpenComposePostAsync(
        CancellationToken cancellationToken)
    {
        return SafeNavigateAsync(
            nameof(ComposePostPage),
            parameters: null,
            cancellationToken);
    }

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private Task OpenPostAsync(
        SocialPost? post,
        CancellationToken cancellationToken)
    {
        if (post is null)
        {
            return Task.CompletedTask;
        }

        var parameters =
            new Dictionary<string, object>
            {
                ["postId"] = post.Id
            };

        return SafeNavigateAsync(
            nameof(PostDetailPage),
            parameters,
            cancellationToken);
    }

    // ============================================================
    // NAVIGATION HELPERS
    // ============================================================

    private Task NavigateRootAsync(
        string route,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return Task.CompletedTask;
        }

        string normalizedRoute =
            route.Trim()
                .TrimStart('/');

        // Absolute navigation: from the Home tab these destinations are
        // top-level Shell items/tabs (e.g. FootballPage, SportsPage,
        // SocialPage). A bare relative route silently fails to resolve
        // against the current stack, so we always target the absolute
        // shell location. "///" mirrors how the app itself opens Home.
        return NavigateWithDiagnosticsAsync(
            $"///{normalizedRoute}",
            parameters: null,
            cancellationToken);
    }

    /// <summary>
    /// Runs a Shell navigation while capturing failures, so a dead tap
    /// is never silent: the error is logged and shown on the dashboard.
    /// </summary>
    private async Task NavigateWithDiagnosticsAsync(
        string route,
        IDictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Dashboard navigation -> {Route}",
            route);

        try
        {
            await SafeNavigateAsync(
                route,
                parameters,
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Dashboard navigation to {Route} failed.",
                route);

            await RunOnMainThreadAsync(
                () => SetError(
                    "That section could not be opened. Please try again."));
        }
    }

    private Task NavigateCategoryAsync(
        string? category,
        string? title,
        string? accent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return Task.CompletedTask;
        }

        var parameters =
            new Dictionary<string, object>
            {
                ["category"] =
                    category.Trim()
                        .ToLowerInvariant(),

                ["title"] =
                    string.IsNullOrWhiteSpace(title)
                        ? category.Trim()
                        : title.Trim(),

                ["accent"] =
                    string.IsNullOrWhiteSpace(accent)
                        ? "#00D6C9"
                        : accent.Trim()
            };

        return NavigateWithDiagnosticsAsync(
            nameof(CategoryChannelPage),
            parameters,
            cancellationToken);
    }

    // ============================================================
    // FEATURED CONTENT
    // ============================================================

    private void SetFeaturedChannel(
        TvChannel? channel)
    {
        _featuredChannel =
            channel;

        if (channel is null)
        {
            HasFeaturedContent = true;

            FeaturedTitle =
                "Live television from around the world";

            FeaturedSubtitle =
                "Explore news, sports, movies, music and entertainment.";

            FeaturedImageUrl =
                string.Empty;

            FeaturedCategory =
                "MARKUPTV";

            return;
        }

        HasFeaturedContent = true;

        FeaturedTitle =
            string.IsNullOrWhiteSpace(
                channel.Name)
                ? "Live Channel"
                : channel.Name.Trim();

        FeaturedSubtitle =
            string.IsNullOrWhiteSpace(
                channel.CurrentProgrammeTitle)
                ? "Tap to begin watching this live channel."
                : channel.CurrentProgrammeTitle.Trim();

        FeaturedImageUrl =
            channel.LogoUrl ??
            string.Empty;

        FeaturedCategory =
            string.IsNullOrWhiteSpace(
                channel.Category)
                ? "LIVE"
                : channel.Category
                    .Trim()
                    .ToUpperInvariant();
    }

    // ============================================================
    // CLOCK
    // ============================================================

    private void UpdateClock(
        object? state)
    {
        if (_disposed)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(
            () =>
            {
                if (_disposed)
                {
                    return;
                }

                DateTime now =
                    DateTime.Now;

                CurrentTime =
                    now.ToString("HH:mm");

                CurrentDate =
                    now.ToString(
                        "dddd, MMMM d");

                GreetingMessage =
                    CreateGreeting();
            });
    }

    private static string CreateGreeting()
    {
        int hour =
            DateTime.Now.Hour;

        return hour switch
        {
            < 12 =>
                "Good morning from MarkupTv",

            < 17 =>
                "Good afternoon from MarkupTv",

            _ =>
                "Good evening from MarkupTv"
        };
    }

    // ============================================================
    // DASHBOARD FEATURES
    // ============================================================

    private void BuildDashboardFeatures()
    {
        PrimaryFeatures.Clear();

        PrimaryFeatures.Add(
            Feature(
                "Live TV",
                "Global channels",
                "◆",
                "#E8B54A",
                category: "general"));

        PrimaryFeatures.Add(
            Feature(
                "Sports",
                "Live action",
                "🏆",
                "#FF8A3D",
                route: "SportsPage"));

        PrimaryFeatures.Add(
            Feature(
                "News",
                "Latest headlines",
                "📰",
                "#3A86FF",
                route: "NewsPage"));

        PrimaryFeatures.Add(
            Feature(
                "Movies",
                "Films and series",
                "🎬",
                "#FF3D6E",
                route: nameof(MoviesPage)));

        PrimaryFeatures.Add(
            Feature(
                "Music",
                "Music channels",
                "🎵",
                "#FFBE0B",
                route: nameof(MusicPage)));

        PrimaryFeatures.Add(
            Feature(
                "Football",
                "Matches and scores",
                "⚽",
                "#FF8A3D",
                route: nameof(FootballPage)));

        PrimaryFeatures.Add(
            Feature(
                "Cartoons",
                "Kids and animation",
                "🎈",
                "#FFBE0B",
                route: "CartoonPage"));

        PrimaryFeatures.Add(
            Feature(
                "Discovery",
                "Explore something new",
                "🧭",
                "#00D6C9",
                route: nameof(DiscoveryPage)));

        PrimaryFeatures.Add(
            Feature(
                "Community",
                "Posts and discussions",
                "💬",
                "#8338EC",
                route: nameof(SocialPage)));

        PrimaryFeatures.Add(
            Feature(
                "Recently Watched",
                "Resume watching",
                "🕘",
                "#00D6C9",
                route: nameof(RecentlyWatchedPage)));

        PrimaryFeatures.Add(
            Feature(
                "Search",
                "Find everything",
                "🔍",
                "#6DFF8F",
                route: nameof(SearchPage)));

        PrimaryFeatures.Add(
            Feature(
                "Support",
                "Support MarkUpTV",
                "💎",
                "#FF3D6E",
                route: nameof(DonationPage)));

        CategoryFeatures.Clear();
        _pendingCategories.Clear();

        AddCategory(
            "Business",
            "Markets and work",
            "💼",
            "#3A86FF",
            "business");

        AddCategory(
            "Classic TV",
            "Retro channels",
            "📼",
            "#FFBE0B",
            "classic");

        AddCategory(
            "Comedy",
            "Laugh and enjoy",
            "😂",
            "#FF3D6E",
            "comedy");

        AddCategory(
            "Cooking",
            "Food and recipes",
            "🍳",
            "#FF8A3D",
            "cooking");

        AddCategory(
            "Culture",
            "Arts and society",
            "🏛️",
            "#00D6C9",
            "culture");

        AddCategory(
            "Documentary",
            "Real stories",
            "🎥",
            "#00D6C9",
            "documentary");

        AddCategory(
            "Education",
            "Learning channels",
            "🎓",
            "#3A86FF",
            "education");

        AddCategory(
            "Entertainment",
            "Shows and stars",
            "🎭",
            "#FF3D6E",
            "entertainment");

        AddCategory(
            "Family",
            "Watch together",
            "👪",
            "#6DFF8F",
            "family");

        AddCategory(
            "Science",
            "Science and nature",
            "🔬",
            "#00D6C9",
            "science");

        AddCategory(
            "Series",
            "Episodes and drama",
            "📺",
            "#FF3D6E",
            "series");

        AddCategory(
            "Travel",
            "Places and adventure",
            "✈️",
            "#6DFF8F",
            "travel");

        AddCategory(
            "Weather",
            "Forecasts",
            "⛅",
            "#3A86FF",
            "weather");

        AddCategory(
            "Wildlife",
            "Nature and animals",
            "🦁",
            "#6DFF8F",
            "wildlife");
    }

    private static DashboardFeature Feature(
        string title,
        string subtitle,
        string icon,
        string accent,
        string? route = null,
        string? category = null)
    {
        return new DashboardFeature
        {
            Title = title,
            Subtitle = subtitle,
            Icon = icon,
            Accent = accent,
            Route = route ?? string.Empty,
            Category = category ?? string.Empty,

            Kind =
                category is null
                    ? "route"
                    : "category"
        };
    }

    private void AddCategory(
        string title,
        string subtitle,
        string icon,
        string accent,
        string category)
    {
        _pendingCategories.Enqueue(
            Feature(
                title,
                subtitle,
                icon,
                accent,
                category: category));
    }

    // ============================================================
    // LIFECYCLE
    // ============================================================

    public void OnPageDisappearing()
    {
        InitializeCommand.Cancel();
        PullToRefreshCommand.Cancel();
        RefreshNewsCommand.Cancel();
        LoadMoreNewsCommand.Cancel();

        // Drop any not-yet-realized staged items so a page left mid-fill does
        // not pump stale content when it reappears.
        _pendingCategories.Clear();
        _pendingNews.Clear();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    /// <summary>
    /// Starts the drip-fill pump. Each dispatcher pass moves one staged item
    /// (category card or news headline) into its visible collection, so card
    /// realization never stacks into a multi-second UI-thread block.
    /// </summary>
    private void StartDripFill()
    {
        if (_dripFillActive)
        {
            return;
        }

        _dripFillActive = true;
        MainThread.BeginInvokeOnMainThread(PumpDripFill);
    }

    private void PumpDripFill()
    {
        try
        {
            if (_disposed)
            {
                _dripFillActive = false;
                return;
            }

            DashboardFeature? nextCategory = null;
            NewsArticle? nextNews = null;

            lock (_pendingCategories)
            {
                if (_pendingCategories.Count > 0)
                {
                    nextCategory = _pendingCategories.Dequeue();
                }
            }

            if (nextCategory == null)
            {
                lock (_pendingNews)
                {
                    if (_pendingNews.Count > 0)
                    {
                        nextNews = _pendingNews.Dequeue();
                    }
                }
            }

            if (nextCategory == null && nextNews == null)
            {
                _dripFillActive = false;
                return;
            }

            if (nextCategory != null)
            {
                CategoryFeatures.Add(nextCategory);
            }
            else if (nextNews != null)
            {
                NewsItems.Add(nextNews);
            }

            NotifyNewsStateChanged();

            if (_dripFillActive && !_disposed)
            {
                MainThread.BeginInvokeOnMainThread(PumpDripFill);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PumpDripFill crashed: {ex}");
            _dripFillActive = false;
        }
    }

    private static Task RunOnMainThreadAsync(
        Action action)
    {
        ArgumentNullException.ThrowIfNull(
            action);

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

        OnPageDisappearing();

        _clockTimer.Dispose();

        /*
         * SemaphoreSlim instances are intentionally not disposed here.
         * A cancelled asynchronous command can still be completing its
         * finally block and releasing a gate. Disposing a gate before
         * that completion can produce ObjectDisposedException.
         */

        GC.SuppressFinalize(
            this);
    }
}