using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MarkUptv.Models;

/// <summary>
/// One film in the free catalogue. Mirrors the backend MovieSummaryDto and
/// adds the presentation helpers the grids bind to.
/// </summary>
public class MovieCatalogItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("posterUrl")]
    public string PosterUrl { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("isForeign")]
    public bool IsForeign { get; set; }

    [JsonPropertyName("collection")]
    public string Collection { get; set; } = string.Empty;

    [JsonPropertyName("downloads")]
    public long Downloads { get; set; }

    [JsonPropertyName("addedUtc")]
    public DateTime? AddedUtc { get; set; }

    [JsonPropertyName("runtimeSeconds")]
    public double? RuntimeSeconds { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long? SizeBytes { get; set; }

    [JsonPropertyName("hasSubtitles")]
    public bool HasSubtitles { get; set; }

    [JsonPropertyName("runtimeText")]
    public string RuntimeText { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("sourcePageUrl")]
    public string SourcePageUrl { get; set; } = string.Empty;

    // ── Presentation ────────────────────────────────────────────────────

    [JsonIgnore]
    public bool HasPoster => !string.IsNullOrWhiteSpace(PosterUrl);

    /// <summary>Release year, or empty so cards never show a "Year n/a" stub.</summary>
    [JsonIgnore]
    public string YearText => Year is > 1880 and < 2100
        ? Year.Value.ToString(CultureInfo.InvariantCulture)
        : string.Empty;

    [JsonIgnore]
    public bool HasYear => YearText.Length > 0;

    /// <summary>Runtime badge text; hidden when the source never published one.</summary>
    [JsonIgnore]
    public string RuntimeLabel => string.IsNullOrWhiteSpace(RuntimeText) ? string.Empty : RuntimeText;

    [JsonIgnore]
    public bool HasRuntime => RuntimeLabel.Length > 0;

    /// <summary>Human-readable file size ("1.2 GB") for download decisions.</summary>
    [JsonIgnore]
    public string SizeText => FormatSize(SizeBytes);

    [JsonIgnore]
    public bool HasSize => SizeText.Length > 0;

    [JsonIgnore]
    public string LanguageBadge => IsForeign && !string.IsNullOrWhiteSpace(Language)
        ? Language
        : string.Empty;

    [JsonIgnore]
    public bool HasLanguageBadge => !string.IsNullOrWhiteSpace(LanguageBadge);

    [JsonIgnore]
    public string DownloadsText => Downloads switch
    {
        >= 1_000_000 => $"{Downloads / 1_000_000.0:0.#}M plays",
        >= 1_000 => $"{Downloads / 1_000.0:0.#}K plays",
        > 0 => $"{Downloads} plays",
        _ => string.Empty
    };

    [JsonIgnore]
    public string AddedText => AddedUtc is null
        ? string.Empty
        : Helpers.RelativeTime.Format(AddedUtc.Value);

    [JsonIgnore]
    public bool HasDownloads => DownloadsText.Length > 0;

    /// <summary>Cards omit empty meta rows instead of showing a dash.</summary>
    [JsonIgnore]
    public bool HasMeta => HasYear || HasSize || HasDownloads || HasLanguageBadge;

    /// <summary>Subtitle/translation cue shown on foreign prints.</summary>
    [JsonIgnore]
    public string TranslationLabel => HasSubtitles
        ? (IsForeign ? "Translated + subtitles" : "Subtitles")
        : (IsForeign ? "Translated" : string.Empty);

    [JsonIgnore]
    public bool HasTranslationLabel => TranslationLabel.Length > 0;

    /// <summary>Two-line synopsis that never overflows a card.</summary>
    [JsonIgnore]
    public string Synopsis
    {
        get
        {
            string text = Description?.Trim() ?? string.Empty;
            return text.Length <= 180 ? text : text[..180] + "…";
        }
    }

    /// <summary>Download state, pushed in by the download service.</summary>
    [JsonIgnore]
    public MovieDownloadState DownloadState { get; set; } = MovieDownloadState.None;

    [JsonIgnore]
    public double DownloadProgress { get; set; }

    [JsonIgnore]
    public bool IsDownloaded => DownloadState == MovieDownloadState.Completed;

    [JsonIgnore]
    public bool IsDownloading => DownloadState == MovieDownloadState.Downloading;

    [JsonIgnore]
    public bool IsQueued => DownloadState == MovieDownloadState.Queued;

    [JsonIgnore]
    public string DownloadBadge => DownloadState switch
    {
        MovieDownloadState.Downloading => $"Downloading {DownloadProgress:P0}",
        MovieDownloadState.Queued => "Queued",
        MovieDownloadState.Completed => "Saved offline",
        _ => string.Empty
    };

    [JsonIgnore]
    public bool HasDownloadBadge => DownloadBadge.Length > 0;

    public static string FormatSize(long? bytes)
    {
        if (bytes is null or <= 0)
        {
            return string.Empty;
        }

        double value = bytes.Value;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}

/// <summary>Full detail for one film, including the playable file.</summary>
public sealed class MovieDetail : MovieCatalogItem
{
    [JsonPropertyName("streamUrl")]
    public string StreamUrl { get; set; } = string.Empty;

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("creator")]
    public string Creator { get; set; } = string.Empty;

    [JsonPropertyName("subtitles")]
    public List<MovieSubtitle> Subtitles { get; set; } = new();

    [JsonPropertyName("languages")]
    public List<string> Languages { get; set; } = new();

    [JsonPropertyName("variants")]
    public List<MovieVariant> Variants { get; set; } = new();

    /// <summary>
    /// Set by the server when this device holds no day pass: the playable and
    /// downloadable links are withheld and the page shows the offer instead.
    /// </summary>
    [JsonPropertyName("requiresPass")]
    public bool RequiresPass { get; set; }

    [JsonPropertyName("passPrice")]
    public decimal PassPrice { get; set; }

    [JsonPropertyName("passCurrency")]
    public string PassCurrency { get; set; } = "UGX";

    [JsonPropertyName("passHours")]
    public int PassHours { get; set; } = 24;

    /// <summary>"1,000 UGX" — exactly what the gateway will charge.</summary>
    [JsonIgnore]
    public string PassPriceText => PassPrice > 0
        ? $"{PassPrice:N0} {PassCurrency}"
        : "1,000 UGX";

    /// <summary>"24 hours" badge for the offer card.</summary>
    [JsonIgnore]
    public string PassDurationText => PassHours switch
    {
        24 => "24 hours",
        1 => "1 hour",
        < 24 => $"{PassHours} hours",
        _ => $"{PassHours / 24} day{(PassHours / 24 == 1 ? string.Empty : "s")}"
    };

    [JsonIgnore]
    public bool CanPlay => !string.IsNullOrWhiteSpace(StreamUrl);

    [JsonIgnore]
    public bool CanDownload => !string.IsNullOrWhiteSpace(DownloadUrl);

    [JsonIgnore]
    public bool HasSubtitlesList => Subtitles.Count > 0;

    [JsonIgnore]
    public string CreatorText => string.IsNullOrWhiteSpace(Creator) ? string.Empty : $"by {Creator}";
}

