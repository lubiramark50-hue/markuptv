using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using MarkUptv.Models;
using MarkUptv.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarkUptv.Services;

/// <summary>
/// Loads channels from MarkUpTvServer and enriches them with
/// current and upcoming EPG programme information.
/// </summary>
public sealed class TvApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TvApiService> _logger;
    private readonly TvApiSettings _settings;

    public TvApiService(
        HttpClient httpClient,
        ILogger<TvApiService> logger,
        IOptions<TvApiSettings> settings)
    {
        _httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));

        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        _settings = settings?.Value
            ?? throw new ArgumentNullException(nameof(settings));

        EnsureBaseAddress();
    }

    /// <summary>
    /// Retrieves channels for a category and adds current EPG data.
    /// </summary>
    public async Task<CategoryResponse?> GetChannelsAsync(
        string category,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            _logger.LogWarning(
                "Channel loading was rejected because the category was empty.");

            return null;
        }

        string normalizedCategory = category.Trim();

        string requestPath = ExpandTemplate(
            _settings.ChannelsPathTemplate,
            ("category", normalizedCategory));

        try
        {
            using HttpResponseMessage response =
                await SendWithRetryAsync(
                    () => new HttpRequestMessage(
                        HttpMethod.Get,
                        requestPath),
                    ct);

            if (!response.IsSuccessStatusCode)
            {
                await LogFailedResponseAsync(
                    response,
                    $"loading category '{normalizedCategory}'",
                    ct);

                return null;
            }

            string json =
                await response.Content.ReadAsStringAsync(ct);

            CategoryResponse? result =
                DeserializeCategoryResponse(json);

            if (result is null)
            {
                _logger.LogWarning(
                    "The backend returned an empty or invalid channel response for category {Category}.",
                    normalizedCategory);

                return null;
            }

            NormalizeChannels(
                result.Channels,
                normalizedCategory);

            result.Channels = result.Channels
                .Where(channel =>
                    channel.Id > 0 &&
                    !string.IsNullOrWhiteSpace(channel.Name) &&
                    !string.IsNullOrWhiteSpace(channel.Url))
                .GroupBy(channel => channel.Id)
                .Select(group => group.First())
                .ToList();

            if (_settings.EnableEpg)
            {
                await EnrichChannelsWithEpgAsync(
                    result.Channels,
                    ct);
            }

            result.Category =
                string.IsNullOrWhiteSpace(result.Category)
                    ? normalizedCategory
                    : result.Category.Trim();

            _logger.LogInformation(
                "Loaded {ChannelCount} channels for category {Category}.",
                result.Channels.Count,
                normalizedCategory);

            return result;
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogWarning(
                exception,
                "Channel request timed out for category {Category}.",
                normalizedCategory);

            return null;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "The TV backend could not be reached for category {Category}.",
                normalizedCategory);

            return null;
        }
        catch (JsonException exception)
        {
            _logger.LogError(
                exception,
                "The TV backend returned invalid channel JSON for category {Category}.",
                normalizedCategory);

            return null;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unexpected error while loading category {Category}.",
                normalizedCategory);

            return null;
        }
    }

    /// <summary>
    /// Retrieves programmes for one channel.
    /// </summary>
    public async Task<IReadOnlyList<TvProgramme>> GetProgrammesAsync(
        int channelId,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken ct = default)
    {
        if (channelId <= 0)
        {
            return Array.Empty<TvProgramme>();
        }

        DateTime normalizedFrom =
            NormalizeUtc(
                fromUtc ??
                DateTime.UtcNow.AddMinutes(
                    -Math.Max(
                        0,
                        _settings.EpgLookBehindMinutes)));

        DateTime normalizedTo =
            NormalizeUtc(
                toUtc ??
                DateTime.UtcNow.AddHours(
                    Math.Max(
                        1,
                        _settings.EpgLookAheadHours)));

        if (normalizedTo <= normalizedFrom)
        {
            normalizedTo =
                normalizedFrom.AddHours(12);
        }

        string endpoint = ExpandTemplate(
            _settings.ProgrammesPathTemplate,
            (
                "channelId",
                channelId.ToString(
                    CultureInfo.InvariantCulture)));

        string requestPath =
            endpoint +
            $"?fromUtc={Uri.EscapeDataString(normalizedFrom.ToString("O", CultureInfo.InvariantCulture))}" +
            $"&toUtc={Uri.EscapeDataString(normalizedTo.ToString("O", CultureInfo.InvariantCulture))}";

        try
        {
            using HttpResponseMessage response =
                await SendWithRetryAsync(
                    () => new HttpRequestMessage(
                        HttpMethod.Get,
                        requestPath),
                    ct);

            if (!response.IsSuccessStatusCode)
            {
                await LogFailedResponseAsync(
                    response,
                    $"loading EPG for channel {channelId}",
                    ct);

                return Array.Empty<TvProgramme>();
            }

            string json =
                await response.Content.ReadAsStringAsync(ct);

            List<TvProgramme> programmes =
                DeserializeProgrammes(json);

            foreach (TvProgramme programme in programmes)
            {
                programme.ChannelId =
                    programme.ChannelId > 0
                        ? programme.ChannelId
                        : channelId;

                programme.Title =
                    programme.Title?.Trim()
                    ?? string.Empty;

                programme.Description =
                    NormalizeOptionalText(
                        programme.Description);

                programme.StartUtc =
                    NormalizeUtc(programme.StartUtc);

                programme.EndUtc =
                    NormalizeUtc(programme.EndUtc);
            }

            return programmes
                .Where(programme =>
                    !string.IsNullOrWhiteSpace(programme.Title) &&
                    programme.EndUtc > programme.StartUtc)
                .OrderBy(programme =>
                    programme.StartUtc)
                .ToList();
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            /*
             * EPG failure should not prevent channels from loading.
             */
            _logger.LogWarning(
                exception,
                "Failed to retrieve EPG for channel {ChannelId}.",
                channelId);

            return Array.Empty<TvProgramme>();
        }
    }

    /// <summary>
    /// Retrieves trending channels, with fallback categories.
    /// A category that answers with zero channels counts as a miss so the
    /// rail always ends up populated whenever any content exists at all.
    /// </summary>
    public async Task<List<TvChannel>> GetTrendingAsync(
        int limit = 15,
        CancellationToken ct = default)
    {
        int normalizedLimit =
            Math.Clamp(limit, 1, 100);

        string[] candidates =
        ["trending", "general", "news", "sports"];

        foreach (string candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            CategoryResponse? response =
                await GetChannelsAsync(candidate, ct);

            if (response is null)
            {
                continue;
            }

            var channels = response.Channels
                .Where(channel =>
                    channel.Id > 0 &&
                    !string.IsNullOrWhiteSpace(channel.Name) &&
                    !string.IsNullOrWhiteSpace(channel.Url))
                .Take(normalizedLimit)
                .ToList();

            if (channels.Count > 0)
            {
                return channels;
            }
        }

        return [];
    }

    /// <summary>
    /// Reports an unusable stream to the backend without
    /// interrupting the application UI.
    /// </summary>
    public async Task ReportFailedUrlAsync(
        string url,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var payload = new FailedStreamReport
        {
            Url = url.Trim()
        };

        try
        {
            using HttpResponseMessage response =
                await SendWithRetryAsync(
                    () =>
                    {
                        string json =
                            JsonSerializer.Serialize(
                                payload,
                                MarkUptvJsonContext
                                    .Default
                                    .FailedStreamReport);

                        return new HttpRequestMessage(
                            HttpMethod.Post,
                            NormalizeRelativePath(
                                _settings.ReportFailurePath))
                        {
                            Content = new StringContent(
                                json,
                                Encoding.UTF8,
                                "application/json")
                        };
                    },
                    ct);

            if (!response.IsSuccessStatusCode)
            {
                await LogFailedResponseAsync(
                    response,
                    "reporting a failed stream",
                    ct);
            }
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to report stream URL {StreamUrl}.",
                payload.Url);
        }
    }

    /// <summary>
    /// Self-heal: asks the backend to extract a fresh stream from the
    /// channel's web-player source. Returns the resolved stream (with the
    /// headers the player must send) or null when no repair was possible.
    /// </summary>
    public async Task<ScrapedStream?> RepairChannelAsync(
        int channelId,
        CancellationToken ct = default)
    {
        string requestPath = ExpandTemplate(
            _settings.RepairChannelPathTemplate,
            ("id", channelId.ToString()));

        try
        {
            using HttpResponseMessage response =
                await SendWithRetryAsync(
                    () => new HttpRequestMessage(
                        HttpMethod.Post,
                        requestPath),
                    ct);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode != HttpStatusCode.NotFound &&
                    response.StatusCode != HttpStatusCode.BadRequest)
                {
                    await LogFailedResponseAsync(
                        response,
                        $"repairing channel {channelId}",
                        ct);
                }

                return null;
            }

            string json =
                await response.Content.ReadAsStringAsync(ct);

            return JsonSerializer.Deserialize(
                       json,
                       MarkUptvJsonContext.Default.ScrapedStream)
                   ?? null;
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to repair channel {ChannelId}.",
                channelId);

            return null;
        }
    }

    private async Task EnrichChannelsWithEpgAsync(
        IReadOnlyCollection<TvChannel> channels,
        CancellationToken ct)
    {
        if (channels.Count == 0)
        {
            return;
        }

        // The backend already ships a current-programme title for most channels,
        // and those need no extra round trip. Sweeping a large category (news
        // returns 700+ channels, one request each) used to exceed the request
        // timeout, so the whole category failed and the page rendered as empty.
        // Only channels that genuinely lack programme information - and only the
        // ones near the top, i.e. the ones the user sees first - are enriched.
        List<TvChannel> epgTargets = channels
            .Where(channel =>
                channel.CurrentProgramme is null &&
                string.IsNullOrWhiteSpace(
                    channel.CurrentProgrammeTitle))
            .Take(Math.Max(0, _settings.MaxEpgPrefetchChannels))
            .ToList();

        if (epgTargets.Count == 0)
        {
            return;
        }

        DateTime nowUtc = DateTime.UtcNow;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(
                _settings.MaxConcurrentEpgRequests,
                1,
                12),

            CancellationToken = ct
        };

        await Parallel.ForEachAsync(
            epgTargets,
            parallelOptions,
            async (channel, token) =>
            {
                if (channel.CurrentProgramme is
                        TvProgramme suppliedProgramme &&
                    suppliedProgramme.StartUtc <= nowUtc &&
                    suppliedProgramme.EndUtc > nowUtc)
                {
                    ApplyProgrammeInformation(
                        channel,
                        suppliedProgramme,
                        channel.NextProgramme);

                    return;
                }

                IReadOnlyList<TvProgramme> programmes =
                    await GetProgrammesAsync(
                        channel.Id,
                        nowUtc.AddMinutes(
                            -Math.Max(
                                0,
                                _settings.EpgLookBehindMinutes)),
                        nowUtc.AddHours(
                            Math.Max(
                                1,
                                _settings.EpgLookAheadHours)),
                        token);

                TvProgramme? currentProgramme =
                    programmes
                        .Where(programme =>
                            programme.StartUtc <= nowUtc &&
                            programme.EndUtc > nowUtc)
                        .OrderByDescending(programme =>
                            programme.StartUtc)
                        .FirstOrDefault();

                TvProgramme? nextProgramme =
                    programmes
                        .Where(programme =>
                            programme.StartUtc > nowUtc)
                        .OrderBy(programme =>
                            programme.StartUtc)
                        .FirstOrDefault();

                ApplyProgrammeInformation(
                    channel,
                    currentProgramme,
                    nextProgramme);
            });
    }

    private static void ApplyProgrammeInformation(
        TvChannel channel,
        TvProgramme? currentProgramme,
        TvProgramme? nextProgramme)
    {
        channel.CurrentProgramme =
            currentProgramme;

        channel.NextProgramme =
            nextProgramme;

        channel.CurrentProgrammeTitle =
            currentProgramme?.Title;

        channel.CurrentProgrammeStartUtc =
            currentProgramme?.StartUtc;

        channel.CurrentProgrammeEndUtc =
            currentProgramme?.EndUtc;

        channel.NextProgrammeTitle =
            nextProgramme?.Title;

        channel.NextProgrammeStartUtc =
            nextProgramme?.StartUtc;
    }

    private void NormalizeChannels(
        IEnumerable<TvChannel> channels,
        string requestedCategory)
    {
        foreach (TvChannel channel in channels)
        {
            channel.Name =
                channel.Name?.Trim()
                ?? string.Empty;

            channel.Category =
                string.IsNullOrWhiteSpace(channel.Category)
                    ? requestedCategory
                    : channel.Category.Trim();

            channel.Group =
                NormalizeOptionalText(channel.Group);

            channel.Logo =
                NormalizeResourceUrl(channel.Logo);

            channel.Url =
                NormalizeResourceUrl(channel.Url)
                ?? string.Empty;

            channel.Initials =
                string.IsNullOrWhiteSpace(channel.Initials)
                    ? CreateInitials(channel.Name)
                    : channel.Initials.Trim();

            channel.QualityLabel =
                string.IsNullOrWhiteSpace(channel.QualityLabel)
                    ? "LIVE"
                    : channel.QualityLabel.Trim();

            channel.CurrentProgrammeTitle =
                NormalizeOptionalText(
                    channel.CurrentProgrammeTitle);

            channel.NextProgrammeTitle =
                NormalizeOptionalText(
                    channel.NextProgrammeTitle);
        }
    }

    private string? NormalizeResourceUrl(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalizedValue =
            value.Trim();

        if (Uri.TryCreate(
                normalizedValue,
                UriKind.Absolute,
                out Uri? absoluteUri))
        {
            return absoluteUri.ToString();
        }

        if (_httpClient.BaseAddress is not null &&
            Uri.TryCreate(
                _httpClient.BaseAddress,
                normalizedValue,
                out Uri? combinedUri))
        {
            return combinedUri.ToString();
        }

        _logger.LogWarning(
            "The backend returned an invalid resource URL: {ResourceUrl}",
            normalizedValue);

        return null;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken ct)
    {
        int retryCount =
            Math.Clamp(
                _settings.RetryCount,
                0,
                5);

        int totalAttempts =
            retryCount + 1;

        Exception? lastException = null;

        for (int attempt = 1;
             attempt <= totalAttempts;
             attempt++)
        {
            ct.ThrowIfCancellationRequested();

            using var timeoutSource =
                CancellationTokenSource
                    .CreateLinkedTokenSource(ct);

            timeoutSource.CancelAfter(
                TimeSpan.FromSeconds(
                    Math.Clamp(
                        _settings.RequestTimeoutSeconds,
                        5,
                        180)));

            using HttpRequestMessage request =
                requestFactory();

            try
            {
                HttpResponseMessage response =
                    await _httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeoutSource.Token);

                bool shouldRetry =
                    IsTransientStatusCode(response.StatusCode) &&
                    attempt < totalAttempts;

                if (!shouldRetry)
                {
                    return response;
                }

                _logger.LogWarning(
                    "TV API returned transient status {StatusCode}. Attempt {Attempt}/{TotalAttempts}.",
                    response.StatusCode,
                    attempt,
                    totalAttempts);

                response.Dispose();
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
                when (exception is HttpRequestException or
                      OperationCanceledException)
            {
                lastException = exception;

                if (attempt >= totalAttempts)
                {
                    throw;
                }

                _logger.LogWarning(
                    exception,
                    "Transient TV API failure. Attempt {Attempt}/{TotalAttempts}.",
                    attempt,
                    totalAttempts);
            }

            int jitterMilliseconds =
                Random.Shared.Next(50, 250);

            double exponentialMilliseconds =
                Math.Pow(2, attempt - 1) * 300;

            TimeSpan delay =
                TimeSpan.FromMilliseconds(
                    exponentialMilliseconds +
                    jitterMilliseconds);

            await Task.Delay(delay, ct);
        }

        throw lastException
            ?? new HttpRequestException(
                "The TV API request failed.");
    }

    private static CategoryResponse?
        DeserializeCategoryResponse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using JsonDocument document =
            JsonDocument.Parse(json);

        if (document.RootElement.ValueKind ==
            JsonValueKind.Array)
        {
            List<TvChannel> channels =
                JsonSerializer.Deserialize(
                    json,
                    MarkUptvJsonContext
                        .Default
                        .TvChannelList)
                ?? [];

            return new CategoryResponse
            {
                Channels = channels
            };
        }

        if (document.RootElement.ValueKind ==
            JsonValueKind.Object)
        {
            return JsonSerializer.Deserialize(
                json,
                MarkUptvJsonContext
                    .Default
                    .CategoryResponse);
        }

        return null;
    }

    private static List<TvProgramme>
        DeserializeProgrammes(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using JsonDocument document =
            JsonDocument.Parse(json);

        if (document.RootElement.ValueKind ==
            JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize(
                       json,
                       MarkUptvJsonContext
                           .Default
                           .TvProgrammeList)
                   ?? [];
        }

        if (document.RootElement.ValueKind ==
            JsonValueKind.Object)
        {
            ProgrammeResponse? response =
                JsonSerializer.Deserialize(
                    json,
                    MarkUptvJsonContext
                        .Default
                        .ProgrammeResponse);

            return response?.Programmes ?? [];
        }

        return [];
    }

    private void EnsureBaseAddress()
    {
        if (_httpClient.BaseAddress is not null)
        {
            return;
        }

        string baseUrl =
            _settings.BaseUrl?.Trim()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "TvApiSettings.BaseUrl has not been configured.");
        }

        if (!baseUrl.EndsWith(
                "/",
                StringComparison.Ordinal))
        {
            baseUrl += "/";
        }

        if (!Uri.TryCreate(
                baseUrl,
                UriKind.Absolute,
                out Uri? baseUri))
        {
            throw new InvalidOperationException(
                $"The TV API base URL is invalid: '{baseUrl}'.");
        }

        if (baseUri.Scheme != Uri.UriSchemeHttp &&
            baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "The TV API base URL must use HTTP or HTTPS.");
        }

        _httpClient.BaseAddress = baseUri;
    }

    private static string ExpandTemplate(
        string template,
        params (string Name, string Value)[] values)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new InvalidOperationException(
                "A required TV API route template is missing.");
        }

        string result =
            NormalizeRelativePath(template);

        foreach ((string name, string value) in values)
        {
            result = result.Replace(
                $"{{{name}}}",
                Uri.EscapeDataString(value),
                StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static string NormalizeRelativePath(
        string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                "A required TV API path is missing.");
        }

        return path.Trim().TrimStart('/');
    }

    private async Task LogFailedResponseAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken ct)
    {
        string body;

        try
        {
            body =
                await response.Content
                    .ReadAsStringAsync(ct);
        }
        catch
        {
            body = string.Empty;
        }

        if (body.Length > 1_000)
        {
            body = body[..1_000];
        }

        _logger.LogWarning(
            "TV API failed while {Operation}. Status: {StatusCode}. Response: {ResponseBody}",
            operation,
            response.StatusCode,
            body);
    }

    private static bool IsTransientStatusCode(
        HttpStatusCode statusCode)
    {
        int numericStatusCode =
            (int)statusCode;

        return statusCode ==
                   HttpStatusCode.RequestTimeout ||
               statusCode ==
                   HttpStatusCode.TooManyRequests ||
               numericStatusCode >= 500;
    }

    private static string? NormalizeOptionalText(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string CreateInitials(
        string channelName)
    {
        if (string.IsNullOrWhiteSpace(channelName))
        {
            return "TV";
        }

        string[] words =
            channelName.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        if (words.Length >= 2)
        {
            return string.Concat(
                char.ToUpperInvariant(words[0][0]),
                char.ToUpperInvariant(words[1][0]));
        }

        string singleWord = words[0];

        if (singleWord.Length >= 2)
        {
            return singleWord[..2]
                .ToUpperInvariant();
        }

        return $"{char.ToUpperInvariant(singleWord[0])}V";
    }

    private static DateTime NormalizeUtc(
        DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc =>
                value,

            DateTimeKind.Local =>
                value.ToUniversalTime(),

            _ =>
                DateTime.SpecifyKind(
                    value,
                    DateTimeKind.Utc)
        };
    }
}