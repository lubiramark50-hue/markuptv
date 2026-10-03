namespace MarkUptv.Helpers;

/// <summary>
/// Builds a minimal full-screen HTML page with a YouTube iframe embed
/// suitable for assignment to a MAUI <see cref="Microsoft.Maui.Controls.WebView"/>
/// via <see cref="Microsoft.Maui.Controls.HtmlWebViewSource"/>.
/// </summary>
public static class YouTubeEmbedHelper
{
    private const string EmbedTemplate = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport"
                  content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no" />
            <style>
                * { margin: 0; padding: 0; box-sizing: border-box; }
                html, body { width: 100%; height: 100%; overflow: hidden; background: #0A0A0A; }
                iframe {
                    position: absolute;
                    top: 0; left: 0;
                    width: 100%; height: 100%;
                    border: none;
                }
            </style>
        </head>
        <body>
            <iframe
                src="{0}"
                allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
                referrerpolicy="strict-origin-when-cross-origin"
                allowfullscreen>
            </iframe>
        </body>
        </html>
        """;

    /// <summary>
    /// Builds an <see cref="HtmlWebViewSource"/> containing a full-screen
    /// YouTube iframe embed for the given video ID.
    /// </summary>
    public static HtmlWebViewSource BuildFromVideoId(string videoId)
    {
        string embedUrl =
            $"https://www.youtube.com/embed/{Uri.EscapeDataString(videoId)}" +
            "?autoplay=1&playsinline=1&modestbranding=1&rel=0&controls=1";

        return new HtmlWebViewSource
        {
            Html = string.Format(EmbedTemplate, embedUrl)
        };
    }

    /// <summary>
    /// Builds an <see cref="HtmlWebViewSource"/> from a full YouTube embed URL
    /// (e.g. <c>https://www.youtube.com/embed/abc123?autoplay=1</c>).
    /// </summary>
    public static HtmlWebViewSource BuildFromEmbedUrl(string embedUrl)
    {
        return new HtmlWebViewSource
        {
            Html = string.Format(EmbedTemplate, embedUrl)
        };
    }

    /// <summary>
    /// Returns <c>true</c> when the URL looks like a YouTube embed URL
    /// (contains <c>youtube.com/embed/</c>).
    /// </summary>
    public static bool IsYouTubeEmbedUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) &&
               url.Contains("youtube.com/embed/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns <c>true</c> when the URL points to any YouTube page
    /// (watch, channel, embed, live, etc.).
    /// </summary>
    public static bool IsYouTubeUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) &&
               (url.Contains("youtube.com/", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase));
    }
}
