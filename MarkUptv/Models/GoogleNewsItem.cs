// ═══════════════════════════════════════════════════════════════════════
//  GoogleNewsItem.cs  — Updated model with interactive properties
//  Add these new properties to your existing GoogleNewsItem model
// ═══════════════════════════════════════════════════════════════════════
using CommunityToolkit.Mvvm.ComponentModel;

namespace MarkUptv.Models;

/// <summary>
/// Observable news item — new fields required by MainPageViewModel v2.
/// Inherits from ObservableObject so LikeCount/IsLiked update the UI
/// without going through the parent ViewModel.
/// </summary>
public partial class GoogleNewsItem : ObservableObject
{
    // ── Core fields (already existed) ──
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime PublishedDate { get; set; } = DateTime.UtcNow;

    // ── NEW: Display fields ──
    public string Source { get; set; } = string.Empty;
    public string TimeAgo { get; set; } = string.Empty;

    // ── NEW: Interactive fields — Observable so FABs update instantly ──
    [ObservableProperty] private int _likeCount = 0;
    [ObservableProperty] private bool _isLiked = false;
    [ObservableProperty] private bool _isBookmarked = false;
}
