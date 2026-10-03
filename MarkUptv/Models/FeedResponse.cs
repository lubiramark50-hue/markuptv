using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MarkUptv.Models;

public class FeedResponse
{
    [JsonPropertyName("items")]
    public List<ContentItem> Items { get; set; } = new();

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }
}