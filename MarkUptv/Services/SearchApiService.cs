using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MarkUptv.Models;

namespace MarkUptv.Services;

public class SearchApiSettings
{
    public string BaseUrl { get; set; } = string.Empty;
}

public class SearchApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SearchApiService> _logger;
    private readonly string _baseUrl;

    private const int MaxRetryAttempts = 3;
    private const int InitialRetryDelayMs = 200;

    public SearchApiService(
        HttpClient httpClient,
        ILogger<SearchApiService> logger,
        IOptions<SearchApiSettings> settings)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var configuredUrl = settings?.Value?.BaseUrl;
        if (string.IsNullOrWhiteSpace(configuredUrl))
        {
            _baseUrl = "https://markuptvnew-fjhjd6bkb6bahqeh.southafricanorth-01.azurewebsites.net/";
        }
        else
        {
            _baseUrl = configuredUrl.TrimEnd('/') + '/';
        }

        _logger.LogInformation("SearchApiService initialized with BaseUrl: {BaseUrl}", _baseUrl);
    }

    /// <summary>
    /// Executes a low-allocation, high-resiliency cloud search stream query against the verified backend parameter format.
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<SearchResult>();

        string escapedQuery = Uri.EscapeDataString(query.Trim());

        // PERFECT FIX: Changed parameter layout from ?q= to ?query= to match your live API schema
        string requestUrl = $"{_baseUrl}api/search?query={escapedQuery}";

        int attempts = 0;
        int delayMs = InitialRetryDelayMs;

        while (true)
        {
            attempts++;
            try
            {
                ct.ThrowIfCancellationRequested();

                _logger.LogDebug("Executing remote API layer (Attempt {Attempt}/{Max}): {Url}", attempts, MaxRetryAttempts, requestUrl);

                using var response = await _httpClient.GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                // Throws explicit failures if the network drops or hits error states
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

                var results = await JsonSerializer.DeserializeAsync(
                    stream,
                    SearchJsonContext.Default.ListSearchResult,
                    ct).ConfigureAwait(false);

                return results ?? new List<SearchResult>();
            }
            catch (Exception ex) when (IsTransientException(ex) && attempts < MaxRetryAttempts)
            {
                int jitter = Random.Shared.Next(-50, 50);
                int totalDelay = Math.Max(10, delayMs + jitter);

                _logger.LogWarning(ex, "Transient infrastructure fault hit on attempt {Attempt}. Retrying in {Delay}ms...", attempts, totalDelay);

                await Task.Delay(totalDelay, ct).ConfigureAwait(false);
                delayMs *= 2;
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Query execution cleanly aborted via structural thread cancellation context token.");
                throw;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Fatal network channel communication failure on search query context target boundary.");
                throw;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Payload schema syntax verification failure during streaming compilation reader path.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Unhandled thread kernel failure inside data services component wrapper block.");
                throw;
            }
        }
    }

    private static bool IsTransientException(Exception ex)
    {
        if (ex is HttpRequestException httpEx)
        {
            if (!httpEx.StatusCode.HasValue) return true;
            int code = (int)httpEx.StatusCode.Value;
            return code == 408 || code == 429 || (code >= 500 && code <= 504);
        }
        return ex is TimeoutException || ex is IOException;
    }
}