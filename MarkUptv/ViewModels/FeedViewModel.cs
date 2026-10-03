using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Models;
using MarkUptv.Pages;
using MarkUptv.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;

namespace MarkUptv.ViewModels;

/// <summary>
/// Controls the main social feed, pagination, refresh,
/// post navigation and post composition.
/// </summary>
public partial class FeedViewModel : SocialBaseViewModel
{
    private const int FeedPageSize = 10;

    private readonly IDeviceService _deviceService;

    private readonly SemaphoreSlim _paginationGate =
        new(1, 1);

    private string? _nextCursor;

    private bool _initialized;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    private bool _isLoadingMore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    private bool _hasMore = true;

    public bool CanLoadMore =>
        HasMore &&
        !IsLoadingMore;

    public FeedViewModel(
        ISocialService socialService,
        INotificationService notificationService,
        ICacheService cacheService,
        IDeviceService deviceService,
        ILogger<FeedViewModel> logger)
        : base(
            socialService,
            notificationService,
            cacheService,
            logger)
    {
        _deviceService = deviceService
            ?? throw new ArgumentNullException(
                nameof(deviceService));

        Logger.LogInformation(
            "FeedViewModel initialized.");
    }

    // ============================================================
    // INITIALIZATION
    // ============================================================

    /// <summary>
    /// Loads the first feed page when the view appears.
    /// Call InitializeCommand from the page lifecycle.
    /// </summary>
    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private async Task InitializeAsync(
        CancellationToken cancellationToken)
    {
        if (_initialized &&
            FeedPosts.Count > 0)
        {
            return;
        }

        if (IsLoading ||
            IsLoadingMore)
        {
            return;
        }

        await ExecuteWithLoadingAsync(
            async token =>
            {
                await LoadFeedPageCoreAsync(
                    reset: true,
                    token);

                _initialized = true;
            },
            cancellationToken,
            "The social feed could not be loaded.");
    }

    // ============================================================
    // REFRESH
    // ============================================================

