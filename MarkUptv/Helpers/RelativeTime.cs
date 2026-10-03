using System;

namespace MarkUptv.Helpers;

/// <summary>
/// One implementation of "how long ago" for the whole app.
///
/// The previous per-model copies each had the same two defects:
///   * a negative delta (device clock a few seconds behind the server, or a
///     just-created item) rendered as "-6s ago";
///   * the seconds branch printed <c>TimeSpan.Seconds</c>, the component, not
///     the elapsed seconds.
///
/// Timestamps arrive as UTC (the API emits ISO-8601 with the "Z" suffix).
/// Inputs are normalized so a value already tagged UTC is never shifted again.
/// </summary>
public static class RelativeTime
{
    public static string Format(DateTime timestamp)
    {
        if (timestamp == default)
        {
            return string.Empty;
        }

        DateTime utc = timestamp.Kind switch
        {
            DateTimeKind.Utc => timestamp,
            DateTimeKind.Local => timestamp.ToUniversalTime(),
            // Unspecified: the servers write UTC, so treat it as UTC rather
            // than letting the platform reinterpret it as local time.
            _ => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
        };

        TimeSpan delta = DateTime.UtcNow - utc;

        // Clock skew: a timestamp a second or two "in the future" is still
        // "just now", never a negative duration.
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalSeconds < 45)
        {
            return "just now";
        }

        if (delta.TotalMinutes < 60)
        {
            return $"{(int)delta.TotalMinutes} min ago";
        }

        if (delta.TotalHours < 24)
        {
            return $"{(int)delta.TotalHours}h ago";
        }

        if (delta.TotalDays < 7)
        {
            return $"{(int)delta.TotalDays}d ago";
        }

        return utc.ToLocalTime().ToString("MMM d");
    }
}
