using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MarkUptv.Models;
using MarkUptv.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;

namespace MarkUptv.ViewModels;

/// <summary>
/// Shared foundation for social feeds, notifications and refresh operations.
/// </summary>
public abstract partial class SocialBaseViewModel : BaseViewModel
{
    private const int DefaultFeedLimit = 20;
    private const int MaximumFeedLimit = 100;

    private readonly SemaphoreSlim _feedLoadGate =
        new(1, 1);

    private readonly SemaphoreSlim _notificationLoadGate =
        new(1, 1);

    private readonly SemaphoreSlim _refreshGate =
        new(1, 1);

    private string _lastDeviceId =
        string.Empty;

    protected ISocialService SocialService { get; }

    protected INotificationService NotificationService { get; }

    protected ICacheService CacheService { get; }

    protected ILogger Logger { get; }

    [ObservableProperty]
    private ObservableCollection<ContentItem> _feedPosts =
        [];

    [ObservableProperty]
    private ObservableCollection<Notification> _notifications =
        [];

    [ObservableProperty]
    private bool _isFeedLoading;

    [ObservableProperty]
    private bool _isNotificationsLoading;

    [ObservableProperty]
    private bool _hasFeedItems;

    [ObservableProperty]
    private bool _hasNotifications;

    [ObservableProperty]
    private bool _isFeedEmpty = true;

    [ObservableProperty]
    private bool _isNotificationsEmpty = true;

    protected SocialBaseViewModel(
        ISocialService socialService,
        INotificationService notificationService,
        ICacheService cacheService,
        ILogger logger)
    {
        SocialService = socialService
            ?? throw new ArgumentNullException(
                nameof(socialService));

        NotificationService = notificationService
            ?? throw new ArgumentNullException(
                nameof(notificationService));

        CacheService = cacheService
            ?? throw new ArgumentNullException(
                nameof(cacheService));

        Logger = logger
            ?? throw new ArgumentNullException(
                nameof(logger));
    }

    /// <summary>
    /// Loads the social feed.
    /// Passing a cursor appends results; omitting it replaces the feed.
    /// </summary>
    public virtual async Task LoadFeedAsync(
        string deviceId,
        string? cursor = null,
        int limit = DefaultFeedLimit,
        CancellationToken cancellationToken = default)
    {
        string normalizedDeviceId =
            NormalizeDeviceId(deviceId);

        int normalizedLimit =
            Math.Clamp(
                limit,
                1,
                MaximumFeedLimit);

        await _feedLoadGate.WaitAsync(
            cancellationToken);

        try
        {
            IsFeedLoading = true;

            await ExecuteWithLoadingAsync(
                async token =>
                {
                    token.ThrowIfCancellationRequested();

                    Logger.LogInformation(
                        "Loading social feed. DeviceId: {DeviceId}; Cursor: {Cursor}; Limit: {Limit}",
                        normalizedDeviceId,
                        cursor ?? "(first page)",
                        normalizedLimit);

                    var response =
                        await SocialService.GetFeedAsync(
                            normalizedDeviceId,
                            cursor,
                            normalizedLimit);

                    token.ThrowIfCancellationRequested();

                    List<ContentItem> items =
                        response?.Items?
                            .Where(item => item is not null)
                            .ToList()
                        ?? [];

                    bool replaceExistingItems =
                        string.IsNullOrWhiteSpace(cursor);

                    await UpdateFeedCollectionAsync(
                        items,
                        replaceExistingItems);

                    _lastDeviceId =
                        normalizedDeviceId;

                    Logger.LogInformation(
                        "Social feed loaded successfully. Received: {Count}; Total displayed: {Total}",
                        items.Count,
                        FeedPosts.Count);
                },
                cancellationToken,
                "The social feed could not be loaded.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            Logger.LogDebug(
                "Social feed loading was cancelled.");
        }
        finally
        {
            IsFeedLoading = false;

            _feedLoadGate.Release();
        }
    }

