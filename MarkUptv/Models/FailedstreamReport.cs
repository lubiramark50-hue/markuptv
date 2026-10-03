namespace MarkUptv.Models;

/// <summary>
/// Request sent to the backend when a channel stream fails.
/// </summary>
public sealed class FailedStreamReport
{
    public string Url { get; set; } = string.Empty;
}