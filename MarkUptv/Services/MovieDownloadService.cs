using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MarkUptv.Models;
using MarkUptv.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace MarkUptv.Services;

/// <summary>Progress pulse raised while a film is being saved.</summary>
public sealed class MovieDownloadProgress
{
    public string MovieId { get; init; } = string.Empty;

    public MovieDownloadState State { get; init; }

    /// <summary>0..1 of the whole file.</summary>
    public double Fraction { get; init; }

    public long BytesReceived { get; init; }

    public long? TotalBytes { get; init; }

    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Owns the app's offline movie library: downloads archive MP4s into the
/// application sandbox, resumes interrupted transfers, saves subtitle
/// sidecars, and persists an index so the library survives restarts.
///
/// Nothing here touches the user's shared storage: films live inside the app
/// (FileSystem.AppDataDirectory), which is exactly the "download inside the
/// application" behaviour the Movies screen promises.
/// </summary>
public sealed class MovieDownloadService : IDisposable
{
    private const string IndexFileName = "movie_downloads.json";
    private const string MediaFolderName = "movies";
    private const int BufferSize = 131_072;

    private readonly HttpClient _http;
    private readonly ILogger<MovieDownloadService> _logger;

    /// <summary>One transfer at a time — this is a phone, not a data centre.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly object _sync = new();
    private readonly Dictionary<string, MovieDownloadState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _fractions = new(StringComparer.OrdinalIgnoreCase);

    private List<MovieDownloadRecord>? _records;
    private bool _disposed;

