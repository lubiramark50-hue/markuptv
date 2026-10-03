using System.Collections.Generic;
using System.Threading;

namespace MarkUptv.Helpers;

/// <summary>
/// Bridge between the view models and the platform media handlers.
/// A page sets the headers the CURRENT stream needs BEFORE assigning
/// MediaElement.Source; every platform handler reads them the moment the
/// native source is (re)configured and re-attaches them to manifest AND
/// segment requests.
/// </summary>
public static class StreamHeaderProvider
{
    private static readonly object Gate = new();
    private static string? _url;
    private static string? _referer;
    private static string? _userAgent;
    private static IReadOnlyDictionary<string, string> _headers =
        new Dictionary<string, string>();

    /// <summary>
    /// Called with the values returned by the repair/extract API. The URL is
    /// stored explicitly (rather than parsed from MediaElement.Source) so the
    /// platform handlers always know which stream the headers belong to.
    /// </summary>
    public static void Set(
        string? url,
        string? referer,
        string? userAgent,
        IReadOnlyDictionary<string, string>? headers)
    {
        lock (Gate)
        {
            _url = string.IsNullOrWhiteSpace(url) ? null : url;
            _referer = string.IsNullOrWhiteSpace(referer) ? null : referer;
            _userAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent;
            _headers = headers ?? new Dictionary<string, string>();
        }
    }

    /// <summary>Clears any stale headers when a plain IPTV channel starts.</summary>
    public static void Clear()
        => Set(null, null, null, null);

    public static (string? Url, string? Referer, string? UserAgent, IReadOnlyDictionary<string, string> Headers) Snapshot()
    {
        lock (Gate)
        {
            return (_url, _referer, _userAgent, _headers);
        }
    }
}
