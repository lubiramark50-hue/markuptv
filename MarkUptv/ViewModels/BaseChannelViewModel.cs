using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Helpers;
using MarkUptv.Models;
using MarkUptv.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace MarkUptv.ViewModels;

/// <summary>
/// Shared channel loading, filtering, playback, recently watched,
/// payment, error and media-failure logic.
/// </summary>
public abstract partial class BaseChannelViewModel :
    ObservableObject,
    IDisposable
{
    private readonly object _channelLock = new();
    private readonly CancellationTokenSource _lifetimeCts = new();

    private CancellationTokenSource? _searchDebounceCts;
    private bool _isDisposed;

    // Dead URLs that already went through one web-source repair attempt, so
    // a failing repaired stream falls through to the skip-to-next behaviour.
    private readonly HashSet<string> _repairedUrls = new(StringComparer.Ordinal);

    protected readonly TvApiService TvApi;
    protected readonly RecentlyWatchedService RecentlyWatched;
    protected readonly PaymentService PaymentService;
    protected readonly ChannelCacheService CacheService;

    protected List<TvChannel> AllChannels { get; private set; } = [];

    protected abstract string Category { get; }

    private static string FriendlyCategoryName(string category) =>
        category.Trim().ToLowerInvariant() switch
        {
            "documentary" => "discovery",
            "general" => "local TV",
            "europeanfootball" => "European football",
            "religious" => "religious TV",
            _ => category
        };

    public abstract string NowPlayingText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isButtonVisible = true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlayerVisible))]
    [NotifyPropertyChangedFor(nameof(NowPlayingText))]
    private TvChannel? _selectedChannel;

    [ObservableProperty]
    private string _selectedGroup = "All";

    [ObservableProperty]
    private bool _isPlayerLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotStreaming))]
    private bool _isStreaming;

    public bool IsNotStreaming =>
        !IsStreaming;

    [ObservableProperty]
    private ObservableCollection<TvChannel> _channels = [];

    [ObservableProperty]
    private ObservableCollection<string> _groups = [];

    [ObservableProperty]
    private ObservableCollection<TvChannel> _recentlyWatchedChannels = [];

    public bool HasError =>
        !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsNotLoading =>
        !IsLoading;

    public bool IsPlayerVisible =>
        SelectedChannel is not null;

    protected BaseChannelViewModel(
        TvApiService tvApi,
        RecentlyWatchedService recentlyWatched,
        PaymentService paymentService,
        ChannelCacheService cacheService)
    {
        TvApi = tvApi
            ?? throw new ArgumentNullException(nameof(tvApi));

        RecentlyWatched = recentlyWatched
            ?? throw new ArgumentNullException(nameof(recentlyWatched));

        PaymentService = paymentService
            ?? throw new ArgumentNullException(nameof(paymentService));

        CacheService = cacheService
            ?? throw new ArgumentNullException(nameof(cacheService));

        RecentlyWatched.RecentlyWatchedChanged +=
            OnRecentlyWatchedChanged;

        LoadRecentlyWatched();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        IsButtonVisible = !value;

        // Views bind to the inverse flag (for example, the "nothing to show"
        // panel hides while the first load is in flight), so it has to be
        // re-notified whenever the loading flag flips.
        OnPropertyChanged(nameof(IsNotLoading));
    }

    partial void OnSelectedGroupChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedChannelChanged(TvChannel? value)
    {
        if (value is not null)
        {
            RecentlyWatched.Add(value);
        }

        if (value is null)
        {
            IsStreaming = false;
        }

        UpdatePlayingFlags(value);
    }

    partial void OnSearchTextChanged(string value)
    {
        ScheduleSearchFilter();
    }

    // =====================================================================
    // LOADING
    // =====================================================================

    [RelayCommand(AllowConcurrentExecutions = false)]
    protected virtual Task LoadChannelsAsync(
        CancellationToken cancellationToken)
    {
        return FetchAndProcessChannelsAsync(
            useCache: true,
            cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public Task RefreshChannelsAsync(
        CancellationToken cancellationToken)
    {
        return FetchAndProcessChannelsAsync(
            useCache: false,
            cancellationToken);
    }

    private async Task FetchAndProcessChannelsAsync(
        bool useCache,
        CancellationToken cancellationToken)
    {
        if (_isDisposed)
        {
            return;
        }

        using CancellationTokenSource linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCts.Token);

        CancellationToken ct = linkedCts.Token;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            IsLoading = true;
            ErrorMessage = null;
        });

        try
        {
            CategoryResponse? response =
                await CacheService.GetChannelsAsync(
                    Category,
                    useCache);

            ct.ThrowIfCancellationRequested();

            if (response?.Channels is null)
            {
                await SetErrorAsync(
                    $"Could not load {FriendlyCategoryName(Category)} channels. " +
                    "Please check your connection.");

                return;
            }

            List<TvChannel> normalizedChannels =
                response.Channels
                    .Where(channel =>
                        channel is not null &&
                        channel.Id > 0 &&
                        !string.IsNullOrWhiteSpace(channel.Name) &&
                        !string.IsNullOrWhiteSpace(channel.Url))
                    .GroupBy(channel => channel.Id)
                    .Select(group => group.First())
                    .ToList();

            lock (_channelLock)
            {
                AllChannels = normalizedChannels;
            }

            List<string> availableGroups =
                BuildGroupList(
                    response.Groups,
                    normalizedChannels);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ReplaceGroups(availableGroups);

                if (!availableGroups.Contains(
                        SelectedGroup,
                        StringComparer.OrdinalIgnoreCase))
                {
                    SelectedGroup = "All";
                }
            });

            ApplyFilter();
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            // Normal page shutdown or command cancellation.
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[BaseChannelViewModel] Channel loading failed: " +
                $"{exception}");

            await SetErrorAsync(
                "Channel synchronization failed. " +
                "Please check the backend and network connection.");
        }
        finally
        {
            if (!_isDisposed)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    IsLoading = false;
                });
            }
        }
    }

    /// <summary>
    /// Server groups can be combined with ';' (for example
    /// "Culture;Entertainment;Music"). Split them into single
    /// chips so each tag is a meaningful, clickable filter.
    /// </summary>
    /// <summary>
    /// Maximum number of filter chips to show. The general pool carries dozens
    /// of tags, and a strip that long is both unreadable and unusable on a
    /// phone - it also pushed the meaningful filters off screen.
    /// </summary>
    private const int MaxGroupChips = 14;

    /// <summary>
    /// Tags that must never become a filter chip: anything adult, and the
    /// source-file markers that occasionally end up in a group string.
    /// </summary>
    private static readonly string[] BlockedGroupTags =
    [
        "porno", "porn", "xxx", "adult", "erotic", "sexy", "18+",
        "geo-blocked", "geoblocked", "unknown", "n/a", "undefined",
        "unsorted", "24/7"
    ];

    private static bool IsPresentableGroupTag(string tag)
    {
        var trimmed = tag.Trim();

        // Reject empties, single characters, numeric-only tags and the source
        // markers that leak in from playlist metadata, e.g. "[Not 24/7]" or
        // "[Geo-blocked]".
        if (trimmed.Length < 2)
        {
            return false;
        }

        if (trimmed.StartsWith('[') || trimmed.StartsWith('('))
        {
            return false;
        }

        if (trimmed.All(ch => char.IsDigit(ch) || ch is ' ' or '+' or '-'))
        {
            return false;
        }

        var lowered = trimmed.ToLowerInvariant();

        foreach (var blocked in BlockedGroupTags)
        {
            if (lowered.Contains(blocked, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static List<string> BuildGroupList(
        IEnumerable<string>? responseGroups,
        IEnumerable<TvChannel> channels)
    {
        IEnumerable<string> suppliedGroups =
            responseGroups ?? [];

        IEnumerable<string> channelGroups =
            channels
                .Select(channel => channel.Group)
                .Where(group =>
                    !string.IsNullOrWhiteSpace(group))
                .Select(group => group!);

        // The server publishes its groups in curated order, so keep that order
        // and append anything the channels add. The previous implementation
        // sorted everything alphabetically, which is what put "Auto" first and
        // a "Porno" tag in the middle of the strip instead of at the end of a
        // list nobody scans.
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddTag(string tag)
        {
            if (string.Equals(tag, "All", StringComparison.OrdinalIgnoreCase))
                return;

            if (!IsPresentableGroupTag(tag))
                return;

            if (seen.Add(tag))
                ordered.Add(tag);
        }

        foreach (var group in suppliedGroups)
        {
            foreach (var tag in SplitTags(group))
            {
                AddTag(tag);
            }
        }

        foreach (var group in channelGroups)
        {
            foreach (var tag in SplitTags(group))
            {
                AddTag(tag);
            }
        }

        var result = new List<string>(Math.Min(ordered.Count, MaxGroupChips) + 1)
        {
            "All"
        };

        result.AddRange(ordered.Take(MaxGroupChips));
        return result;
    }

    protected static string[] SplitTags(string? group)
    {
        if (string.IsNullOrWhiteSpace(group))
        {
            return [];
        }

        return group.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
    }

    // =====================================================================
    // FILTERING
    // =====================================================================

    private void ScheduleSearchFilter()
    {
        CancellationTokenSource replacement = new();

        CancellationTokenSource? previous =
            Interlocked.Exchange(
                ref _searchDebounceCts,
                replacement);

        if (previous is not null)
        {
            try
            {
                previous.Cancel();
            }
            finally
            {
                previous.Dispose();
            }
        }

        _ = ApplySearchAfterDelayAsync(
            replacement.Token);
    }

    private async Task ApplySearchAfterDelayAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(300),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
            // A newer search value replaced the previous one.
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[BaseChannelViewModel] Search failed: {exception}");
        }
    }

    protected void ApplyFilter()
    {
        if (_isDisposed)
        {
            return;
        }

        List<TvChannel> filteredChannels =
            CreateFilteredSnapshot();

        if (MainThread.IsMainThread)
        {
            ReplaceChannels(filteredChannels);
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(
                () => ReplaceChannels(filteredChannels));
        }
    }

    private List<TvChannel> CreateFilteredSnapshot()
    {
        lock (_channelLock)
        {
            IEnumerable<TvChannel> query =
                AllChannels;

            string search =
                SearchText?.Trim() ?? string.Empty;

            string group =
                SelectedGroup?.Trim() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(channel =>
                    channel.Name.Contains(
                        search,
                        StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(
                         channel.CurrentProgrammeTitle) &&
                     channel.CurrentProgrammeTitle.Contains(
                         search,
                         StringComparison.OrdinalIgnoreCase)));
            }

            if (!string.IsNullOrWhiteSpace(group) &&
                !string.Equals(
                    group,
                    "All",
                    StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(channel =>
                    SplitTags(channel.Group)
                        .Any(tag => string.Equals(
                            tag,
                            group,
                            StringComparison.OrdinalIgnoreCase)));
            }

            return query.ToList();
        }
    }

    private void ReplaceChannels(
        IReadOnlyCollection<TvChannel> channels)
    {
        if (Channels.Count == channels.Count &&
            Channels.Select(channel => channel.Id)
                .SequenceEqual(
                    channels.Select(channel => channel.Id)))
        {
            return;
        }

        // Swap the whole list in one assignment: a single collection
        // change for CollectionView to react to. Clear + N Add calls
        // fire N+1 events and force a UI-thread re-measure per event,
        // which ANR'd the app on 700-channel categories (News).
        Channels = new ObservableCollection<TvChannel>(channels);
    }

    private void ReplaceGroups(
        IReadOnlyCollection<string> groups)
    {
        if (Groups.SequenceEqual(
                groups,
                StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        // Same batched-swap reasoning as ReplaceChannels.
        Groups = new ObservableCollection<string>(groups);
    }

    // =====================================================================
    // PLAYBACK
    // =====================================================================

    [RelayCommand(AllowConcurrentExecutions = false)]
    protected virtual async Task PlayChannel(
        TvChannel? channel)
    {
        if (_isDisposed ||
            channel is null ||
            string.IsNullOrWhiteSpace(channel.Url))
        {
            return;
        }

        try
        {
            var paymentStatus =
                await PaymentService.GetStatusAsync(
                    useCache: true);

            if (paymentStatus?.CanWatch != true)
            {
                if (Shell.Current is not null)
                {
                    await MainThread.InvokeOnMainThreadAsync(
                        () => Shell.Current.GoToAsync(
                            "///PaymentRequiredPage"));
                }

                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // A fresh user-initiated channel needs no spoofed headers.
                StreamHeaderProvider.Clear();

                SelectedChannel = channel;
                IsPlayerLoading = true;
                ErrorMessage = null;
            });
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[BaseChannelViewModel] Playback preparation failed: " +
                $"{exception}");

            await SetErrorAsync(
                "The channel could not be opened.");
        }
    }

    [RelayCommand]
    protected virtual void MediaReady()
    {
        IsPlayerLoading = false;
        IsStreaming = true;
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    protected virtual async Task MediaFailed()
    {
        IsPlayerLoading = false;
        IsStreaming = false;

        TvChannel? failedChannel =
            SelectedChannel;

        if (failedChannel is null)
        {
            return;
        }

        var failedUrl = failedChannel.Url;

        // Self-heal: one backend repair attempt per dead URL. When the
        // channel carries a web-player source and the backend extracts a
        // fresh stream, retry the SAME channel (with headers staged) instead
        // of silently skipping to the next one.
        if (!string.IsNullOrWhiteSpace(failedUrl) &&
            _repairedUrls.Add(failedUrl) &&
            await TryRepairChannelAsync(failedChannel))
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                IsPlayerLoading = true;
                ErrorMessage =
                    "Stream recovered from its web source — restarting playback…";

                // Force the Source binding to re-read SelectedChannel.StreamUrl.
                OnPropertyChanged(nameof(SelectedChannel));
            });

            return;
        }

        _ = ReportFailedStreamSafelyAsync(
            failedChannel.Url);

        lock (_channelLock)
        {
            AllChannels.RemoveAll(channel =>
                channel.Id == failedChannel.Id);
        }

        List<TvChannel> remainingChannels =
            CreateFilteredSnapshot();

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            ReplaceChannels(remainingChannels);

            SelectedChannel =
                remainingChannels.FirstOrDefault();

            IsPlayerLoading =
                SelectedChannel is not null;

            ErrorMessage =
                remainingChannels.Count == 0
                    ? "This stream failed and no alternative channel is available."
                    : "The stream failed. Trying the next available channel.";
        });
    }

    /// <summary>
    /// Asks the backend to extract a fresh stream from the channel's
    /// web-player source. On success the channel's Url is swapped in place
    /// and the headers the stream needs are staged for the platform player.
    /// </summary>
    private async Task<bool> TryRepairChannelAsync(TvChannel channel)
    {
        try
        {
            var result = await TvApi.RepairChannelAsync(
                channel.Id,
                _lifetimeCts.Token);

            if (result?.HasUrl != true)
            {
                return false;
            }

            channel.Url = result.StreamUrl;

            StreamHeaderProvider.Set(
                result.StreamUrl,
                result.Referer ?? result.PageUrl,
                result.UserAgent,
                result.Headers);

            System.Diagnostics.Debug.WriteLine(
                $"[BaseChannelViewModel] Channel {channel.Id} repaired " +
                $"(level {result.SourceLevel}) → {result.StreamUrl}");

            return true;
        }
        catch (OperationCanceledException)
            when (_lifetimeCts.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[BaseChannelViewModel] Channel repair failed: {exception}");

            return false;
        }
    }

    private async Task ReportFailedStreamSafelyAsync(
        string url)
    {
        try
        {
            await TvApi.ReportFailedUrlAsync(
                url,
                _lifetimeCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Application or page is shutting down.
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[BaseChannelViewModel] Failure reporting failed: " +
                $"{exception}");
        }
    }

    [RelayCommand]
    protected virtual void ClearError()
    {
        ErrorMessage = null;
    }

    // =====================================================================
    // RECENTLY WATCHED
    // =====================================================================

    private void OnRecentlyWatchedChanged()
    {
        if (MainThread.IsMainThread)
        {
            LoadRecentlyWatched();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(
                LoadRecentlyWatched);
        }
    }

    private void LoadRecentlyWatched()
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(LoadRecentlyWatched);
            return;
        }

        try
        {
            IReadOnlyList<TvChannel>? recentChannels =
                RecentlyWatched.GetRecent();

            if (recentChannels is null)
            {
                return;
            }

            if (RecentlyWatchedChannels
                .Select(channel => channel.Id)
                .SequenceEqual(
                    recentChannels.Select(channel => channel.Id)))
            {
                return;
            }

            RecentlyWatchedChannels.Clear();

            foreach (TvChannel channel in recentChannels)
            {
                RecentlyWatchedChannels.Add(channel);
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[BaseChannelViewModel] Recently watched loading failed: " +
                $"{exception}");
        }
    }

    private Task SetErrorAsync(string message)
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            ErrorMessage = message;
        });
    }

    /// <summary>
    /// Marks exactly one channel (the one being played) as
    /// <see cref="TvChannel.IsPlaying"/> so lists can highlight it.
    /// </summary>
    private void UpdatePlayingFlags(
        TvChannel? selected)
    {
        if (_isDisposed)
        {
            return;
        }

        List<TvChannel> snapshot;

        lock (_channelLock)
        {
            snapshot = AllChannels.ToList();
        }

        foreach (TvChannel channel in snapshot)
        {
            channel.IsPlaying =
                selected is not null &&
                channel.Id == selected.Id;
        }
    }

    // =====================================================================
    // DISPOSAL
    // =====================================================================

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            RecentlyWatched.RecentlyWatchedChanged -=
                OnRecentlyWatchedChanged;

            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();

            CancellationTokenSource? searchCts =
                Interlocked.Exchange(
                    ref _searchDebounceCts,
                    null);

            if (searchCts is not null)
            {
                try
                {
                    searchCts.Cancel();
                }
                finally
                {
                    searchCts.Dispose();
                }
            }
        }

        _isDisposed = true;
    }
}