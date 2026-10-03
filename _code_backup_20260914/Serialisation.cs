using System.Text.Json.Serialization;
using MarkUptv.Models;

namespace MarkUptv.Serialization;

/// <summary>
/// Trimming-safe JSON metadata for the MarkUptv MAUI application.
/// </summary>
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(
    typeof(CategoryResponse),
    TypeInfoPropertyName = "CategoryResponse")]
[JsonSerializable(
    typeof(TvChannel),
    TypeInfoPropertyName = "TvChannel")]
[JsonSerializable(
    typeof(List<TvChannel>),
    TypeInfoPropertyName = "TvChannelList")]
[JsonSerializable(
    typeof(TvProgramme),
    TypeInfoPropertyName = "TvProgramme")]
[JsonSerializable(
    typeof(List<TvProgramme>),
    TypeInfoPropertyName = "TvProgrammeList")]
[JsonSerializable(
    typeof(ProgrammeResponse),
    TypeInfoPropertyName = "ProgrammeResponse")]
[JsonSerializable(
    typeof(FailedStreamReport),
    TypeInfoPropertyName = "FailedStreamReport")]
[JsonSerializable(
    typeof(ScrapedStream),
    TypeInfoPropertyName = "ScrapedStream")]
[JsonSerializable(
    typeof(LiveMatch),
    TypeInfoPropertyName = "LiveMatch")]
[JsonSerializable(
    typeof(List<LiveMatch>),
    TypeInfoPropertyName = "LiveMatchList")]
public partial class MarkUptvJsonContext : JsonSerializerContext
{
}