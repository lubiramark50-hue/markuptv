using MarkUptv.Models;
using Microsoft.Extensions.Logging;

namespace MarkUptv.Services;

/// <summary>
/// Loads and indexes the embedded catalogue of free, official live sources
/// (Resources/Raw/live-sources.json). The catalogue is the app's offline
/// fallback when the MarkUpTV backend cannot resolve a broadcast.
/// </summary>
public sealed class LiveCatalogService
{
    private readonly ILogger<LiveCatalogService> _logger;
    private readonly ILiveCatalogProvider _provider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LiveCatalog? _catalog;

    public LiveCatalogService(
        ILogger<LiveCatalogService> logger,
        ILiveCatalogProvider provider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>Returns the catalogue, loading it once from the app package.</summary>
    public async Task<LiveCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (_catalog is not null)
        {
            return _catalog;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_catalog is not null)
            {
                return _catalog;
            }

            LiveCatalog catalog = await _provider
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);

            _catalog = catalog;

            _logger.LogInformation(
                "Live catalogue loaded: {SourceCount} sources, {LeagueCount} leagues.",
                _catalog.Sources.Count,
                _catalog.Leagues.Count);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Live catalogue could not be loaded; running with an empty catalogue.");

            _catalog = new LiveCatalog();
        }
        finally
        {
            _gate.Release();
        }

        return _catalog;
    }

    /// <summary>All catalogue entries that can be browsed as free world channels.</summary>
    public async Task<IReadOnlyList<LiveSource>> GetWorldChannelsAsync(
        CancellationToken cancellationToken = default)
    {
        LiveCatalog catalog = await GetCatalogAsync(cancellationToken).ConfigureAwait(false);

        return catalog.Sources
            .Where(source => !source.IsDiagnostic)
            .Where(source => !string.IsNullOrWhiteSpace(source.OfficialPage) || source.IsDirectlyPlayable)
            .OrderBy(source => source.Country ?? "zzz")
            .ThenBy(source => source.Priority)
            .ToList();
    }

    /// <summary>Public sample streams used by the built-in playback self-test.</summary>
    public async Task<IReadOnlyList<LiveSource>> GetDiagnosticSourcesAsync(
        CancellationToken cancellationToken = default)
    {
        LiveCatalog catalog = await GetCatalogAsync(cancellationToken).ConfigureAwait(false);

        return catalog.Sources
            .Where(source => source.IsDiagnostic && source.IsDirectlyPlayable)
            .OrderBy(source => source.Priority)
            .ToList();
    }

    /// <summary>Catalogue candidates for one fixture, best first.</summary>
    public async Task<IReadOnlyList<LiveSource>> GetForFixtureAsync(
        FixtureStreamRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        LiveCatalog catalog = await GetCatalogAsync(cancellationToken).ConfigureAwait(false);

        List<LiveSource> results = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        void Consider(LiveSource source)
        {
            if (!source.IsDiagnostic || request.AllowDiagnostic)
            {
                if (seen.Add(source.Id))
                {
                    results.Add(source);
                }
            }
        }

        // 1. League-specific coverage first.
        if (!string.IsNullOrWhiteSpace(request.League))
        {
            string needle = request.League.Trim();

            foreach (LeagueCoverage league in catalog.Leagues)
            {
                if (!IsLeagueMatch(needle, league.Name))
                {
                    continue;
                }

                foreach (string id in league.SourceIds)
                {
                    LiveSource? source = catalog.Sources
                        .FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));

                    if (source is not null)
                    {
                        Consider(source);
                    }
                }
            }

            foreach (LiveSource source in catalog.Sources
                         .Where(entry => !string.IsNullOrWhiteSpace(entry.League))
                         .Where(entry => entry.League!.Contains(needle, StringComparison.OrdinalIgnoreCase)
                                         || needle.Contains(entry.League!, StringComparison.OrdinalIgnoreCase)))
            {
                Consider(source);
            }
        }

        // 2. Global free match-time platforms.
        foreach (LiveSource source in catalog.Sources
                     .Where(entry => entry.MatchTimeOnly)
                     .OrderBy(entry => entry.Priority))
        {
            if (string.Equals(source.Country, "Global", StringComparison.OrdinalIgnoreCase))
            {
                Consider(source);
            }
        }

        // 3. Always let the viewer find a schedule companion.
        foreach (LiveSource source in catalog.Sources.Where(entry => entry.Priority >= 90))
        {
            Consider(source);
        }

        return results
            .OrderBy(source => source.Priority)
            .ToList();
    }

    /// <summary>Coverage note for a competition name, when one is catalogued.</summary>
    public async Task<LeagueCoverage?> GetLeagueCoverageAsync(
        string? leagueName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(leagueName))
        {
            return null;
        }

        LiveCatalog catalog = await GetCatalogAsync(cancellationToken).ConfigureAwait(false);

        return catalog.Leagues
            .FirstOrDefault(league => IsLeagueMatch(leagueName, league.Name));
    }

    private static bool IsLeagueMatch(string fixtureLeague, string catalogLeague)
    {
        if (string.Equals(fixtureLeague, catalogLeague, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Bidirectional containment: every significant word of the fixture must
        // appear in the catalog entry and vice versa. This keeps "FKF Premier
        // League" from matching the English Premier League note ("English" is
        // missing) while still matching "CAF Champions League / AFCON".
        string[] fixtureTokens = SignificantTokens(fixtureLeague);
        string[] catalogTokens = SignificantTokens(catalogLeague);

        if (fixtureTokens.Length == 0 || catalogTokens.Length == 0)
        {
            return false;
        }

        return fixtureTokens.All(token =>
                   catalogLeague.Contains(token, StringComparison.OrdinalIgnoreCase)) &&
               catalogTokens.All(token =>
                   fixtureLeague.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] SignificantTokens(string value)
        => value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Trim('/', '-', ','))
            .Where(token => token.Length > 3)
            .ToArray();
}
