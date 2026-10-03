using MarkUptv.Models;
using Microsoft.Extensions.Logging;

namespace MarkUptv.Services;

/// <summary>
/// Turns a fixture into an ordered, actually-working playback plan.
///
/// Order of trust:
///   1. the stream the MarkUpTV backend already resolved for this match,
///   2. catalogue sources whose competition matches the fixture,
///   3. global free match-time platforms,
///   4. a schedule companion so the viewer always has a next step.
///
/// Direct HLS candidates are probed before playback so that dead links are
/// skipped automatically; the player keeps the full ordered list and can fail
/// over without asking the backend again mid-match.
/// </summary>
public sealed class LiveResolutionService
{
    private const int MaxParallelProbes = 3;

    private readonly LiveCatalogService _catalog;
    private readonly StreamProbeService _probe;
    private readonly YouTubeLiveResolver _youtubeResolver;
    private readonly ILogger<LiveResolutionService> _logger;

    public LiveResolutionService(
        LiveCatalogService catalog,
        StreamProbeService probe,
        YouTubeLiveResolver youtubeResolver,
        ILogger<LiveResolutionService> logger)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _youtubeResolver = youtubeResolver ?? throw new ArgumentNullException(nameof(youtubeResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Builds the playback plan for one fixture.</summary>
    public async Task<LiveResolution> ResolveAsync(
        FixtureStreamRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<LiveSource> sources = new();

        // 1. Backend-resolved stream always ranks first.
        if (!string.IsNullOrWhiteSpace(request.BackendStreamUrl))
        {
            sources.Add(new LiveSource
            {
                Id = "backend-primary",
                Label = string.IsNullOrWhiteSpace(request.BackendStreamLabel)
                    ? "MarkUpTV live"
                    : request.BackendStreamLabel!,
                Channel = "MarkUpTV",
                Kind = string.Equals(request.BackendStreamKind, "youtube", StringComparison.OrdinalIgnoreCase)
                    ? "youtube"
                    : "hls",
                Url = request.BackendStreamUrl,
                OfficialPage = request.BackendStreamEmbedUrl,
                Country = "Global",
                Language = "Live",
                League = request.League,
                Region = "Resolved by the MarkUpTV service",
                Legality = "Stream supplied by the MarkUpTV backend for this fixture.",
                Priority = 0
            });
        }

        // 2-4. Catalogue fallbacks.
        IReadOnlyList<LiveSource> catalogue = await _catalog
            .GetForFixtureAsync(request, cancellationToken)
            .ConfigureAwait(false);

        sources.AddRange(catalogue);

        return await BuildResolutionAsync(request, sources, isSelfTest: false, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Playback self-test: proves the player pipeline end to end.</summary>
    public async Task<LiveResolution> BuildSelfTestAsync(CancellationToken cancellationToken = default)
    {
        FixtureStreamRequest request = new()
        {
            HomeTeam = "Playback",
            AwayTeam = "Self-test",
            League = "Diagnostics",
            AllowDiagnostic = true
        };

        IReadOnlyList<LiveSource> diagnostics = await _catalog
            .GetDiagnosticSourcesAsync(cancellationToken)
            .ConfigureAwait(false);

        return await BuildResolutionAsync(request, diagnostics.ToList(), isSelfTest: true, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Playback plan for one browsed channel (used by the channel dialogs).</summary>
    public async Task<LiveResolution> ResolveChannelAsync(
        string label,
        string? streamUrl,
        string kind = "hls",
        CancellationToken cancellationToken = default)
    {
        FixtureStreamRequest request = new()
        {
            HomeTeam = label,
            AwayTeam = string.Empty,
            League = "Channel",
            BackendStreamUrl = streamUrl,
            BackendStreamKind = kind,
            BackendStreamLabel = label
        };

        List<LiveSource> sources = new();

        if (!string.IsNullOrWhiteSpace(streamUrl))
        {
            sources.Add(new LiveSource
            {
                Id = "channel-primary",
                Label = label,
                Channel = label,
                Kind = kind,
                Url = streamUrl,
                Country = "Global",
                Legality = "Channel supplied by the MarkUpTV catalogue.",
                Priority = 0
            });
        }

        return await BuildResolutionAsync(request, sources, isSelfTest: false, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// How many candidates for a competition can play inside the app right
    /// now (direct streams plus in-app embeds, excluding browse-only links).
    /// Used to label match tiles honestly as coverage loads.
    /// </summary>
    public async Task<int> CountPlayableSourcesAsync(
        string? league,
        CancellationToken cancellationToken = default)
    {
        FixtureStreamRequest request = new() { League = league };

        IReadOnlyList<LiveSource> sources = await _catalog
            .GetForFixtureAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return sources.Count(source => source.IsDirectlyPlayable || source.IsEmbeddable);
    }

    private async Task<LiveResolution> BuildResolutionAsync(
        FixtureStreamRequest request,
        List<LiveSource> sources,
        bool isSelfTest,
        CancellationToken cancellationToken)
    {
        List<LiveSource> direct = sources
            .Where(source => source.IsDirectlyPlayable)
            .OrderBy(source => source.Priority)
            .ToList();

        List<LiveSource> embeds = sources
            .Where(source => source.IsEmbeddable)
            .OrderBy(source => source.Priority)
            .ToList();

        List<LiveSource> browseOnly = sources
            .Where(source => source.BrowseOnly && !string.IsNullOrWhiteSpace(source.OfficialPage))
            .OrderBy(source => source.Priority)
            .ToList();

        List<StreamCandidate> candidates = new();

        // Probe direct streams in small batches; verified ones go to the front.
        List<StreamCandidate> verifiedDirect = new();
        List<StreamCandidate> unverifiedDirect = new();

        foreach (List<LiveSource> batch in Chunk(direct, MaxParallelProbes))
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task<StreamCandidate>[] tasks = batch
                .Select(source => ProbeDirectAsync(source, cancellationToken))
                .ToArray();

            StreamCandidate[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

            foreach (StreamCandidate candidate in results)
            {
                if (candidate.Verified)
                {
                    verifiedDirect.Add(candidate);
                }
                else
                {
                    unverifiedDirect.Add(candidate);
                }
            }

            // Stop probing once something is confirmed playable and we also
            // have a couple of backups - keeps tap-to-video fast.
            if (verifiedDirect.Count >= 2)
            {
                break;
            }
        }

        candidates.AddRange(verifiedDirect.OrderBy(candidate => candidate.Source.Priority));
        candidates.AddRange(unverifiedDirect.OrderBy(candidate => candidate.Source.Priority));

        foreach (LiveSource source in embeds)
        {
            if (YouTubeLiveResolver.IsYouTubeSchemeUrl(source.Url))
            {
                string? embedUrl = await _youtubeResolver
                    .ResolveEmbedUrlAsync(source.Url!, cancellationToken)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(embedUrl))
                {
                    candidates.Add(new StreamCandidate
                    {
                        Source = source,
                        Mode = "embed",
                        ResolvedUrl = embedUrl,
                        Verified = true // We verified it is live by resolving it
                    });
                }
                else
                {
                    // Fall back to browse-only on the channel page
                    candidates.Add(new StreamCandidate
                    {
                        Source = source,
                        Mode = "browse",
                        ResolvedUrl = YouTubeLiveResolver.GetFallbackUrl(source.Url!),
                        Verified = false
                    });
                }
            }
            else
            {
                candidates.Add(new StreamCandidate
                {
                    Source = source,
                    Mode = "embed",
                    ResolvedUrl = source.Url,
                    Verified = false
                });
            }
        }

        foreach (LiveSource source in browseOnly)
        {
            candidates.Add(new StreamCandidate
            {
                Source = source,
                Mode = "browse",
                ResolvedUrl = source.OfficialPage,
                Verified = false
            });
        }

        LiveResolution resolution = new()
        {
            Request = request,
            Candidates = candidates,
            IsSelfTest = isSelfTest
        };

        _logger.LogInformation(
            "Resolved '{Fixture}' ({League}): {Summary} [{Modes}]",
            request.FixtureTitle,
            request.League ?? "unknown league",
            resolution.Summary,
            string.Join(", ", candidates.Select(candidate => $"{candidate.Source.Id}:{candidate.Mode}")));

        return resolution;
    }

    private async Task<StreamCandidate> ProbeDirectAsync(LiveSource source, CancellationToken cancellationToken)
    {
        ProbeResult probe = await _probe.ProbeAsync(source, cancellationToken).ConfigureAwait(false);

        return new StreamCandidate
        {
            Source = source,
            Mode = "hls",
            ResolvedUrl = source.Url,
            Verified = probe.Ok && probe.Probed,
            FailReason = probe.Reason,
            LatencyMs = probe.LatencyMs
        };
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> items, int size)
    {
        for (int index = 0; index < items.Count; index += size)
        {
            yield return items.GetRange(index, Math.Min(size, items.Count - index));
        }
    }
}