    public MovieDownloadService(HttpClient http, ILogger<MovieDownloadService> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Raised on the calling thread whenever a transfer changes state.</summary>
    public event EventHandler<MovieDownloadProgress>? ProgressChanged;

    /// <summary>Raised after a film is added to or removed from the library.</summary>
    public event EventHandler? LibraryChanged;

    // ════════════════════════════════════════════════════════════════════
    // LIBRARY
    // ════════════════════════════════════════════════════════════════════

    public string MediaDirectory
    {
        get
        {
            string path = Path.Combine(FileSystem.AppDataDirectory, MediaFolderName);
            Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <summary>Every finished film, newest first.</summary>
    public IReadOnlyList<MovieDownloadRecord> GetDownloads()
    {
        lock (_sync)
        {
            return (_records ??= LoadIndex())
                .Where(record => File.Exists(record.LocalPath))
                .OrderByDescending(record => record.DownloadedUtc)
                .ToList();
        }
    }

    public MovieDownloadRecord? GetRecord(string movieId)
        => GetDownloads().FirstOrDefault(record =>
            string.Equals(record.Id, movieId, StringComparison.OrdinalIgnoreCase));

    public bool IsDownloaded(string movieId) => GetRecord(movieId) is not null;

    /// <summary>Absolute path to the offline copy, or null when not saved.</summary>
    public string? GetLocalPath(string movieId) => GetRecord(movieId)?.LocalPath;

    public MovieDownloadState GetState(string movieId)
    {
        lock (_sync)
        {
            if (IsDownloaded(movieId))
            {
                return MovieDownloadState.Completed;
            }

            return _states.TryGetValue(movieId, out MovieDownloadState state)
                ? state
                : MovieDownloadState.None;
        }
    }

    public double GetFraction(string movieId)
    {
        lock (_sync)
        {
            return _fractions.TryGetValue(movieId, out double value) ? value : 0;
        }
    }

    public long TotalBytesUsed() => GetDownloads().Sum(record => record.SizeBytes);

    // ════════════════════════════════════════════════════════════════════
    // DOWNLOAD
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Saves a film for offline playback. Returns the library record when the
    /// file is complete, or null when the transfer failed or was cancelled.
    /// </summary>
    public async Task<MovieDownloadRecord?> DownloadAsync(
        MovieDetail movie,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(movie);

        if (string.IsNullOrWhiteSpace(movie.Id) ||
            string.IsNullOrWhiteSpace(movie.DownloadUrl))
        {
            return null;
        }

        if (IsDownloaded(movie.Id))
        {
            return GetRecord(movie.Id);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            Publish(movie.Id, MovieDownloadState.Queued, 0, 0, null, "Starting…");

            string extension = ResolveExtension(movie);
            string destination = Path.Combine(MediaDirectory, SanitizeId(movie.Id) + extension);
            string partial = destination + ".part";

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            long existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;

            Publish(movie.Id, MovieDownloadState.Downloading, 0, existing, movie.SizeBytes, "Connecting…");

            using var request = new HttpRequestMessage(HttpMethod.Get, movie.DownloadUrl);

            if (existing > 0)
            {
                // The archive serves range requests, so an interrupted transfer
                // resumes instead of starting the film again from zero.
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
            }

            using HttpResponseMessage response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode &&
                response.StatusCode != HttpStatusCode.PartialContent)
            {
                Publish(
                    movie.Id, MovieDownloadState.Failed, 0, existing, null,
                    $"Server replied {(int)response.StatusCode}.");

                return null;
            }

            long? total = ResolveTotalSize(response, existing);
            bool resumed = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;

            if (existing > 0 && !resumed)
            {
                // Server ignored the range; start over cleanly.
                existing = 0;
                if (File.Exists(partial))
                {
                    File.Delete(partial);
                }
            }

            await using Stream source = await response.Content
                .ReadAsStreamAsync(ct)
                .ConfigureAwait(false);

            await using (var target = new FileStream(
                             partial,
                             resumed ? FileMode.Append : FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             BufferSize,
                             useAsync: true))
            {
                byte[] buffer = new byte[BufferSize];
                long received = existing;
                int read;
                int sinceReport = 0;

                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    received += read;
                    sinceReport += read;

                    // Throttle UI chatter to roughly every half megabyte.
                    if (sinceReport >= BufferSize * 4)
                    {
                        sinceReport = 0;
                        double fraction = total is > 0 ? Math.Clamp((double)received / total.Value, 0, 1) : 0;

                        Publish(
                            movie.Id,
                            MovieDownloadState.Downloading,
                            fraction,
                            received,
                            total,
                            $"{MovieCatalogItem.FormatSize(received)} of {MovieCatalogItem.FormatSize(total)}");
                    }
                }
            }

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(partial, destination);

            List<string> subtitles = await SaveSubtitlesAsync(movie, ct).ConfigureAwait(false);

            var record = new MovieDownloadRecord
            {
                Id = movie.Id,
                Title = movie.Title,
                Year = movie.Year,
                PosterUrl = movie.PosterUrl,
                Language = movie.Language,
                RuntimeText = movie.RuntimeText,
                SourcePageUrl = movie.SourcePageUrl,
                LocalPath = destination,
                RemoteUrl = movie.DownloadUrl,
                SizeBytes = new FileInfo(destination).Length,
                DownloadedUtc = DateTime.UtcNow,
                SubtitlePaths = subtitles
            };

            lock (_sync)
            {
                var records = (_records ??= LoadIndex());
                records.RemoveAll(existingRecord =>
                    string.Equals(existingRecord.Id, movie.Id, StringComparison.OrdinalIgnoreCase));
                records.Add(record);
                SaveIndex(records);
            }

            Publish(
                movie.Id, MovieDownloadState.Completed, 1, record.SizeBytes, record.SizeBytes,
                "Saved offline");

            LibraryChanged?.Invoke(this, EventArgs.Empty);

            _logger.LogInformation(
                "Movie {Id} saved offline ({Size} bytes).", movie.Id, record.SizeBytes);

            return record;
        }
        catch (OperationCanceledException)
        {
            Publish(movie.Id, MovieDownloadState.None, 0, 0, null, "Cancelled");
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Movie {Id} could not be downloaded.", movie.Id);
            Publish(movie.Id, MovieDownloadState.Failed, 0, 0, null, "Download failed");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Removes a film (and its subtitles) from the offline library.</summary>
    public Task<bool> DeleteAsync(string movieId)
    {
        if (string.IsNullOrWhiteSpace(movieId))
        {
            return Task.FromResult(false);
        }

        try
        {
            lock (_sync)
            {
                var records = (_records ??= LoadIndex());
                MovieDownloadRecord? record = records.FirstOrDefault(entry =>
                    string.Equals(entry.Id, movieId, StringComparison.OrdinalIgnoreCase));

                if (record is not null)
                {
                    TryDelete(record.LocalPath);

                    foreach (string subtitle in record.SubtitlePaths)
                    {
                        TryDelete(subtitle);
                    }

                    records.Remove(record);
                    SaveIndex(records);
                }

                _states.Remove(movieId);
                _fractions.Remove(movieId);
            }

            LibraryChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Movie {Id} could not be removed.", movieId);
            return Task.FromResult(false);
        }
    }

    /// <summary>Number of films currently saved for offline playback.</summary>
    public int DownloadCount => GetDownloads().Count;

    /// <summary>
    /// Copies the current offline state onto a catalogue card so grids and
    /// detail pages show "Saved offline" / "Downloading 42%" without every
    /// screen having to query the library itself.
    /// </summary>
    public void ApplyState(MovieCatalogItem item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Id))
        {
            return;
        }

        item.DownloadState = GetState(item.Id);
        item.DownloadProgress = GetFraction(item.Id);
    }

    // ════════════════════════════════════════════════════════════════════
    // INTERNALS
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Pulls the subtitle sidecars down next to the film.</summary>
    private async Task<List<string>> SaveSubtitlesAsync(MovieDetail movie, CancellationToken ct)
    {
        var saved = new List<string>();

        if (movie.Subtitles.Count == 0)
        {
            return saved;
        }

        foreach (MovieSubtitle subtitle in movie.Subtitles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                string extension = subtitle.Format == "vtt" ? ".vtt" : ".srt";
                string language = string.IsNullOrWhiteSpace(subtitle.Language) ? "sub" : subtitle.Language;
                string path = Path.Combine(
                    MediaDirectory,
                    $"{SanitizeId(movie.Id)}.{SanitizeId(language)}{extension}");

                using HttpResponseMessage response = await _http
                    .GetAsync(subtitle.Url, ct)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                byte[] content = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

                if (content.Length == 0)
                {
                    continue;
                }

                await File.WriteAllBytesAsync(path, content, ct).ConfigureAwait(false);
                saved.Add(path);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A missing subtitle must never fail the film download.
                _logger.LogDebug(exception, "Subtitle {Name} could not be saved.", subtitle.Name);
            }
        }

        return saved;
    }

