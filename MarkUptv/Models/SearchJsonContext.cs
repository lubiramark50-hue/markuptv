using System.Collections.Generic;
using System.Text.Json.Serialization;
using MarkUptv.Models;

namespace MarkUptv.Services;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(List<SearchResult>))]
public partial class SearchJsonContext : JsonSerializerContext

{
}