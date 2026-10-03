using System.Collections.Generic;

namespace MarkUptv.Models;

/// <summary>
/// A stream resolved by the backend extraction pipeline
/// (/api/stream/extract, /api/channels/{id}/repair). Mirrors the server's
/// ScrapedStreamResult (JSON is case-insensitively matched by the context).
/// </summary>
public class ScrapedStream
{
    public string StreamUrl { get; set; } = string.Empty;

    /// <summary>"hls" | "dash" | "direct"</summary>
    public string Kind { get; set; } = "hls";

    /// <summary>1 = raw-HTML regex, 2 = yt-dlp, 3 = headless-browser sniffer.</summary>
    public int SourceLevel { get; set; }

    public string? PageUrl { get; set; }

    /// <summary>Referer the CDN expects (usually the page the stream came from).</summary>
    public string? Referer { get; set; }

    public string? UserAgent { get; set; }

    public Dictionary<string, string> Headers { get; set; } = new();

    public bool HasUrl => !string.IsNullOrWhiteSpace(StreamUrl);
}
