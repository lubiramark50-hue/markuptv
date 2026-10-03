using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Models;
using MarkUptv.Pages;
using MarkUptv.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;

namespace MarkUptv.ViewModels;

public partial class FootballViewModel : BaseChannelViewModel
{
    private readonly FootballApiService _footballApi;

    private static readonly TimeSpan AutoRefreshInterval =
        TimeSpan.FromSeconds(60);

    private IDispatcherTimer? _autoRefreshTimer;
    private IDispatcherTimer? _countdownTimer;
    private bool _isRebuildingGroups;

    protected override string Category => "football";
    public override string NowPlayingText => SelectedChannel != null ? $"⚽ {SelectedChannel.Name}" : "Select a football channel";

    [RelayCommand]
    private static void OpenFlyout()
    {
        if (Shell.Current is not null)
        {
            Shell.Current.FlyoutIsPresented = true;
        }
    }


    /// <summary>All matches, most recent kickoff first, as returned by the backend.</summary>
    public ObservableCollection<LiveMatch> LiveMatches { get; } = [];

    /// <summary>The next six fixtures to kick off — the scheduling "UP NEXT" strip.</summary>
    public ObservableCollection<LiveMatch> UpNextMatches { get; } = [];

    public bool HasUpNext => UpNextMatches.Count > 0;

    /// <summary>Matches grouped by competition for the league-by-league board.</summary>
    public ObservableCollection<LeagueGroup> LeagueGroups { get; } = [];

    /// <summary>League filter chips ("All Leagues" plus every competition present).</summary>
    public ObservableCollection<string> LeagueNames { get; } = [];

    /// <summary>The league chip currently selected in the UI.</summary>
    [ObservableProperty]
    private string _selectedLeague = AllLeagues;

    public bool HasLiveMatches => LiveMatches.Count > 0;

    /// <summary>Matches genuinely in play right now (not every fixture on the board).</summary>
    public int LiveNowCount => LiveMatches.Count(m => m.IsLive);

    public bool HasLiveNow => LiveNowCount > 0;

    /// <summary>Spotlight match for the featured hero — the first fixture in play.</summary>
    public LiveMatch? FeaturedMatch => LiveMatches.FirstOrDefault(m => m.IsLive);

    public bool HasFeaturedLive => FeaturedMatch is not null;

    public const string AllLeagues = "All Leagues";

    /// <summary>Pinned first section of the board while any match is in play.</summary>
    public const string LiveNowSection = "\U0001F534 Live Now";

