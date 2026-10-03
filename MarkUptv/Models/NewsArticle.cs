using System;

namespace MarkUptv.Models;

public class NewsArticle
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? Source { get; set; }
    public DateTime PublishedAt { get; set; }
    public string Category { get; set; } = "all";

    // NEW CUTTING-EDGE PROPERTIES
    public string? VideoUrl { get; set; }
    public string MediaType { get; set; } = "Article"; // "Article" or "Video"

    public string TimeAgo
    {
        get
        {
            var diff = DateTime.UtcNow - PublishedAt.ToUniversalTime();
            if (diff.TotalSeconds < 60) return "just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
            return PublishedAt.ToString("MMM d");
        }
    }
}
