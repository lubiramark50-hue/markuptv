using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MarkUptv.Models;

/// <summary>
/// Represents a playable television channel enriched with EPG information.
/// </summary>
public sealed class TvChannel : INotifyPropertyChanged
{
    private bool _isPlaying;

    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? Logo { get; set; }

    public string? Initials { get; set; }

    public string? Group { get; set; }

    public string Category { get; set; } = string.Empty;

    public string? QualityLabel { get; set; }

    public string? CurrentProgrammeTitle { get; set; }

    public DateTime? CurrentProgrammeStartUtc { get; set; }

    public DateTime? CurrentProgrammeEndUtc { get; set; }

    public string? NextProgrammeTitle { get; set; }

    public DateTime? NextProgrammeStartUtc { get; set; }

    public TvProgramme? CurrentProgramme { get; set; }

    public TvProgramme? NextProgramme { get; set; }

    [JsonIgnore]
    public string StreamUrl => Url;

    [JsonIgnore]
    public string? LogoUrl => Logo;

    [JsonIgnore]
    public bool HasCurrentProgramme =>
        CurrentProgramme is not null ||
        !string.IsNullOrWhiteSpace(CurrentProgrammeTitle);

    [JsonIgnore]
    public bool HasNextProgramme =>
        NextProgramme is not null ||
        !string.IsNullOrWhiteSpace(NextProgrammeTitle);

    [JsonIgnore]
    public bool IsCurrentlyLive
    {
        get
        {
            if (CurrentProgrammeStartUtc is not DateTime startUtc ||
                CurrentProgrammeEndUtc is not DateTime endUtc)
            {
                return false;
            }

            DateTime nowUtc = DateTime.UtcNow;

            return startUtc <= nowUtc &&
                   endUtc > nowUtc;
        }
    }

    /// <summary>
    /// Current programme progress between 0 and 1.
    /// </summary>
    [JsonIgnore]
    public double ProgrammeProgress
    {
        get
        {
            if (CurrentProgrammeStartUtc is not DateTime startUtc ||
                CurrentProgrammeEndUtc is not DateTime endUtc ||
                endUtc <= startUtc)
            {
                return 0;
            }

            double totalSeconds =
                (endUtc - startUtc).TotalSeconds;

            double elapsedSeconds =
                (DateTime.UtcNow - startUtc).TotalSeconds;

            return Math.Clamp(
                elapsedSeconds / totalSeconds,
                0,
                1);
        }
    }

    /// <summary>
    /// True when this channel is the one currently selected for playback.
    /// Updated by the view model as playback moves between channels.
    /// </summary>
    [JsonIgnore]
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value)
            {
                return;
            }

            _isPlaying = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}