using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Models;
using MarkUptv.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace MarkUptv.ViewModels;

/// <summary>
/// The app's own movie library: every film saved for offline playback, with
/// the storage it consumes and a one-tap offline play.
/// </summary>
public partial class MovieDownloadsViewModel : BaseViewModel
{
    private readonly MovieDownloadService _downloads;
    private readonly ILogger<MovieDownloadsViewModel> _logger;

    public MovieDownloadsViewModel(
        MovieDownloadService downloads,
        ILogger<MovieDownloadsViewModel> logger)
    {
        _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _downloads.LibraryChanged += OnLibraryChanged;
    }

    public ObservableCollection<MovieDownloadRecord> Library { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isEmpty;

    [ObservableProperty]
    private string _storageText = string.Empty;

    [ObservableProperty]
    private string _folderText = string.Empty;

    /// <summary>Currently playing saved film, if any.</summary>
    [ObservableProperty]
    private MediaSource? _playerSource;

    [ObservableProperty]
    private bool _isPlayerVisible;

    [ObservableProperty]
    private string _nowPlaying = string.Empty;

    public bool ShowEmptyState => IsEmpty && !IsLoading;

    public bool HasItems => Library.Count > 0;

    [RelayCommand]
    private Task LoadAsync()
    {
        Refresh();
        return Task.CompletedTask;
    }

    private void Refresh()
    {
        Library.Clear();

        foreach (MovieDownloadRecord record in _downloads.GetDownloads())
        {
            Library.Add(record);
        }

        IsEmpty = Library.Count == 0;
        StorageText = Library.Count == 0
            ? "No films saved yet"
            : $"{Library.Count} film(s) · {MovieCatalogItem.FormatSize(_downloads.TotalBytesUsed())} used";
        FolderText = "Stored inside the app — nothing is written to your gallery.";

        OnPropertyChanged(nameof(HasItems));
    }

    [RelayCommand]
    private void PlayOffline(MovieDownloadRecord? record)
    {
        if (record is null || string.IsNullOrWhiteSpace(record.LocalPath))
        {
            return;
        }

        if (!System.IO.File.Exists(record.LocalPath))
        {
            _logger.LogInformation("Offline file for {Id} is missing; dropping the record.", record.Id);
            _ = _downloads.DeleteAsync(record.Id);
            Refresh();
            return;
        }

        // One player at a time: assigning a new source stops the previous film.
        PlayerSource = MediaSource.FromFile(record.LocalPath);
        IsPlayerVisible = true;
        NowPlaying = record.Title;
    }

    [RelayCommand]
    private void StopPlayback()
    {
        PlayerSource = null;
        IsPlayerVisible = false;
        NowPlaying = string.Empty;
    }

    [RelayCommand]
    private async Task DeleteAsync(MovieDownloadRecord? record)
    {
        if (record is null)
        {
            return;
        }

        if (IsPlayerVisible && string.Equals(NowPlaying, record.Title, StringComparison.Ordinal))
        {
            StopPlayback();
        }

        await _downloads.DeleteAsync(record.Id).ConfigureAwait(true);
        Refresh();
    }

    [RelayCommand]
    private static async Task OpenCatalogueAsync()
        => await Shell.Current.GoToAsync("../MovieCatalogPage");

    private void OnLibraryChanged(object? sender, EventArgs e)
        => MainThread.BeginInvokeOnMainThread(Refresh);
}
