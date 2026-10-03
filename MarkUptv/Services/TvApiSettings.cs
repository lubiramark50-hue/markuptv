namespace MarkUptv.Services;

/// <summary>
/// Configuration for communicating with the MarkUpTV backend.
/// </summary>
public sealed class TvApiSettings
{
    /// <summary>
    /// Root address of the backend.
    /// Example: https://localhost:7099/
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Route used to retrieve channels.
    /// Change this to match the actual backend controller route.
    /// </summary>
    public string ChannelsPathTemplate { get; set; } =
        "api/channels/category/{category}";

    /// <summary>
    /// Route used to retrieve programmes for one channel.
    /// Change this to match the actual EPG controller route.
    /// </summary>
    public string ProgrammesPathTemplate { get; set; } =
        "api/epg/channels/{channelId}/programmes";

    /// <summary>
    /// Route used to report dead stream URLs.
    /// </summary>
    public string ReportFailurePath { get; set; } =
        "api/channels/report-failure";

    /// <summary>
    /// Route used to self-heal a channel: extracts a fresh stream from the
    /// channel's web-player source. {id} is the channel identifier.
    /// </summary>
    public string RepairChannelPathTemplate { get; set; } =
        "api/channels/{id}/repair";

    /// <summary>
    /// Enables programme enrichment after channels are loaded.
    /// </summary>
    public bool EnableEpg { get; set; } = true;

    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Number of retries after the initial request.
    /// </summary>
    public int RetryCount { get; set; } = 2;

    public int EpgLookBehindMinutes { get; set; } = 30;

    public int EpgLookAheadHours { get; set; } = 12;

    public int MaxConcurrentEpgRequests { get; set; } = 4;

    /// <summary>
    /// Upper bound on how many channels get their programmes pre-fetched when a
    /// category is opened. Large categories ("news" returns 700+ channels) would
    /// otherwise fire one request per channel, exceed the request timeout and
    /// leave the page looking empty. Only the channels near the top of the list
    /// - the ones the user actually sees first - are enriched up front.
    /// </summary>
    public int MaxEpgPrefetchChannels { get; set; } = 24;
}