using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MarkUptv.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Maui.ApplicationModel;

namespace MarkUptv.Services;

/// <summary>
/// Production social API client for community posts, reactions,
/// comments and live chat.
///
/// A successful result is returned only when the backend confirms
/// that the operation succeeded.
/// </summary>
public sealed class SocialService :
    ISocialService,
    IDisposable
{
    private const string DefaultBaseUrl =
        "https://markuptvnew-fjhjd6bkb6bahqeh.southafricanorth-01.azurewebsites.net/";

    private const int MaximumAuthorLength = 80;
    private const int MaximumPostLength = 2_000;
    private const int MaximumCommentLength = 1_000;
    private const int MaximumChatLength = 500;
    private const int MaximumErrorBodyLength = 2_000;
    private const int MaximumRememberedChatMessages = 250;

    private static readonly TimeSpan LiveChatPollingInterval =
        TimeSpan.FromSeconds(4);

    private static readonly string[] AvatarPalette =
    [
        "#FF006E",
        "#3A86FF",
        "#8338EC",
        "#FFBE0B",
        "#FB5607",
        "#00F5D4"
    ];

    private readonly HttpClient _httpClient;
    private readonly ILogger<SocialService> _logger;

    /// <summary>
    /// Stable per-install identity. Likes/bookmarks used to hardcode
    /// "anonymous", while the feed read with no identity at all, so the
    /// heart state could never round-trip: a like was written under one
    /// id and read under another. Every social call now uses this id.
    /// </summary>
    private readonly IDeviceService _deviceService;

    private readonly object _subscriptionLock =
        new();

    private readonly Dictionary<
        int,
        HashSet<Action<LiveChatBubble>>> _chatSubscriptions =
            [];

    private readonly Dictionary<
        int,
        ChatDeduplicationState> _chatDeduplicationStates =
            [];

    private CancellationTokenSource? _pollingCancellation;
    private Task? _pollingTask;

    private bool _disposed;

    public SocialService(
        HttpClient httpClient,
        IOptions<SocialApiSettings> settings,
        ILogger<SocialService> logger,
        IDeviceService deviceService)
    {
        _httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));

        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        _deviceService = deviceService
            ?? throw new ArgumentNullException(nameof(deviceService));

        ArgumentNullException.ThrowIfNull(settings);

        string baseUrl =
            NormalizeBaseUrl(settings.Value?.BaseUrl);

        _httpClient.BaseAddress =
            new Uri(
                baseUrl,
                UriKind.Absolute);

        _httpClient.DefaultRequestHeaders.Accept.Clear();

        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        _logger.LogInformation(
            "SocialService initialized. Base address: {BaseAddress}",
            _httpClient.BaseAddress);
    }

    // ============================================================
    // COMMUNITY FEED
    // ============================================================

    public Task<FeedResponse?> GetFeedAsync(
        string deviceId,
        string? cursor = null,
        int limit = 20)
    {
        return GetFeedAsync(
            deviceId,
            cursor,
            limit,
            CancellationToken.None);
    }

    public async Task<FeedResponse?> GetFeedAsync(
        string deviceId,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        string normalizedDeviceId =
            NormalizeDeviceId(deviceId);

        int normalizedLimit =
            Math.Clamp(limit, 1, 100);

        var path =
            new StringBuilder(
                "api/social/feed");

        path.Append(
            $"?deviceId={Uri.EscapeDataString(normalizedDeviceId)}");

        path.Append(
            $"&limit={normalizedLimit}");

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            path.Append(
                $"&cursor={Uri.EscapeDataString(cursor.Trim())}");
        }

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                path.ToString());

        return await SendForJsonAsync(
            request,
            NewsContextContainer.Default.FeedResponse,
            operationName: "Load social feed",
            cancellationToken,
            "data",
            "feed",
            "result");
    }

    // ============================================================
    // CREATE POST
    // ============================================================

    public Task<SocialPost?> CreatePostAsync(
        string deviceId,
        string authorName,
        string content,
        string? mediaUrl = null)
    {
        return CreatePostCoreAsync(
            deviceId,
            authorName,
            content,
            mediaUrl,
            channelTag: null,
            CancellationToken.None);
    }

    /// <summary>
    /// Cancellation-aware overload used by ComposePostViewModel.
    /// </summary>
    public Task<SocialPost?> CreatePostAsync(
        string deviceId,
        string authorName,
        string content,
        CancellationToken cancellationToken)
    {
        return CreatePostCoreAsync(
            deviceId,
            authorName,
            content,
            mediaUrl: null,
            channelTag: null,
            cancellationToken);
    }

    public Task<SocialPost?> CreatePostAsync(
        string deviceId,
        string authorName,
        string content,
        string? mediaUrl,
        CancellationToken cancellationToken)
    {
        return CreatePostCoreAsync(
            deviceId,
            authorName,
            content,
            mediaUrl,
            channelTag: null,
            cancellationToken);
    }

    public Task<SocialPost?> CreatePostAsync(
        string deviceId,
        string authorName,
        string content,
        string? mediaUrl = null,
        string? channelTag = null,
        CancellationToken cancellationToken = default)
    {
        return CreatePostCoreAsync(
            deviceId,
            authorName,
            content,
            mediaUrl,
            channelTag,
            cancellationToken);
    }

    private async Task<SocialPost?> CreatePostCoreAsync(
        string deviceId,
        string authorName,
        string content,
        string? mediaUrl,
        string? channelTag,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        string normalizedDeviceId =
            NormalizeDeviceId(deviceId);

        string normalizedAuthor =
            NormalizeAuthorName(authorName);

        string normalizedContent =
            content?.Trim() ??
            string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedContent))
        {
            _logger.LogWarning(
                "CreatePostAsync rejected an empty post.");

            return null;
        }

        if (normalizedContent.Length > MaximumPostLength)
        {
            _logger.LogWarning(
                "CreatePostAsync rejected a post with {Length} characters.",
                normalizedContent.Length);

            return null;
        }

        if (normalizedAuthor.Length > MaximumAuthorLength)
        {
            _logger.LogWarning(
                "CreatePostAsync rejected an author name with {Length} characters.",
                normalizedAuthor.Length);

            return null;
        }

        var payload =
            new Dictionary<string, string>
            {
                ["deviceId"] =
                    normalizedDeviceId,

                ["authorName"] =
                    normalizedAuthor,

                ["content"] =
                    normalizedContent
            };

        if (!string.IsNullOrWhiteSpace(mediaUrl))
        {
            string normalizedMediaUrl =
                mediaUrl.Trim();

            if (!IsSupportedHttpUrl(normalizedMediaUrl))
            {
                _logger.LogWarning(
                    "CreatePostAsync rejected an invalid media URL.");

                return null;
            }

            payload["mediaUrl"] =
                normalizedMediaUrl;
        }

        if (!string.IsNullOrWhiteSpace(channelTag))
        {
            payload["channelTag"] =
                channelTag.Trim();
        }

        using HttpRequestMessage request =
            CreateJsonRequest(
                HttpMethod.Post,
                "api/social/posts",
                payload,
                includeIdempotencyKey: true);

        SocialPost? post =
            await SendForJsonAsync(
                request,
                NewsContextContainer.Default.SocialPost,
                operationName: "Create social post",
                cancellationToken,
                "data",
                "post",
                "result");

        if (post is null)
        {
            _logger.LogWarning(
                "The backend did not return a created social post.");

            return null;
        }

        _logger.LogInformation(
            "Social post successfully created. Post ID: {PostId}",
            post.Id);

        return post;
    }

    // ============================================================
    // LIST POSTS
    // ============================================================

    public Task<List<SocialPost>> GetPostsAsync(
        string filter = "All",
        int page = 1,
        int pageSize = 20)
    {
        return GetPostsAsync(
            filter,
            page,
            pageSize,
            CancellationToken.None);
    }

    public async Task<List<SocialPost>> GetPostsAsync(
        string filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        string normalizedFilter =
            string.IsNullOrWhiteSpace(filter)
                ? "All"
                : filter.Trim();

        int normalizedPage =
            Math.Max(1, page);

        int normalizedPageSize =
            Math.Clamp(pageSize, 1, 100);

        string path =
            "api/social/posts" +
            $"?filter={Uri.EscapeDataString(normalizedFilter)}" +
            $"&page={normalizedPage}" +
            $"&pageSize={normalizedPageSize}" +
            $"&deviceId={Uri.EscapeDataString(ResolveDeviceId())}";

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                path);

        List<SocialPost>? posts =
            await SendForJsonAsync(
                request,
                NewsContextContainer.Default.ListSocialPost,
                operationName: "Load social posts",
                cancellationToken,
                "data",
                "posts",
                "items",
                "result");

        return posts ?? [];
    }

    // ============================================================
    // LIKES AND BOOKMARKS
    // ============================================================

    public Task<bool> LikePostAsync(
        int postId,
        string deviceId)
    {
        if (postId <= 0)
        {
            return Task.FromResult(false);
        }

        return TogglePostLikeCoreAsync(
            postId.ToString(),
            deviceId,
            CancellationToken.None);
    }

    public Task<bool> ToggleLikeAsync(
        string postId,
        bool isLiked)
    {
        /*
         * The backend endpoint is a toggle endpoint, so the current
         * UI state does not need to determine a separate URL.
         * The caller's real device id is used so the feed can report
         * isLiked back for this same identity.
         */
        return TogglePostLikeCoreAsync(
            postId,
            ResolveDeviceId(),
            CancellationToken.None);
    }

    /// <summary>
    /// The install's stable identity, falling back to "anonymous" only if
    /// the platform provider is unavailable (never throws).
    /// </summary>
    private string ResolveDeviceId()
    {
        try
        {
            string deviceId = _deviceService?.GetDeviceId() ?? string.Empty;
            return string.IsNullOrWhiteSpace(deviceId)
                ? "anonymous"
                : deviceId;
        }
        catch
        {
            return "anonymous";
        }
    }

    private async Task<bool> TogglePostLikeCoreAsync(
        string postId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(postId))
        {
            return false;
        }

        string normalizedPostId =
            postId.Trim();

        var payload =
            new Dictionary<string, string>
            {
                ["deviceId"] =
                    NormalizeDeviceId(deviceId)
            };

        using HttpRequestMessage request =
            CreateJsonRequest(
                HttpMethod.Post,
                $"api/social/posts/{Uri.EscapeDataString(normalizedPostId)}/like",
                payload);

        return await SendForSuccessAsync(
            request,
            "Toggle post like",
            cancellationToken);
    }

    public async Task<bool> BookmarkPostAsync(
        string postId,
        bool isBookmarked)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(postId))
        {
            return false;
        }

        var payload =
            new Dictionary<string, string>
            {
                ["deviceId"] =
                    ResolveDeviceId(),

                ["isBookmarked"] =
                    isBookmarked
                        .ToString()
                        .ToLowerInvariant()
            };

        using HttpRequestMessage request =
            CreateJsonRequest(
                HttpMethod.Post,
                $"api/social/posts/{Uri.EscapeDataString(postId.Trim())}/bookmark",
                payload);

        return await SendForSuccessAsync(
            request,
            "Update post bookmark",
            CancellationToken.None);
    }

    // ============================================================
    // ONLINE COUNT
    // ============================================================

    public async Task<int> GetOnlineCountAsync()
    {
        ThrowIfDisposed();

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                "api/social/online-count");

        try
        {
            using HttpResponseMessage response =
                await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    CancellationToken.None);

            string body =
                await ReadResponseBodyAsync(
                    response,
                    CancellationToken.None);

            if (!response.IsSuccessStatusCode)
            {
                LogBackendFailure(
                    "Load social online count",
                    response,
                    body);

                return 0;
            }

            return ParseIntegerResponse(
                body,
                "count",
                "onlineCount",
                "data");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Online count could not be loaded.");

            return 0;
        }
    }

    // ============================================================
    // CONTENT VIEWS AND REACTIONS
    // ============================================================

    public async Task LogViewAsync(
        string deviceId,
        string contentType,
        int contentId)
    {
        ThrowIfDisposed();

        if (contentId <= 0 ||
            string.IsNullOrWhiteSpace(contentType))
        {
            return;
        }

        var payload =
            new Dictionary<string, string>
            {
                ["deviceId"] =
                    NormalizeDeviceId(deviceId),

                ["contentType"] =
                    contentType.Trim(),

                ["contentId"] =
                    contentId.ToString()
            };

        using HttpRequestMessage request =
            CreateJsonRequest(
                HttpMethod.Post,
                "api/social/views",
                payload);

        bool success =
            await SendForSuccessAsync(
                request,
                "Log content view",
                CancellationToken.None);

        if (!success)
        {
            _logger.LogDebug(
                "View logging failed without blocking the user interface.");
        }
    }

    public async Task<int> GetReactionCountAsync(
        string contentType,
        int contentId)
    {
        ThrowIfDisposed();

        if (contentId <= 0 ||
            string.IsNullOrWhiteSpace(contentType))
        {
            return 0;
        }

        string path =
            $"api/social/{Uri.EscapeDataString(contentType.Trim())}" +
            $"/{contentId}/reactions/count";

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                path);

        try
        {
            using HttpResponseMessage response =
                await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    CancellationToken.None);

            string body =
                await ReadResponseBodyAsync(
                    response,
                    CancellationToken.None);

            if (!response.IsSuccessStatusCode)
            {
                LogBackendFailure(
                    "Load reaction count",
                    response,
                    body);

                return 0;
            }

            return ParseIntegerResponse(
                body,
                "count",
                "reactionCount",
                "data");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Reaction count could not be loaded.");

            return 0;
        }
    }

    public async Task<bool> ToggleReactionAsync(
        string deviceId,
        string contentType,
        int contentId)
    {
        ThrowIfDisposed();

        if (contentId <= 0 ||
            string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var payload =
            new Dictionary<string, string>
            {
                ["deviceId"] =
                    NormalizeDeviceId(deviceId)
            };

        string path =
            $"api/social/{Uri.EscapeDataString(contentType.Trim())}" +
            $"/{contentId}/reaction";

        using HttpRequestMessage request =
            CreateJsonRequest(
                HttpMethod.Post,
                path,
                payload);

        return await SendForSuccessAsync(
            request,
            "Toggle content reaction",
            CancellationToken.None);
    }

    // ============================================================
    // POST COMMENTS
    // ============================================================

    public Task<List<SocialComment>> GetCommentsAsync(
        int postId)
    {
        return GetPostCommentsAsync(
            postId,
            CancellationToken.None);
    }

    private async Task<List<SocialComment>> GetPostCommentsAsync(
        int postId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (postId <= 0)
        {
            return [];
        }

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                $"api/social/posts/{postId}/comments");

        List<SocialComment>? comments =
            await SendForJsonAsync(
                request,
                NewsContextContainer.Default.ListSocialComment,
                operationName: "Load post comments",
                cancellationToken,
                "data",
                "comments",
                "items",
                "result");

        return comments ?? [];
    }

    public Task<SocialComment?> CreateCommentAsync(
        int postId,
        string deviceId,
        string authorName,
        string content)
    {
        return CreateCommentAsync(
            postId,
            deviceId,
            authorName,
            content,
            CancellationToken.None);
    }

    public async Task<SocialComment?> CreateCommentAsync(
        int postId,
        string deviceId,
        string authorName,
        string content,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        string normalizedContent =
            content?.Trim() ??
            string.Empty;

        if (postId <= 0 ||
            string.IsNullOrWhiteSpace(normalizedContent) ||
            normalizedContent.Length > MaximumCommentLength)
        {
            return null;
        }

        var payload =
            new Dictionary<string, string>
            {
                ["deviceId"] =
                    NormalizeDeviceId(deviceId),

                ["authorName"] =
                    NormalizeAuthorName(authorName),

                ["content"] =
                    normalizedContent
            };

        using HttpRequestMessage request =
            CreateJsonRequest(
                HttpMethod.Post,
                $"api/social/posts/{postId}/comments",
                payload,
                includeIdempotencyKey: true);

        return await SendForJsonAsync(
            request,
            NewsContextContainer.Default.SocialComment,
            operationName: "Create post comment",
            cancellationToken,
            "data",
            "comment",
            "result");
    }

    // ============================================================
    // CONTENT COMMENTS
    // ============================================================

    public Task<List<UserComment>> GetCommentsAsync(
        string contentType,
        int contentId)
    {
        return GetContentCommentsAsync(
            contentType,
            contentId,
            CancellationToken.None);
    }

    private async Task<List<UserComment>> GetContentCommentsAsync(
        string contentType,
        int contentId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (contentId <= 0 ||
            string.IsNullOrWhiteSpace(contentType))
        {
            return [];
        }

        string path =
            $"api/social/{Uri.EscapeDataString(contentType.Trim())}" +
            $"/{contentId}/comments";

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                path);

        List<UserComment>? comments =
            await SendForJsonAsync(
                request,
                NewsContextContainer.Default.ListUserComment,
                operationName: "Load content comments",
                cancellationToken,
                "data",
                "comments",
                "items",
                "result");

        return comments ?? [];
    }

    public Task<UserComment?> AddCommentAsync(
        string deviceId,
        string contentType,
        int contentId,
        string text)
    {
        return AddCommentAsync(
            deviceId,
            contentType,
            contentId,
            text,
            CancellationToken.None);
    }

    public async Task<UserComment?> AddCommentAsync(
        string deviceId,
        string contentType,
        int contentId,
        string text,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        string normalizedText =
            text?.Trim() ??
            string.Empty;

        if (contentId <= 0 ||
            string.IsNullOrWhiteSpace(contentType) ||
            string.IsNullOrWhiteSpace(normalizedText) ||
            normalizedText.Length > MaximumCommentLength)
        {
            return null;
        }

        var payload =
            new Dictionary<string, string>
            {
                ["deviceId"] =
                    NormalizeDeviceId(deviceId),

                ["text"] =
                    normalizedText
            };

        string path =
            $"api/social/{Uri.EscapeDataString(contentType.Trim())}" +
            $"/{contentId}/comments";

        using HttpRequestMessage request =
            CreateJsonRequest(
                HttpMethod.Post,
                path,
                payload,
                includeIdempotencyKey: true);

        /*
         * Do not fabricate a local comment when the backend fails.
         * A non-null result now means that the backend confirmed it.
         */
        return await SendForJsonAsync(
            request,
            NewsContextContainer.Default.UserComment,
            operationName: "Add content comment",
            cancellationToken,
            "data",
            "comment",
            "result");
    }

    // ============================================================
    // LIVE CHAT
    // ============================================================

    public async Task<bool> SubmitLiveChatMessageAsync(
        int channelId,
        string username,
        string message)
    {
        ThrowIfDisposed();

        string normalizedUsername =
            NormalizeAuthorName(username);

        string normalizedMessage =
            message?.Trim() ??
            string.Empty;

        if (channelId <= 0 ||
            string.IsNullOrWhiteSpace(normalizedMessage) ||
            normalizedMessage.Length > MaximumChatLength)
        {
            return false;
        }

        var bubble =
            new LiveChatBubble
            {
                ChannelId = channelId,
                Username = normalizedUsername,
                Text = normalizedMessage,
                Timestamp = DateTime.UtcNow,
                AvatarHexColor =
                    AssignRandomAvatarAccentColor()
            };

        string json =
            JsonSerializer.Serialize(
                bubble,
                NewsContextContainer.Default.LiveChatBubble);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                $"api/social/chat/{channelId}/send")
            {
                Content =
                    new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json")
            };

        request.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            Guid.NewGuid().ToString("N"));

        bool success =
            await SendForSuccessAsync(
                request,
                "Send live-chat message",
                CancellationToken.None);

        if (!success)
        {
            return false;
        }

        if (TryRememberChatMessage(bubble))
        {
            DispatchChatBubble(
                channelId,
                bubble);
        }

        return true;
    }

    public void SubscribeToLiveChat(
        int channelId,
        Action<LiveChatBubble> onNewMessageReceived)
    {
        ThrowIfDisposed();

        if (channelId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channelId));
        }

        ArgumentNullException.ThrowIfNull(
            onNewMessageReceived);

        lock (_subscriptionLock)
        {
            if (!_chatSubscriptions.TryGetValue(
                    channelId,
                    out HashSet<Action<LiveChatBubble>>? handlers))
            {
                handlers = [];

                _chatSubscriptions[channelId] =
                    handlers;
            }

            handlers.Add(
                onNewMessageReceived);

            StartPollingIfRequiredLocked();
        }

        _logger.LogInformation(
            "Live-chat subscription added. Channel: {ChannelId}",
            channelId);
    }

    public void UnsubscribeFromLiveChat(
        int channelId)
    {
        CancellationTokenSource? cancellation =
            null;

        lock (_subscriptionLock)
        {
            _chatSubscriptions.Remove(
                channelId);

            _chatDeduplicationStates.Remove(
                channelId);

            if (_chatSubscriptions.Count == 0)
            {
                cancellation =
                    _pollingCancellation;

                _pollingCancellation =
                    null;

                _pollingTask =
                    null;
            }
        }

        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        _logger.LogInformation(
            "Live-chat subscriptions removed. Channel: {ChannelId}",
            channelId);
    }

    private void StartPollingIfRequiredLocked()
    {
        if (_pollingCancellation is not null &&
            _pollingTask is not null &&
            !_pollingTask.IsCompleted)
        {
            return;
        }

        var cancellation =
            new CancellationTokenSource();

        _pollingCancellation =
            cancellation;

        _pollingTask =
            StartLiveChatNetworkPollingLoopAsync(
                cancellation.Token);
    }

    private async Task StartLiveChatNetworkPollingLoopAsync(
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Live-chat polling loop started.");

        try
        {
            using var timer =
                new PeriodicTimer(
                    LiveChatPollingInterval);

            while (await timer.WaitForNextTickAsync(
                       cancellationToken))
            {
                int[] activeChannels;

                lock (_subscriptionLock)
                {
                    activeChannels =
                        _chatSubscriptions.Keys
                            .ToArray();
                }

                foreach (int channelId in activeChannels)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await PollChannelAsync(
                        channelId,
                        cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Live-chat polling loop was cancelled.");
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Live-chat polling loop stopped unexpectedly.");
        }
        finally
        {
            _logger.LogInformation(
                "Live-chat polling loop stopped.");
        }
    }

    private async Task PollChannelAsync(
        int channelId,
        CancellationToken cancellationToken)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                $"api/social/chat/{channelId}/poll?secondsAgo=6");

        List<LiveChatBubble>? bubbles =
            await SendForJsonAsync(
                request,
                NewsContextContainer.Default.ListLiveChatBubble,
                operationName: "Poll live chat",
                cancellationToken,
                "data",
                "messages",
                "items",
                "result");

        if (bubbles is null ||
            bubbles.Count == 0)
        {
            return;
        }

        foreach (LiveChatBubble bubble in bubbles)
        {
            if (!TryRememberChatMessage(bubble))
            {
                continue;
            }

            DispatchChatBubble(
                channelId,
                bubble);
        }
    }

    private void DispatchChatBubble(
        int channelId,
        LiveChatBubble bubble)
    {
        Action<LiveChatBubble>[] handlers;

        lock (_subscriptionLock)
        {
            if (!_chatSubscriptions.TryGetValue(
                    channelId,
                    out HashSet<Action<LiveChatBubble>>? subscriptions) ||
                subscriptions.Count == 0)
            {
                return;
            }

            handlers =
                subscriptions.ToArray();
        }

        foreach (Action<LiveChatBubble> handler in handlers)
        {
            MainThread.BeginInvokeOnMainThread(
                () =>
                {
                    try
                    {
                        handler(bubble);
                    }
                    catch (Exception exception)
                    {
                        _logger.LogWarning(
                            exception,
                            "A live-chat subscriber failed. Channel: {ChannelId}",
                            channelId);
                    }
                });
        }
    }

    private bool TryRememberChatMessage(
        LiveChatBubble bubble)
    {
        string signature =
            CreateChatMessageSignature(
                bubble);

        lock (_subscriptionLock)
        {
            if (!_chatDeduplicationStates.TryGetValue(
                    bubble.ChannelId,
                    out ChatDeduplicationState? state))
            {
                state =
                    new ChatDeduplicationState();

                _chatDeduplicationStates[bubble.ChannelId] =
                    state;
            }

            if (!state.Signatures.Add(signature))
            {
                return false;
            }

            state.Order.Enqueue(
                signature);

            while (state.Order.Count >
                   MaximumRememberedChatMessages)
            {
                string oldest =
                    state.Order.Dequeue();

                state.Signatures.Remove(
                    oldest);
            }

            return true;
        }
    }

    private static string CreateChatMessageSignature(
        LiveChatBubble bubble)
    {
        return string.Join(
            "|",
            bubble.ChannelId,
            bubble.Username?.Trim(),
            bubble.Timestamp.ToUniversalTime().Ticks,
            bubble.Text?.Trim());
    }

    // ============================================================
    // HTTP HELPERS
    // ============================================================

    private HttpRequestMessage CreateJsonRequest(
        HttpMethod method,
        string path,
        Dictionary<string, string> payload,
        bool includeIdempotencyKey = false)
    {
        string json =
            JsonSerializer.Serialize(
                payload,
                NewsContextContainer.Default.DictionaryStringString);

        var request =
            new HttpRequestMessage(
                method,
                path)
            {
                Content =
                    new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json")
            };

        if (includeIdempotencyKey)
        {
            /*
             * The backend should persist this key and return the
             * original result for repeated requests with the same key.
             */
            request.Headers.TryAddWithoutValidation(
                "Idempotency-Key",
                Guid.NewGuid().ToString("N"));
        }

        return request;
    }

    private async Task<T?> SendForJsonAsync<T>(
        HttpRequestMessage request,
        JsonTypeInfo<T> jsonTypeInfo,
        string operationName,
        CancellationToken cancellationToken,
        params string[] wrapperNames)
    {
        try
        {
            using HttpResponseMessage response =
                await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            string body =
                await ReadResponseBodyAsync(
                    response,
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogBackendFailure(
                    operationName,
                    response,
                    body);

                return default;
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                _logger.LogWarning(
                    "{Operation} succeeded but returned an empty body.",
                    operationName);

                return default;
            }

            try
            {
                return DeserializePossiblyWrapped(
                    body,
                    jsonTypeInfo,
                    wrapperNames);
            }
            catch (JsonException exception)
            {
                _logger.LogError(
                    exception,
                    "{Operation} returned invalid JSON. Body: {Body}",
                    operationName,
                    Truncate(body));

                return default;
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogError(
                exception,
                "{Operation} timed out.",
                operationName);

            return default;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "{Operation} failed because the backend could not be reached.",
                operationName);

            return default;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "{Operation} failed unexpectedly.",
                operationName);

            return default;
        }
    }

    private async Task<bool> SendForSuccessAsync(
        HttpRequestMessage request,
        string operationName,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response =
                await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            string body =
                await ReadResponseBodyAsync(
                    response,
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogBackendFailure(
                    operationName,
                    response,
                    body);

                return false;
            }

            /*
             * Some APIs return 200 with {"success":false}.
             * Detect that instead of trusting the HTTP status alone.
             */
            if (ResponseExplicitlyReportsFailure(body))
            {
                _logger.LogWarning(
                    "{Operation} returned HTTP success but reported failure. Body: {Body}",
                    operationName,
                    Truncate(body));

                return false;
            }

            return true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogError(
                exception,
                "{Operation} timed out.",
                operationName);

            return false;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "{Operation} could not reach the backend.",
                operationName);

            return false;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "{Operation} failed unexpectedly.",
                operationName);

            return false;
        }
    }

    private static T? DeserializePossiblyWrapped<T>(
        string json,
        JsonTypeInfo<T> jsonTypeInfo,
        params string[] wrapperNames)
    {
        using JsonDocument document =
            JsonDocument.Parse(json);

        JsonElement root =
            document.RootElement;

        JsonElement payload =
            FindPayloadElement(
                root,
                wrapperNames,
                maximumDepth: 3);

        return JsonSerializer.Deserialize(
            payload.GetRawText(),
            jsonTypeInfo);
    }

    private static JsonElement FindPayloadElement(
        JsonElement element,
        string[] wrapperNames,
        int maximumDepth)
    {
        if (maximumDepth <= 0 ||
            element.ValueKind != JsonValueKind.Object)
        {
            return element;
        }

        foreach (string wrapperName in wrapperNames)
        {
            if (element.TryGetProperty(
                    wrapperName,
                    out JsonElement wrapped))
            {
                return FindPayloadElement(
                    wrapped,
                    wrapperNames,
                    maximumDepth - 1);
            }
        }

        /*
         * Common API wrapper:
         * { "success": true, "data": { "items": [...] } }
         */
        if (element.TryGetProperty(
                "data",
                out JsonElement data))
        {
            return FindPayloadElement(
                data,
                wrapperNames,
                maximumDepth - 1);
        }

        return element;
    }

    private static bool ResponseExplicitlyReportsFailure(
        string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using JsonDocument document =
                JsonDocument.Parse(body);

            JsonElement root =
                document.RootElement;

            return
                root.ValueKind ==
                JsonValueKind.Object &&
                root.TryGetProperty(
                    "success",
                    out JsonElement successElement) &&
                successElement.ValueKind ==
                JsonValueKind.False;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int ParseIntegerResponse(
        string body,
        params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return 0;
        }

        if (int.TryParse(
                body.Trim(),
                out int directNumber))
        {
            return Math.Max(0, directNumber);
        }

        try
        {
            using JsonDocument document =
                JsonDocument.Parse(body);

            JsonElement root =
                document.RootElement;

            if (root.ValueKind == JsonValueKind.Number &&
                root.TryGetInt32(out int number))
            {
                return Math.Max(0, number);
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return 0;
            }

            foreach (string propertyName in propertyNames)
            {
                if (!root.TryGetProperty(
                        propertyName,
                        out JsonElement property))
                {
                    continue;
                }

                if (property.ValueKind == JsonValueKind.Number &&
                    property.TryGetInt32(out int propertyNumber))
                {
                    return Math.Max(
                        0,
                        propertyNumber);
                }

                if (property.ValueKind == JsonValueKind.String &&
                    int.TryParse(
                        property.GetString(),
                        out int stringNumber))
                {
                    return Math.Max(
                        0,
                        stringNumber);
                }
            }
        }
        catch (JsonException)
        {
            // Invalid response is treated as zero.
        }

        return 0;
    }

    private static async Task<string> ReadResponseBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            return string.Empty;
        }

        return await response.Content.ReadAsStringAsync(
            cancellationToken);
    }

    private void LogBackendFailure(
        string operationName,
        HttpResponseMessage response,
        string responseBody)
    {
        string body =
            Truncate(responseBody);

        if (response.StatusCode ==
            HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning(
                "{Operation} was rejected as unauthorized. Status: {StatusCode}; Body: {Body}",
                operationName,
                (int)response.StatusCode,
                body);

            return;
        }

        if (response.StatusCode ==
            HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "{Operation} endpoint was not found. Status: {StatusCode}; URI: {Uri}; Body: {Body}",
                operationName,
                (int)response.StatusCode,
                response.RequestMessage?.RequestUri,
                body);

            return;
        }

        _logger.LogError(
            "{Operation} failed. Status: {StatusCode} {Reason}; URI: {Uri}; Body: {Body}",
            operationName,
            (int)response.StatusCode,
            response.ReasonPhrase,
            response.RequestMessage?.RequestUri,
            body);
    }

    // ============================================================
    // VALIDATION
    // ============================================================

    private static string NormalizeBaseUrl(
    string? configuredBaseUrl)
    {
        string value =
            string.IsNullOrWhiteSpace(configuredBaseUrl)
                ? DefaultBaseUrl
                : configuredBaseUrl.Trim();

        if (!value.EndsWith(
                "/",
                StringComparison.Ordinal))
        {
            value += "/";
        }

        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out Uri? uri))
        {
            throw new InvalidOperationException(
                "SocialApiSettings.BaseUrl is not a valid absolute URL.");
        }

        bool isHttp =
            string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase);

        bool isHttps =
            string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase);

        if (!isHttp && !isHttps)
        {
            throw new InvalidOperationException(
                "SocialApiSettings.BaseUrl must use HTTP or HTTPS.");
        }

        return uri.AbsoluteUri.EndsWith(
            "/",
            StringComparison.Ordinal)
                ? uri.AbsoluteUri
                : uri.AbsoluteUri + "/";
    }

    private static string NormalizeDeviceId(
        string? deviceId)
    {
        return string.IsNullOrWhiteSpace(deviceId)
            ? "anonymous"
            : deviceId.Trim();
    }

    private static string NormalizeAuthorName(
        string? authorName)
    {
        string value =
            string.IsNullOrWhiteSpace(authorName)
                ? "MarkUp Viewer"
                : authorName.Trim();

        return value.Length <= MaximumAuthorLength
            ? value
            : value[..MaximumAuthorLength];
    }

    private static bool IsSupportedHttpUrl(
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

    private static string Truncate(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        string normalized =
            value.Trim();

        return normalized.Length <= MaximumErrorBodyLength
            ? normalized
            : normalized[..MaximumErrorBodyLength] +
              "…";
    }

    private static string AssignRandomAvatarAccentColor()
    {
        return AvatarPalette[
            Random.Shared.Next(
                AvatarPalette.Length)];
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    // ============================================================
    // DISPOSAL
    // ============================================================

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        CancellationTokenSource? cancellation;

        lock (_subscriptionLock)
        {
            cancellation =
                _pollingCancellation;

            _pollingCancellation =
                null;

            _pollingTask =
                null;

            _chatSubscriptions.Clear();
            _chatDeduplicationStates.Clear();
        }

        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        /*
         * Do not dispose HttpClient here. Its lifetime should be
         * managed by IHttpClientFactory and dependency injection.
         */

        GC.SuppressFinalize(this);
    }

    private sealed class ChatDeduplicationState
    {
        public HashSet<string> Signatures { get; } =
            new(StringComparer.Ordinal);

        public Queue<string> Order { get; } =
            new();
    }
}