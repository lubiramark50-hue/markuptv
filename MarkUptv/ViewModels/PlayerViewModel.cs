using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Models;
using MarkUptv.Serialization;
using MarkUptv.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;

namespace MarkUptv.ViewModels;

/// <summary>
/// Drives the live player: holds the ordered playback plan for a fixture,
/// switches sources automatically when one fails, and exposes honest status
/// to the viewer instead of a black screen.
/// </summary>
public partial class PlayerViewModel : BaseViewModel, IQueryAttributable
{
    private readonly LiveResolutionService _resolver;
    private readonly LiveSessionService _session;
    private readonly ILogger<PlayerViewModel> _logger;
    private readonly List<StreamCandidate> _plan = new();
    private readonly System.Diagnostics.Stopwatch _startWatch = new();

    private int _planIndex = -1;
    private bool _selfTestRequested;
    private bool _started;

    public PlayerViewModel(
        LiveResolutionService resolver,
        LiveSessionService session,
        ILogger<PlayerViewModel> logger)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Raised when the page must hand a direct stream to the player.</summary>
    public event EventHandler<StreamCandidate>? PlayRequested;

    /// <summary>Raised when the page must show an in-app official page instead.</summary>
    public event EventHandler<StreamCandidate>? BrowseRequested;

    public ObservableCollection<StreamCandidate> Sources { get; } = new();

    [ObservableProperty]
    private string _title = "Live Player";

    [ObservableProperty]
    private string _statusText = "Preparing player…";

    [ObservableProperty]
    private string _statusDetail = string.Empty;

    [ObservableProperty]
    private string _sourceLabel = string.Empty;

    [ObservableProperty]
    private string _sourceMeta = string.Empty;

    [ObservableProperty]
    private string _positionText = string.Empty;

    [ObservableProperty]
    private string _modeBadge = "LIVE";

    [ObservableProperty]
    private string _league = string.Empty;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isVideoVisible;

