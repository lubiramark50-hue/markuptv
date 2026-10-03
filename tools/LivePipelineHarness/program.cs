using System.Text.Json;
using MarkUptv.LivePipelineHarness;
using MarkUptv.Models;
using MarkUptv.Services;

// ===========================================================================
// MarkUpTV live pipeline harness
//
// Executes the REAL production resolution code (linked from the app project)
// against the REAL catalogue asset and REAL live streams. No backend and no GUI
// are required, so the playback pipeline can be verified anywhere.
// ===========================================================================

int passed = 0;
int failed = 0;

void Check(string name, bool condition, string detail)
{
    if (condition)
    {
        passed++;
        Console.WriteLine($"  PASS  {name} - {detail}");
    }
    else
    {
        failed++;
        Console.WriteLine($"  FAIL  {name} - {detail}");
    }
}

string repoRoot = FindRepoRoot();
string catalogDir = Path.Combine(repoRoot, "MarkUptv", "MarkUptv", "Resources", "Raw");
Console.WriteLine($"repository:   {repoRoot}");
Console.WriteLine($"catalogue:    {catalogDir}\\live-sources.json");

var provider = new RepositoryCatalogProvider { CatalogDirectory = catalogDir };
var catalog = new LiveCatalogService(new ConsoleLogger<LiveCatalogService>(), provider);
var probe = new StreamProbeService(new ConsoleLogger<StreamProbeService>());
var resolver = new LiveResolutionService(catalog, probe, new ConsoleLogger<LiveResolutionService>());

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("TEST 1  catalogue loads through the production service");
LiveCatalog loaded = await catalog.GetCatalogAsync();
int worldChannels = (await catalog.GetWorldChannelsAsync()).Count;
int diagnostics = (await catalog.GetDiagnosticSourcesAsync()).Count;
Check("sources loaded", loaded.Sources.Count >= 25, $"{loaded.Sources.Count} sources");
Check("league notes loaded", loaded.Leagues.Count >= 10, $"{loaded.Leagues.Count} leagues");
Check("browsable world channels", worldChannels >= 25, $"{worldChannels} entries");
Check("self-test streams present", diagnostics >= 2, $"{diagnostics} streams");

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("TEST 2  African league fixture -> free sources");
LiveResolution fkf = await resolver.ResolveAsync(new FixtureStreamRequest
{
    HomeTeam = "Gor Mahia",
    AwayTeam = "AFC Leopards",
    League = "FKF Premier League"
});
Console.WriteLine($"  PLAN: {fkf.Summary}");
foreach (StreamCandidate c in fkf.Candidates)
{
    Console.WriteLine($"    - {c.Source.Id,-24} mode={c.Mode,-6} verified={c.Verified} region={c.Source.Region}");
}
Check("fixture resolves candidates", fkf.Candidates.Count > 0, $"{fkf.Candidates.Count} candidates");
Check("Kenyan broadcasters selected",
    fkf.Candidates.Any(c => c.Source.Id is "citizen-tv-kenya" or "ktn-news-kenya"),
    "Citizen TV / KTN present");
Check("self-test streams excluded by default",
    !fkf.Candidates.Any(c => c.Source.IsDiagnostic),
    "no diagnostic leak into a real fixture");

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("TEST 3  Premier League fixture -> free coverage");
LiveResolution epl = await resolver.ResolveAsync(new FixtureStreamRequest
{
    HomeTeam = "Arsenal",
    AwayTeam = "Chelsea",
    League = "English Premier League"
});
foreach (StreamCandidate c in epl.Candidates)
{
    Console.WriteLine($"    - {c.Source.Id,-24} mode={c.Mode,-6} region={c.Source.Region}");
}
Check("EPL coverage found", epl.Candidates.Count >= 2, $"{epl.Candidates.Count} candidates");
Check("UK free broadcaster listed",
    epl.Candidates.Any(c => c.Source.Id == "bbc-iplayer"),
    "BBC iPlayer present");
Check("schedule companion listed",
    epl.Candidates.Any(c => c.Source.Id == "livescore-free-epg"),
    "LiveScore present");

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("TEST 4  self-test streams probe as live (real network calls)");
LiveResolution selfTest = await resolver.BuildSelfTestAsync();
foreach (StreamCandidate c in selfTest.Candidates)
{
    Console.WriteLine($"    - {c.Source.Id,-24} verified={c.Verified} latency={c.LatencyMs} ms reason={c.FailReason ?? "ok"}");
}
int verifiedStreams = selfTest.Candidates.Count(c => c.Verified);
Check("self-test resolves streams", selfTest.Candidates.Count >= 2, $"{selfTest.Candidates.Count} streams");
Check("probe confirms live HLS (#EXTM3U)", verifiedStreams >= 2, $"{verifiedStreams} verified");
Check("probe latency recorded", selfTest.Candidates.Any(c => c.LatencyMs > 0),
    $"max latency {selfTest.Candidates.Max(c => c.LatencyMs)} ms");

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("TEST 5  dead source is skipped, working source plays (failover)");
LiveResolution failover = await resolver.ResolveAsync(new FixtureStreamRequest
{
    HomeTeam = "Failover",
    AwayTeam = "Simulation",
    League = "Diagnostics",
    BackendStreamUrl = "http://127.0.0.1:9/dead-stream.m3u8",
    BackendStreamKind = "hls",
    BackendStreamLabel = "Dead backend feed",
    AllowDiagnostic = true
});
foreach (StreamCandidate c in failover.Candidates)
{
    Console.WriteLine($"    - {c.Source.Id,-24} mode={c.Mode,-6} verified={c.Verified} reason={c.FailReason ?? "ok"}");
}
StreamCandidate? dead = failover.Candidates.FirstOrDefault(c => c.Source.Id == "backend-primary");
StreamCandidate? first = failover.Candidates.FirstOrDefault();
Check("dead backend feed captured", dead is not null, "candidate present");
Check("dead feed marked failed with a reason",
    dead is not null && !dead.Verified && !string.IsNullOrWhiteSpace(dead.FailReason),
    $"reason={dead?.FailReason}");
