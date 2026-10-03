using System;
using System.Text.Json.Serialization;

namespace MarkUptv.Models;

public class UserComment
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("userProfileImage")]
    public string UserProfileImage { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("isVerified")]
    public bool IsVerified { get; set; }

    [JsonIgnore]
    public string TimeAgo => Helpers.RelativeTime.Format(Timestamp);

    [JsonIgnore]
    public string Initials => Username.Length > 0 ? Username[0].ToString().ToUpper() : "?";

    // Formatting now lives in Helpers/RelativeTime so every surface agrees
    // on the same wording, seconds boundary and clock-skew handling.
}