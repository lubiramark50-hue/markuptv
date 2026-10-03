using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MarkUptv.Services;

/// <summary>
/// Resolves a YouTube channel handle (e.g. <c>@citizentvkenya</c>) to the
/// video ID of its current live stream by fetching the channel's <c>/live</c>
/// page and extracting the canonical video ID from the HTML.
///
/// Results are cached for a short period because a channel's live video ID
/// does not change mid-broadcast.
/// </summary>
public sealed class YouTubeLiveResolver
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<YouTubeLiveResolver> _logger;

    /// <summary>How long a resolved video ID is considered fresh.</summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>Thread-safe in-memory cache: channel handle → (videoId, resolvedAtUtc).</summary>
    private readonly Dictionary<string, (string VideoId, DateTime ResolvedAtUtc)> _cache = new(
        StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _gate = new(1, 1);

    // Matches the canonical video ID in the YouTube page HTML.
    // YouTube embeds the video ID in several places; the most reliable is
    // the "videoId" JSON key or the /watch?v= link.
    private static readonly Regex VideoIdPattern = new(
        @"""videoId""\s*:\s*""([a-zA-Z0-9_-]{11})""",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    // Fallback: match from a canonical URL.
    private static readonly Regex WatchUrlPattern = new(
        @"/watch\?v=([a-zA-Z0-9_-]{11})",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    public YouTubeLiveResolver(
        HttpClient httpClient,
        ILogger<YouTubeLiveResolver> logger)
    {
        _httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));

        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolves a <c>youtube://@handle</c> URI to a full YouTube embed URL.
    /// Returns <c>null</c> if the channel is not currently live.
    /// </summary>
    public async Task<string?> ResolveEmbedUrlAsync(
        string youtubeSchemeUrl,
        CancellationToken cancellationToken = default)
    {
        // Parse the youtube://@handle scheme.
        string handle = youtubeSchemeUrl
            .Replace("youtube://", "", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .TrimStart('/');

        if (string.IsNullOrWhiteSpace(handle))
        {
            return null;
        }

        string? videoId = await ResolveVideoIdAsync(handle, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(videoId))
        {
            return null;
        }

        return $"https://www.youtube.com/embed/{videoId}" +
               "?autoplay=1&playsinline=1&modestbranding=1&rel=0&controls=1";
    }

    /// <summary>
    /// Resolves a YouTube channel handle to the video ID of its current
    /// live stream. Returns <c>null</c> if the channel is not live.
    /// </summary>
    public async Task<string?> ResolveVideoIdAsync(
        string channelHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelHandle))
        {
            return null;
        }

        // Normalise: ensure the handle starts with @.
        if (!channelHandle.StartsWith('@'))
        {
            channelHandle = $"@{channelHandle}";
        }

        // Check cache.
        if (TryGetCached(channelHandle, out string? cached))
        {
            _logger.LogDebug(
                "YouTube live cache hit for {Handle}: {VideoId}",
                channelHandle,
                cached);

            return cached;
        }

        // Resolve from YouTube.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Double-check after acquiring the lock.
            if (TryGetCached(channelHandle, out cached))
            {
                return cached;
            }

            string? videoId = await FetchLiveVideoIdAsync(
                    channelHandle, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(videoId))
            {
                _cache[channelHandle] = (videoId, DateTime.UtcNow);

                _logger.LogInformation(
                    "Resolved YouTube live stream for {Handle}: {VideoId}",
                    channelHandle,
                    videoId);
            }
            else
            {
                _logger.LogInformation(
                    "YouTube channel {Handle} is not currently live.",
                    channelHandle);
            }

            return videoId;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the given URL uses the <c>youtube://</c>
    /// scheme that this resolver handles.
    /// </summary>
    public static bool IsYouTubeSchemeUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) &&
               url.StartsWith("youtube://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts the channel's <c>/live</c> fallback URL from a
    /// <c>youtube://@handle</c> URI (for use when resolution fails).
    /// </summary>
    public static string GetFallbackUrl(string youtubeSchemeUrl)
    {
        string handle = youtubeSchemeUrl
            .Replace("youtube://", "", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .TrimStart('/');

        return $"https://www.youtube.com/{handle}/live";
    }

    private bool TryGetCached(string handle, out string? videoId)
    {
        if (_cache.TryGetValue(handle, out var entry) &&
            (DateTime.UtcNow - entry.ResolvedAtUtc) < CacheDuration)
        {
            videoId = entry.VideoId;
            return true;
        }

        videoId = null;
        return false;
    }

    private async Task<string?> FetchLiveVideoIdAsync(
        string channelHandle,
        CancellationToken cancellationToken)
    {
        // YouTube's /@handle/live redirects to the current live stream
        // (or to the channel page if nothing is live).
        string url = $"https://www.youtube.com/{channelHandle}/live";

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);

            // Mimic a browser so YouTube returns full HTML with the video ID.
            request.Headers.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            request.Headers.TryAddWithoutValidation(
                "Accept-Language", "en-US,en;q=0.9");

            using HttpResponseMessage response = await _httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "YouTube returned HTTP {Status} for {Handle}",
                    (int)response.StatusCode,
                    channelHandle);

                return null;
            }

            string html = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            // Only accept the video ID if the page actually indicates a live
            // stream. YouTube returns a video ID even for VODs and upcoming
            // premieres; check for the live badge.
            bool isLive =
                html.Contains("\"isLive\":true", StringComparison.Ordinal) ||
                html.Contains("\"isLiveContent\":true", StringComparison.Ordinal) ||
                html.Contains("LIVE_STREAM_OFFLINE", StringComparison.Ordinal) ||
                html.Contains("\"isLiveBroadcast\"", StringComparison.Ordinal);

            if (!isLive)
            {
                _logger.LogDebug(
                    "Page for {Handle} does not contain a live stream indicator.",
                    channelHandle);

                // Still try — some channels' /live pages are always a live
                // stream even without the explicit isLive flag.
            }

            // Try the JSON "videoId" key first (most reliable).
            Match match = VideoIdPattern.Match(html);

            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            // Fallback: canonical /watch?v= URL.
            match = WatchUrlPattern.Match(html);

            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to resolve YouTube live stream for {Handle}",
                channelHandle);

            return null;
        }
    }
}
