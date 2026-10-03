using System.Collections.Generic;
using System.Text.Json.Serialization;
using MarkUptv.Models;

namespace MarkUptv.Services
{
    /// <summary>
    /// High-performance source-generated JSON serializer context container.
    /// Eliminates all reflection-based compilation trimming warnings and ensures 
    /// absolute Native AOT compatibility across all supported target operational platforms.
    /// </summary>
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        GenerationMode = JsonSourceGenerationMode.Metadata)]

    // --- 📺 Core Media Systems Serialization Context Mapping ---
    [JsonSerializable(typeof(TvChannel))]
    [JsonSerializable(typeof(List<TvChannel>))]
    [JsonSerializable(typeof(CategoryResponse))]

    // --- 📰 News Processing Infrastructure Context Mapping ---
    [JsonSerializable(typeof(NewsArticle))]
    [JsonSerializable(typeof(List<NewsArticle>))]
    [JsonSerializable(typeof(CacheEntry<List<NewsArticle>>))]
    [JsonSerializable(typeof(CacheEntry<List<string>>))]

    // --- 💬 Unified Social & Engagement Engine Context Mapping ---
    [JsonSerializable(typeof(SocialPost))]
    [JsonSerializable(typeof(List<SocialPost>))]
    [JsonSerializable(typeof(SocialComment))]
    [JsonSerializable(typeof(List<SocialComment>))]
    [JsonSerializable(typeof(FeedResponse))]
    [JsonSerializable(typeof(LiveChatBubble))]
    [JsonSerializable(typeof(List<LiveChatBubble>))]
    [JsonSerializable(typeof(UserComment))]
    [JsonSerializable(typeof(List<UserComment>))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    public partial class NewsContextContainer : JsonSerializerContext
    {
        // The compilation framework automatically generates high-performance 
        // type metadata injection logic here during compilation routines.
    }
}
