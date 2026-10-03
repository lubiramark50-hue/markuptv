using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MarkUptv.Models;

public class LiveMatch : INotifyPropertyChanged
{
    private string? _countdownText;
    private string? _dayLabel;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public int Id { get; set; }

    public string HomeTeam { get; set; } = string.Empty;

    public string AwayTeam { get; set; } = string.Empty;

    /// <summary>
    /// The backend /api/football/live payload names this field "kickoff";
    /// the attribute keeps JSON case-insensitive matching from missing it.
    /// </summary>
    [JsonPropertyName("kickoff")]
    public DateTime KickoffUtc { get; set; }

    public string Status { get; set; } = string.Empty;

    /// <summary>Competition this match belongs to, e.g. "English Premier League".</summary>
    public string? League { get; set; }

    public string? Score { get; set; }

    public string? Minute { get; set; }

    public int? ChannelId { get; set; }

    /// <summary>A playable stream URL (IPTV channel or a free YouTube live broadcast).</summary>
    public string? StreamUrl { get; set; }

    /// <summary>"iptv" (native player) or "youtube" (WebView embed).</summary>
    public string? StreamKind { get; set; }

    /// <summary>Human-friendly stream source label.</summary>
    public string? StreamLabel { get; set; }

    /// <summary>Embeddable URL for YouTube streams.</summary>
    public string? StreamEmbedUrl { get; set; }

    /// <summary>
    /// Ticking kick-off countdown ("in 2h 05m", "in 40m"). Set by the page's
    /// countdown timer while the football board is on screen.
    /// </summary>
    public string? CountdownText
    {
        get => _countdownText;
        set
        {
            if (_countdownText == value)
            {
                return;
            }

            _countdownText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCountdown));
        }
    }

    /// <summary>"TODAY", "TOMORROW" or "ddd dd MMM" for the fixture list.</summary>
    public string? DayLabel
    {
        get => _dayLabel;
        set
        {
            if (_dayLabel == value)
            {
                return;
            }

            _dayLabel = value;
            OnPropertyChanged();
        }
    }

    public bool HasCountdown => !string.IsNullOrWhiteSpace(CountdownText);

    public bool HasStream => !string.IsNullOrWhiteSpace(StreamUrl);

    /// <summary>Free-coverage hint for the tile, set when the board loads.</summary>
    public string? CoverageLabel { get; set; }

    /// <summary>False only when the resolver found no candidate at all.</summary>
    public bool HasFreeCoverage { get; set; } = true;

    public bool IsLive =>
        Status.Equals("LIVE", StringComparison.OrdinalIgnoreCase) ||
        Status.Equals("HALFTIME", StringComparison.OrdinalIgnoreCase) ||
        Status.Equals("SUSPENDED", StringComparison.OrdinalIgnoreCase);

    public string StatusDisplay => IsLive ? "🔴 LIVE" : Status;

    public string KickoffDisplay =>
        KickoffUtc == default
            ? string.Empty
            : KickoffUtc.ToLocalTime().ToString("ddd HH:mm");

    /// <summary>In-match clock: "67'", "HT", or empty when not underway.</summary>
    public string MinuteDisplay =>
        string.IsNullOrWhiteSpace(Minute)
            ? string.Empty
            : string.Equals(Minute, "HT", StringComparison.OrdinalIgnoreCase)
                ? "HT"
                : $"{Minute}'";

    /// <summary>Scoreboard label; a dash while the match has not kicked off.</summary>
    public string ScoreDisplay =>
        string.IsNullOrWhiteSpace(Score) ? "–" : Score;

    /// <summary>Live clock while playing, otherwise the kick-off time.</summary>
    public string ClockDisplay =>
        IsLive && !string.IsNullOrWhiteSpace(MinuteDisplay)
            ? MinuteDisplay
            : KickoffDisplay;
}