    private static long? ResolveTotalSize(HttpResponseMessage response, long existing)
    {
        if (response.Content.Headers.ContentRange?.Length is long rangeLength and > 0)
        {
            return rangeLength;
        }

        long? declared = response.Content.Headers.ContentLength;

        return declared is > 0 ? declared + existing : null;
    }

    private static string ResolveExtension(MovieDetail movie)
    {
        string name = movie.FileName;

        if (!string.IsNullOrWhiteSpace(name))
        {
            string extension = Path.GetExtension(name);
            if (!string.IsNullOrWhiteSpace(extension) && extension.Length <= 6)
            {
                return extension.ToLowerInvariant();
            }
        }

        return ".mp4";
    }

    private static string SanitizeId(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(value.Length);

        foreach (char character in value)
        {
            builder.Append(
                invalid.Contains(character) || character is '/' or '\\' or ':' or '*'
                    ? '_'
                    : character);
        }

        string result = builder.ToString().Trim();
        return result.Length > 120 ? result[..120] : result;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A locked file is not worth failing the whole library operation.
        }
    }

    private void Publish(
        string movieId,
        MovieDownloadState state,
        double fraction,
        long received,
        long? total,
        string message)
    {
        lock (_sync)
        {
            _states[movieId] = state;
            _fractions[movieId] = fraction;
        }

        ProgressChanged?.Invoke(this, new MovieDownloadProgress
        {
            MovieId = movieId,
            State = state,
            Fraction = fraction,
            BytesReceived = received,
            TotalBytes = total,
            Message = message
        });
    }

    private List<MovieDownloadRecord> LoadIndex()
    {
        try
        {
            string path = IndexPath;

            if (!File.Exists(path))
            {
                return new List<MovieDownloadRecord>();
            }

            string json = File.ReadAllText(path);

            return JsonSerializer.Deserialize(
                       json,
                       MarkUptvJsonContext.Default.MovieDownloadRecordList)
                   ?? new List<MovieDownloadRecord>();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Offline movie index could not be read.");
            return new List<MovieDownloadRecord>();
        }
    }

    private void SaveIndex(List<MovieDownloadRecord> records)
    {
        try
        {
            File.WriteAllText(
                IndexPath,
                JsonSerializer.Serialize(
                    records,
                    MarkUptvJsonContext.Default.MovieDownloadRecordList));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Offline movie index could not be written.");
        }
    }

    private static string IndexPath => Path.Combine(FileSystem.AppDataDirectory, IndexFileName);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }
}
