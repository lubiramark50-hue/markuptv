using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Models;
using MarkUptv.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace MarkUptv.ViewModels;

/// <summary>
/// Community Hub: followed teams, moderation queue and the audit log, with a
/// one-tap export so a reviewer can read back everything that was stored.
/// </summary>
public partial class CommunityHubViewModel : BaseViewModel
{
    private readonly CommunityStore _store;
    private readonly ILogger<CommunityHubViewModel> _logger;

    public CommunityHubViewModel(
        CommunityStore store,
        ILogger<CommunityHubViewModel> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ObservableCollection<string> FollowedTeams { get; } = new();

    public ObservableCollection<string> BlockedAuthors { get; } = new();

    public ObservableCollection<CommunityReport> Reports { get; } = new();

    public ObservableCollection<CommunityRecord> Records { get; } = new();

    public ObservableCollection<MatchThread> Threads { get; } = new();

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExportPath))]
    private string _exportPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoThreads))]
    private bool _hasThreads;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoFollows))]
    private bool _hasFollows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoBlocks))]
    private bool _hasBlocks;

    public bool HasExportPath => !string.IsNullOrWhiteSpace(ExportPath);

    public bool HasNoThreads => !HasThreads;

    public bool HasNoFollows => !HasFollows;

    public bool HasNoBlocks => !HasBlocks;

    [ObservableProperty]
    private string _moderationText = string.Empty;

    [RelayCommand]
    private void Reload()
    {
        FollowedTeams.Clear();
        foreach (string team in _store.GetFollowedTeams())
        {
            FollowedTeams.Add(team);
        }

        BlockedAuthors.Clear();
        foreach (string author in _store.GetBlockedAuthors())
        {
            BlockedAuthors.Add(author);
        }

        Reports.Clear();
        foreach (CommunityReport report in _store.GetReports())
        {
            Reports.Add(report);
        }

        Records.Clear();
        foreach (CommunityRecord record in _store.GetRecords(40))
        {
            Records.Add(record);
        }

        Threads.Clear();
        foreach (MatchThread thread in _store.GetThreads())
        {
            Threads.Add(thread);
        }

        int open = Reports.Count(report => report.IsOpen);

        HasThreads = Threads.Count > 0;
        HasFollows = FollowedTeams.Count > 0;
        HasBlocks = BlockedAuthors.Count > 0;

        ModerationText = open == 0
            ? "No open reports."
            : $"{open} open report{(open == 1 ? string.Empty : "s")} awaiting review";

        SummaryText =
            $"{FollowedTeams.Count} followed team{(FollowedTeams.Count == 1 ? string.Empty : "s")} · " +
            $"{Threads.Count} match thread{(Threads.Count == 1 ? string.Empty : "s")} · " +
            $"{Records.Count} recorded action{(Records.Count == 1 ? string.Empty : "s")}";
    }

    [RelayCommand]
    private void Unfollow(string? team)
    {
        if (!string.IsNullOrWhiteSpace(team))
        {
            _store.ToggleFollowTeam(team);
            Reload();
        }
    }

    [RelayCommand]
    private void Unblock(string? author)
    {
        if (!string.IsNullOrWhiteSpace(author))
        {
            _store.ToggleBlock(author);
            Reload();
        }
    }

    [RelayCommand]
    private void Uphold(CommunityReport? report)
    {
        if (report is not null)
        {
            _store.ResolveReport(report.Id, upheld: true, note: "Actioned by moderator");
            Reload();
        }
    }

    [RelayCommand]
    private void Dismiss(CommunityReport? report)
    {
        if (report is not null)
        {
            _store.ResolveReport(report.Id, upheld: false, note: "No action needed");
            Reload();
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        try
        {
            ExportPath = await _store.ExportAsync();
            await ShowAsync($"Records exported to:\n{ExportPath}");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Community export failed");
            await ShowAsync("Export failed. Please try again.");
        }
    }

    [RelayCommand]
    private Task OpenThreadAsync(MatchThread? thread)
        => thread is null || Shell.Current is null
            ? Task.CompletedTask
            : Shell.Current.GoToAsync(
                $"///MatchThreadPage?title={Uri.EscapeDataString(thread.Title)}" +
                $"&league={Uri.EscapeDataString(thread.League ?? string.Empty)}");

    private static Task ShowAsync(string message)
        => Shell.Current?.CurrentPage is Page page
            ? page.DisplayAlertAsync("Community Hub", message, "OK")
            : Task.CompletedTask;
}
