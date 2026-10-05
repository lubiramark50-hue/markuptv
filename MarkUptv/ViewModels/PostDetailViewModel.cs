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
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;

namespace MarkUptv.ViewModels;

/// <summary>
/// Controls the social content detail page, reactions,
/// comments, sharing and navigation.
/// </summary>
public partial class PostDetailViewModel :
    SocialBaseViewModel,
    IQueryAttributable,
    IDisposable
{
    private const string WindowsLocalBaseUrl =
        "http://localhost:5293";

    private const string AndroidEmulatorBaseUrl =
        "http://10.0.2.2:5293";

    /*
     * Replace this address with the IPv4 address of the computer
     * running MarkUpTvServer when testing on a physical device.
     */
    private const string PhysicalDeviceBaseUrl =
        "http://192.168.1.25:5293";

    private readonly IDeviceService _deviceService;

    private readonly SemaphoreSlim _contentLoadGate =
        new(1, 1);

    private readonly SemaphoreSlim _reactionGate =
        new(1, 1);

    private readonly SemaphoreSlim _commentGate =
        new(1, 1);

    private CancellationTokenSource? _queryLoadCancellation;

    private ContentItem? _navigationItem;

    private string _contentType =
        string.Empty;

    private int _contentId;

    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCommentsEmpty))]
    private ObservableCollection<UserComment> _comments =
        [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPostComment))]
    private string _newCommentText =
        string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCommentsEmpty))]
    private bool _isLoadingComments;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContentLoaded))]
    [NotifyPropertyChangedFor(nameof(IsCommentsEmpty))]
    [NotifyPropertyChangedFor(nameof(CanPostComment))]
    [NotifyPropertyChangedFor(nameof(CanToggleLike))]
    [NotifyPropertyChangedFor(nameof(CanShare))]
    private ContentItem? _item;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPostComment))]
    private bool _isPostingComment;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleLike))]
    private bool _isTogglingLike;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShare))]
    private bool _isSharing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCommentsError))]
    private string _commentsErrorMessage =
        string.Empty;

    [ObservableProperty]
    private string _shareUrl =
        string.Empty;

    public bool IsContentLoaded =>
        Item is not null &&
        !HasError &&
        !IsLoading;

    public bool IsCommentsEmpty =>
        Comments.Count == 0 &&
        !IsLoadingComments &&
        IsContentLoaded;

    public bool HasCommentsError =>
        !string.IsNullOrWhiteSpace(
            CommentsErrorMessage);

    public bool CanPostComment =>
        Item is not null &&
        !IsPostingComment &&
        !string.IsNullOrWhiteSpace(
            NewCommentText);

    public bool CanToggleLike =>
        Item is not null &&
        !IsTogglingLike;

    public bool CanShare =>
        Item is not null &&
        !IsSharing;

    public PostDetailViewModel(
        ISocialService socialService,
        INotificationService notificationService,
        ICacheService cacheService,
        IDeviceService deviceService,
        ILogger<PostDetailViewModel> logger)
        : base(
            socialService,
            notificationService,
            cacheService,
            logger)
    {
        _deviceService = deviceService
            ?? throw new ArgumentNullException(
                nameof(deviceService));

        Comments.CollectionChanged +=
            OnCommentsCollectionChanged;

        PropertyChanged +=
            OnViewModelPropertyChanged;

        Logger.LogInformation(
            "PostDetailViewModel initialized.");
    }

    // ============================================================
    // SHELL QUERY PARAMETERS
    // ============================================================

    public void ApplyQueryAttributes(
        IDictionary<string, object> query)
    {
        if (_disposed)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(query);

        CancelPendingQueryLoad();

        ContentItem? suppliedItem =
            ReadContentItem(query);

        string contentType =
            suppliedItem?.ContentType ??
            ReadString(
                query,
                "contentType");

        int contentId =
            suppliedItem?.Id ??
            ReadInteger(
                query,
                "contentId");

        /*
         * Support older navigation that used postId only.
         */
        if (contentId <= 0)
        {
            contentId =
                ReadInteger(
                    query,
                    "postId");

            if (contentId > 0 &&
                string.IsNullOrWhiteSpace(contentType))
            {
                contentType = "post";
            }
        }

        contentType =
            NormalizeContentType(
                contentType);

        if (contentId <= 0 ||
            string.IsNullOrWhiteSpace(contentType))
        {
            SetError(
                "The selected content could not be identified.");

            Logger.LogWarning(
                "Post detail navigation contained invalid parameters.");

            return;
        }

        ClearError();

        _contentId = contentId;
        _contentType = contentType;
        _navigationItem = suppliedItem;

        ShareUrl =
            BuildLocalContentUrl(
                contentType,
                contentId);

        var cancellation =
            new CancellationTokenSource();

        _queryLoadCancellation =
            cancellation;

        _ = LoadFromQuerySafelyAsync(
            cancellation.Token);
    }

    private async Task LoadFromQuerySafelyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await RunContentLoadAsync(
                isRefresh: false,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            Logger.LogDebug(
                "Post detail query loading was cancelled.");
        }
        catch (Exception exception)
        {
            SetError(
                "The selected content could not be loaded.");

            Logger.LogError(
                exception,
                "Unexpected post detail loading failure.");
        }
    }

    // ============================================================
    // CONTENT LOADING
    // ============================================================

    private async Task RunContentLoadAsync(
        bool isRefresh,
        CancellationToken cancellationToken)
    {
        await _contentLoadGate.WaitAsync(
            cancellationToken);

        try
        {
            if (isRefresh)
            {
                await ExecuteWithRefreshingAsync(
                    LoadContentCoreAsync,
                    cancellationToken,
                    "The content could not be refreshed.");
            }
            else
            {
                await ExecuteWithLoadingAsync(
                    LoadContentCoreAsync,
                    cancellationToken,
                    "The content could not be loaded.");
            }
        }
        finally
        {
            _contentLoadGate.Release();
        }
    }

    private async Task LoadContentCoreAsync(
        CancellationToken cancellationToken)
    {
        ValidateContentIdentity();

        cancellationToken.ThrowIfCancellationRequested();

        ContentItem content =
            _navigationItem ??
            CreateMinimalContentReference();

        await RunOnMainThreadAsync(
            () =>
            {
                Item = content;

                Comments.Clear();

                CommentsErrorMessage =
                    string.Empty;
            });

        SetCurrentDeviceId(
            GetSafeDeviceId());

        await LoadReactionCountAsync(
            cancellationToken);

        await LoadCommentsCoreAsync(
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        Logger.LogInformation(
            "Post detail loaded. Type: {ContentType}; Id: {ContentId}; Comments: {CommentCount}",
            _contentType,
            _contentId,
            Comments.Count);
    }

    private async Task LoadReactionCountAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            int reactionCount =
                await SocialService
                    .GetReactionCountAsync(
                        _contentType,
                        _contentId);

            cancellationToken.ThrowIfCancellationRequested();

            await RunOnMainThreadAsync(
                () =>
                {
                    if (Item is null)
                    {
                        return;
                    }

                    Item.LikeCount =
                        Math.Max(
                            0,
                            reactionCount);

                    /*
                     * Raising Item causes bindings such as
                     * Item.LikeCount to be re-evaluated even when
                     * ContentItem is not an ObservableObject.
                     */
                    OnPropertyChanged(
                        nameof(Item));
                });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.LogWarning(
                exception,
                "Reaction count could not be loaded. Type: {ContentType}; Id: {ContentId}",
                _contentType,
                _contentId);
        }
    }

    private async Task LoadCommentsCoreAsync(
        CancellationToken cancellationToken)
    {
        await SetCommentsLoadingAsync(
            true);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response =
                await SocialService
                    .GetCommentsAsync(
                        _contentType,
                        _contentId);

            cancellationToken.ThrowIfCancellationRequested();

            List<UserComment> comments =
                response?
                    .Where(comment =>
                        comment is not null)
                    .ToList()
                ?? [];

            await RunOnMainThreadAsync(
                () =>
                {
                    Comments.Clear();

                    foreach (UserComment comment
                             in comments)
                    {
                        Comments.Add(comment);
                    }

                    if (Item is not null)
                    {
                        Item.CommentCount =
                            Comments.Count;

                        OnPropertyChanged(
                            nameof(Item));
                    }

                    CommentsErrorMessage =
                        string.Empty;
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
                    CommentsErrorMessage =
                        "Comments could not be loaded.";
                });

            Logger.LogError(
                exception,
                "Failed to load comments. Type: {ContentType}; Id: {ContentId}",
                _contentType,
                _contentId);
        }
        finally
        {
            await SetCommentsLoadingAsync(
                false);
        }
    }

    // ============================================================
    // REFRESH
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    public override async Task RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        if (_contentId <= 0 ||
            string.IsNullOrWhiteSpace(_contentType))
        {
            SetError(
                "There is no content available to refresh.");

            return;
        }

        try
        {
            Logger.LogInformation(
                "Refreshing post detail. Type: {ContentType}; Id: {ContentId}",
                _contentType,
                _contentId);

            await RunContentLoadAsync(
                isRefresh: true,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            Logger.LogDebug(
                "Post detail refresh was cancelled.");
        }
    }

    // ============================================================
    // REACTIONS
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false,
        CanExecute = nameof(CanToggleLike))]
    private async Task ToggleLikeAsync(
        CancellationToken cancellationToken)
    {
        if (Item is null)
        {
            return;
        }

        await _reactionGate.WaitAsync(
            cancellationToken);

        IsTogglingLike = true;
        ClearError();

        try
        {
            TryVibrate();

            string deviceId =
                GetSafeDeviceId();

            bool previouslyLiked =
                Item.IsLiked;

            bool nowLiked =
                await SocialService
                    .ToggleReactionAsync(
                        deviceId,
                        _contentType,
                        _contentId);

            cancellationToken.ThrowIfCancellationRequested();

            await RunOnMainThreadAsync(
                () =>
                {
                    if (Item is null)
                    {
                        return;
                    }

                    Item.IsLiked =
                        nowLiked;

                    if (previouslyLiked != nowLiked)
                    {
                        Item.LikeCount =
                            Math.Max(
                                0,
                                Item.LikeCount +
                                (nowLiked ? 1 : -1));
                    }

                    OnPropertyChanged(
                        nameof(Item));
                });

            Logger.LogInformation(
                "Reaction updated. ContentId: {ContentId}; Liked: {Liked}",
                _contentId,
                nowLiked);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            Logger.LogDebug(
                "Reaction update was cancelled.");
        }
        catch (Exception exception)
        {
            SetError(
                "Your reaction could not be saved.");

            Logger.LogError(
                exception,
                "Failed to update reaction. ContentId: {ContentId}",
                _contentId);
        }
        finally
        {
            IsTogglingLike = false;

            _reactionGate.Release();
        }
    }

    // ============================================================
    // COMMENTS
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false,
        CanExecute = nameof(CanPostComment))]
    private async Task PostCommentAsync(
        CancellationToken cancellationToken)
    {
        if (Item is null)
        {
            return;
        }

        string commentText =
            NewCommentText.Trim();

        if (string.IsNullOrWhiteSpace(
                commentText))
        {
            return;
        }

        await _commentGate.WaitAsync(
            cancellationToken);

        IsPostingComment = true;
        CommentsErrorMessage = string.Empty;

        try
        {
            string deviceId =
                GetSafeDeviceId();

            UserComment? comment =
                await SocialService
                    .AddCommentAsync(
                        deviceId,
                        _contentType,
                        _contentId,
                        commentText);

            cancellationToken.ThrowIfCancellationRequested();

            if (comment is null)
            {
                CommentsErrorMessage =
                    "The comment was not accepted.";

                return;
            }

            await RunOnMainThreadAsync(
                () =>
                {
                    Comments.Insert(
                        0,
                        comment);

                    NewCommentText =
                        string.Empty;

                    if (Item is not null)
                    {
                        Item.CommentCount =
                            Comments.Count;

                        OnPropertyChanged(
                            nameof(Item));
                    }
                });

            TryVibrate();

            Logger.LogInformation(
                "Comment added. ContentId: {ContentId}",
                _contentId);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            Logger.LogDebug(
                "Comment submission was cancelled.");
        }
        catch (Exception exception)
        {
            CommentsErrorMessage =
                "Your comment could not be posted.";

            Logger.LogError(
                exception,
                "Failed to post comment. ContentId: {ContentId}",
                _contentId);
        }
        finally
        {
            IsPostingComment = false;

            _commentGate.Release();
        }
    }

    // ============================================================
    // SHARING
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false,
        CanExecute = nameof(CanShare))]
    private async Task ShareAsync(
        CancellationToken cancellationToken)
    {
        if (Item is null)
        {
            return;
        }

        IsSharing = true;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            string title =
                string.IsNullOrWhiteSpace(Item.Title)
                    ? "MarkUpTV content"
                    : Item.Title.Trim();

            string description =
                string.IsNullOrWhiteSpace(
                    Item.Description)
                    ? "Watch this on MarkUpTV."
                    : Item.Description.Trim();

            await Share.Default.RequestAsync(
                new ShareTextRequest
                {
                    Title = $"Share {title}",
                    Text =
                        $"{title}\n\n" +
                        $"{description}\n\n" +
                        ShareUrl,

                    Uri = ShareUrl
                });

            Logger.LogInformation(
                "Share sheet opened. ContentId: {ContentId}; Url: {ShareUrl}",
                _contentId,
                ShareUrl);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            Logger.LogDebug(
                "Content sharing was cancelled.");
        }
        catch (Exception exception)
        {
            SetError(
                "The share window could not be opened.");

            Logger.LogError(
                exception,
                "Failed to share content. ContentId: {ContentId}",
                _contentId);
        }
        finally
        {
            IsSharing = false;
        }
    }

    // ============================================================
    // COMPOSE NAVIGATION
    // ============================================================

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private async Task ComposeAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await SafeNavigateAsync(
                nameof(ComposePostPage),
                parameters: null,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            Logger.LogDebug(
                "Compose navigation was cancelled.");
        }
        catch (Exception exception)
        {
            SetError(
                "The post composer could not be opened.");

            Logger.LogError(
                exception,
                "Failed to open ComposePostPage.");
        }
    }

    // ============================================================
    // PROPERTY AND COLLECTION NOTIFICATIONS
    // ============================================================

    partial void OnCommentsChanged(
        ObservableCollection<UserComment>? oldValue,
        ObservableCollection<UserComment> newValue)
    {
        if (oldValue is not null)
        {
            oldValue.CollectionChanged -=
                OnCommentsCollectionChanged;
        }

        if (newValue is not null)
        {
            newValue.CollectionChanged +=
                OnCommentsCollectionChanged;
        }

        OnPropertyChanged(
            nameof(IsCommentsEmpty));
    }

    partial void OnItemChanged(
        ContentItem? value)
    {
        OnPropertyChanged(
            nameof(IsContentLoaded));

        OnPropertyChanged(
            nameof(IsCommentsEmpty));

        PostCommentCommand
            .NotifyCanExecuteChanged();

        ToggleLikeCommand
            .NotifyCanExecuteChanged();

        ShareCommand
            .NotifyCanExecuteChanged();
    }

    partial void OnNewCommentTextChanged(
        string value)
    {
        PostCommentCommand
            .NotifyCanExecuteChanged();
    }

    partial void OnIsPostingCommentChanged(
        bool value)
    {
        PostCommentCommand
            .NotifyCanExecuteChanged();
    }

    partial void OnIsTogglingLikeChanged(
        bool value)
    {
        ToggleLikeCommand
            .NotifyCanExecuteChanged();
    }

    partial void OnIsSharingChanged(
        bool value)
    {
        ShareCommand
            .NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingCommentsChanged(
        bool value)
    {
        OnPropertyChanged(
            nameof(IsCommentsEmpty));
    }

    private void OnCommentsCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(
            nameof(IsCommentsEmpty));
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
            OnPropertyChanged(
                nameof(IsContentLoaded));

            OnPropertyChanged(
                nameof(IsCommentsEmpty));
        }
    }

    // ============================================================
    // HELPERS
    // ============================================================

    public void OnPageDisappearing()
    {
        CancelPendingQueryLoad();
    }

    private ContentItem
        CreateMinimalContentReference()
    {
        /*
         * This fallback is used only for a direct link that does not
         * include the original ContentItem. The normal FeedViewModel
         * flow should pass the complete ContentItem.
         */
        return new ContentItem
        {
            Id = _contentId,
            ContentType = _contentType,
            Title = "MarkUpTV content",
            Description = string.Empty,
            ThumbnailUrl = string.Empty,
            AuthorName = "MarkUpTV",
            AuthorInitials = "MTV",
            Timestamp = DateTime.UtcNow,
            LikeCount = 0,
            CommentCount = 0,
            IsLiked = false
        };
    }

    private void ValidateContentIdentity()
    {
        if (_contentId <= 0)
        {
            throw new InvalidOperationException(
                "The content identifier is invalid.");
        }

        if (string.IsNullOrWhiteSpace(
                _contentType))
        {
            throw new InvalidOperationException(
                "The content type is missing.");
        }
    }

    private string GetSafeDeviceId()
    {
        try
        {
            string? deviceId =
                _deviceService.GetDeviceId();

            return string.IsNullOrWhiteSpace(
                    deviceId)
                ? "anonymous"
                : deviceId.Trim();
        }
        catch (Exception exception)
        {
            Logger.LogWarning(
                exception,
                "Device ID could not be read. Anonymous mode will be used.");

            return "anonymous";
        }
    }

    private static ContentItem? ReadContentItem(
        IDictionary<string, object> query)
    {
        if (query.TryGetValue(
                "item",
                out object? itemValue) &&
            itemValue is ContentItem item)
        {
            return item;
        }

        if (query.TryGetValue(
                "contentItem",
                out object? contentValue) &&
            contentValue is ContentItem contentItem)
        {
            return contentItem;
        }

        return null;
    }

    private static string ReadString(
        IDictionary<string, object> query,
        string key)
    {
        if (!query.TryGetValue(
                key,
                out object? value))
        {
            return string.Empty;
        }

        return value?.ToString()?.Trim() ??
               string.Empty;
    }

    private static int ReadInteger(
        IDictionary<string, object> query,
        string key)
    {
        if (!query.TryGetValue(
                key,
                out object? value) ||
            value is null)
        {
            return 0;
        }

        return value switch
        {
            int integerValue =>
                integerValue,

            long longValue
                when longValue is > 0 and <= int.MaxValue =>
                (int)longValue,

            _ when int.TryParse(
                value.ToString(),
                out int parsed) =>
                parsed,

            _ => 0
        };
    }

    private static string NormalizeContentType(
        string? contentType)
    {
        if (string.IsNullOrWhiteSpace(
                contentType))
        {
            return string.Empty;
        }

        return contentType
            .Trim()
            .ToLowerInvariant();
    }

    private static string BuildLocalContentUrl(
        string contentType,
        int contentId)
    {
        string baseUrl =
            GetDevelopmentBaseUrl();

        return
            $"{baseUrl.TrimEnd('/')}/" +
            $"content/" +
            $"{Uri.EscapeDataString(contentType)}/" +
            $"{contentId}";
    }

    private static string GetDevelopmentBaseUrl()
    {
        DevicePlatform platform =
            DeviceInfo.Current.Platform;

        if (platform == DevicePlatform.Android)
        {
            return DeviceInfo.Current.DeviceType ==
                   DeviceType.Virtual
                ? AndroidEmulatorBaseUrl
                : PhysicalDeviceBaseUrl;
        }

        if (platform == DevicePlatform.WinUI)
        {
            return WindowsLocalBaseUrl;
        }

        /*
         * iOS Simulator and Mac Catalyst normally reach services
         * running on the same Mac through localhost.
         */
        if (platform == DevicePlatform.iOS ||
            platform == DevicePlatform.MacCatalyst)
        {
            return WindowsLocalBaseUrl;
        }

        return WindowsLocalBaseUrl;
    }

    private static void TryVibrate()
    {
        try
        {
            Vibration.Default.Vibrate(
                TimeSpan.FromMilliseconds(
                    45));
        }
        catch
        {
            /*
             * Vibration may be unavailable on desktop computers,
             * simulators or devices without vibration permission.
             */
        }
    }

    private Task SetCommentsLoadingAsync(
        bool value)
    {
        if (MainThread.IsMainThread)
        {
            IsLoadingComments = value;
            return Task.CompletedTask;
        }

        return MainThread.InvokeOnMainThreadAsync(
            () => IsLoadingComments = value);
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

    private void CancelPendingQueryLoad()
    {
        CancellationTokenSource? cancellation =
            Interlocked.Exchange(
                ref _queryLoadCancellation,
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelPendingQueryLoad();
        Comments.CollectionChanged -= OnCommentsCollectionChanged;
        PropertyChanged -= OnViewModelPropertyChanged;
        _commentGate.Dispose();
        GC.SuppressFinalize(this);
    }
}