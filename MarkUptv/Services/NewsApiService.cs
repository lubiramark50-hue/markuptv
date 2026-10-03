using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MarkUptv.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarkUptv.Services;

public sealed class NewsApiSettings
{
    public string BaseUrl { get; set; } =
        "https://markuptvnew-fjhjd6bkb6bahqeh.southafricanorth-01.azurewebsites.net/api/news";

    /// <summary>
    /// Maximum duration allowed for each individual HTTP attempt.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 20;
}

/// <summary>
/// Production news API client with caching, retries, timeout handling,
/// wrapped-response support and detailed backend error reporting.
/// </summary>
public sealed class NewsApiService
{
    private const string DefaultBaseUrl =
        "https://markuptvnew-fjhjd6bkb6bahqeh.southafricanorth-01.azurewebsites.net/api/news";

    private const int MaximumRetryAttempts = 3;
    private const int InitialRetryDelayMilliseconds = 350;
    private const int MaximumPageSize = 100;
    private const int MaximumErrorBodyLength = 2_000;

    private readonly HttpClient _httpClient;
    private readonly NewsCacheService _cacheService;
    private readonly ILogger<NewsApiService> _logger;

    private readonly string _baseUrl;
    private readonly TimeSpan _requestTimeout;

    public NewsApiService(
        HttpClient httpClient,
        NewsCacheService cacheService,
        ILogger<NewsApiService> logger,
        IOptions<NewsApiSettings> settings)
    {
        _httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));

        _cacheService = cacheService
            ?? throw new ArgumentNullException(nameof(cacheService));

        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        ArgumentNullException.ThrowIfNull(settings);

        _baseUrl =
            NormalizeBaseUrl(
                settings.Value.BaseUrl);

        int timeoutSeconds =
            Math.Clamp(
                settings.Value.RequestTimeoutSeconds,
                5,
                120);

        _requestTimeout =
            TimeSpan.FromSeconds(timeoutSeconds);

        _httpClient.DefaultRequestHeaders.Accept.Clear();

        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        _logger.LogInformation(
            "NewsApiService initialized. Base URL: {BaseUrl}; Request timeout: {TimeoutSeconds}s",
            _baseUrl,
            timeoutSeconds);
    }

    // ============================================================
    // PAGINATED NEWS
    // ============================================================

    public async Task<List<NewsArticle>> GetNewsAsync(
        string category = "all",
        string mediaType = "all",
        string? searchQuery = null,
        int page = 1,
        int pageSize = 20,
        bool useCache = true,
        CancellationToken ct = default)
    {
        string normalizedCategory =
            NormalizeFilter(category, "all");

        string normalizedMediaType =
            NormalizeFilter(mediaType, "all");

        string normalizedSearch =
            NormalizeSearch(searchQuery);

        int normalizedPage =
            Math.Max(1, page);

        int normalizedPageSize =
            Math.Clamp(
                pageSize,
                1,
                MaximumPageSize);

        string requestUrl =
            BuildNewsRequestUrl(
                normalizedCategory,
                normalizedMediaType,
                normalizedSearch,
                normalizedPage,
                normalizedPageSize);

        string cacheKey =
            CreateNewsCacheKey(
                normalizedCategory,
                normalizedMediaType,
                normalizedSearch,
                normalizedPage,
                normalizedPageSize);

        _logger.LogDebug(
            "Loading news. Category: {Category}; Media: {MediaType}; Page: {Page}; Page size: {PageSize}; Cache: {UseCache}",
            normalizedCategory,
            normalizedMediaType,
            normalizedPage,
            normalizedPageSize,
            useCache);

        List<NewsArticle> cachedOrFresh =
            await _cacheService.GetArticlesAsync(
                cacheKey,
                async () =>
                {
                    return await FetchArticlesAsync(
                        requestUrl,
                        ct).ConfigureAwait(false);
                },
                useCache).ConfigureAwait(false)
            ?? [];

        List<NewsArticle> articles =
            SanitizeArticles(
                cachedOrFresh);

        /*
         * Recover from a stale or poisoned empty cache.
         *
         * A previous network failure may have caused an empty list
         * to be cached. When that happens, perform one fresh request.
         */
        if (useCache &&
            normalizedPage == 1 &&
            articles.Count == 0)
        {
            _logger.LogWarning(
                "Cached news result was empty. Performing a direct recovery request.");

            List<NewsArticle> freshArticles =
                await FetchArticlesAsync(
                    requestUrl,
                    ct).ConfigureAwait(false);

            freshArticles =
                SanitizeArticles(
                    freshArticles);

            if (freshArticles.Count > 0)
            {
                articles =
                    freshArticles;
            }
        }

        _logger.LogInformation(
            "News loading completed. Page: {Page}; Articles: {Count}",
            normalizedPage,
            articles.Count);

        return articles;
    }

    // ============================================================
    // BREAKING NEWS
    // ============================================================

    public async Task<List<NewsArticle>> GetBreakingNewsAsync(
        int limit = 5,
        bool useCache = true,
        CancellationToken ct = default)
    {
        int normalizedLimit =
            Math.Clamp(
                limit,
                1,
                50);

        string cacheKey =
            $"breaking_news_limit_{normalizedLimit}";

        TimeSpan cacheDuration =
            TimeSpan.FromMinutes(5);

        string requestUrl =
            $"{_baseUrl}/breaking" +
            $"?limit={normalizedLimit}";

        List<NewsArticle> results =
            await _cacheService.GetArticlesAsync(
                cacheKey,
                async () =>
                {
                    return await FetchArticlesAsync(
                        requestUrl,
                        ct).ConfigureAwait(false);
                },
                useCache,
                cacheDuration).ConfigureAwait(false)
            ?? [];

        results =
            SanitizeArticles(results);

        if (useCache &&
            results.Count == 0)
        {
            _logger.LogWarning(
                "Cached breaking-news response was empty. Performing a fresh request.");

            results =
                SanitizeArticles(
                    await FetchArticlesAsync(
                        requestUrl,
                        ct).ConfigureAwait(false));
        }

        return results;
    }

    // ============================================================
    // CATEGORIES
    // ============================================================

    public async Task<List<string>> GetAvailableCategoriesAsync(
        bool useCache = true,
        CancellationToken ct = default)
    {
        const string cacheKey =
            "news_dynamic_navigation_categories";

        string requestUrl =
            $"{_baseUrl}/categories";

        List<string> categories =
            await _cacheService.GetCategoriesAsync(
                cacheKey,
                async () =>
                {
                    List<string>? response =
                        await ExecuteResilientFetchAsync(
                            requestUrl,
                            NewsContextContainer.Default.ListString,
                            ct,
                            "categories",
                            "items",
                            "data",
                            "result")
                        .ConfigureAwait(false);

                    return SanitizeCategories(
                        response);
                },
                useCache).ConfigureAwait(false)
            ?? [];

        return SanitizeCategories(
            categories);
    }

    // ============================================================
    // ARTICLE FETCHING
    // ============================================================

    private async Task<List<NewsArticle>> FetchArticlesAsync(
        string requestUrl,
        CancellationToken cancellationToken)
    {
        List<NewsArticle>? response =
            await ExecuteResilientFetchAsync(
                    requestUrl,
                    NewsContextContainer.Default.ListNewsArticle,
                    cancellationToken,
                    "articles",
                    "news",
                    "items",
                    "data",
                    "result")
                .ConfigureAwait(false);

        return SanitizeArticles(
            response);
    }

    // ============================================================
    // RESILIENT HTTP PIPELINE
    // ============================================================

    private async Task<T?> ExecuteResilientFetchAsync<T>(
        string requestUrl,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken,
        params string[] wrapperNames)
    {
        Exception? lastFailure =
            null;

        for (int attempt = 1;
             attempt <= MaximumRetryAttempts;
             attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var attemptCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            attemptCancellation.CancelAfter(
                _requestTimeout);

            try
            {
                using var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        requestUrl);

                /*
                 * A retry should not be answered by an intermediary
                 * with the same stale failed response.
                 */
                if (attempt > 1)
                {
                    request.Headers.CacheControl =
                        new CacheControlHeaderValue
                        {
                            NoCache = true,
                            NoStore = true
                        };
                }

                _logger.LogDebug(
                    "Sending news request. Attempt {Attempt}/{MaximumAttempts}; URL: {Url}",
                    attempt,
                    MaximumRetryAttempts,
                    requestUrl);

                using HttpResponseMessage response =
                    await _httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        attemptCancellation.Token)
                    .ConfigureAwait(false);

                string responseBody =
                    await ReadResponseBodyAsync(
                        response,
                        attemptCancellation.Token)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    string backendMessage =
                        ExtractBackendMessage(
                            responseBody);

                    var failure =
                        new HttpRequestException(
                            $"News backend returned " +
                            $"{(int)response.StatusCode} " +
                            $"{response.ReasonPhrase}. " +
                            $"{backendMessage}",
                            inner: null,
                            response.StatusCode);

                    lastFailure =
                        failure;

                    if (IsTransientStatusCode(
                            response.StatusCode) &&
                        attempt < MaximumRetryAttempts)
                    {
                        TimeSpan retryDelay =
                            CalculateRetryDelay(
                                response,
                                attempt);

                        _logger.LogWarning(
                            "Transient news backend failure. Status: {StatusCode}; Attempt: {Attempt}; Retrying after {DelayMilliseconds}ms; Body: {Body}",
                            (int)response.StatusCode,
                            attempt,
                            retryDelay.TotalMilliseconds,
                            Truncate(responseBody));

                        await Task.Delay(
                            retryDelay,
                            cancellationToken)
                            .ConfigureAwait(false);

                        continue;
                    }

                    _logger.LogError(
                        "News backend request failed. Status: {StatusCode} {ReasonPhrase}; URL: {Url}; Body: {Body}",
                        (int)response.StatusCode,
                        response.ReasonPhrase,
                        requestUrl,
                        Truncate(responseBody));

                    throw failure;
                }

                if (ResponseExplicitlyReportsFailure(
                        responseBody))
                {
                    string backendMessage =
                        ExtractBackendMessage(
                            responseBody);

                    throw new InvalidDataException(
                        "The news backend returned HTTP success " +
                        $"but reported a failure. {backendMessage}");
                }

                if (string.IsNullOrWhiteSpace(
                        responseBody))
                {
                    _logger.LogWarning(
                        "News backend returned an empty successful response. URL: {Url}",
                        requestUrl);

                    return default;
                }

                try
                {
                    T? result =
                        DeserializePossiblyWrapped(
                            responseBody,
                            jsonTypeInfo,
                            wrapperNames);

                    return result;
                }
                catch (JsonException exception)
                {
                    _logger.LogError(
                        exception,
                        "News backend returned invalid or unexpected JSON. URL: {Url}; Body: {Body}",
                        requestUrl,
                        Truncate(responseBody));

                    throw new InvalidDataException(
                        "The news backend returned an unsupported JSON response.",
                        exception);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug(
                    "News request was cancelled by the caller.");

                throw;
            }
            catch (OperationCanceledException exception)
            {
                var timeoutException =
                    new TimeoutException(
                        $"News request exceeded the {_requestTimeout.TotalSeconds:N0}-second timeout.",
                        exception);

                lastFailure =
                    timeoutException;

                if (attempt < MaximumRetryAttempts)
                {
                    TimeSpan retryDelay =
                        CalculateRetryDelay(
                            response: null,
                            attempt);

                    _logger.LogWarning(
                        timeoutException,
                        "News request timed out. Attempt: {Attempt}; Retrying after {DelayMilliseconds}ms.",
                        attempt,
                        retryDelay.TotalMilliseconds);

                    await Task.Delay(
                        retryDelay,
                        cancellationToken)
                        .ConfigureAwait(false);

                    continue;
                }

                _logger.LogError(
                    timeoutException,
                    "News request timed out after {Attempts} attempts. URL: {Url}",
                    attempt,
                    requestUrl);

                throw timeoutException;
            }
            catch (HttpRequestException exception)
                when (IsTransientException(exception) &&
                      attempt < MaximumRetryAttempts)
            {
                lastFailure =
                    exception;

                TimeSpan retryDelay =
                    CalculateRetryDelay(
                        response: null,
                        attempt);

                _logger.LogWarning(
                    exception,
                    "Temporary news network failure. Attempt: {Attempt}; Retrying after {DelayMilliseconds}ms.",
                    attempt,
                    retryDelay.TotalMilliseconds);

                await Task.Delay(
                    retryDelay,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (IOException exception)
                when (attempt < MaximumRetryAttempts)
            {
                lastFailure =
                    exception;

                TimeSpan retryDelay =
                    CalculateRetryDelay(
                        response: null,
                        attempt);

                _logger.LogWarning(
                    exception,
                    "Temporary news response-stream failure. Attempt: {Attempt}; Retrying after {DelayMilliseconds}ms.",
                    attempt,
                    retryDelay.TotalMilliseconds);

                await Task.Delay(
                    retryDelay,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "News request failed permanently. URL: {Url}; Attempt: {Attempt}",
                    requestUrl,
                    attempt);

                throw;
            }
        }

        throw new InvalidOperationException(
            "The news request failed after all retry attempts.",
            lastFailure);
    }

    // ============================================================
    // RESPONSE DESERIALIZATION
    // ============================================================

    private static T? DeserializePossiblyWrapped<T>(
        string json,
        JsonTypeInfo<T> jsonTypeInfo,
        params string[] wrapperNames)
    {
        using JsonDocument document =
            JsonDocument.Parse(json);

        JsonElement payload =
            FindPayloadElement(
                document.RootElement,
                wrapperNames,
                remainingDepth: 5);

        return JsonSerializer.Deserialize(
            payload.GetRawText(),
            jsonTypeInfo);
    }

    private static JsonElement FindPayloadElement(
        JsonElement element,
        string[] wrapperNames,
        int remainingDepth)
    {
        if (remainingDepth <= 0 ||
            element.ValueKind != JsonValueKind.Object)
        {
            return element;
        }

        foreach (string wrapperName in wrapperNames)
        {
            if (TryGetPropertyIgnoreCase(
                    element,
                    wrapperName,
                    out JsonElement wrapped))
            {
                return FindPayloadElement(
                    wrapped,
                    wrapperNames,
                    remainingDepth - 1);
            }
        }

        /*
         * Standard response wrappers supported automatically:
         *
         * { "success": true, "data": [...] }
         * { "data": { "items": [...] } }
         * { "result": { "articles": [...] } }
         */
        string[] commonWrappers =
        [
            "data",
            "result",
            "items",
            "articles",
            "news",
            "categories"
        ];

        foreach (string wrapperName in commonWrappers)
        {
            if (TryGetPropertyIgnoreCase(
                    element,
                    wrapperName,
                    out JsonElement wrapped))
            {
                return FindPayloadElement(
                    wrapped,
                    wrapperNames,
                    remainingDepth - 1);
            }
        }

        return element;
    }

    private static bool ResponseExplicitlyReportsFailure(
        string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        try
        {
            using JsonDocument document =
                JsonDocument.Parse(responseBody);

            JsonElement root =
                document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetPropertyIgnoreCase(
                    root,
                    "success",
                    out JsonElement successElement))
            {
                return false;
            }

            if (successElement.ValueKind ==
                JsonValueKind.False)
            {
                return true;
            }

            if (successElement.ValueKind ==
                    JsonValueKind.String &&
                bool.TryParse(
                    successElement.GetString(),
                    out bool success))
            {
                return !success;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // ============================================================
    // ARTICLE SANITIZATION
    // ============================================================

    private static List<NewsArticle> SanitizeArticles(
        IEnumerable<NewsArticle>? articles)
    {
        if (articles is null)
        {
            return [];
        }

        return articles
            .Where(article =>
                article is not null)
            .Where(article =>
                !string.IsNullOrWhiteSpace(article.Title) ||
                !string.IsNullOrWhiteSpace(article.Description) ||
                !string.IsNullOrWhiteSpace(article.Url))
            .DistinctBy(
                CreateArticleIdentity,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string CreateArticleIdentity(
        NewsArticle article)
    {
        if (!string.IsNullOrWhiteSpace(article.Url))
        {
            return $"url:{article.Url.Trim()}";
        }

        return
            $"title:{article.Title?.Trim()}|" +
            $"source:{article.Source?.Trim()}";
    }

    private static List<string> SanitizeCategories(
        IEnumerable<string>? categories)
    {
        if (categories is null)
        {
            return [];
        }

        return categories
            .Where(category =>
                !string.IsNullOrWhiteSpace(category))
            .Select(category =>
                category.Trim())
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(category =>
                category,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ============================================================
    // URL AND CACHE BUILDERS
    // ============================================================

    private string BuildNewsRequestUrl(
        string category,
        string mediaType,
        string searchQuery,
        int page,
        int pageSize)
    {
        var builder =
            new StringBuilder(
                _baseUrl.Length + 160);

        builder.Append(_baseUrl);

        builder.Append(
            "?category=");

        builder.Append(
            Uri.EscapeDataString(category));

        builder.Append(
            "&mediaType=");

        builder.Append(
            Uri.EscapeDataString(mediaType));

        builder.Append(
            "&page=");

        builder.Append(page);

        builder.Append(
            "&pageSize=");

        builder.Append(pageSize);

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            builder.Append(
                "&searchQuery=");

            builder.Append(
                Uri.EscapeDataString(searchQuery));
        }

        return builder.ToString();
    }

    private static string CreateNewsCacheKey(
        string category,
        string mediaType,
        string searchQuery,
        int page,
        int pageSize)
    {
        string searchToken =
            CreateStableToken(
                searchQuery);

        return
            $"news_" +
            $"cat_{category}_" +
            $"media_{mediaType}_" +
            $"page_{page}_" +
            $"size_{pageSize}_" +
            $"query_{searchToken}";
    }

    private static string CreateStableToken(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        byte[] bytes =
            Encoding.UTF8.GetBytes(value);

        byte[] hash =
            SHA256.HashData(bytes);

        /*
         * The first eight hash bytes are sufficient for a compact
         * deterministic cache-key token.
         */
        return Convert
            .ToHexString(
                hash.AsSpan(0, 8))
            .ToLowerInvariant();
    }

    private static string NormalizeBaseUrl(
        string? configuredBaseUrl)
    {
        string value =
            string.IsNullOrWhiteSpace(configuredBaseUrl)
                ? DefaultBaseUrl
                : configuredBaseUrl.Trim();

        value =
            value.TrimEnd('/');

        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out Uri? uri))
        {
            throw new InvalidOperationException(
                "NewsApiSettings.BaseUrl must be a valid absolute URL.");
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

        if (!isHttp &&
            !isHttps)
        {
            throw new InvalidOperationException(
                "NewsApiSettings.BaseUrl must use HTTP or HTTPS.");
        }

        return uri.AbsoluteUri.TrimEnd('/');
    }

    private static string NormalizeFilter(
        string? value,
        string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value
            .Trim()
            .ToLowerInvariant();
    }

    private static string NormalizeSearch(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    // ============================================================
    // RETRY HELPERS
    // ============================================================

    private static bool IsTransientStatusCode(
        HttpStatusCode statusCode)
    {
        int code =
            (int)statusCode;

        return
            code == 408 ||
            code == 425 ||
            code == 429 ||
            code == 500 ||
            code == 502 ||
            code == 503 ||
            code == 504;
    }

    private static bool IsTransientException(
        HttpRequestException exception)
    {
        if (!exception.StatusCode.HasValue)
        {
            return true;
        }

        return IsTransientStatusCode(
            exception.StatusCode.Value);
    }

    private static TimeSpan CalculateRetryDelay(
        HttpResponseMessage? response,
        int attempt)
    {
        if (response?.Headers.RetryAfter?.Delta is
            TimeSpan retryAfterDelta)
        {
            return ClampRetryDelay(
                retryAfterDelta);
        }

        if (response?.Headers.RetryAfter?.Date is
            DateTimeOffset retryAfterDate)
        {
            TimeSpan serverDelay =
                retryAfterDate -
                DateTimeOffset.UtcNow;

            if (serverDelay > TimeSpan.Zero)
            {
                return ClampRetryDelay(
                    serverDelay);
            }
        }

        double exponentialDelay =
            InitialRetryDelayMilliseconds *
            Math.Pow(
                2,
                Math.Max(0, attempt - 1));

        int jitter =
            Random.Shared.Next(
                0,
                180);

        return ClampRetryDelay(
            TimeSpan.FromMilliseconds(
                exponentialDelay + jitter));
    }

    private static TimeSpan ClampRetryDelay(
        TimeSpan delay)
    {
        if (delay < TimeSpan.FromMilliseconds(100))
        {
            return TimeSpan.FromMilliseconds(100);
        }

        if (delay > TimeSpan.FromSeconds(10))
        {
            return TimeSpan.FromSeconds(10);
        }

        return delay;
    }

    // ============================================================
    // JSON AND ERROR HELPERS
    // ============================================================

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind ==
            JsonValueKind.Object)
        {
            foreach (JsonProperty property
                     in element.EnumerateObject())
            {
                if (string.Equals(
                        property.Name,
                        propertyName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    value =
                        property.Value;

                    return true;
                }
            }
        }

        value =
            default;

        return false;
    }

    private static string ExtractBackendMessage(
        string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "The backend returned no error details.";
        }

        try
        {
            using JsonDocument document =
                JsonDocument.Parse(responseBody);

            JsonElement root =
                document.RootElement;

            string[] messageProperties =
            [
                "message",
                "error",
                "detail",
                "title",
                "errorMessage"
            ];

            foreach (string propertyName
                     in messageProperties)
            {
                if (!TryGetPropertyIgnoreCase(
                        root,
                        propertyName,
                        out JsonElement property))
                {
                    continue;
                }

                if (property.ValueKind ==
                    JsonValueKind.String)
                {
                    return property.GetString() ??
                           "The request failed.";
                }

                return property.GetRawText();
            }
        }
        catch (JsonException)
        {
            // Fall through and return the plain response.
        }

        return Truncate(responseBody);
    }

    private static async Task<string> ReadResponseBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            return string.Empty;
        }

        return await response.Content
            .ReadAsStringAsync(
                cancellationToken)
            .ConfigureAwait(false);
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

        if (normalized.Length <=
            MaximumErrorBodyLength)
        {
            return normalized;
        }

        return
            normalized[..MaximumErrorBodyLength] +
            "…";
    }
}