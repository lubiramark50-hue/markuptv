using System.Text.Json;
using MarkUptv.Models;
using MarkUptv.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarkUptv.Services;

/// <summary>
/// Retrieves football channels and current live matches.
/// </summary>
public sealed class FootballApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FootballApiService> _logger;
    private readonly FootballApiSettings _settings;

    public FootballApiService(
        HttpClient httpClient,
        ILogger<FootballApiService> logger,
        IOptions<FootballApiSettings> settings)
    {
        _httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));

        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        _settings = settings?.Value
            ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<CategoryResponse?> GetFootballChannelsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response =
                await _httpClient.GetAsync(
                    NormalizePath(_settings.ChannelsPath),
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                await LogFailureAsync(
                    response,
                    "loading football channels",
                    cancellationToken);

                return null;
            }

            string json =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

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
                        MarkUptvJsonContext.Default.TvChannelList)
                    ?? [];

                return new CategoryResponse
                {
                    Category = "football",
                    Groups = channels
                        .Select(channel => channel.Group)
                        .Where(group =>
                            !string.IsNullOrWhiteSpace(group))
                        .Select(group => group!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(group => group)
                        .ToList(),
                    Channels = channels
                };
            }

            if (document.RootElement.ValueKind ==
                JsonValueKind.Object)
            {
                return JsonSerializer.Deserialize(
                    json,
                    MarkUptvJsonContext.Default.CategoryResponse);
            }

            return null;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogWarning(
                exception,
                "The football channel request timed out.");

            return null;
        }
        catch (JsonException exception)
        {
            _logger.LogError(
                exception,
                "The football channel endpoint returned invalid JSON.");

            return null;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "The football backend could not be reached.");

            return null;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unexpected failure while loading football channels.");

            return null;
        }
    }

    public async Task<List<LiveMatch>> GetLiveMatchesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response =
                await _httpClient.GetAsync(
                    NormalizePath(_settings.LiveMatchesPath),
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                await LogFailureAsync(
                    response,
                    "loading live football matches",
                    cancellationToken);

                return [];
            }

            string json =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            return JsonSerializer.Deserialize(
                       json,
                       MarkUptvJsonContext.Default.LiveMatchList)
                   ?? [];
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogWarning(
                exception,
                "The live-match request timed out.");

            return [];
        }
        catch (JsonException exception)
        {
            _logger.LogError(
                exception,
                "The live-match endpoint returned invalid JSON.");

            return [];
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "The live-match endpoint could not be reached.");

            return [];
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unexpected failure while loading live football matches.");

            return [];
        }
    }

    public async Task<LiveMatch?> GetCurrentLiveMatchAsync(
        CancellationToken cancellationToken = default)
    {
        List<LiveMatch> matches =
            await GetLiveMatchesAsync(
                cancellationToken);

        return matches.FirstOrDefault();
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                "A football API route has not been configured.");
        }

        return path.Trim().TrimStart('/');
    }

    private async Task LogFailureAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        string responseBody;

        try
        {
            responseBody =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);
        }
        catch
        {
            responseBody = string.Empty;
        }

        if (responseBody.Length > 1_000)
        {
            responseBody =
                responseBody[..1_000];
        }

        _logger.LogWarning(
            "Football API failed while {Operation}. " +
            "Status: {StatusCode}. Response: {ResponseBody}",
            operation,
            response.StatusCode,
            responseBody);
    }
}