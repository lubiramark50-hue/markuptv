#pragma warning disable IL2026
#pragma warning disable IL3050

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MarkUptv.Models;
using MarkUptv.Services;
using Microsoft.Maui.Controls;

namespace MarkUptv.ViewModels;

/// <summary>
/// ViewModel for the Social Community feed page.
/// Handles post feed loading, pagination, likes, sharing, bookmarking,
/// compose navigation, and filter tabs. All errors are caught and surfaced
/// as user-visible state rather than unhandled exceptions.
/// </summary>
public class SocialViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private readonly SocialService _socialService;

    // ── Observable collections ──────────────────────────────────────────
    public ObservableCollection<SocialPost> Posts { get; } = new();

    // ── Loading states ───────────────────────────────────────────────────
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEmpty)); }
    }

    private bool _isRefreshing;
    public bool IsRefreshing
    {
        get => _isRefreshing;
        set { _isRefreshing = value; OnPropertyChanged(); }
    }

    private bool _isLoadingMore;
    public bool IsLoadingMore
    {
        get => _isLoadingMore;
        set { _isLoadingMore = value; OnPropertyChanged(); }
    }

    private bool _isEmpty;
    public bool IsEmpty
    {
        get => _isEmpty && !_isLoading;
        set { _isEmpty = value; OnPropertyChanged(); }
    }

    // ── Pagination ───────────────────────────────────────────────────────
    private int _currentPage = 1;
    private const int PageSize = 20;
    private bool _hasMorePages = true;
    private string _currentFilter = "All";

    // ── Stats ────────────────────────────────────────────────────────────
    private string _onlineCount = "—";
    public string OnlineCount
    {
        get => _onlineCount;
        set { _onlineCount = value; OnPropertyChanged(); }
    }

    // ── Commands ─────────────────────────────────────────────────────────
    public ICommand RefreshFeedCommand { get; }
    public ICommand LoadMorePostsCommand { get; }
    public ICommand FilterFeedCommand { get; }
    public ICommand LikePostCommand { get; }
    public ICommand OpenPostDetailCommand { get; }
    public ICommand SharePostCommand { get; }
    public ICommand BookmarkPostCommand { get; }
    public ICommand OpenComposeCommand { get; }

    public SocialViewModel(SocialService socialService)
    {
        _socialService = socialService ?? throw new ArgumentNullException(nameof(socialService));

        RefreshFeedCommand = new Command(async () => await RefreshAsync());
        LoadMorePostsCommand = new Command(async () => await LoadMoreAsync());
        FilterFeedCommand = new Command<string>(async (filter) => await ApplyFilterAsync(filter));
        LikePostCommand = new Command<SocialPost>(async (post) => await ToggleLikeAsync(post));
        OpenPostDetailCommand = new Command<SocialPost>(async (post) => await OpenPostDetailAsync(post));
        SharePostCommand = new Command<SocialPost>(async (post) => await SharePostAsync(post));
        BookmarkPostCommand = new Command<SocialPost>(async (post) => await BookmarkPostAsync(post));
        OpenComposeCommand = new Command(async () => await OpenComposeAsync());
    }

    // ── Init ─────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        if (IsLoading) return;
        await LoadFeedAsync(reset: true);
        _ = LoadOnlineCountAsync();
    }

    // ── Core load ────────────────────────────────────────────────────────

    private async Task LoadFeedAsync(bool reset)
    {
        try
        {
            if (reset)
            {
                IsLoading = true;
                _currentPage = 1;
                _hasMorePages = true;
                Posts.Clear();
            }
            else
            {
                if (!_hasMorePages || IsLoadingMore) return;
                IsLoadingMore = true;
            }

            var result = await _socialService.GetPostsAsync(
                filter: _currentFilter,
                page: _currentPage,
                pageSize: PageSize);

            if (result is null || result.Count == 0)
            {
                if (reset) IsEmpty = true;
                _hasMorePages = false;
                return;
            }

            foreach (var post in result)
                Posts.Add(post);

            IsEmpty = Posts.Count == 0;
            _hasMorePages = result.Count == PageSize;
            _currentPage++;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SocialViewModel] Feed load error: {ex.Message}");
            // Surface non-fatal error — feed stays visible with existing posts
            if (Posts.Count == 0) IsEmpty = true;
        }
        finally
        {
            IsLoading = false;
            IsLoadingMore = false;
            IsRefreshing = false;
        }
    }

    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await LoadFeedAsync(reset: true);
    }

    private async Task LoadMoreAsync()
    {
        await LoadFeedAsync(reset: false);
    }

    private async Task ApplyFilterAsync(string filter)
    {
        if (_currentFilter == filter) return;
        _currentFilter = filter;
        await LoadFeedAsync(reset: true);
    }

    // ── Interactions ─────────────────────────────────────────────────────

    private async Task ToggleLikeAsync(SocialPost post)
    {
        if (post is null) return;
        try
        {
            // Optimistic UI update
            post.IsLiked = !post.IsLiked;
            post.LikesCount += post.IsLiked ? 1 : -1;

            bool ok = await _socialService.ToggleLikeAsync(post.Id, post.IsLiked);
            if (!ok)
            {
                // Rollback on failure
                post.IsLiked = !post.IsLiked;
                post.LikesCount += post.IsLiked ? 1 : -1;
            }
        }
        catch (Exception ex)
        {
            LastActionError = $"like: {ex.GetType().Name}: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[SocialViewModel] Like error: {ex.Message}");
        }
    }

    /// <summary>
    /// Last failure surfaced by a card action, shown by the page's temporary
    /// diagnostics strip. Empty when the last action succeeded.
    /// </summary>
    public string LastActionError { get; private set; } = string.Empty;

    private async Task OpenPostDetailAsync(SocialPost post)
    {
        if (post is null) return;
        try
        {
            LastActionError = string.Empty;
            await Shell.Current.GoToAsync($"PostDetailPage?postId={post.Id}");
        }
        catch (Exception ex)
        {
            LastActionError = $"nav: {ex.GetType().Name}: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[SocialViewModel] Nav error: {ex.Message}");
        }
    }

    private async Task SharePostAsync(SocialPost post)
    {
        if (post is null) return;
        try
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Text = $"{post.AuthorName} on MarkUpTV: {post.Content}",
                Title = "Share Post"
            });
        }
        catch (Exception ex)
        {
            LastActionError = $"share: {ex.GetType().Name}: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[SocialViewModel] Share error: {ex.Message}");
        }
    }

    private async Task BookmarkPostAsync(SocialPost post)
    {
        if (post is null) return;
        try
        {
            post.IsBookmarked = !post.IsBookmarked;
            await _socialService.BookmarkPostAsync(post.Id, post.IsBookmarked);
        }
        catch (Exception ex)
        {
            post.IsBookmarked = !post.IsBookmarked; // rollback
            LastActionError = $"bookmark: {ex.GetType().Name}: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[SocialViewModel] Bookmark error: {ex.Message}");
        }
    }

    private async Task OpenComposeAsync()
    {
        try
        {
            await Shell.Current.GoToAsync("ComposePostPage");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SocialViewModel] Compose nav error: {ex.Message}");
        }
    }

    private async Task LoadOnlineCountAsync()
    {
        try
        {
            var count = await _socialService.GetOnlineCountAsync();
            OnlineCount = count >= 1000
                ? $"{count / 1000.0:F1}k"
                : count.ToString();
        }
        catch
        {
            OnlineCount = "—";
        }
    }
}
