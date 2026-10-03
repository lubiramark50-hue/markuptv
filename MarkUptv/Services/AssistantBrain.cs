using System.Text;
using MarkUptv.Models;
using Microsoft.Extensions.Logging;

namespace MarkUptv.Services;

/// <summary>A button or route the assistant suggests alongside its answer.</summary>
public sealed record AssistantAction(string Label, string Route);

/// <summary>One grounded answer from the assistant.</summary>
public sealed class AssistantAnswer
{
    public string Intent { get; init; } = "fallback";

    public string Text { get; init; } = string.Empty;

    /// <summary>Where the answer came from, so it can be shown and audited.</summary>
    public string? Source { get; init; }

    public IReadOnlyList<AssistantAction> Actions { get; init; } = Array.Empty<AssistantAction>();

    /// <summary>True when the answer used live app data rather than a generic line.</summary>
    public bool Grounded { get; init; }
}

/// <summary>
/// The app's own assistant brain.
///
/// Everything it says is grounded in data the app already holds: the free-source
/// catalogue with its legality/region notes, today's real fixtures, the playback
/// resolver (what can actually play for a fixture), and the community store.
/// It runs offline, needs no API key, and can be tested deterministically - so
/// the assistant can answer questions the keyword templates could not, such as
/// "where can I watch Arsenal free?" or "why is my stream not playing?".
/// </summary>
public sealed class AssistantBrain
{
    private readonly LiveCatalogService _catalog;
    private readonly LiveResolutionService _resolver;
    private readonly PublicFixturesProvider _fixtures;
    private readonly CommunityStore _community;
    private readonly ILogger<AssistantBrain> _logger;

