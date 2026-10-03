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
/// Match thread: supporters post, react, reply, report and follow the teams
/// while the fixture is on. Everything is stored locally first, so nothing is
/// lost when the network or the backend is unavailable.
/// </summary>
public partial class MatchThreadViewModel : BaseViewModel, IQueryAttributable
{
    private readonly CommunityStore _store;
    private readonly ILogger<MatchThreadViewModel> _logger;

    private MatchThread? _thread;

    public MatchThreadViewModel(
        CommunityStore store,
        ILogger<MatchThreadViewModel> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ObservableCollection<ThreadPost> Posts { get; } = new();

    [ObservableProperty]
    private string _threadTitle = "Match thread";

    [ObservableProperty]
    private string _leagueText = string.Empty;

    [ObservableProperty]
    private string _composerText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReplyTarget))]
    private string _replyTargetLabel = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowLabel))]
    private bool _isFollowing;

    public string FollowLabel => IsFollowing ? "Following" : "Follow team";

    public bool HasReplyTarget => !string.IsNullOrEmpty(ReplyTargetLabel);

    private ThreadPost? _replyTarget;
    private string _homeTeam = string.Empty;
    private string _awayTeam = string.Empty;

    /// <summary>Thread identity is derived from the fixture, not from the server.</summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        string title = query.TryGetValue("title", out object? titleValue)
            ? Uri.UnescapeDataString(titleValue?.ToString() ?? string.Empty)
            : string.Empty;

        string league = query.TryGetValue("league", out object? leagueValue)
            ? Uri.UnescapeDataString(leagueValue?.ToString() ?? string.Empty)
            : string.Empty;

        Configure(title, league);
    }

    public void Configure(string title, string? league)
    {
        ThreadTitle = string.IsNullOrWhiteSpace(title) ? "Match thread" : title;
        LeagueText = string.IsNullOrWhiteSpace(league) ? "Live football" : league;

        string[] sides = ThreadTitle.Split(" vs ", 2, StringSplitOptions.TrimEntries);
        _homeTeam = sides.Length > 0 ? sides[0] : ThreadTitle;
        _awayTeam = sides.Length > 1 ? sides[1] : string.Empty;

        string fixtureKey = $"{_homeTeam}|{_awayTeam}|{DateTime.UtcNow:yyyy-MM-dd}";

        _thread = _store.GetOrCreateThread(fixtureKey, ThreadTitle, league);
        IsFollowing = _store.IsFollowing(_homeTeam);

        Reload();
    }

    [RelayCommand]
    private void Reload()
    {
        if (_thread is null)
        {
            return;
        }

        List<ThreadPost> posts = _store.GetPosts(_thread.Id).ToList();

        Posts.Clear();

        foreach (ThreadPost post in posts)
        {
            Posts.Add(post);
        }

        SummaryText = posts.Count == 0
            ? "No posts yet. Be the first to call the match."
            : $"{posts.Count} post{(posts.Count == 1 ? string.Empty : "s")} · " +
              $"{posts.Sum(item => item.Reactions)} reaction{(posts.Sum(item => item.Reactions) == 1 ? string.Empty : "s")} · " +
              $"{posts.Sum(item => item.Comments.Count)} repl{(posts.Sum(item => item.Comments.Count) == 1 ? "y" : "ies")}";
    }

    [RelayCommand]
    private void Post()
    {
        string text = ComposerText?.Trim() ?? string.Empty;

        if (_thread is null || text.Length == 0)
        {
            return;
        }

        if (text.Length > 500)
        {
            text = text[..500];
        }

        _store.AddPost(_thread.Id, "You", text);
        ComposerText = string.Empty;
        ClearError();
        Reload();
    }

    [RelayCommand]
    private void React(ThreadPost? post)
    {
        if (post is null)
        {
            return;
        }

        _store.ToggleReaction(post.Id, "You");
        Reload();
    }

    [RelayCommand]
    private void Reply(ThreadPost? post)
    {
        if (post is null)
        {
            return;
        }

        _replyTarget = post;
        ReplyTargetLabel = $"Replying to {post.Author}";
    }

    [RelayCommand]
    private void CancelReply()
    {
        _replyTarget = null;
        ReplyTargetLabel = string.Empty;
    }

    [RelayCommand]
    private void SendReply()
    {
        string text = ComposerText?.Trim() ?? string.Empty;

        if (_replyTarget is null || text.Length == 0)
        {
            return;
        }

        if (text.Length > 400)
        {
            text = text[..400];
        }

        _store.AddComment(_replyTarget.Id, "You", text);
        ComposerText = string.Empty;
        CancelReply();
        Reload();
    }

    [RelayCommand]
    private async Task FollowTeamAsync()
    {
        if (string.IsNullOrWhiteSpace(_homeTeam))
        {
            return;
        }

        IsFollowing = _store.ToggleFollowTeam(_homeTeam);

        await ShowToastAsync(IsFollowing
            ? $"Following {_homeTeam} - fixtures will be highlighted."
            : $"Unfollowed {_homeTeam}.");
    }

    [RelayCommand]
    private async Task ReportAsync(ThreadPost? post)
    {
        if (post is null || Shell.Current is null)
        {
            return;
        }

        string action = await Shell.Current.DisplayActionSheetAsync(
            "Report this post",
            "Cancel",
            null,
            "Abuse or harassment",
            "Spam",
            "Hate speech",
            "Off topic");

        if (string.IsNullOrWhiteSpace(action) || action == "Cancel")
        {
            return;
        }

        _store.AddReport("post", post.Id, action, "You", post.Text);

        string block = await Shell.Current.DisplayActionSheetAsync(
            "Post reported. Also block this author?",
            "No thanks",
            null,
            $"Block {post.Author}");

        if (!string.IsNullOrWhiteSpace(block) && block.StartsWith("Block", StringComparison.Ordinal))
        {
            _store.ToggleBlock(post.Author);
        }

        await ShowToastAsync("Report filed. Moderators can review it in the Community Hub.");
    }

    private static Task ShowToastAsync(string message)
    {
        if (Shell.Current?.CurrentPage is Page page)
        {
            return page.DisplayAlertAsync("Community", message, "OK");
        }

        return Task.CompletedTask;
    }
}