    [ObservableProperty]
    private bool _isWebVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWebUrl))]
    private string? _webUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoSource))]
    private bool _hasAnySource;

    [ObservableProperty]
    private bool _isSelfTest;

    [ObservableProperty]
    private bool _canGoNext;

    [ObservableProperty]
    private string _nextLabel = "Next source";

    [ObservableProperty]
    private string _errorTitle = string.Empty;

    [ObservableProperty]
    private string _errorHint = string.Empty;

    public bool HasWebUrl => !string.IsNullOrWhiteSpace(WebUrl);

    public bool HasNoSource => !HasAnySource;

    // ------------------------------------------------------------------
    // Query + startup
    // ------------------------------------------------------------------

    private string? _queryTitle;
    private string? _queryStreamUrl;
    private string? _querySourceLabel;
    private string? _queryCandidates;
    private string? _queryHome;
    private string? _queryAway;
    private string? _queryLeague;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        string Decode(string key)
            => query.TryGetValue(key, out object? value)
                ? Uri.UnescapeDataString(value?.ToString() ?? string.Empty)
                : string.Empty;

        _queryTitle = Decode("title");
        _queryStreamUrl = Decode("streamUrl");
        _querySourceLabel = Decode("sourceLabel");
        _queryCandidates = Decode("candidates");
        _queryHome = Decode("home");
        _queryAway = Decode("away");
        _queryLeague = Decode("league");
        _selfTestRequested = Decode("selfTest") == "1";

        if (!string.IsNullOrWhiteSpace(_queryTitle))
        {
            Title = _queryTitle!;
        }

        League = string.IsNullOrWhiteSpace(_queryLeague)
            ? string.Empty
            : _queryLeague!;
    }

    /// <summary>
    /// Clears state from the previous visit. The player page is a singleton, so
    /// each navigation starts a fresh session on the same instance.
    /// </summary>
    public void PrepareForNewSession()
    {
        _started = false;
        _planIndex = -1;
        _selfTestRequested = false;

        Sources.Clear();

        HasAnySource = false;
        IsConnecting = false;
        IsPlaying = false;
        IsVideoVisible = false;
        IsWebVisible = false;
        WebUrl = null;
        CanGoNext = false;
        IsSelfTest = false;
        ModeBadge = "LIVE";
        SourceLabel = string.Empty;
        SourceMeta = string.Empty;
        PositionText = string.Empty;
        StatusText = "Preparing playerâ€¦";
        StatusDetail = string.Empty;

        ClearError();
    }

    /// <summary>Resolves sources (when needed) and starts playback.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        IsLoading = true;
        IsConnecting = true;
        ClearError();
        StatusText = "Finding a live source…";
        StatusDetail = string.Empty;

        try
        {
            List<StreamCandidate> plan = new();

            // The calling screen hands the resolved plan over in-process.
            // Navigation query strings proved unreliable for carrying a
            // multi-kilobyte candidate list in this app.
            if (_session.TryTake(out PendingLivePlayback? pending) && pending is not null)
            {
                if (!string.IsNullOrWhiteSpace(pending.Title))
                {
                    Title = pending.Title;
                }

                if (!string.IsNullOrWhiteSpace(pending.League))
                {
                    League = pending.League!;
                }

                if (pending.IsSelfTest)
                {
                    IsSelfTest = true;
                    ModeBadge = "SELF-TEST";
                }

                plan.AddRange(pending.Candidates);
            }
            else if (_selfTestRequested)
            {
                IsSelfTest = true;
                ModeBadge = "SELF-TEST";
                StatusText = "Running the playback self-test…";

                LiveResolution selfTest = await _resolver
                    .BuildSelfTestAsync(cancellationToken)
                    .ConfigureAwait(false);

                plan.AddRange(selfTest.Candidates);
            }
            else if (!string.IsNullOrWhiteSpace(_queryCandidates))
            {
                plan.AddRange(DeserializeCandidates(_queryCandidates!));
            }

            if (plan.Count == 0)
            {
                FixtureStreamRequest request = new()
                {
                    HomeTeam = _queryHome ?? string.Empty,
                    AwayTeam = _queryAway ?? string.Empty,
                    League = string.IsNullOrWhiteSpace(_queryLeague) ? null : _queryLeague,
                    BackendStreamUrl = string.IsNullOrWhiteSpace(_queryStreamUrl) ? null : _queryStreamUrl,
                    BackendStreamKind = "hls",
                    BackendStreamLabel = string.IsNullOrWhiteSpace(_querySourceLabel) ? null : _querySourceLabel
                };

                bool channelMode = string.IsNullOrWhiteSpace(request.League) &&
                                   string.IsNullOrWhiteSpace(request.AwayTeam);

                LiveResolution resolution = channelMode
                    ? await _resolver.ResolveChannelAsync(
                        request.HomeTeam,
                        request.BackendStreamUrl,
                        "hls",
                        cancellationToken).ConfigureAwait(false)
                    : await _resolver.ResolveAsync(request, cancellationToken).ConfigureAwait(false);

                plan.AddRange(resolution.Candidates);
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Sources.Clear();

                foreach (StreamCandidate candidate in plan)
                {
                    Sources.Add(candidate);
                }

                HasAnySource = plan.Count > 0;

                if (plan.Count == 0)
                {
                    ShowUnavailable(
                        "No free live source for this match",
                        "Nothing free and official is publishing this fixture right now. " +
                        "Try the schedule companion below, or come back closer to kick-off.");
                }
            });

            if (plan.Count > 0)
            {
                await PlayAtAsync(0).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Player startup failed");

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ShowUnavailable(
                    "The player could not start",
                    "Something went wrong while preparing the stream. Try again.");
            });
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsLoading = false);
        }
    }

    // ------------------------------------------------------------------
    // Playback control
    // ------------------------------------------------------------------

    private Task PlayAtAsync(int index)
        => MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (index < 0 || index >= Sources.Count)
            {
                return;
            }

            _planIndex = index;
            StreamCandidate candidate = Sources[index];

            ClearError();
            SourceLabel = candidate.Source.Label;
            SourceMeta = candidate.Source.OriginLine;
            PositionText = $"Source {index + 1} of {Sources.Count}";
            ModeBadge = candidate.Mode switch
            {
                "hls" => "LIVE",
                "embed" => "IN-APP",
                _ => "OFFICIAL"
            };

            CanGoNext = index < Sources.Count - 1;
            NextLabel = CanGoNext
                ? $"Next source ({index + 2}/{Sources.Count})"
                : "Next source";

            if (candidate.Mode == "hls")
            {
                IsWebVisible = false;
                WebUrl = null;
                IsVideoVisible = true;
                IsConnecting = true;
                IsPlaying = false;
                StatusText = candidate.Verified
                    ? $"Connecting to {candidate.Source.Label}…"
                    : $"Trying {candidate.Source.Label}…";
                StatusDetail = candidate.Verified
                    ? $"Verified {(candidate.LatencyMs > 0 ? $"{candidate.LatencyMs} ms · " : string.Empty)}free source"
                    : "Unverified source, connecting now";

                PlayRequested?.Invoke(this, candidate);

                // Player start latency evidence: restarted for every attempt and
                // logged when the media surface reports success.
                _startWatch.Restart();
            }
            else
            {
                // Official free player / embed: render inside the app so the
                // viewer never lands on a dead end.
                IsVideoVisible = false;
                IsConnecting = false;
                IsPlaying = false;
                IsWebVisible = true;
                WebUrl = candidate.ResolvedUrl;
                StatusText = candidate.Mode == "embed"
                    ? "Playing on the official in-app player"
                    : $"Opened {candidate.Source.Channel}";
                StatusDetail = candidate.Source.Legality;

                BrowseRequested?.Invoke(this, candidate);
            }
        });

    /// <summary>Called by the page when the media surface reports success.</summary>
    public void ReportOpened()
        => MainThread.BeginInvokeOnMainThread(() =>
        {
            IsConnecting = false;
            IsPlaying = true;
            StatusText = "Live now";

            if (_startWatch.IsRunning)
            {
                _startWatch.Stop();

                _logger.LogInformation(
                    "Player start latency: {ElapsedMs} ms for {Source}",
                    _startWatch.ElapsedMilliseconds,
                    SourceLabel);
            }
            StatusDetail = Sources.Count > 0 && _planIndex >= 0
                ? Sources[_planIndex].Source.OriginLine
                : string.Empty;
        });

    public void ReportBuffering()
        => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (IsVideoVisible)
            {
                IsConnecting = true;
                StatusText = "Buffering…";
            }
        });

    /// <summary>Called by the page when playback fails - advances automatically.</summary>
    public void ReportFailed(string? reason)
        => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_planIndex >= 0 && _planIndex < Sources.Count)
            {
                Sources[_planIndex].FailReason = reason ?? "playback failed";
            }

            string label = _planIndex >= 0 && _planIndex < Sources.Count
                ? Sources[_planIndex].Source.Label
                : "the source";

            _logger.LogWarning("Playback failed on {Label}: {Reason}", label, reason);

            if (_planIndex < Sources.Count - 1)
            {
                int next = _planIndex + 1;
                StreamCandidate upcoming = Sources[next];

                StatusText = $"{label} failed. Switching to {upcoming.Source.Label}…";
                StatusDetail = reason ?? string.Empty;

                _ = PlayAtAsync(next);
                return;
            }

            ShowUnavailable(
                "The live feed stopped responding",
                $"Every available source was tried ({reason ?? "playback error"}). " +
                "Free streams do change during a match - retry in a moment or open the official free player.");
        });

    private void ShowUnavailable(string title, string hint)
    {
        IsConnecting = false;
        IsPlaying = false;
        IsVideoVisible = false;
        IsWebVisible = false;
        StatusText = title;
        StatusDetail = hint;
        ErrorTitle = title;
        ErrorHint = hint;
        SetError(title);
    }

    [RelayCommand]
    private Task RetryAsync()
        => StartRetryAsync();

    private async Task StartRetryAsync()
    {
        _started = false;
        _planIndex = -1;
        ClearError();
        await StartAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private Task NextSourceAsync()
    {
        if (_planIndex < Sources.Count - 1)
        {
            return PlayAtAsync(_planIndex + 1);
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private static Task OpenInBrowserAsync(string? url)
        => string.IsNullOrWhiteSpace(url)
            ? Task.CompletedTask
            : Launcher.Default.OpenAsync(url);

    [RelayCommand]
    private Task OpenMatchThreadAsync()
    {
        if (Shell.Current is null)
        {
            return Task.CompletedTask;
        }

        string route =
            $"///MatchThreadPage?title={Uri.EscapeDataString(Title)}" +
            $"&league={Uri.EscapeDataString(League)}";

        return Shell.Current.GoToAsync(route);
    }

    [RelayCommand]
    private Task PlayCandidateAsync(StreamCandidate? candidate)
    {
        if (candidate is null)
        {
            return Task.CompletedTask;
        }

        int index = Sources.IndexOf(candidate);

        return index < 0
            ? Task.CompletedTask
            : PlayAtAsync(index);
    }

    /// <summary>Formats the plan for the "Sources" strip.</summary>
    public string DescribePlan()
        => Sources.Count == 0
            ? "No sources"
            : string.Join(
                " · ",
                Sources.Select(candidate =>
                    $"{candidate.Source.Label} ({candidate.Mode})"));

    private static IEnumerable<StreamCandidate> DeserializeCandidates(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(
                       json,
                       MarkUptvJsonContext.Default.ListStreamCandidate)
                   ?? Enumerable.Empty<StreamCandidate>();
        }
        catch
        {
            return Enumerable.Empty<StreamCandidate>();
        }
    }
}
