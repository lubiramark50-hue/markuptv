using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MarkUptv.Models;
using MarkUptv.Serialization;
using Microsoft.Extensions.Logging;

namespace MarkUptv.Services;

/// <summary>
/// Client for the MarkUpTV movie catalogue (<c>/api/movies</c>).
///
/// The catalogue aggregates free public archives that publish films with
/// direct, token-free URLs, so the same response serves streaming and the
/// offline download manager. Every call degrades to an empty result instead
/// of throwing: the UI shows its empty state rather than an error wall.
/// </summary>
public sealed class MovieCatalogService
{
    private readonly HttpClient _http;
    private readonly ILogger<MovieCatalogService> _logger;

    /// <summary>
    /// Reason the last catalogue call came back empty. The pages surface it so
    /// a slow upstream shows as "catalogue is busy" instead of a silent blank
    /// screen — and so failures are diagnosable without a debugger.
    /// </summary>
    public string LastError { get; private set; } = string.Empty;

    public MovieCatalogService(HttpClient http, ILogger<MovieCatalogService> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Searches films by title, cast, creator or plot.</summary>
    public Task<MovieSearchResult> SearchAsync(
        string? query,
        string? language = null,
        string? sort = null,
        int page = 1,
        int pageSize = 30,
        CancellationToken ct = default)
    {
        string path = $"api/movies/search?page={page}&pageSize={pageSize}" +
                      $"&q={Uri.EscapeDataString(query?.Trim() ?? string.Empty)}";

        if (!string.IsNullOrWhiteSpace(language))
        {
            path += $"&language={Uri.EscapeDataString(language.Trim())}";
        }

        if (!string.IsNullOrWhiteSpace(sort))
        {
            path += $"&sort={Uri.EscapeDataString(sort.Trim())}";
        }

        return GetPageAsync(path, "movie search", ct);
    }

    /// <summary>Newest additions to the free catalogue.</summary>
    public Task<MovieSearchResult> GetLatestAsync(
        string? language = null, int page = 1, int pageSize = 30, CancellationToken ct = default)
        => GetPageAsync(
            BuildShelfPath("api/movies/latest", language, page, pageSize),
            "latest movies",
            ct);

    /// <summary>Most-watched films in the free catalogue.</summary>
    public Task<MovieSearchResult> GetPopularAsync(
        string? language = null, int page = 1, int pageSize = 30, CancellationToken ct = default)
        => GetPageAsync(
            BuildShelfPath("api/movies/popular", language, page, pageSize),
            "popular movies",
            ct);

    /// <summary>
    /// Translated (non-English) features. Without a language filter the server
    /// spans every translated language, so this shelf is never empty.
    /// </summary>
    public Task<MovieSearchResult> GetTranslatedAsync(
        string? language = null, int page = 1, int pageSize = 30, CancellationToken ct = default)
        => GetPageAsync(
            BuildShelfPath("api/movies/translated", language, page, pageSize),
            "translated movies",
            ct);

    private static string BuildShelfPath(string route, string? language, int page, int pageSize)
    {
        string path = $"{route}?page={page}&pageSize={pageSize}";

        if (!string.IsNullOrWhiteSpace(language))
        {
            path += $"&language={Uri.EscapeDataString(language.Trim())}";
        }

        return path;
    }

    /// <summary>Language shelves, including the translated (non-English) list.</summary>
    public async Task<List<MovieLanguage>> GetLanguagesAsync(CancellationToken ct = default)
    {
        try
        {
            using HttpResponseMessage response =
                await _http.GetAsync("api/movies/languages", ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new List<MovieLanguage>();
            }

            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            return JsonSerializer.Deserialize(json, MarkUptvJsonContext.Default.MovieLanguageList)
                   ?? new List<MovieLanguage>();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Movie languages could not be loaded.");
            return new List<MovieLanguage>();
        }
    }

    /// <summary>Resolves one title to its playable file and subtitle tracks.</summary>
    public async Task<MovieDetail?> GetMovieAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        try
        {
            using HttpResponseMessage response = await _http
                .GetAsync($"api/movies/{Uri.EscapeDataString(id.Trim())}", ct)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Movie {Id} is not available.", id);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Movie {Id} lookup failed with {Status}.", id, (int)response.StatusCode);
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            return JsonSerializer.Deserialize(json, MarkUptvJsonContext.Default.MovieDetail);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Movie {Id} could not be loaded.", id);
            return null;
        }
    }

    private async Task<MovieSearchResult> GetPageAsync(string path, string operation, CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage response =
                await _http.GetAsync(path, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LastError = $"{operation}: server replied {(int)response.StatusCode}";
                _logger.LogWarning(
                    "Movie {Operation} failed with {Status}.", operation, (int)response.StatusCode);
                return new MovieSearchResult();
            }

            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            MovieSearchResult? parsed = JsonSerializer.Deserialize(
                json,
                MarkUptvJsonContext.Default.MovieSearchResult);

            if (parsed is null)
            {
                LastError = $"{operation}: the catalogue response could not be read";
                return new MovieSearchResult();
            }

            LastError = string.Empty;
            return parsed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LastError = $"{operation}: {exception.GetType().Name}: {exception.Message}";
            _logger.LogWarning(exception, "Movie {Operation} could not be completed.", operation);
            return new MovieSearchResult();
        }
    }
}
