using System;
using System.Text.Json.Serialization;

namespace MarkUptv.Models;

/// <summary>
/// A high-performance, immutable, Native AOT-ready Data Transfer Object (DTO) for global search payloads.
/// </summary>
public sealed class SearchResult
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("thumbnailUrl")]
    public string? ThumbnailUrl { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Swapped string-based serialization for a source-generated, case-insensitive string-to-enum mapper.
    /// This completely avoids string overhead and allocation during high-throughput queries.
    /// </summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter<SearchResultType>))]
    public SearchResultType Type { get; init; } = SearchResultType.Unknown;
}