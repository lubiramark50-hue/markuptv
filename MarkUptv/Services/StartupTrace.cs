using System;
using System.Diagnostics;
using System.IO;

namespace MarkUptv.Services;

/// <summary>
/// Cold-start timeline. Every mark is appended to the same startup_log.txt the
/// App lifecycle writes, stamped with the milliseconds elapsed since this type
/// was first touched (i.e. the first line of <c>CreateMauiApp</c>). One device
/// run then shows exactly where startup time goes: runtime/assembly init,
/// service graph, shell inflation, page inflation, first frame.
/// </summary>
internal static class StartupTrace
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Gate = new();

    private static string? _path;

    /// <summary>Appends one timestamped mark to the startup log (never throws).</summary>
    public static void Mark(string message)
    {
        long ms = Clock.ElapsedMilliseconds;

#if ANDROID
        try
        {
            Android.Util.Log.Info("MarkUpTV.Startup", $"[+{ms} ms] {message}");
        }
        catch
        {
            // Logging must never break startup.
        }
#endif

        string line =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - [+{ms} ms] [T{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}";

        Debug.WriteLine(line);

        lock (Gate)
        {
            try
            {
                File.AppendAllText(ResolvePath(), line);
            }
            catch
            {
                // Best effort only.
            }
        }
    }

    private static string ResolvePath()
    {
        if (_path is not null)
        {
            return _path;
        }

        try
        {
            _path = Path.Combine(FileSystem.AppDataDirectory, "startup_log.txt");
        }
        catch
        {
            _path = Path.Combine(Path.GetTempPath(), "startup_log.txt");
        }

        return _path;
    }
}
