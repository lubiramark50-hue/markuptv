using System.Text.Json.Serialization;

namespace MarkUptv.Models;

/// <summary>
/// Represents one EPG programme returned by the MarkUpTV backend.
/// </summary>
public sealed class TvProgramme
{
    public int Id { get; set; }

    public int ChannelId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    [JsonIgnore]
    public bool IsCurrentlyPlaying
    {
        get
        {
            DateTime nowUtc = DateTime.UtcNow;

            return StartUtc <= nowUtc &&
                   EndUtc > nowUtc;
        }
    }

    [JsonIgnore]
    public TimeSpan Duration =>
        EndUtc > StartUtc
            ? EndUtc - StartUtc
            : TimeSpan.Zero;
}