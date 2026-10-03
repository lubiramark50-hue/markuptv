using System.Text.Json.Serialization;

namespace MarkUptv.Models;

/// <summary>
/// One place a fixture can be watched. Catalogue entries describe free,
/// official broadcasters; backend entries describe streams resolved by the
/// MarkUpTV server. Only publicly free or user-authorised sources belong here.
/// </summary>
public sealed class LiveSource
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Short display label, e.g. "FIFA+".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Broadcaster / service name, e.g. "BBC iPlayer".</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>"hls" | "youtube" | "web".</summary>
    public string Kind { get; set; } = "hls";

    /// <summary>Playable stream URL (hls) or embeddable URL (youtube).</summary>
    public string? Url { get; set; }

    /// <summary>Official page a viewer can open when no embed is permitted.</summary>
    public string? OfficialPage { get; set; }

    public string? Country { get; set; }

    public string? Language { get; set; }

    /// <summary>Competitions this source carries, free-form description.</summary>
    public string? League { get; set; }

    /// <summary>Human note about where the free stream can be watched.</summary>
    public string? Region { get; set; }

    /// <summary>Provenance / legality note. Required for catalogue entries.</summary>
    public string Legality { get; set; } = string.Empty;

    /// <summary>Lower runs first.</summary>
    public int Priority { get; set; } = 50;

    /// <summary>True when the source only carries live video during matches.</summary>
    public bool MatchTimeOnly { get; set; }

    /// <summary>True when the entry must be opened on its official page (no embed).</summary>
    public bool BrowseOnly { get; set; }

    /// <summary>Public test stream used by the built-in playback self-test.</summary>
    public bool IsDiagnostic { get; set; }

    [JsonIgnore]
    public bool IsDirectlyPlayable =>
        !BrowseOnly &&
        IsDiagnostic == false &&
        string.Equals(Kind, "hls", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(Url);

    [JsonIgnore]
    public bool IsEmbeddable =>
        !BrowseOnly &&
        (string.Equals(Kind, "youtube", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(Kind, "web", StringComparison.OrdinalIgnoreCase)) &&
        !string.IsNullOrWhiteSpace(Url);

    [JsonIgnore]
    public string OriginLine =>
        string.Join(
            " · ",
            new[] { Country, Language, Region }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

    [JsonIgnore]
    public string AccessLine =>
        BrowseOnly ? "Opens official free player" : "Plays in app";
}

/// <summary>The embedded catalogue of free sources plus league coverage notes.</summary>
public sealed class LiveCatalog
{
    public int Version { get; set; } = 1;

    public string Updated { get; set; } = string.Empty;

    public List<LiveSource> Sources { get; set; } = new();

    public List<LeagueCoverage> Leagues { get; set; } = new();
}

/// <summary>Honest note of what free coverage exists for a competition.</summary>
public sealed class LeagueCoverage
{
    public string Name { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    /// <summary>Plain-language description of where it can be watched free.</summary>
    public string FreeCoverage { get; set; } = string.Empty;

    public List<string> SourceIds { get; set; } = new();
}

/// <summary>What the app knows about a fixture when it starts resolving.</summary>
public sealed class FixtureStreamRequest
{
    public string HomeTeam { get; set; } = string.Empty;

    public string AwayTeam { get; set; } = string.Empty;

    public string? League { get; set; }

    public string? KickoffIso { get; set; }

    /// <summary>Stream already resolved by the MarkUpTV backend, if any.</summary>
    public string? BackendStreamUrl { get; set; }

    public string? BackendStreamKind { get; set; }

    public string? BackendStreamEmbedUrl { get; set; }

    public string? BackendStreamLabel { get; set; }

    /// <summary>Include the public test stream (used by the self-test screen).</summary>
    public bool AllowDiagnostic { get; set; }

    public string FixtureTitle =>
        string.IsNullOrWhiteSpace(HomeTeam) && string.IsNullOrWhiteSpace(AwayTeam)
            ? "Live match"
            : $"{HomeTeam} vs {AwayTeam}";
}

/// <summary>One probed candidate in priority order.</summary>
public sealed class StreamCandidate
{
    public LiveSource Source { get; set; } = new();

    /// <summary>Direct HLS, embeddable page, or browse-only official page.</summary>
    public string Mode { get; set; } = "browse";

    public string? ResolvedUrl { get; set; }

    /// <summary>True when probing confirmed the stream answers.</summary>
    public bool Verified { get; set; }

    public string? FailReason { get; set; }

    public long LatencyMs { get; set; }

    public string ModeLabel =>
        Mode switch
        {
            "hls" => "Direct stream",
            "embed" => "In-app player",
            _ => "Official free player"
        };
}

/// <summary>Result handed to the player page: primary plus ordered fallbacks.</summary>
public sealed class LiveResolution
{
    public FixtureStreamRequest Request { get; set; } = new();

    public List<StreamCandidate> Candidates { get; set; } = new();

    public DateTimeOffset ResolvedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool IsSelfTest { get; set; }

    public bool HasAnyCandidate => Candidates.Count > 0;

    public bool HasPlayableCandidate =>
        Candidates.Any(candidate => candidate.Mode is "hls" or "embed");

    public string Summary
    {
        get
        {
            if (Candidates.Count == 0)
            {
                return "No free source found";
            }

            int playable = Candidates.Count(candidate => candidate.Mode is "hls" or "embed");
            return playable > 0
                ? $"{playable} playable source{(playable == 1 ? string.Empty : "s")}"
                : $"{Candidates.Count} official free link{(Candidates.Count == 1 ? string.Empty : "s")}";
        }
    }
}