Check("a working source remains in the plan",
    failover.Candidates.Any(c => c.Verified),
    $"{failover.Candidates.Count(c => c.Verified)} verified source(s)");
Check("working source is ordered before the dead one",
    first?.Verified == true && first.Source.Id != "backend-primary",
    $"first = {first?.Source.Id}");

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("TEST 6  channel path (World Channels -> player)");
LiveResolution channel = await resolver.ResolveChannelAsync(
    "Playback self-test (Apple sample)",
    "https://devstreaming-cdn.apple.com/videos/streaming/examples/img_bipbop_adv_example_fmp4/master.m3u8",
    "hls");
StreamCandidate? channelCandidate = channel.Candidates.FirstOrDefault();
Check("channel resolves to a verified stream",
    channel.Candidates.Count == 1 && channelCandidate?.Verified == true,
    $"verified={channelCandidate?.Verified} latency={channelCandidate?.LatencyMs} ms");

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("TEST 7  plan ordering invariant");
int lastRank = -1;
bool ordered = true;
foreach (StreamCandidate c in failover.Candidates)
{
    int rank = c switch
    {
        { Mode: "hls", Verified: true } => 0,
        { Mode: "hls" } => 1,
        { Mode: "embed" } => 2,
        _ => 3
    };
    if (rank < lastRank) { ordered = false; }
    lastRank = rank;
}
Check("verified direct > unverified > embed > browse", ordered, "order holds");

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("TEST 8  community records round-trip (persistence format)");
CommunitySnapshot snapshot = new();
MatchThread thread = new()
{
    FixtureKey = "gor-mahia|afc-leopards|2026-09-14",
    Title = "Gor Mahia vs AFC Leopards",
    League = "FKF Premier League",
    PostCount = 1
};
snapshot.Threads.Add(thread);

ThreadPost post = new()
{
    ThreadId = thread.Id,
    Author = "Wanjiru",
    Text = "Omalla is on fire tonight"
};
post.ReactedBy.Add("You");
post.ReactedBy.Add("Otieno");
post.Comments.Add(new ThreadComment { PostId = post.Id, Author = "Kiptoo", Text = "2-1, called it" });
snapshot.Posts.Add(post);
snapshot.FollowedTeams.Add("Gor Mahia");
snapshot.BlockedAuthors.Add("SpamBot");
snapshot.Reports.Add(new CommunityReport
{
    TargetType = "post",
    TargetId = post.Id,
    Reason = "Spam",
    Reporter = "You",
    TargetPreview = post.Text,
    Status = "resolved",
    Resolution = "Actioned by moderator"
});
snapshot.Records.Add(new CommunityRecord { Action = "post.created", Detail = post.Text });

var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
string json = JsonSerializer.Serialize(snapshot, options);
CommunitySnapshot? restored = JsonSerializer.Deserialize<CommunitySnapshot>(json, options);

Check("snapshot round-trips", restored is not null, $"{json.Length} bytes");
Check("thread preserved",
    restored!.Threads.Count == 1 && restored.Threads[0].FixtureKey == thread.FixtureKey,
    $"fixture={restored.Threads[0].FixtureKey}");
Check("post + reactions preserved",
    restored.Posts.Count == 1 && restored.Posts[0].ReactedBy.Count == 2 && restored.Posts[0].Reactions == 2,
    $"reactions={restored.Posts[0].Reactions}");
Check("nested reply preserved",
    restored.Posts[0].Comments.Count == 1 && restored.Posts[0].Comments[0].Text == "2-1, called it",
    $"replies={restored.Posts[0].Comments.Count}");
Check("report + resolution preserved",
    restored.Reports.Count == 1 && restored.Reports[0].Status == "resolved" &&
    restored.Reports[0].Resolution == "Actioned by moderator",
    $"status={restored.Reports[0].Status}");
Check("follows, blocks and audit log preserved",
    restored.FollowedTeams.Count == 1 && restored.BlockedAuthors.Count == 1 && restored.Records.Count == 1,
    $"{restored.FollowedTeams.Count} follow / {restored.BlockedAuthors.Count} block / {restored.Records.Count} record");
Check("computed members are not persisted",
    !json.Contains("\"Reactions\"") && !json.Contains("\"TimeAgo\"") && !json.Contains("\"Summary\"") &&
    !json.Contains("\"Line\""),
    "no [JsonIgnore] leakage into storage");

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

static string FindRepoRoot()
{
    DirectoryInfo? dir = new(AppContext.BaseDirectory);

    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "MarkUptv", "MarkUptv")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate the MarkUptv repository root.");
}
