using System.Text.Json.Serialization;

namespace MarkUptv.Models;

/// <summary>A community thread tied to a live fixture.</summary>
public sealed class MatchThread
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Stable key for the fixture, e.g. "gor-mahia|afc-leopards|2026-09-14".</summary>
    public string FixtureKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? League { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public int PostCount { get; set; }

    [JsonIgnore]
    public string Subtitle => string.IsNullOrWhiteSpace(League)
        ? $"{PostCount} post{(PostCount == 1 ? string.Empty : "s")}"
        : $"{League} · {PostCount} post{(PostCount == 1 ? string.Empty : "s")}";
}

/// <summary>A post inside a match thread. Comments are nested for storage simplicity.</summary>
public sealed class ThreadPost
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string ThreadId { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<string> ReactedBy { get; set; } = new();

    public List<ThreadComment> Comments { get; set; } = new();

    [JsonIgnore]
    public int Reactions => ReactedBy.Count;

    [JsonIgnore]
    public string TimeAgo => Format(DateTime.UtcNow - CreatedAtUtc);

    [JsonIgnore]
    public string ReactionLabel => $"{Reactions}";

    [JsonIgnore]
    public string CommentLabel => Comments.Count == 0
        ? "Reply"
        : $"{Comments.Count} repl{(Comments.Count == 1 ? "y" : "ies")}";

    [JsonIgnore]
    public bool HasReactions => ReactedBy.Count > 0;

    internal static string Format(TimeSpan delta)
    {
        if (delta.TotalSeconds < 60) return "just now";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h ago";
        if (delta.TotalDays < 7) return $"{(int)delta.TotalDays}d ago";
        return $"{(int)delta.TotalDays}d ago";
    }
}

/// <summary>A reply under a thread post.</summary>
public sealed class ThreadComment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string PostId { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string TimeAgo => ThreadPost.Format(DateTime.UtcNow - CreatedAtUtc);

    [JsonIgnore]
    public string Line => $"{Author} · {TimeAgo}: {Text}";
}

/// <summary>A user report against a post, comment or author.</summary>
public sealed class CommunityReport
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>"post" | "comment" | "author".</summary>
    public string TargetType { get; set; } = "post";

    public string TargetId { get; set; } = string.Empty;

    public string TargetPreview { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string Reporter { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>"open" | "resolved" | "dismissed".</summary>
    public string Status { get; set; } = "open";

    public string? Resolution { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }

    [JsonIgnore]
    public string Summary => $"{TargetType}: {Reason}";

    [JsonIgnore]
    public string StatusLabel => Status.ToUpperInvariant();

    [JsonIgnore]
    public bool IsOpen => Status == "open";

    [JsonIgnore]
    public string TimeAgo => ThreadPost.Format(DateTime.UtcNow - CreatedAtUtc);
}

/// <summary>Append-only audit record of everything the user did in the community layer.</summary>
public sealed class CommunityRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime AtUtc { get; set; } = DateTime.UtcNow;

    public string Action { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    [JsonIgnore]
    public string Line => $"{AtUtc.ToLocalTime():dd MMM HH:mm} · {Action} · {Detail}";
}

/// <summary>Everything the community layer persists, in one exportable document.</summary>
public sealed class CommunitySnapshot
{
    public int Version { get; set; } = 1;

    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;

    public List<MatchThread> Threads { get; set; } = new();

    public List<ThreadPost> Posts { get; set; } = new();

    public List<CommunityReport> Reports { get; set; } = new();

    public List<CommunityRecord> Records { get; set; } = new();

    public List<string> FollowedTeams { get; set; } = new();

    public List<string> BlockedAuthors { get; set; } = new();
}
