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
    typeof(MovieCatalogItem),
    TypeInfoPropertyName = "MovieCatalogItem")]
[JsonSerializable(
    typeof(List<MovieCatalogItem>),
    TypeInfoPropertyName = "MovieCatalogItemList")]
[JsonSerializable(
    typeof(MovieDetail),
    TypeInfoPropertyName = "MovieDetail")]
[JsonSerializable(
    typeof(MovieSearchResult),
    TypeInfoPropertyName = "MovieSearchResult")]
[JsonSerializable(
    typeof(MovieLanguage),
    TypeInfoPropertyName = "MovieLanguage")]
[JsonSerializable(
    typeof(List<MovieLanguage>),
    TypeInfoPropertyName = "MovieLanguageList")]
[JsonSerializable(
    typeof(MovieDownloadRecord),
    TypeInfoPropertyName = "MovieDownloadRecord")]
[JsonSerializable(
    typeof(List<MovieDownloadRecord>),
    TypeInfoPropertyName = "MovieDownloadRecordList")]
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
[JsonSerializable(
    typeof(LiveSource),
    TypeInfoPropertyName = "LiveSource")]
[JsonSerializable(
    typeof(List<LiveSource>),
    TypeInfoPropertyName = "LiveSourceList")]
[JsonSerializable(
    typeof(LiveCatalog),
    TypeInfoPropertyName = "LiveCatalog")]
[JsonSerializable(
    typeof(LeagueCoverage),
    TypeInfoPropertyName = "LeagueCoverage")]
[JsonSerializable(
    typeof(List<LeagueCoverage>),
    TypeInfoPropertyName = "LeagueCoverageList")]
[JsonSerializable(
    typeof(FixtureStreamRequest),
    TypeInfoPropertyName = "FixtureStreamRequest")]
[JsonSerializable(
    typeof(StreamCandidate),
    TypeInfoPropertyName = "StreamCandidate")]
[JsonSerializable(
    typeof(List<StreamCandidate>),
    TypeInfoPropertyName = "ListStreamCandidate")]
[JsonSerializable(
    typeof(LiveResolution),
    TypeInfoPropertyName = "LiveResolution")][JsonSerializable(
    typeof(MatchThread),
    TypeInfoPropertyName = "MatchThread")]
[JsonSerializable(
    typeof(List<MatchThread>),
    TypeInfoPropertyName = "ListMatchThread")]
[JsonSerializable(
    typeof(ThreadPost),
    TypeInfoPropertyName = "ThreadPost")]
[JsonSerializable(
    typeof(List<ThreadPost>),
    TypeInfoPropertyName = "ListThreadPost")]
[JsonSerializable(
    typeof(ThreadComment),
    TypeInfoPropertyName = "ThreadComment")]
[JsonSerializable(
    typeof(CommunityReport),
    TypeInfoPropertyName = "CommunityReport")]
[JsonSerializable(
    typeof(List<CommunityReport>),
    TypeInfoPropertyName = "ListCommunityReport")]
[JsonSerializable(
    typeof(CommunityRecord),
    TypeInfoPropertyName = "CommunityRecord")]
[JsonSerializable(
    typeof(List<CommunityRecord>),
    TypeInfoPropertyName = "ListCommunityRecord")]
[JsonSerializable(
    typeof(List<string>),
    TypeInfoPropertyName = "ListString")]
[JsonSerializable(
    typeof(CommunitySnapshot),
    TypeInfoPropertyName = "CommunitySnapshot")]
public partial class MarkUptvJsonContext : JsonSerializerContext
{
}