    /// <summary>
    /// Clears pagination state and reloads the first feed page.
    /// This generates RefreshCommand for RefreshView binding.
    /// </summary>
    [RelayCommand(
        AllowConcurrentExecutions = false)]
    public override async Task RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsRefreshing)
        {
            return;
        }

        await ExecuteWithRefreshingAsync(
            async token =>
            {
                Logger.LogInformation(
                    "Refreshing the social feed.");

                await LoadFeedPageCoreAsync(
                    reset: true,
                    token);

                _initialized = true;

                /*
                 * Notifications are refreshed after the feed.
                 * A notification failure is handled by the shared
                 * SocialBaseViewModel loading method.
                 */
                await LoadNotificationsAsync(
                    token);
            },
            cancellationToken,
            "The social feed could not be refreshed.");
    }

    // ============================================================
    // PAGINATION
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false,
        CanExecute = nameof(CanLoadMore))]
    private async Task LoadMoreAsync(
        CancellationToken cancellationToken)
    {
        if (!HasMore ||
            IsLoadingMore ||
            IsLoading)
        {
            return;
        }

        IsLoadingMore = true;
        ClearError();

        try
        {
            await LoadFeedPageCoreAsync(
                reset: false,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            Logger.LogDebug(
                "Loading additional feed posts was cancelled.");
        }
        catch (Exception exception)
        {
            SetError(
                "More posts could not be loaded.");

            Logger.LogError(
                exception,
                "Failed to load another social feed page.");
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    /// <summary>
    /// Loads one page and either replaces or appends feed items.
    /// </summary>
    private async Task LoadFeedPageCoreAsync(
        bool reset,
        CancellationToken cancellationToken)
    {
        await _paginationGate.WaitAsync(
            cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            string deviceId =
                GetSafeDeviceId();

            SetCurrentDeviceId(
                deviceId);

            string? requestCursor =
                reset
                    ? null
                    : _nextCursor;

            if (!reset &&
                string.IsNullOrWhiteSpace(requestCursor))
            {
                HasMore = false;
                return;
            }

            Logger.LogInformation(
                "Loading feed page. Device: {DeviceId}; Cursor: {Cursor}; Reset: {Reset}",
                deviceId,
                requestCursor ?? "(first page)",
                reset);

            var response =
                await SocialService.GetFeedAsync(
                    deviceId,
                    requestCursor,
                    FeedPageSize);

            cancellationToken.ThrowIfCancellationRequested();

            List<ContentItem> items =
                response?.Items?
                    .Where(item => item is not null)
                    .ToList()
                ?? [];

            string? receivedCursor =
                string.IsNullOrWhiteSpace(
                    response?.NextCursor)
                    ? null
                    : response.NextCursor.Trim();

            await UpdateFeedCollectionAsync(
                items,
                reset);

            /*
             * Protect against APIs returning the same cursor repeatedly,
             * which would otherwise create an infinite pagination loop.
             */
            bool cursorAdvanced =
                !string.Equals(
                    requestCursor,
                    receivedCursor,
                    StringComparison.Ordinal);

            _nextCursor =
                receivedCursor;

            HasMore =
                items.Count > 0 &&
                receivedCursor is not null &&
                cursorAdvanced;

            Logger.LogInformation(
                "Feed page loaded. Received: {Received}; Displayed: {Displayed}; HasMore: {HasMore}",
                items.Count,
                FeedPosts.Count,
                HasMore);
        }
        catch (Exception exception)
        {
            Logger.LogError(
                exception,
                "Feed page loading failed.");

            throw;
        }
        finally
        {
            _paginationGate.Release();
        }
    }

    private async Task UpdateFeedCollectionAsync(
        IReadOnlyCollection<ContentItem> items,
        bool replaceExisting)
    {
        await RunOnMainThreadAsync(
            () =>
            {
                if (replaceExisting)
                {
                    FeedPosts.Clear();
                }

                foreach (ContentItem item in items)
                {
                    if (ContainsPost(item))
                    {
                        continue;
                    }

                    FeedPosts.Add(item);
                }

                HasFeedItems =
                    FeedPosts.Count > 0;

                IsFeedEmpty =
                    !HasFeedItems;
            });
    }

    private bool ContainsPost(
        ContentItem candidate)
    {
        return FeedPosts.Any(existing =>
            Equals(
                existing.Id,
                candidate.Id) &&
            Equals(
                existing.ContentType,
                candidate.ContentType));
    }

    // ============================================================
    // POST NAVIGATION
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private async Task OpenPostAsync(
        ContentItem? item,
        CancellationToken cancellationToken)
    {
        if (item is null)
        {
            return;
        }

        string deviceId =
            GetSafeDeviceId();

        try
        {
            /*
             * Recommendation logging is useful but must not prevent
             * the user from opening the selected post.
             */
            try
            {
                await SocialService.LogViewAsync(
                    deviceId,
                    item.ContentType,
                    item.Id);
            }
            catch (Exception exception)
            {
                Logger.LogWarning(
                    exception,
                    "The post view could not be logged. ContentId: {ContentId}",
                    item.Id);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var parameters =
                new Dictionary<string, object>
                {
                    ["contentType"] = item.ContentType,
                    ["contentId"] = item.Id
                };

            Logger.LogInformation(
                "Opening social post. Type: {ContentType}; Id: {ContentId}",
                item.ContentType,
                item.Id);

            await SafeNavigateAsync(
                nameof(PostDetailPage),
                parameters,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            Logger.LogDebug(
                "Opening the social post was cancelled.");
        }
        catch (Exception exception)
        {
            SetError(
                "The selected post could not be opened.");

            Logger.LogError(
                exception,
                "Failed to navigate to the social post.");
        }
    }

    // ============================================================
    // COMPOSE
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private async Task ComposeAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            Logger.LogInformation(
                "Opening the post composer.");

            await SafeNavigateAsync(
                nameof(ComposePostPage),
                parameters: null,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            Logger.LogDebug(
                "Opening the post composer was cancelled.");
        }
        catch (Exception exception)
        {
            SetError(
                "The post composer could not be opened.");

            Logger.LogError(
                exception,
                "Failed to navigate to ComposePostPage.");
        }
    }

    // ============================================================
    // HELPERS
    // ============================================================

    private string GetSafeDeviceId()
    {
        try
        {
            string? deviceId =
                _deviceService.GetDeviceId();

            return string.IsNullOrWhiteSpace(deviceId)
                ? "anonymous"
                : deviceId.Trim();
        }
        catch (Exception exception)
        {
            Logger.LogWarning(
                exception,
                "The device identifier could not be read. Anonymous mode will be used.");

            return "anonymous";
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
}