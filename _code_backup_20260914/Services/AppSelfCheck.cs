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

        try
        {
            LiveResolutionService resolver = services.GetRequiredService<LiveResolutionService>();
            LiveResolution plan = await resolver.BuildSelfTestAsync(cancellationToken);

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

        string route =
            "///PlayerPage?title=" + Uri.EscapeDataString("Playback self-check") +
            "&candidates=" + Uri.EscapeDataString(candidatesJson);

        try
        {
            await MainThread.InvokeOnMainThreadAsync(
                () => Shell.Current!.GoToAsync(route));
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

    public static async Task<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
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
