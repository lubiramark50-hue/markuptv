using System.Globalization;
using System.Text.Json;
using MarkUptv.Models;
using Microsoft.Extensions.Logging;

namespace MarkUptv.Services;

/// <summary>
/// Reads today's real football fixtures from TheSportsDB's public endpoint.
///
/// This is the board's fallback when the MarkUpTV backend is unreachable: it
/// carries schedule data only (no streams), so the app always has a fixture
/// list and every fixture still goes through the same legal source resolver.
///
/// The public endpoint is the community/test tier of TheSportsDB and is used
/// here for schedule data; the operator can swap this provider for their own
/// fixtures feed at any time.
/// </summary>
public sealed class PublicFixturesProvider
{
    private const string EndpointTemplate =
        "https://www.thesportsdb.com/api/v1/json/3/eventsday.php?d={0}&s=Soccer";

    private readonly ILogger<PublicFixturesProvider> _logger;
    private readonly HttpClient _httpClient;

    public PublicFixturesProvider(ILogger<PublicFixturesProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MarkUpTV/1.1 (+fixtures)");
    }

    private const string LeagueEndpointTemplate =
        "https://www.thesportsdb.com/api/v1/json/3/eventsnextleague.php?id={0}";

    /// <summary>
    /// Upcoming fixtures for one competition id. Used to build the league
    /// coverage table (which leagues can be served today).
    /// </summary>
    public async Task<IReadOnlyList<LiveMatch>> GetLeagueFixturesAsync(
        string leagueId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(leagueId))
        {
            return Array.Empty<LiveMatch>();
        }

        string url = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            LeagueEndpointTemplate,
            leagueId.Trim());

        try
        {
            using HttpResponseMessage response = await _httpClient
                .GetAsync(url, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<LiveMatch>();
            }

            string json = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("events", out JsonElement events) ||
                events.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<LiveMatch>();
            }

            List<LiveMatch> fixtures = new();

            foreach (JsonElement element in events.EnumerateArray())
            {
                LiveMatch? match = Map(element);

                if (match is not null)
                {
                    fixtures.Add(match);
                }
            }

            return fixtures
                .OrderBy(match => match.KickoffUtc)
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "League fixtures failed for {LeagueId}", leagueId);
            return Array.Empty<LiveMatch>();
        }
    }

    /// <summary>Fixtures for a date (defaults to today), ordered by kick-off.</summary>
    public async Task<IReadOnlyList<LiveMatch>> GetFixturesAsync(
        DateTime? date = null,
        CancellationToken cancellationToken = default)
    {
        DateTime day = (date ?? DateTime.UtcNow).Date;
        string url = string.Format(
            CultureInfo.InvariantCulture,
            EndpointTemplate,
            day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        List<LiveMatch> fixtures = new();

        try
        {
            using HttpResponseMessage response = await _httpClient
                .GetAsync(url, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Public fixtures endpoint returned HTTP {Status}",
                    (int)response.StatusCode);

                return fixtures;
            }

            string json = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("events", out JsonElement events) ||
                events.ValueKind != JsonValueKind.Array)
            {
                _logger.LogInformation("Public fixtures endpoint returned no events for {Day:yyyy-MM-dd}", day);
                return fixtures;
            }

            foreach (JsonElement element in events.EnumerateArray())
            {
                LiveMatch? match = Map(element);

                if (match is not null)
                {
                    fixtures.Add(match);
                }
            }

            fixtures = fixtures
                .OrderBy(match => match.KickoffUtc)
                .ToList();

            _logger.LogInformation(
                "Public fixtures loaded: {Count} matches on {Day:yyyy-MM-dd}",
                fixtures.Count,
                day);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Public fixtures could not be loaded; the board will stay on the backend data.");
        }

        return fixtures;
    }

    private static LiveMatch? Map(JsonElement element)
    {
        string home = GetString(element, "strHomeTeam");
        string away = GetString(element, "strAwayTeam");

        if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away))
        {
            return null;
        }

        string league = GetString(element, "strLeague");
        string status = GetString(element, "strStatus");

        int? homeScore = GetInt(element, "intHomeScore");
        int? awayScore = GetInt(element, "intAwayScore");

        return new LiveMatch
        {
            Id = StableId(GetString(element, "idEvent")),
            HomeTeam = home.Trim(),
            AwayTeam = away.Trim(),
            League = string.IsNullOrWhiteSpace(league) ? null : league.Trim(),
            KickoffUtc = ParseKickoff(element),
            Status = string.IsNullOrWhiteSpace(status)
                ? "SCHEDULED"
                : status.Trim().ToUpperInvariant(),
            Score = homeScore.HasValue && awayScore.HasValue
                ? $"{homeScore}-{awayScore}"
                : null,
            Minute = GetString(element, "strProgress") is { Length: > 0 } progress
                ? progress
                : null
        };
    }

    private static DateTime ParseKickoff(JsonElement element)
    {
        string timestamp = GetString(element, "strTimestamp");

        if (!string.IsNullOrWhiteSpace(timestamp) &&
            DateTime.TryParse(
                timestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out DateTime parsed))
        {
            return parsed;
        }

        string date = GetString(element, "dateEvent");
        string time = GetString(element, "strTime");

        if (DateTime.TryParse(
                $"{date}T{(string.IsNullOrWhiteSpace(time) ? "00:00:00" : time)}",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out DateTime combined))
        {
            return combined;
        }

        return default;
    }

    private static string GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out JsonElement value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int? GetInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out int number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out int parsed) => parsed,
            _ => null
        };
    }

    /// <summary>Stable numeric id derived from the provider's event id.</summary>
    private static int StableId(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return 0;
        }

        unchecked
        {
            int hash = 17;

            foreach (char character in eventId)
            {
                hash = (hash * 31) + character;
            }

            return hash & 0x7FFFFFFF;
        }
    }
}
