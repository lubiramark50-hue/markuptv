using System;
using System.Text.Json.Serialization;

namespace MarkUptv.Models;

public class ContentItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = string.Empty; // "video", "channel", "post", etc.

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("thumbnailUrl")]
    public string ThumbnailUrl { get; set; } = string.Empty;

    [JsonPropertyName("videoUrl")]
    public string VideoUrl { get; set; } = string.Empty;

    [JsonPropertyName("authorName")]
    public string AuthorName { get; set; } = string.Empty;

    [JsonPropertyName("authorInitials")]
    public string AuthorInitials { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("likeCount")]
    public int LikeCount { get; set; }

    [JsonPropertyName("commentCount")]
    public int CommentCount { get; set; }

    [JsonPropertyName("isLiked")]
    public bool IsLiked { get; set; }

    [JsonIgnore]
    public bool HasMedia => !string.IsNullOrEmpty(ThumbnailUrl) || !string.IsNullOrEmpty(VideoUrl);

    [JsonIgnore]
    public string TimeAgo => Helpers.RelativeTime.Format(Timestamp);
}