/// <summary>A subtitle sidecar published next to the film.</summary>
public sealed class MovieSubtitle
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(Language)
        ? Name
        : $"{Language} · {Format.ToUpperInvariant()}";
}

/// <summary>An alternative rendition of the same film.</summary>
public sealed class MovieVariant
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long? SizeBytes { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonIgnore]
    public string Label
    {
        get
        {
            string quality = Height is > 0 ? $"{Height}p" : Format;
            string size = MovieCatalogItem.FormatSize(SizeBytes);
            return size.Length > 0 ? $"{quality} · {size}" : quality;
        }
    }
}

/// <summary>Paged catalogue response.</summary>
public sealed class MovieSearchResult
{
    [JsonPropertyName("items")]
    public List<MovieCatalogItem> Items { get; set; } = new();

    [JsonPropertyName("page")]
    public int Page { get; set; } = 1;

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }

    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("sort")]
    public string Sort { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
}

/// <summary>A language shelf offered by the catalogue.</summary>
public sealed partial class MovieLanguage : ObservableObject
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("translated")]
    public bool Translated { get; set; }

    /// <summary>Drives the chip highlight so a tap gives instant feedback.</summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>One shelf chip on the Movies screen.</summary>
public sealed partial class ShelfOption : ObservableObject
{
    public ShelfOption(string name, string label)
    {
        Name = name;
        Label = label;
    }

    public string Name { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Lifecycle of one offline download.</summary>
public enum MovieDownloadState
{
    None = 0,
    Queued = 1,
    Downloading = 2,
    Completed = 3,
    Failed = 4
}

/// <summary>
/// One film held in the app's own offline library. Persisted as JSON next to
/// the media file so the library survives restarts and needs no database.
/// </summary>
public sealed class MovieDownloadRecord
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int? Year { get; set; }

    public string PosterUrl { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string RuntimeText { get; set; } = string.Empty;

    public string SourcePageUrl { get; set; } = string.Empty;

    /// <summary>Absolute path inside the app sandbox.</summary>
    public string LocalPath { get; set; } = string.Empty;

    public string RemoteUrl { get; set; } = string.Empty;

    /// <summary>Total size of the finished file, when known.</summary>
    public long SizeBytes { get; set; }

    public DateTime DownloadedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Sidecars saved alongside the film.</summary>
    public List<string> SubtitlePaths { get; set; } = new();

    [JsonIgnore]
    public string SizeText => MovieCatalogItem.FormatSize(SizeBytes);

    [JsonIgnore]
    public string SavedText => $"Saved {Helpers.RelativeTime.Format(DownloadedUtc)}";

    [JsonIgnore]
    public string YearText => Year is > 1880 and < 2100 ? Year.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

    [JsonIgnore]
    public string FileName => string.IsNullOrEmpty(LocalPath) ? string.Empty : System.IO.Path.GetFileName(LocalPath);
}
