using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Graphics;

namespace MarkUptv.Models;

/// <summary>
/// Represents a user post in the social community feed.
/// Implements INotifyPropertyChanged so optimistic like/bookmark
/// updates reflect immediately in the UI without a full rebind.
/// </summary>
public class SocialPost : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string AuthorId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>First letter of the author's display name, used for avatar fallback.</summary>
    public string AuthorInitial => AuthorName.Length > 0 ? AuthorName[0].ToString().ToUpper() : "?";

    /// <summary>Background color string for avatar, derived from user ID hash.</summary>
    public string AvatarColor { get; set; } = "#8338EC";

    public bool IsVerified { get; set; }

    public string Content { get; set; } = string.Empty;

    /// <summary>Optional TV channel or show the post is tagged to.</summary>
    public string? ChannelTag { get; set; }
    public bool HasChannelTag => !string.IsNullOrWhiteSpace(ChannelTag);

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Human-readable relative time (e.g. "3 min ago").</summary>
    public string TimeAgo => Helpers.RelativeTime.Format(CreatedAt);

    // ── Interaction counters (raise PropertyChanged for optimistic UI) ──

    private int _likesCount;
    public int LikesCount
    {
        get => _likesCount;
        set { _likesCount = value < 0 ? 0 : value; OnPropertyChanged(); }
    }

    private int _commentsCount;
    public int CommentsCount
    {
        get => _commentsCount;
        set { _commentsCount = value < 0 ? 0 : value; OnPropertyChanged(); }
    }

    private int _sharesCount;
    public int SharesCount
    {
        get => _sharesCount;
        set { _sharesCount = value < 0 ? 0 : value; OnPropertyChanged(); }
    }

    private bool _isLiked;
    public bool IsLiked
    {
        get => _isLiked;
        set { _isLiked = value; OnPropertyChanged(); OnPropertyChanged(nameof(LikeIcon)); }
    }

    /// <summary>Heart icon — filled when liked, outline when not.</summary>
    public string LikeIcon => IsLiked ? "❤️" : "🤍";

    private bool _isBookmarked;
    public bool IsBookmarked
    {
        get => _isBookmarked;
        set { _isBookmarked = value; OnPropertyChanged(); }
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// A comment on a SocialPost, shown on the post detail page.
/// </summary>
public class SocialComment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string PostId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorInitial => AuthorName.Length > 0 ? AuthorName[0].ToString().ToUpper() : "?";
    public string AvatarColor { get; set; } = "#FF006E";
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string TimeAgo => Helpers.RelativeTime.Format(CreatedAt);

    public int LikesCount { get; set; }
    public bool IsLiked { get; set; }
}

/// <summary>Settings/config class for the Social API base URL.</summary>
public class SocialApiSettings
{
    public string BaseUrl { get; set; } = string.Empty;
}