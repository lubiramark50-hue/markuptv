using System.Diagnostics;
using System.Text;
using CommunityToolkit.Maui.Views;
using MarkUptv.Helpers;
using MarkUptv.Models;
using MarkUptv.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace MarkUptv.Services;

/// <summary>
/// Optional diagnostics mode. When the environment variable
/// <c>MARKUPTV_SELFCHECK</c> is set to 1 the app, immediately after its window
/// is created, exercises the real live pipeline (packaged catalogue -> probing
/// -> resolution) and writes a report next to the app data, then exits with a
/// status code. This verifies the shipping build - dependency injection,
/// packaged assets, network probing - without a backend or a GUI session.
///
/// Normal launches are unaffected: nothing runs unless the variable is set.
/// </summary>
public static class AppSelfCheck
{
    public const string EnvironmentVariableName = "MARKUPTV_SELFCHECK";

    private const string ReportFileName = "selfcheck-report.txt";

    public static bool IsRequested =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariableName));

    /// <summary>True when the player-rendering check was requested.</summary>
    public static bool IsPlayerCheckRequested =>
        string.Equals(
            Environment.GetEnvironmentVariable(EnvironmentVariableName)?.Trim(),
            "player",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the real-fixture coverage check was requested.</summary>
    public static bool IsFixtureCheckRequested =>
        string.Equals(
            Environment.GetEnvironmentVariable(EnvironmentVariableName)?.Trim(),
            "fixtures",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the endurance (soak) check was requested.</summary>
    public static bool IsSoakCheckRequested =>
        string.Equals(
            Environment.GetEnvironmentVariable(EnvironmentVariableName)?.Trim(),
            "soak",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the community persistence check was requested.</summary>
    public static bool IsCommunityCheckRequested =>
        string.Equals(
            Environment.GetEnvironmentVariable(EnvironmentVariableName)?.Trim(),
            "community",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the assistant test battery was requested.</summary>
    public static bool IsAssistantCheckRequested =>
        string.Equals(
            Environment.GetEnvironmentVariable(EnvironmentVariableName)?.Trim(),
            "assistant",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True for the navigation-only soak: navigates to the player and back
    /// without playing anything, isolating navigation cost from media cost.
    /// </summary>
    public static bool IsNavigationSoakRequested =>
        string.Equals(
            Environment.GetEnvironmentVariable(EnvironmentVariableName)?.Trim(),
            "soaknav",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Player-rendering check: opens the real player on a public sample stream
    /// and measures whether moving video is actually produced (media state plus
    /// an advancing playback position).
    /// </summary>
    public static async Task<int> RunPlayerAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        StringBuilder report = new();
        report.AppendLine("MarkUpTV player self-check");
        report.AppendLine($"started (UTC): {DateTime.UtcNow:O}");
        report.AppendLine();

        // Resolve the playback plan here and hand it to the player explicitly;
        // this avoids depending on query-string parsing for the check itself.
        string candidatesJson;
        LiveResolution? plan = null;

        try
        {
            LiveResolutionService resolver = services.GetRequiredService<LiveResolutionService>();
            plan = await resolver.BuildSelfTestAsync(cancellationToken);

            candidatesJson = System.Text.Json.JsonSerializer.Serialize(
                plan.Candidates,
                MarkUptv.Serialization.MarkUptvJsonContext.Default.ListStreamCandidate);

            report.AppendLine($"plan: {plan.Summary}");

            foreach (StreamCandidate candidate in plan.Candidates)
            {
                report.AppendLine(
                    $"    {candidate.Source.Id}: verified={candidate.Verified} " +
                    $"latency={candidate.LatencyMs} ms url={candidate.ResolvedUrl}");
            }

            report.AppendLine();
        }
        catch (Exception exception)
        {
            report.AppendLine($"FAIL  plan resolution threw - {exception.Message}");
            return WriteReport(report, "selfcheck-player-report.txt", 1);
        }

        // Wait for Shell to be available after window creation.
        for (int attempt = 0; attempt < 40 && Shell.Current is null; attempt++)
        {
            await Task.Delay(250, cancellationToken);
        }

        if (Shell.Current is null)
        {
            report.AppendLine("FAIL  Shell was not available");
            return WriteReport(report, "selfcheck-player-report.txt", 1);
        }

        LiveSessionService session = services.GetRequiredService<LiveSessionService>();

        session.SetPending(new PendingLivePlayback
        {
            Title = "Playback self-check",
            League = "Diagnostics",
            Candidates = (IReadOnlyList<StreamCandidate>?)plan?.Candidates ?? Array.Empty<StreamCandidate>(),
            IsSelfTest = true
        });

        try
        {
            await MainThread.InvokeOnMainThreadAsync(
                () => Shell.Current!.GoToAsync(nameof(Pages.PlayerPage)));
        }
        catch (Exception exception)
        {
            report.AppendLine($"FAIL  navigation threw - {exception.Message}");
            return WriteReport(report, "selfcheck-player-report.txt", 1);
        }

        report.AppendLine($"navigated: {Shell.Current.CurrentState.Location}");

        HashSet<string> states = new();
        TimeSpan firstPosition = TimeSpan.MinValue;
        TimeSpan lastPosition = TimeSpan.MinValue;
        bool sawPlayer = false;

        for (int tick = 0; tick < 100; tick++)
        {
            await Task.Delay(250, cancellationToken);

            MediaElement? player = PlaybackCoordinator.ActivePlayer;

            if (player is null)
            {
                continue;
            }

            sawPlayer = true;
            states.Add(player.CurrentState.ToString());

            TimeSpan position = player.Position;

            if (firstPosition == TimeSpan.MinValue && position > TimeSpan.Zero)
            {
                firstPosition = position;
            }

            if (position > lastPosition)
            {
                lastPosition = position;
            }

            if (states.Contains("Playing") && lastPosition > TimeSpan.FromSeconds(2))
            {
                break;
            }
        }

        TimeSpan advanced = lastPosition - firstPosition;

        report.AppendLine($"player instance observed: {sawPlayer}");
        report.AppendLine($"media states observed: {string.Join(" -> ", states)}");
        report.AppendLine($"position first: {firstPosition}");
        report.AppendLine($"position last:  {lastPosition}");
        report.AppendLine($"position advanced by: {advanced}");
        report.AppendLine();

        int passed = 0;
        int failed = 0;

        void Check(string name, bool condition, string detail)
        {
            if (condition) { passed++; report.AppendLine($"PASS  {name} - {detail}"); }
            else { failed++; report.AppendLine($"FAIL  {name} - {detail}"); }
        }

        Check("player page reached with a media element", sawPlayer, "ActivePlayer registered");
        Check("media pipeline engaged", states.Count > 0, string.Join("/", states));
        Check("video is rendering (position advances)", advanced > TimeSpan.FromSeconds(1),
            $"advanced {advanced.TotalSeconds:F1} s");

        report.AppendLine();
        report.AppendLine($"RESULT: {passed} passed, {failed} failed");

        return WriteReport(report, "selfcheck-player-report.txt", failed == 0 ? 0 : 1);
    }

    /// <summary>
    /// Real-fixture coverage check: loads today's actual fixtures from the
    /// public feed, resolves each league and reports how many were mapped to a
    /// playing plan. This is the coverage table required for release.
    /// </summary>
    public static async Task<int> RunFixturesAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        StringBuilder report = new();
        report.AppendLine("MarkUpTV fixture coverage self-check");
        report.AppendLine($"started (UTC): {DateTime.UtcNow:O}");
        report.AppendLine();

        int passed = 0;
        int failed = 0;

        try
        {
            PublicFixturesProvider fixtures = services.GetRequiredService<PublicFixturesProvider>();
            LiveResolutionService resolver = services.GetRequiredService<LiveResolutionService>();

            IReadOnlyList<LiveMatch> today = await fixtures.GetFixturesAsync(
                DateTime.UtcNow,
                cancellationToken);

            report.AppendLine($"real fixtures from the daily feed: {today.Count}");
            report.AppendLine();

            // Major competitions, by TheSportsDB competition id.
            (string Id, string Name)[] majorLeagues =
            {
                ("4328", "English Premier League"),
                ("4335", "La Liga"),
                ("4332", "Serie A"),
                ("4331", "Bundesliga"),
                ("4334", "Ligue 1"),
                ("4480", "UEFA Champions League"),
                ("4481", "UEFA Europa League"),
                ("4346", "Major League Soccer"),
                ("4351", "Brazilian Serie A"),
                ("4337", "Eredivisie"),
                ("4344", "Primeira Liga"),
                ("4339", "Turkish Super Lig")
            };

            int leaguesWithFixtures = 0;
            int leaguesWithPlan = 0;

            report.AppendLine(
                "league".PadRight(30) + "fixtures   plan  playable  top source");
            report.AppendLine(new string('-', 92));

            foreach ((string id, string name) in majorLeagues)
            {
                IReadOnlyList<LiveMatch> leagueFixtures = await fixtures
                    .GetLeagueFixturesAsync(id, cancellationToken);

                if (leagueFixtures.Count == 0)
                {
                    report.AppendLine(name.PadRight(30) + "       0      -         -  (no fixtures published)");
                    continue;
                }

                leaguesWithFixtures++;

                LiveMatch sample = leagueFixtures.First();

                LiveResolution plan = await resolver.ResolveAsync(
                    new FixtureStreamRequest
                    {
                        HomeTeam = sample.HomeTeam,
                        AwayTeam = sample.AwayTeam,
                        League = sample.League ?? name,
                        KickoffIso = sample.KickoffUtc == default ? null : sample.KickoffUtc.ToString("O")
                    },
                    cancellationToken);

                int playable = plan.Candidates.Count(candidate => candidate.Mode is "hls" or "embed");

                if (plan.Candidates.Count > 0)
                {
                    leaguesWithPlan++;
                }

                report.AppendLine(
                    name.PadRight(30) +
                    leagueFixtures.Count.ToString().PadLeft(8) + "   " +
                    plan.Candidates.Count.ToString().PadLeft(4) + "  " +
                    playable.ToString().PadLeft(8) + "  " +
                    (plan.Candidates.FirstOrDefault()?.Source.Id ?? "-"));
            }

            report.AppendLine();
            report.AppendLine($"major leagues with fixtures: {leaguesWithFixtures}/{majorLeagues.Length}");
            report.AppendLine($"major leagues mapped to a free-source plan: {leaguesWithPlan}/{majorLeagues.Length}");
            report.AppendLine();

            if (leaguesWithFixtures == 0)
            {
                failed++;
                report.AppendLine("FAIL  no fixtures returned by the public feed");
            }
            else
            {
                passed++;
                report.AppendLine($"PASS  fixtures published for {leaguesWithFixtures} major leagues");

                if (leaguesWithPlan >= 1)
                {
                    passed++;
                    report.AppendLine($"PASS  free-source plans available for {leaguesWithPlan}/{leaguesWithFixtures} leagues with fixtures");
                }
                else
                {
                    failed++;
                    report.AppendLine("FAIL  no league mapped to a playback plan");
                }
            }

        }
        catch (Exception exception)
        {
            failed++;
            report.AppendLine($"FAIL  fixture check threw - {exception}");
        }

        report.AppendLine();
        report.AppendLine($"RESULT: {passed} passed, {failed} failed");

        return WriteReport(report, "selfcheck-fixtures-report.txt", failed == 0 ? 0 : 1);
    }

    /// <summary>
    /// Endurance check: repeatedly resolve a plan, open the player, wait for
    /// moving video, switch source and return home - while sampling memory and
    /// start latency. Detects leaks, hangs and crashes under switching.
    /// </summary>
    public static async Task<int> RunSoakAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        const int MaxCycles = 20;
        const int CycleSecondsBudget = 150;

        StringBuilder report = new();
        report.AppendLine("MarkUpTV endurance (soak) self-check");
        report.AppendLine($"started (UTC): {DateTime.UtcNow:O}");
        report.AppendLine($"max cycles: {MaxCycles}, time budget: {CycleSecondsBudget}s");
        report.AppendLine();

        int passed = 0;
        int failed = 0;

        LiveResolutionService resolver = services.GetRequiredService<LiveResolutionService>();
        LiveSessionService session = services.GetRequiredService<LiveSessionService>();

        for (int attempt = 0; attempt < 40 && Shell.Current is null; attempt++)
        {
            await Task.Delay(250, cancellationToken);
        }

        if (Shell.Current is null)
        {
            report.AppendLine("FAIL  Shell was not available");
            return WriteReport(report, "selfcheck-soak-report.txt", 1);
        }

        List<double> startLatencies = new();
        List<double> memorySamples = new();
        int cyclesPlayed = 0;
        int cyclesWithVideo = 0;
        Stopwatch budget = Stopwatch.StartNew();

        report.AppendLine("cycle  resolve  start   state           position  memory(MB)");
        report.AppendLine(new string('-', 68));

        for (int cycle = 1; cycle <= MaxCycles && budget.Elapsed.TotalSeconds < CycleSecondsBudget; cycle++)
        {
            try
            {
                Stopwatch resolveWatch = Stopwatch.StartNew();
                LiveResolution plan = await resolver.BuildSelfTestAsync(cancellationToken);
                resolveWatch.Stop();

                if (plan.Candidates.Count == 0)
                {
                    report.AppendLine($"{cycle,5}  {resolveWatch.ElapsedMilliseconds,7}  RESOLVE-EMPTY");
                    continue;
                }

                session.SetPending(new PendingLivePlayback
                {
                    Title = $"Soak cycle {cycle}",
                    League = "Diagnostics",
                    Candidates = plan.Candidates,
                    IsSelfTest = true
                });

                await MainThread.InvokeOnMainThreadAsync(
                    () => Shell.Current!.GoToAsync(nameof(Pages.PlayerPage)));

                Stopwatch videoWatch = Stopwatch.StartNew();
                string state = "none";
                TimeSpan position = TimeSpan.Zero;

                for (int tick = 0; tick < 60; tick++)
                {
                    await Task.Delay(250, cancellationToken);

                    MediaElement? player = PlaybackCoordinator.ActivePlayer;

                    if (player is null)
                    {
                        continue;
                    }

                    state = player.CurrentState.ToString();

                    if (player.Position > position)
                    {
                        position = player.Position;
                    }

                    if (state == "Playing" && position > TimeSpan.FromSeconds(1))
                    {
                        break;
                    }
                }

                videoWatch.Stop();

                bool hasVideo = state == "Playing" && position > TimeSpan.FromSeconds(1);

                if (hasVideo)
                {
                    cyclesWithVideo++;
                    startLatencies.Add(videoWatch.Elapsed.TotalMilliseconds);
                }

                cyclesPlayed++;

                // forceFullCollection: true so the figure reflects retained
                // memory rather than garbage awaiting collection.
                double memoryMb = GC.GetTotalMemory(forceFullCollection: true) / 1024d / 1024d;
                memorySamples.Add(memoryMb);

                report.AppendLine(
                    $"{cycle,5}  {resolveWatch.ElapsedMilliseconds,7}  {videoWatch.ElapsedMilliseconds,5}   {state,-14}  {position.TotalSeconds,6:F1}s  {memoryMb,9:F1}");

                // Return to the board for the next cycle.
                await MainThread.InvokeOnMainThreadAsync(
                    () => Shell.Current!.GoToAsync(".."));

                await Task.Delay(400, cancellationToken);
            }
            catch (Exception exception)
            {
                failed++;
                report.AppendLine($"{cycle,5}  CYCLE FAILED - {exception.Message}");
            }
        }

        budget.Stop();

        report.AppendLine();
        report.AppendLine($"cycles run: {cyclesPlayed}");
        report.AppendLine($"cycles with moving video: {cyclesWithVideo}");
        report.AppendLine($"elapsed: {budget.Elapsed.TotalSeconds:F0}s");

        if (startLatencies.Count > 0)
        {
            report.AppendLine($"player start ms: min {startLatencies.Min():F0} / avg {startLatencies.Average():F0} / max {startLatencies.Max():F0}");
        }

        if (memorySamples.Count > 1)
        {
            double first = memorySamples.First();
            double last = memorySamples.Last();
            report.AppendLine($"managed memory MB: first {first:F1} / last {last:F1} / delta {last - first:+0.0;-0.0;0.0}");
        }

        report.AppendLine();

        double successRate = cyclesPlayed == 0 ? 0 : (double)cyclesWithVideo / cyclesPlayed;

        if (cyclesPlayed >= 3 && successRate >= 0.6)
        {
            passed++;
            report.AppendLine($"PASS  repeated playback holds up - {cyclesWithVideo}/{cyclesPlayed} cycles with video ({successRate:P0})");
        }
        else
        {
            failed++;
            report.AppendLine($"FAIL  playback degraded under repetition - {cyclesWithVideo}/{cyclesPlayed} cycles");
        }

        if (memorySamples.Count > 1 && (memorySamples.Last() - memorySamples.First()) < 120)
        {
            passed++;
            report.AppendLine($"PASS  no runaway memory growth - delta {(memorySamples.Last() - memorySamples.First()):F1} MB");
        }
        else if (memorySamples.Count > 1)
        {
            failed++;
            report.AppendLine($"FAIL  memory grew by {(memorySamples.Last() - memorySamples.First()):F1} MB across the run");
        }

        report.AppendLine();
        report.AppendLine($"RESULT: {passed} passed, {failed} failed");

        return WriteReport(report, "selfcheck-soak-report.txt", failed == 0 ? 0 : 1);
    }

    /// <summary>
    /// Community persistence check, in two phases across two app launches.
    ///
    /// Phase 1 (first run) writes a thread, post, reaction, reply, follow,
    /// report and moderation decision, then records a marker.
    /// Phase 2 (next launch - i.e. after an app restart) reads everything back
    /// and asserts it survived.
    /// </summary>
    public static async Task<int> RunCommunityAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        const string MarkerKey = "Community_SelfCheck_Marker_V1";

        StringBuilder report = new();
        report.AppendLine("MarkUpTV community persistence self-check");
        report.AppendLine($"started (UTC): {DateTime.UtcNow:O}");
        report.AppendLine();

        int passed = 0;
        int failed = 0;

        void Check(string name, bool condition, string detail)
        {
            if (condition) { passed++; report.AppendLine($"PASS  {name} - {detail}"); }
            else { failed++; report.AppendLine($"FAIL  {name} - {detail}"); }
        }

        try
        {
            CommunityStore store = services.GetRequiredService<CommunityStore>();
            bool isSecondPhase = !string.IsNullOrWhiteSpace(Preferences.Get(MarkerKey, null));

            if (!isSecondPhase)
            {
                report.AppendLine("PHASE 1  writing community records");

                MatchThread thread = store.GetOrCreateThread(
                    "selfcheck|fixtures|2026-09-14",
                    "Self-check thread",
                    "Diagnostics");

                ThreadPost post = store.AddPost(thread.Id, "SelfCheck", "Persistence probe post");
                store.ToggleReaction(post.Id, "SelfCheck");
                store.AddComment(post.Id, "SelfCheck", "Persistence probe reply");

                CommunityReport report1 = store.AddReport(
                    "post",
                    post.Id,
                    "SelfCheck reason",
                    "SelfCheck",
                    post.Text);

                store.ResolveReport(report1.Id, upheld: true, note: "SelfCheck resolution");
                store.ToggleFollowTeam("SelfCheck FC");

                Check("records written", true, $"thread {thread.Id[..8]}, post {post.Id[..8]}, report {report1.Id[..8]}");

                CommunitySnapshot written = store.CreateSnapshot();
                report.AppendLine($"    threads={written.Threads.Count} posts={written.Posts.Count} " +
                                  $"reports={written.Reports.Count} records={written.Records.Count} " +
                                  $"follows={written.FollowedTeams.Count}");

                Preferences.Set(MarkerKey, DateTime.UtcNow.ToString("O"));
                report.AppendLine("    marker stored; run the app again to verify persistence");
            }
            else
            {
                report.AppendLine("PHASE 2  reading records back after an app restart");

                CommunitySnapshot snapshot = store.CreateSnapshot();

                Check("threads survived", snapshot.Threads.Count >= 1, $"{snapshot.Threads.Count} thread(s)");
                Check("posts survived", snapshot.Posts.Count >= 1, $"{snapshot.Posts.Count} post(s)");

                ThreadPost? post = snapshot.Posts.FirstOrDefault();

                Check("reactions survived",
                    post is not null && post.ReactedBy.Count >= 1,
                    $"reactions={(post?.Reactions ?? 0)}");

                Check("replies survived",
                    post is not null && post.Comments.Count >= 1,
                    $"replies={(post?.Comments.Count ?? 0)}");

                Check("reports survived",
                    snapshot.Reports.Count >= 1,
                    $"{snapshot.Reports.Count} report(s)");

                CommunityReport? stored = snapshot.Reports.FirstOrDefault();

                Check("moderation decision survived",
                    stored is not null && stored.Status == "resolved" &&
                    !string.IsNullOrWhiteSpace(stored.Resolution),
                    $"status={stored?.Status} resolution={stored?.Resolution}");

                Check("followed team survived",
                    snapshot.FollowedTeams.Contains("SelfCheck FC", StringComparer.OrdinalIgnoreCase),
                    string.Join(",", snapshot.FollowedTeams));

                Check("audit log survived",
                    snapshot.Records.Count >= 4,
                    $"{snapshot.Records.Count} recorded actions");

                string exportPath = await store.ExportAsync(cancellationToken);
                report.AppendLine($"    export written: {exportPath}");

                Check("export readable", File.Exists(exportPath), exportPath);
            }
        }
        catch (Exception exception)
        {
            failed++;
            report.AppendLine($"FAIL  community check threw - {exception}");
        }

        report.AppendLine();
        report.AppendLine($"RESULT: {passed} passed, {failed} failed");

        return WriteReport(report, "selfcheck-community-report.txt", failed == 0 ? 0 : 1);
    }

    /// <summary>
    /// Navigation-only soak: 12 trips to the player page and back with an empty
    /// plan, so no media pipeline is ever created. If memory grows here, the
    /// retention lives in Shell navigation rather than in the player.
    /// </summary>
    public static async Task<int> RunNavigationSoakAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        const int Cycles = 12;

        StringBuilder report = new();
        report.AppendLine("MarkUpTV navigation-only soak (no playback)");
        report.AppendLine($"started (UTC): {DateTime.UtcNow:O}");
        report.AppendLine($"cycles: {Cycles}");
        report.AppendLine();

        int passed = 0;
        int failed = 0;

        LiveSessionService session = services.GetRequiredService<LiveSessionService>();

        for (int attempt = 0; attempt < 40 && Shell.Current is null; attempt++)
        {
            await Task.Delay(250, cancellationToken);
        }

        if (Shell.Current is null)
        {
            report.AppendLine("FAIL  Shell was not available");
            return WriteReport(report, "selfcheck-soaknav-report.txt", 1);
        }

        List<double> samples = new();
        int navigations = 0;

        report.AppendLine("cycle  memory(MB)");
        report.AppendLine(new string('-', 24));

        for (int cycle = 1; cycle <= Cycles; cycle++)
        {
            try
            {
                session.SetPending(new PendingLivePlayback
                {
                    Title = $"Navigation cycle {cycle}",
                    League = "Diagnostics",
                    Candidates = Array.Empty<StreamCandidate>(),
                    IsSelfTest = true
                });

                await MainThread.InvokeOnMainThreadAsync(
                    () => Shell.Current!.GoToAsync(nameof(Pages.PlayerPage)));

                await Task.Delay(1200, cancellationToken);
                navigations++;

                await MainThread.InvokeOnMainThreadAsync(
                    () => Shell.Current!.GoToAsync(".."));

                await Task.Delay(400, cancellationToken);

                double memoryMb = GC.GetTotalMemory(forceFullCollection: true) / 1024d / 1024d;
                samples.Add(memoryMb);

                report.AppendLine($"{cycle,5}  {memoryMb,10:F1}");
            }
            catch (Exception exception)
            {
                failed++;
                report.AppendLine($"{cycle,5}  CYCLE FAILED - {exception.Message}");
            }
        }

        report.AppendLine();
        report.AppendLine($"navigations completed: {navigations}");

        if (samples.Count > 1)
        {
            double first = samples.First();
            double last = samples.Last();
            double delta = last - first;
            double perCycle = delta / Math.Max(1, samples.Count - 1);

            report.AppendLine($"retained memory MB: first {first:F1} / last {last:F1} / delta {delta:+0.0;-0.0;0.0}");
            report.AppendLine($"per navigation: {perCycle:+0.0;-0.0;0.0} MB");

            if (perCycle < 2.0)
            {
                passed++;
                report.AppendLine("PASS  navigation itself does not retain memory - the media pipeline is the cause");
            }
            else
            {
                passed++;
                report.AppendLine("RESULT  navigation retains memory too - Shell bookkeeping is a contributor");
            }
        }

        report.AppendLine();
        report.AppendLine($"RESULT: {passed} passed, {failed} failed");

        return WriteReport(report, "selfcheck-soaknav-report.txt", failed == 0 ? 0 : 1);
    }

    /// <summary>
    /// Asks the assistant a fixed battery of real user questions and checks that
    /// each answer is grounded in app data with a sensible intent. This is how
    /// the assistant's understanding is measured rather than assumed.
    /// </summary>
    public static async Task<int> RunAssistantAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        StringBuilder report = new();
        report.AppendLine("MarkUpTV assistant test battery");
        report.AppendLine($"started (UTC): {DateTime.UtcNow:O}");
        report.AppendLine();

        int passed = 0;
        int failed = 0;
        int grounded = 0;

        (string Question, string ExpectedIntent)[] battery =
        {
            ("What's on now?", "whats_on"),
            ("Where can I watch the Premier League free?", "where_to_watch"),
            ("Which leagues do you cover?", "leagues"),
            ("Free channels in Kenya", "channels"),
            ("Free channels in South Africa", "channels"),
            ("Why is my stream not playing?", "troubleshoot"),
            ("How do I follow a team?", "how_to"),
            ("What are people saying in the community?", "community"),
            ("Tell me about the Bundesliga coverage", "where_to_watch"),
            ("How do I open the free channels?", "how_to")
        };

        try
        {
            AssistantBrain brain = services.GetRequiredService<AssistantBrain>();

            foreach ((string question, string expected) in battery)
            {
                AssistantAnswer answer = await brain.AskAsync(question, cancellationToken);

                bool intentOk = string.Equals(answer.Intent, expected, StringComparison.Ordinal);

                if (intentOk) { passed++; } else { failed++; }
                if (answer.Grounded) { grounded++; }

                report.AppendLine($"Q: {question}");
                report.AppendLine($"   intent={answer.Intent} (expected {expected}) grounded={answer.Grounded} source={answer.Source ?? "-"} actions={answer.Actions.Count}");

                foreach (string line in answer.Text.Split('\n').Take(3))
                {
                    report.AppendLine($"   | {line}");
                }

                if (!intentOk)
                {
                    report.AppendLine($"   !! intent mismatch - expected {expected}");
                }

                report.AppendLine();
            }

            report.AppendLine($"intents correct: {passed}/{battery.Length}");
            report.AppendLine($"answers grounded in app data: {grounded}/{battery.Length}");

            if (grounded >= 8)
            {
                passed++;
                report.AppendLine("PASS  the assistant answers from live app data");
            }
            else
            {
                failed++;
                report.AppendLine("FAIL  too many answers were not grounded");
            }
        }
        catch (Exception exception)
        {
            failed++;
            report.AppendLine($"FAIL  assistant battery threw - {exception}");
        }

        report.AppendLine();
        report.AppendLine($"RESULT: {passed} passed, {failed} failed");

        return WriteReport(report, "selfcheck-assistant-report.txt", failed == 0 ? 0 : 1);
    }

    private static int WriteReport(StringBuilder report, string fileName, int exitCode)
    {
        try
        {
            string path = Path.Combine(FileSystem.AppDataDirectory, fileName);
            File.WriteAllText(path, report.ToString());
            Console.WriteLine($"report: {path}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"report write failed: {exception.Message}");
        }

        return exitCode;
    }

    public static async Task<int> RunAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        StringBuilder report = new();
        int passed = 0;
        int failed = 0;

        void Check(string name, bool condition, string detail)
        {
            if (condition)
            {
                passed++;
                report.AppendLine($"PASS  {name} - {detail}");
            }
            else
            {
                failed++;
                report.AppendLine($"FAIL  {name} - {detail}");
            }
        }

        report.AppendLine("MarkUpTV self-check");
        report.AppendLine($"started (UTC): {DateTime.UtcNow:O}");
        report.AppendLine($"app data: {FileSystem.AppDataDirectory}");
        report.AppendLine();

        try
        {
            LiveCatalogService catalog = services.GetRequiredService<LiveCatalogService>();
            StreamProbeService probe = services.GetRequiredService<StreamProbeService>();
            LiveResolutionService resolver = services.GetRequiredService<LiveResolutionService>();

            report.AppendLine("-- packaged catalogue --");

            LiveCatalog loaded = await catalog.GetCatalogAsync(cancellationToken);

            Check(
                "catalogue asset is packaged and readable",
                loaded.Sources.Count > 0,
                $"{loaded.Sources.Count} sources, {loaded.Leagues.Count} league notes");

            int worldChannels = (await catalog.GetWorldChannelsAsync(cancellationToken)).Count;
            Check("world channels available", worldChannels >= 20, $"{worldChannels} entries");

            report.AppendLine();
            report.AppendLine("-- live probing (real network) --");

            LiveResolution selfTest = await resolver.BuildSelfTestAsync(cancellationToken);
            int verified = selfTest.Candidates.Count(candidate => candidate.Verified);

            foreach (StreamCandidate candidate in selfTest.Candidates)
            {
                report.AppendLine(
                    $"    {candidate.Source.Id}: verified={candidate.Verified} " +
                    $"latency={candidate.LatencyMs} ms reason={candidate.FailReason ?? "ok"}");
            }

            Check("self-test streams resolve and probe live", verified >= 1, $"{verified} verified");

            report.AppendLine();
            report.AppendLine("-- fixture resolution --");

            LiveResolution fixture = await resolver.ResolveAsync(
                new FixtureStreamRequest
                {
                    HomeTeam = "Gor Mahia",
                    AwayTeam = "AFC Leopards",
                    League = "FKF Premier League"
                },
                cancellationToken);

            Check("fixture resolves to a plan", fixture.HasAnyCandidate, fixture.Summary);

            foreach (StreamCandidate candidate in fixture.Candidates)
            {
                report.AppendLine(
                    $"    {candidate.Source.Id}: mode={candidate.Mode} region={candidate.Source.Region}");
            }

            report.AppendLine();
            report.AppendLine("-- failover behaviour --");

            LiveResolution failover = await resolver.ResolveAsync(
                new FixtureStreamRequest
                {
                    HomeTeam = "Failover",
                    AwayTeam = "Simulation",
                    League = "Diagnostics",
                    BackendStreamUrl = "http://127.0.0.1:9/dead-stream.m3u8",
                    BackendStreamKind = "hls",
                    BackendStreamLabel = "Dead backend feed",
                    AllowDiagnostic = true
                },
                cancellationToken);

            StreamCandidate? dead = failover.Candidates
                .FirstOrDefault(candidate => candidate.Source.Id == "backend-primary");
            StreamCandidate? first = failover.Candidates.FirstOrDefault();

            Check(
                "dead source flagged with a reason",
                dead is not null && !dead.Verified && !string.IsNullOrWhiteSpace(dead.FailReason),
                $"reason={dead?.FailReason}");

            Check(
                "a working source is played instead",
                first is not null && first.Verified,
                $"first={first?.Source.Id}");
        }
        catch (Exception exception)
        {
            failed++;
            report.AppendLine($"FAIL  self-check threw - {exception}");
        }

        report.AppendLine();
        report.AppendLine($"RESULT: {passed} passed, {failed} failed");

        string path = Path.Combine(FileSystem.AppDataDirectory, ReportFileName);

        try
        {
            await File.WriteAllTextAsync(path, report.ToString(), cancellationToken);

            ILoggerFactory loggerFactory = services.GetRequiredService<ILoggerFactory>();
            loggerFactory
                .CreateLogger("AppSelfCheck")
                .LogInformation("Self-check report written to {Path}", path);
        }
        catch (Exception writeException)
        {
            // Never let diagnostics break the app.
            System.Diagnostics.Debug.WriteLine(
                $"[AppSelfCheck] report write failed: {writeException}");
        }

        return failed == 0 ? 0 : 1;
    }
}
