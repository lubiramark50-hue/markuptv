namespace MarkUptv.Models;

/// <summary>
/// Explicitly maps network payload types to static integer positions.
/// </summary>
public enum SearchResultType
{
    Unknown = 0,
    Channel,
    Article,
    Video,
    Podcast,
    Image,
    Web,
    Link
}