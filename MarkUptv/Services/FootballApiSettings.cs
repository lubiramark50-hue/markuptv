namespace MarkUptv.Services;

/// <summary>
/// Routes used by the football API client.
/// </summary>
public sealed class FootballApiSettings
{
    public string ChannelsPath { get; set; } =
        "api/football";

    public string LiveMatchesPath { get; set; } =
        "api/football/live";

    public int TimeoutSeconds { get; set; } = 30;
}