    public AssistantBrain(
        LiveCatalogService catalog,
        LiveResolutionService resolver,
        PublicFixturesProvider fixtures,
        CommunityStore community,
        ILogger<AssistantBrain> logger)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _fixtures = fixtures ?? throw new ArgumentNullException(nameof(fixtures));
        _community = community ?? throw new ArgumentNullException(nameof(community));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AssistantAnswer> AskAsync(
        string? question,
        CancellationToken cancellationToken = default)
    {
        string q = (question ?? string.Empty).Trim();

        if (q.Length == 0)
        {
            return new AssistantAnswer
            {
                Intent = "greeting",
                Text = "I can look things up in the app: what is on now, where a match can be " +
                       "watched free, which leagues are covered, free channels by country, and " +
                       "what to do when a stream misbehaves.",
                Actions = DefaultActions()
            };
        }

        string lower = q.ToLowerInvariant();

        try
        {
            if (ContainsAny(lower, "not playing", "won't play", "wont play", "black screen", "no video", "buffering", "stuck", "frozen", "error"))
            {
                return Troubleshoot();
            }

            if (ContainsAny(lower, "what's on", "whats on", "what is on", "on now", "tonight", "this evening", "live now"))
            {
                return await WhatIsOnAsync(cancellationToken).ConfigureAwait(false);
            }

            if (ContainsAny(lower, "free", "where can i watch", "where to watch", "which channel", "broadcast", "stream the"))
            {
                return await WhereToWatchAsync(q, cancellationToken).ConfigureAwait(false);
            }

            if (ContainsAny(lower, "league", "leagues", "competition", "which football do you", "coverage"))
            {
                return await LeaguesAsync(cancellationToken).ConfigureAwait(false);
            }

            if (ContainsAny(lower, "channel", "channels", "country", "countries", "broadcaster", "tv"))
            {
                return await ChannelsAsync(q, cancellationToken).ConfigureAwait(false);
            }

            if (ContainsAny(lower, "community", "people saying", "comments", "thread", "reports", "moderation"))
            {
                return Community();
            }

            if (ContainsAny(lower, "how do i", "how do you", "how to", "can i", "where is"))
            {
                return HowTo(lower);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Assistant lookup failed for '{Question}'", q);
        }

        return new AssistantAnswer
        {
            Intent = "fallback",
            Text = "I did not catch that one. Try asking where a match can be watched free, " +
                   "what is on now, which leagues are covered, or what to do when a stream stops.",
            Actions = DefaultActions()
        };
    }

    // ------------------------------------------------------------------
    // Intents
    // ------------------------------------------------------------------

    private async Task<AssistantAnswer> WhatIsOnAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<LiveMatch> fixtures = await _fixtures
            .GetFixturesAsync(DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (fixtures.Count == 0)
        {
            return new AssistantAnswer
            {
                Intent = "whats_on",
                Text = "No fixtures are published for today yet. Open the football board to see " +
                       "the upcoming schedule, or pick a channel from World Channels.",
                Source = "public fixtures feed",
                Grounded = true,
                Actions = DefaultActions()
            };
        }

        StringBuilder text = new();
        text.AppendLine($"There are {fixtures.Count} match{(fixtures.Count == 1 ? string.Empty : "es")} in today's list.");

        foreach (LiveMatch match in fixtures
                     .OrderBy(item => item.KickoffUtc)
                     .Take(6))
        {
            string when = match.KickoffUtc == default
                ? "time to be confirmed"
                : match.KickoffUtc.ToLocalTime().ToString("HH:mm");

            text.AppendLine($"- {match.HomeTeam} vs {match.AwayTeam} ({match.League ?? "competition"}) at {when}");
        }

        return new AssistantAnswer
        {
            Intent = "whats_on",
            Text = text.ToString().TrimEnd(),
            Source = "public fixtures feed",
            Grounded = true,
            Actions = DefaultActions()
        };
    }

    private async Task<AssistantAnswer> WhereToWatchAsync(string question, CancellationToken cancellationToken)
    {
        IReadOnlyList<LiveMatch> fixtures = await _fixtures
            .GetFixturesAsync(DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        LiveMatch? match = MatchFixture(question, fixtures);

        if (match is null)
        {
            LeagueCoverage? note = await FindCoverageAsync(question, cancellationToken).ConfigureAwait(false);

            if (note is not null)
            {
                return new AssistantAnswer
                {
                    Intent = "where_to_watch",
                    Text = $"For the {note.Name}: {note.FreeCoverage}",
                    Source = "coverage notes",
                    Grounded = true,
                    Actions = DefaultActions()
                };
            }

            return new AssistantAnswer
            {
                Intent = "where_to_watch",
                Text = "I could not match that to a fixture or competition in today's list. " +
                       "Tell me the two teams or the competition name and I will look it up.",
                Actions = DefaultActions()
            };
        }

        return await WhereToWatchAsync(match, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AssistantAnswer> WhereToWatchAsync(LiveMatch match, CancellationToken cancellationToken)
    {
        LiveResolution plan = await _resolver
            .ResolveAsync(
                new FixtureStreamRequest
                {
                    HomeTeam = match.HomeTeam,
                    AwayTeam = match.AwayTeam,
                    League = match.League,
                    KickoffIso = match.KickoffUtc == default ? null : match.KickoffUtc.ToString("O")
                },
                cancellationToken)
            .ConfigureAwait(false);

        StringBuilder text = new();
        text.AppendLine($"{match.HomeTeam} vs {match.AwayTeam} ({match.League ?? "fixture"}):");

        if (plan.Candidates.Count == 0)
        {
            text.AppendLine("No free source is listed for this fixture yet. Free feeds usually appear closer to kick-off.");
        }
        else
        {
            foreach (StreamCandidate candidate in plan.Candidates.Take(4))
            {
                text.AppendLine($"- {candidate.Source.Label} ({candidate.Source.Country}) - {candidate.ModeLabel}" +
                                (string.IsNullOrWhiteSpace(candidate.Source.Region) ? string.Empty : $" - {candidate.Source.Region}"));
            }

            StreamCandidate? playable = plan.Candidates.FirstOrDefault(candidate => candidate.Mode is "hls" or "embed");

            text.AppendLine();
            text.AppendLine(playable is not null
                ? "One of these plays inside the app; the rest open the broadcaster's own free player."
                : "These open the official free player. For the big paid leagues there is no legal free " +
                  "live feed anywhere, so the app never substitutes an unofficial stream.");
        }

        return new AssistantAnswer
        {
            Intent = "where_to_watch",
            Text = text.ToString().TrimEnd(),
            Source = "catalogue + resolver",
            Grounded = true,
            Actions = DefaultActions()
        };
    }

    private async Task<AssistantAnswer> LeaguesAsync(CancellationToken cancellationToken)
    {
        LiveCatalog catalog = await _catalog.GetCatalogAsync(cancellationToken).ConfigureAwait(false);

        StringBuilder text = new();
        text.AppendLine($"I cover {catalog.Leagues.Count} competitions in the free-source notes:");

        foreach (LeagueCoverage league in catalog.Leagues.Take(8))
        {
            text.AppendLine($"- {league.Name} ({league.Country})");
        }

        text.AppendLine();
        text.AppendLine("Ask about any one of them and I will tell you where it can be watched free.");

        return new AssistantAnswer
        {
            Intent = "leagues",
            Text = text.ToString().TrimEnd(),
            Source = "catalogue",
            Grounded = true,
            Actions = DefaultActions()
        };
    }

    private async Task<AssistantAnswer> ChannelsAsync(string question, CancellationToken cancellationToken)
    {
        IReadOnlyList<LiveSource> channels = await _catalog
            .GetWorldChannelsAsync(cancellationToken)
            .ConfigureAwait(false);

        string wanted = ExtractCountry(question);

        IEnumerable<LiveSource> matches = string.IsNullOrWhiteSpace(wanted)
            ? channels
            : channels.Where(channel =>
                (channel.Country ?? string.Empty).Contains(wanted, StringComparison.OrdinalIgnoreCase));

        List<LiveSource> list = matches.Take(6).ToList();

        if (list.Count == 0)
        {
            return new AssistantAnswer
            {
                Intent = "channels",
                Text = $"I do not have a free broadcaster listed for '{wanted}'. " +
                       "South Africa (SABC+), Kenya (Citizen TV, KTN), Uganda (NBS), the UK (BBC iPlayer, ITVX), " +
                       "Germany (ARD, ZDF), Italy (RaiPlay), Spain (RTVE) and France (France.tv) are covered.",
                Source = "catalogue",
                Grounded = true,
                Actions = DefaultActions()
            };
        }

        StringBuilder text = new();
        text.AppendLine(string.IsNullOrWhiteSpace(wanted)
            ? $"Free channels I know about ({channels.Count} in total):"
            : $"Free channels I know about in {wanted}:");

        foreach (LiveSource channel in list)
        {
            text.AppendLine($"- {channel.Label} ({channel.Channel}) - {channel.AccessLine}");
        }

        return new AssistantAnswer
        {
            Intent = "channels",
            Text = text.ToString().TrimEnd(),
            Source = "catalogue",
            Grounded = true,
            Actions = DefaultActions()
        };
    }

    private AssistantAnswer Community()
    {
        IReadOnlyList<MatchThread> threads = _community.GetThreads();
        int openReports = _community.GetReports(openOnly: true).Count;

        StringBuilder text = new();

        if (threads.Count == 0)
        {
            text.AppendLine("No match threads yet. Open any fixture in the player and tap Match thread to start one.");
        }
        else
        {
            text.AppendLine($"{threads.Count} match thread{(threads.Count == 1 ? string.Empty : "s")}:");

            foreach (MatchThread thread in threads.Take(4))
            {
                text.AppendLine($"- {thread.Title} - {thread.Subtitle}");
            }
        }

        text.AppendLine(openReports == 0
            ? "No open reports in the moderation queue."
            : $"{openReports} report{(openReports == 1 ? string.Empty : "s")} awaiting review in Community Hub.");

        return new AssistantAnswer
        {
            Intent = "community",
            Text = text.ToString().TrimEnd(),
            Source = "community store",
            Grounded = true,
            Actions = new[]
            {
                new AssistantAction("Open Community Hub", "///CommunityHubPage")
            }
        };
    }

    private static AssistantAnswer Troubleshoot()
    {
        return new AssistantAnswer
        {
            Intent = "troubleshoot",
            Text =
                "What I would check, in order:\n" +
                "1. The player shows which source it is on and switches automatically if one fails; " +
                "the status line under the title tells you what it is doing.\n" +
                "2. If the first source is slow, it is given 7 seconds before the app moves to the next one.\n" +
                "3. Use Retry on the error card, or Next source to skip ahead manually.\n" +
                "4. If every source fails, the app says so instead of freezing - free streams do drop during matches.\n" +
                "5. Run the playback self-test from World Channels to prove the player itself is fine.",
            Source = "player behaviour",
            Grounded = true,
            Actions = new[]
            {
                new AssistantAction("World Channels", "///WorldChannelsPage"),
                new AssistantAction("Football board", "///FootballPage")
            }
        };
    }

    private static AssistantAnswer HowTo(string lower)
    {
        (string[] Keys, string Answer, string Label, string Route)[] topics =
        {
            (new[] { "favourite", "favorite", "save" },
                "Favourites and watch history live on the Home shelves; opening a channel from Search, " +
                "Football or World Channels records it to Continue watching automatically.",
                "Open Home", "///MainPage"),

            (new[] { "follow", "team" },
                "To follow a team: open a match thread from the player and tap Follow team. " +
                "Your followed teams are listed in Community Hub.",
                "Community Hub", "///CommunityHubPage"),

            (new[] { "community", "post", "comment", "report" },
                "Open a fixture in the player, tap Match thread, and you can post, react, reply, " +
                "report a post or block its author. Reports land in Community Hub for review.",
                "Community Hub", "///CommunityHubPage"),

            (new[] { "free channel", "world channel", "broadcaster" },
                "World Channels groups free, official broadcasters by country; tap one to play it " +
                "in the app or open the broadcaster's own free player.",
                "World Channels", "///WorldChannelsPage"),

            (new[] { "search" },
                "Search looks across channels and categories; results open straight into the player.",
                "Open Search", "///SearchPage")
        };

        foreach ((string[] keys, string answer, string label, string route) in topics)
        {
            if (keys.Any(key => lower.Contains(key, StringComparison.Ordinal)))
            {
                return new AssistantAnswer
                {
                    Intent = "how_to",
                    Text = answer,
                    Source = "app guide",
                    Grounded = true,
                    Actions = new[] { new AssistantAction(label, route) }
                };
            }
        }

        return new AssistantAnswer
        {
            Intent = "how_to",
            Text = "I can explain favourites, following a team, match threads, reporting, " +
                   "free channels and search. Which one?",
            Actions = DefaultActions()
        };
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static IReadOnlyList<AssistantAction> DefaultActions() => new[]
    {
        new AssistantAction("Football board", "///FootballPage"),
        new AssistantAction("World Channels", "///WorldChannelsPage")
    };

    private static bool ContainsAny(string haystack, params string[] needles)
        => needles.Any(needle => haystack.Contains(needle, StringComparison.Ordinal));

    private static LiveMatch? MatchFixture(string question, IReadOnlyList<LiveMatch> fixtures)
    {
        foreach (LiveMatch fixture in fixtures)
        {
            bool home = fixture.HomeTeam.Length > 2 &&
                        question.Contains(fixture.HomeTeam, StringComparison.OrdinalIgnoreCase);

            bool away = fixture.AwayTeam.Length > 2 &&
                        question.Contains(fixture.AwayTeam, StringComparison.OrdinalIgnoreCase);

            if (home || away)
            {
                return fixture;
            }
        }

        return null;
    }

    private async Task<LeagueCoverage?> FindCoverageAsync(string question, CancellationToken cancellationToken)
    {
        LiveCatalog catalog = await _catalog.GetCatalogAsync(cancellationToken).ConfigureAwait(false);

        return catalog.Leagues.FirstOrDefault(league =>
            question.Contains(league.Name, StringComparison.OrdinalIgnoreCase) ||
            league.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length > 3)
                .Any(token => question.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }

    private static string ExtractCountry(string question)
    {
        string[] countries =
        {
            "kenya", "south africa", "uganda", "nigeria", "ghana", "tanzania",
            "united kingdom", "uk", "england", "germany", "italy", "spain", "france",
            "india", "brazil", "united states", "usa", "global"
        };

        string lower = question.ToLowerInvariant();

        return countries.FirstOrDefault(country => lower.Contains(country, StringComparison.Ordinal)) ?? string.Empty;
    }
}