    public FootballViewModel(
        TvApiService tvApi,
        RecentlyWatchedService recentlyWatched,
        PaymentService paymentService,
        ChannelCacheService cache,
        FootballApiService footballApi)
        : base(tvApi, recentlyWatched, paymentService, cache)
    {
        _footballApi = footballApi;
        LiveMatches.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasLiveMatches));
            OnPropertyChanged(nameof(LiveNowCount));
            OnPropertyChanged(nameof(HasLiveNow));
            OnPropertyChanged(nameof(FeaturedMatch));
            OnPropertyChanged(nameof(HasFeaturedLive));
        };
        UpNextMatches.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasUpNext));

        // Match search: the base view model filters channels; we also filter
        // the live-match board so typing a team or league name narrows both.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchText) && !_isRebuildingGroups)
            {
                RebuildLeagueGroups();
            }
        };
    }

    partial void OnSelectedLeagueChanged(string value)
    {
        if (!_isRebuildingGroups)
        {
            RebuildLeagueGroups();
        }
    }

    /// <summary>
    /// Loads both the live-match board and the football channel list.
    /// The generated LoadChannelsCommand (bound to the page) hits this override.
    /// </summary>
    protected override async Task LoadChannelsAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            LoadLiveMatchesCoreAsync(cancellationToken),
            base.LoadChannelsAsync(cancellationToken));
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LoadLiveMatchesAsync(CancellationToken cancellationToken)
        => await LoadLiveMatchesCoreAsync(cancellationToken);

    private async Task LoadLiveMatchesCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var matches = await _footballApi.GetLiveMatchesAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (IsSameBoard(matches))
                {
                    return;
                }

                LiveMatches.Clear();
                foreach (var match in matches)
                    LiveMatches.Add(match);

                TickCountdowns();
                RebuildLeagueGroups();
                RebuildUpNext();
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal page shutdown.
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[FootballViewModel] Live-match loading failed: {exception}");
        }
    }

    /// <summary>
    /// True when the incoming payload is identical to what is already shown,
    /// so silent auto-refresh ticks do not disturb the visible board.
    /// </summary>
    private bool IsSameBoard(List<LiveMatch> matches)
    {
        if (matches.Count != LiveMatches.Count)
        {
            return false;
        }

        for (int i = 0; i < matches.Count; i++)
        {
            var incoming = matches[i];
            var current = LiveMatches[i];

            if (incoming.Id != current.Id ||
                !string.Equals(incoming.Score ?? string.Empty, current.Score ?? string.Empty, StringComparison.Ordinal) ||
                !string.Equals(incoming.Status ?? string.Empty, current.Status ?? string.Empty, StringComparison.Ordinal) ||
                !string.Equals(incoming.StreamUrl ?? string.Empty, current.StreamUrl ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Rebuilds the league sections from <see cref="LiveMatches"/> honouring the
    /// currently selected league chip. Kept on the UI thread — callers must be
    /// on the main thread.
    /// </summary>
    private void RebuildLeagueGroups()
    {
        if (_isRebuildingGroups)
        {
            return;
        }

        _isRebuildingGroups = true;
        try
        {
            RebuildLeagueGroupsCore();
        }
        finally
        {
            _isRebuildingGroups = false;
        }
    }

    private void RebuildLeagueGroupsCore()
    {
        var search = SearchText?.Trim();

        IEnumerable<LiveMatch> boardMatches = LiveMatches;

        if (!string.IsNullOrWhiteSpace(search))
        {
            boardMatches = boardMatches.Where(m =>
                m.HomeTeam.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                m.AwayTeam.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (m.League?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var board = boardMatches.ToList();

        var liveMatches = board
            .Where(m => m.IsLive)
            .OrderBy(m => m.KickoffUtc)
            .ToList();

        var scheduledMatches = board
            .Where(m => !m.IsLive)
            .ToList();

        var hasLive = liveMatches.Count > 0;

        // League sections (in-play matches move into the pinned Live Now
        // section so nothing appears twice).
        var leagueGroups = scheduledMatches
            .GroupBy(m => LeagueNameOf(m.League))
            .Select(g => new LeagueGroup
            {
                League = g.Key,
                Matches = g
                    .OrderBy(m => m.KickoffUtc)
                    .ThenBy(m => m.HomeTeam, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .OrderBy(g => LeagueRank(g.League))
            .ThenBy(g => g.League, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var liveNowGroup = hasLive
            ? new LeagueGroup
            {
                League = LiveNowSection,
                Matches = liveMatches
            }
            : null;

        // Filter chips: All Leagues, [Live Now], then every league present
        // (live fixtures included) in board order.
        var names = new List<string> { AllLeagues };

        if (hasLive)
        {
            names.Add(LiveNowSection);
        }

        var allLeagueNames = board
            .Select(m => LeagueNameOf(m.League))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(LeagueRank)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase);

        names.AddRange(allLeagueNames);

        if (!names.Contains(SelectedLeague, StringComparer.OrdinalIgnoreCase))
        {
            SelectedLeague = AllLeagues;
        }

        if (!LeagueNames.SequenceEqual(names, StringComparer.OrdinalIgnoreCase))
        {
            LeagueNames.Clear();
            foreach (var name in names)
                LeagueNames.Add(name);
        }

        List<LeagueGroup> visible;

        if (SelectedLeague.Equals(LiveNowSection, StringComparison.OrdinalIgnoreCase))
        {
            visible = liveNowGroup is not null
                ? [liveNowGroup]
                : [];
        }
        else if (SelectedLeague.Equals(AllLeagues, StringComparison.OrdinalIgnoreCase))
        {
            visible = new List<LeagueGroup>();
            if (liveNowGroup is not null)
                visible.Add(liveNowGroup);
            visible.AddRange(leagueGroups);
        }
        else
        {
            // A specific league chip shows everything in that competition,
            // including matches that are in play right now.
            var matches = board
                .Where(m => LeagueNameOf(m.League)
                    .Equals(SelectedLeague, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.IsLive)
                .ThenBy(m => m.KickoffUtc)
                .ToList();

            visible = matches.Count > 0
                ? [new LeagueGroup { League = SelectedLeague, Matches = matches }]
                : [];
        }

        var same = LeagueGroups.Count == visible.Count &&
                   LeagueGroups.Select(g => g.League).SequenceEqual(visible.Select(g => g.League), StringComparer.OrdinalIgnoreCase) &&
                   visible.All(g =>
                   {
                       var shown = LeagueGroups.FirstOrDefault(x => x.League.Equals(g.League, StringComparison.OrdinalIgnoreCase));
                       return shown is not null &&
                              shown.MatchCount == g.MatchCount &&
                              shown.LiveCount == g.LiveCount;
                   });

        if (same)
        {
            return;
        }

        LeagueGroups.Clear();
        foreach (var group in visible)
            LeagueGroups.Add(group);

        RebuildUpNext();
    }

    /// <summary>
    /// Fills the "UP NEXT" strip with the six nearest kick-offs, honouring
    /// the current search. Kept on the UI thread.
    /// </summary>
    private void RebuildUpNext()
    {
        var search = SearchText?.Trim();
        var nowUtc = DateTime.UtcNow;

        var upcoming = LiveMatches
            .Where(m => !m.IsLive)
            .Where(m => m.KickoffUtc > nowUtc.AddMinutes(-5))
            .Where(m =>
                string.IsNullOrWhiteSpace(search) ||
                m.HomeTeam.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                m.AwayTeam.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (m.League?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(m => m.KickoffUtc)
            .Take(6)
            .ToList();

        if (upcoming.Count == UpNextMatches.Count &&
            upcoming.SequenceEqual(UpNextMatches))
        {
            return;
        }

        UpNextMatches.Clear();
        foreach (var match in upcoming)
            UpNextMatches.Add(match);
    }

    /// <summary>
    /// Friendly, stable display names so the board reads "Premier League",
    /// "La Liga", "Serie A" etc. regardless of how the upstream feed spells them.
    /// </summary>
    private static string LeagueNameOf(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Other";
        }

        var lower = raw.Trim().ToLowerInvariant();

        if (lower.Contains("premier league", StringComparison.Ordinal))
            return "Premier League";
        if (lower.Contains("la liga", StringComparison.Ordinal) || lower.Contains("laliga", StringComparison.Ordinal))
            return "La Liga";
        if (lower.Contains("serie b", StringComparison.Ordinal))
            return "Serie B";
        if (lower.Contains("serie a", StringComparison.Ordinal))
            return "Serie A";
        if (lower.Contains("bundesliga", StringComparison.Ordinal))
            return "Bundesliga";
        if (lower.Contains("ligue 1", StringComparison.Ordinal))
            return "Ligue 1";
        if (lower.Contains("champions league", StringComparison.Ordinal))
            return "Champions League";
        if (lower.Contains("europa league", StringComparison.Ordinal))
            return "Europa League";
        if (lower.Contains("conference league", StringComparison.Ordinal))
            return "Conference League";
        if (lower.Contains("championship", StringComparison.Ordinal))
            return "EFL Championship";
        if (lower.Contains("eredivisie", StringComparison.Ordinal))
            return "Eredivisie";
        if (lower.Contains("primeira", StringComparison.Ordinal))
            return "Primeira Liga";
        if (lower.Contains("mls", StringComparison.Ordinal))
            return "MLS";
        if (lower.Contains("süper lig", StringComparison.Ordinal) || lower.Contains("super lig", StringComparison.Ordinal))
            return "Süper Lig";
        if (lower.Contains("allsvenskan", StringComparison.Ordinal))
            return "Allsvenskan";
        if (lower.Contains("eliteserien", StringComparison.Ordinal))
            return "Eliteserien";
        if (lower.Contains("superliga", StringComparison.Ordinal))
            return "Superliga";
        if (lower.Contains("serie", StringComparison.Ordinal))
            return "Other Series";
        if (lower.Contains("league", StringComparison.Ordinal) || lower.Contains("liga", StringComparison.Ordinal) ||
            lower.Contains("cup", StringComparison.Ordinal) || lower.Contains("copa", StringComparison.Ordinal))
            return raw.Trim();

        return raw.Trim();
    }

    private static int LeagueRank(string league)
    {
        return league switch
        {
            "\U0001F534 Live Now" => -1,
            "Premier League" => 0,
            "La Liga" => 1,
            "Serie A" => 2,
            "Bundesliga" => 3,
            "Ligue 1" => 4,
            "Champions League" => 5,
            "Europa League" => 6,
            "Conference League" => 7,
            "Other" => 1000,
            _ => 100
        };
    }

    // =====================================================================
    // AUTO-REFRESH (kept alive only while the page is on screen)
    // =====================================================================

    public void StartAutoRefresh()
    {
        StopAutoRefresh();

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _autoRefreshTimer = dispatcher.CreateTimer();
        _autoRefreshTimer.Interval = AutoRefreshInterval;
        _autoRefreshTimer.IsRepeating = true;
        _autoRefreshTimer.Tick += OnAutoRefreshTick;
        _autoRefreshTimer.Start();

        // A faster tick just for the kick-off countdowns so the schedule
        // always reads "in 2h 05m", not a stale snapshot.
        _countdownTimer = dispatcher.CreateTimer();
        _countdownTimer.Interval = TimeSpan.FromSeconds(30);
        _countdownTimer.IsRepeating = true;
        _countdownTimer.Tick += OnCountdownTick;
        _countdownTimer.Start();
    }

    public void StopAutoRefresh()
    {
        if (_autoRefreshTimer is not null)
        {
            _autoRefreshTimer.Tick -= OnAutoRefreshTick;
            _autoRefreshTimer.Stop();
            _autoRefreshTimer = null;
        }

        if (_countdownTimer is not null)
        {
            _countdownTimer.Tick -= OnCountdownTick;
            _countdownTimer.Stop();
            _countdownTimer = null;
        }
    }

    private void OnAutoRefreshTick(object? sender, EventArgs e)
    {
        if (!LoadLiveMatchesCommand.CanExecute(null))
        {
            return;
        }

        _ = LoadLiveMatchesCommand.ExecuteAsync(null);
    }

    private void OnCountdownTick(object? sender, EventArgs e)
        => TickCountdowns();

    /// <summary>
    /// Refreshes the countdown/day labels on every scheduled match in place.
    /// Must be called on the UI thread.
    /// </summary>
    private void TickCountdowns()
    {
        var now = DateTime.Now;

        foreach (var match in LiveMatches)
        {
            if (match.IsLive)
            {
                match.CountdownText = null;
                continue;
            }

            match.CountdownText = BuildCountdown(match, now);
            match.DayLabel = BuildDayLabel(match, now);
        }
    }

    private static string BuildCountdown(LiveMatch match, DateTime now)
    {
        var kickoff = match.KickoffUtc.ToLocalTime();
        var remaining = kickoff - now;

        if (remaining <= TimeSpan.Zero)
        {
            return "Kick-off";
        }

        if (remaining.TotalMinutes < 1)
        {
            return "Kick-off";
        }

        if (remaining.TotalMinutes < 60)
        {
            return $"in {Math.Max(1, (int)remaining.TotalMinutes)}m";
        }

        var hours = (int)remaining.TotalHours;
        return $"in {hours}h {remaining.Minutes:00}m";
    }

    private static string BuildDayLabel(LiveMatch match, DateTime now)
    {
        var kickoff = match.KickoffUtc.ToLocalTime();
        var today = now.Date;

        if (kickoff.Date == today)
        {
            return "TODAY";
        }

        if (kickoff.Date == today.AddDays(1))
        {
            return "TOMORROW";
        }

        return kickoff.ToString("ddd dd MMM").ToUpperInvariant();
    }

    // =====================================================================
    // WATCHING
    // =====================================================================

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task WatchLiveMatch(LiveMatch? match)
    {
        if (match is null)
        {
            return;
        }

        if (!match.HasStream)
        {
            if (match.IsLive)
            {
                await SetErrorMessageAsync(
                    "No broadcast found for this match yet — searching again…");

                _ = RetryStreamLookupAsync();
            }
            else
            {
                await SetErrorMessageAsync(
                    "The stream appears here once the match goes live — keep this page open.");
            }

            return;
        }

        try
        {
            var paymentStatus = await PaymentService.GetStatusAsync(useCache: true);
            if (paymentStatus?.CanWatch != true)
            {
                if (Shell.Current is not null)
                {
                    await MainThread.InvokeOnMainThreadAsync(
                        () => Shell.Current.GoToAsync("///PaymentRequiredPage"));
                }

                return;
            }

            var title = $"{match.HomeTeam} vs {match.AwayTeam}";

            if (string.Equals(match.StreamKind, "youtube", StringComparison.OrdinalIgnoreCase))
            {
                var embedUrl = !string.IsNullOrWhiteSpace(match.StreamEmbedUrl)
                    ? match.StreamEmbedUrl
                    : match.StreamUrl;

                var webRoute =
                    $"{nameof(WebViewPage)}?pageTitle={Uri.EscapeDataString(title)}" +
                    $"&url={Uri.EscapeDataString(embedUrl!)}";

                await MainThread.InvokeOnMainThreadAsync(
                    () => Shell.Current!.GoToAsync(webRoute));
            }
            else
            {
                var playerRoute =
                    $"///{nameof(PlayerPage)}?title={Uri.EscapeDataString(title)}" +
                    $"&streamUrl={Uri.EscapeDataString(match.StreamUrl!)}";

                await MainThread.InvokeOnMainThreadAsync(
                    () => Shell.Current!.GoToAsync(playerRoute));
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[FootballViewModel] Live-match watch failed: {exception}");

            await SetErrorMessageAsync("The live stream could not be opened.");
        }
    }

    private async Task RetryStreamLookupAsync()
    {
        try
        {
            // Give the backend a moment to resolve and cache the broadcast.
            await Task.Delay(TimeSpan.FromSeconds(2.5));

            if (!LoadLiveMatchesCommand.CanExecute(null))
            {
                return;
            }

            await LoadLiveMatchesCommand.ExecuteAsync(null);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[FootballViewModel] Stream re-check failed: {exception}");
        }
    }

    private Task SetErrorMessageAsync(string message)
        => MainThread.InvokeOnMainThreadAsync(() => ErrorMessage = message);
}