    /// <summary>
    /// Loads the current user's notifications.
    /// </summary>
    public virtual async Task LoadNotificationsAsync(
        CancellationToken cancellationToken = default)
    {
        await _notificationLoadGate.WaitAsync(
            cancellationToken);

        try
        {
            IsNotificationsLoading = true;

            await ExecuteWithLoadingAsync(
                async token =>
                {
                    token.ThrowIfCancellationRequested();

                    Logger.LogInformation(
                        "Loading social notifications.");

                    var response =
                        await NotificationService
                            .GetNotificationsAsync();

                    token.ThrowIfCancellationRequested();

                    List<Notification> items =
                        response?
                            .Where(notification =>
                                notification is not null)
                            .ToList()
                        ?? [];

                    await ReplaceNotificationsAsync(
                        items);

                    Logger.LogInformation(
                        "Social notifications loaded successfully. Count: {Count}",
                        items.Count);
                },
                cancellationToken,
                "Notifications could not be loaded.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            Logger.LogDebug(
                "Notification loading was cancelled.");
        }
        finally
        {
            IsNotificationsLoading = false;

            _notificationLoadGate.Release();
        }
    }

    /// <summary>
    /// Refreshes both the feed and notifications.
    /// </summary>
    public override async Task RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(
                0,
                cancellationToken))
        {
            Logger.LogDebug(
                "Social refresh skipped because another refresh is already running.");

            return;
        }

        try
        {
            await ExecuteWithRefreshingAsync(
                async token =>
                {
                    string deviceId =
                        NormalizeDeviceId(
                            _lastDeviceId);

                    Logger.LogInformation(
                        "Refreshing social feed and notifications.");

                    /*
                     * Loading remains sequential because both operations use
                     * the shared BaseViewModel loading and error state.
                     * This prevents competing UI state changes.
                     */
                    await LoadFeedAsync(
                        deviceId,
                        cursor: null,
                        limit: DefaultFeedLimit,
                        token);

                    token.ThrowIfCancellationRequested();

                    await LoadNotificationsAsync(
                        token);

                    Logger.LogInformation(
                        "Social refresh completed successfully.");
                },
                cancellationToken,
                "The social page could not be refreshed.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            Logger.LogDebug(
                "Social refresh was cancelled.");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Updates the device identifier used during future refresh operations.
    /// </summary>
    protected void SetCurrentDeviceId(
        string? deviceId)
    {
        _lastDeviceId =
            NormalizeDeviceId(deviceId);
    }

    protected void ClearSocialCollections()
    {
        void ClearCollections()
        {
            FeedPosts.Clear();
            Notifications.Clear();

            UpdateFeedState();
            UpdateNotificationState();
        }

        if (MainThread.IsMainThread)
        {
            ClearCollections();
            return;
        }

        MainThread.BeginInvokeOnMainThread(
            ClearCollections);
    }

    private async Task UpdateFeedCollectionAsync(
        IReadOnlyCollection<ContentItem> items,
        bool replaceExistingItems)
    {
        await RunOnMainThreadAsync(
            () =>
            {
                if (replaceExistingItems)
                {
                    FeedPosts.Clear();
                }

                foreach (ContentItem item in items)
                {
                    /*
                     * Cursor-based loads append to the existing collection.
                     * Duplicate filtering can be added here once the exact
                     * ContentItem identifier property is confirmed.
                     */
                    FeedPosts.Add(item);
                }

                UpdateFeedState();
            });
    }

    private async Task ReplaceNotificationsAsync(
        IReadOnlyCollection<Notification> items)
    {
        await RunOnMainThreadAsync(
            () =>
            {
                Notifications.Clear();

                foreach (Notification notification in items)
                {
                    Notifications.Add(notification);
                }

                UpdateNotificationState();
            });
    }

    private void UpdateFeedState()
    {
        HasFeedItems =
            FeedPosts.Count > 0;

        IsFeedEmpty =
            !HasFeedItems;
    }

    private void UpdateNotificationState()
    {
        HasNotifications =
            Notifications.Count > 0;

        IsNotificationsEmpty =
            !HasNotifications;
    }

    private static string NormalizeDeviceId(
        string? deviceId)
    {
        return string.IsNullOrWhiteSpace(deviceId)
            ? "anonymous"
            : deviceId.Trim();
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