using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using MarkUptv.Models;
using Microsoft.Extensions.Logging;

namespace MarkUptv.Services;

/// <summary>Outcome of checking whether a stream answers right now.</summary>
public readonly record struct ProbeResult(
    bool Probed,
    bool Ok,
    string? Reason,
    long LatencyMs);

/// <summary>
/// Verifies that a stream URL is actually answering before the player commits
/// to it. HLS manifests are fetched (first few KB) and checked for the
/// #EXTM3U signature, so a dead source fails in under two seconds instead of
/// leaving the viewer on a black screen.
/// </summary>
public sealed class StreamProbeService
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(75);

    private readonly ILogger<StreamProbeService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, ProbeResult Result)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public StreamProbeService(ILogger<StreamProbeService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        SocketsHttpHandler handler = new()
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(4),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = true
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.apple.mpegurl"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/x-mpegURL"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("*/*", 0.8));
    }

    /// <summary>
    /// Probes a direct (HLS) source. Embed and browse-only entries are not
    /// probed - they are validated by the in-app browser instead.
    /// </summary>
    public async Task<ProbeResult> ProbeAsync(LiveSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.IsDirectlyPlayable || string.IsNullOrWhiteSpace(source.Url))
        {
            return new ProbeResult(Probed: false, Ok: true, Reason: "not-a-direct-stream", LatencyMs: 0);
        }

        string url = source.Url!.Trim();

        if (_cache.TryGetValue(url, out (DateTimeOffset At, ProbeResult Result) cached) &&
            DateTimeOffset.UtcNow - cached.At < CacheLifetime)
        {
            return cached.Result;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        ProbeResult result;

        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);

            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, 2047);

            using HttpResponseMessage response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                result = new ProbeResult(true, false, $"HTTP {(int)response.StatusCode}", stopwatch.ElapsedMilliseconds);
            }
            else
            {
                string contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                string body = string.Empty;

                try
                {
                    using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                    byte[] buffer = new byte[2048];
                    int read = await stream.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false);
                    body = System.Text.Encoding.UTF8.GetString(buffer, 0, Math.Max(0, read));
                }
                catch (Exception readException)
                {
                    _logger.LogDebug(readException, "Probe body read failed for {Url}", url);
                }

                bool looksLikeHls = body.Contains("#EXTM3U", StringComparison.OrdinalIgnoreCase);
                bool looksLikeVideo =
                    contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
                    contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) ||
                    contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase);

                result = looksLikeHls || looksLikeVideo
                    ? new ProbeResult(true, true, null, stopwatch.ElapsedMilliseconds)
                    : new ProbeResult(true, false, "unexpected payload", stopwatch.ElapsedMilliseconds);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            result = new ProbeResult(true, false, "timed out", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogDebug(exception, "Probe failed for {Url}", url);
            result = new ProbeResult(true, false, "unreachable", stopwatch.ElapsedMilliseconds);
        }

        _cache[url] = (DateTimeOffset.UtcNow, result);

        _logger.LogInformation(
            "Probe {Url} -> ok={Ok} reason={Reason} {Latency}ms",
            url,
            result.Ok,
            result.Reason ?? "ok",
            result.LatencyMs);

        return result;
    }
}
