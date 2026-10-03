using System.Text.Json;
using MarkUptv.Models;
using MarkUptv.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace MarkUptv.Services;

/// <summary>
/// Local-first community store. Threads, posts, comments, reactions, follows,
/// reports, blocks and an append-only activity log are persisted immediately
/// (so nothing is ever lost when the server is unreachable) and can be
/// exported as a single JSON document for review.
/// </summary>
public sealed class CommunityStore
{
    private const string ThreadsKey = "Community_Threads_V1";
    private const string PostsKey = "Community_Posts_V1";
    private const string ReportsKey = "Community_Reports_V1";
    private const string RecordsKey = "Community_Records_V1";
    private const string FollowsKey = "Community_Follows_V1";
    private const string BlocksKey = "Community_Blocks_V1";
    private const int MaxRecords = 400;

    private readonly ILogger<CommunityStore> _logger;
    private readonly object _sync = new();

    private List<MatchThread> _threads;
    private List<ThreadPost> _posts;
    private List<CommunityReport> _reports;
    private List<CommunityRecord> _records;
    private List<string> _follows;
    private List<string> _blocks;

    public CommunityStore(ILogger<CommunityStore> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _threads = Load(ThreadsKey, MarkUptvJsonContext.Default.ListMatchThread);
        _posts = Load(PostsKey, MarkUptvJsonContext.Default.ListThreadPost);
        _reports = Load(ReportsKey, MarkUptvJsonContext.Default.ListCommunityReport);
        _records = Load(RecordsKey, MarkUptvJsonContext.Default.ListCommunityRecord);
        _follows = Load(FollowsKey, MarkUptvJsonContext.Default.ListString);
        _blocks = Load(BlocksKey, MarkUptvJsonContext.Default.ListString);
    }

    public event Action? Changed;

    // ----------------------------------------------------------------
    // Threads
    // ----------------------------------------------------------------

    public MatchThread GetOrCreateThread(string fixtureKey, string title, string? league)
    {
        lock (_sync)
        {
            MatchThread? existing = _threads.FirstOrDefault(thread =>
                string.Equals(thread.FixtureKey, fixtureKey, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                return existing;
            }

            MatchThread created = new()
            {
                FixtureKey = fixtureKey,
                Title = title,
                League = league
            };

            _threads.Insert(0, created);
            Log("thread.created", title);
            Persist();
            return created;
        }
    }

    public IReadOnlyList<MatchThread> GetThreads()
    {
        lock (_sync)
        {
            return _threads
                .OrderByDescending(thread => thread.CreatedAtUtc)
                .ToList();
        }
    }

    // ----------------------------------------------------------------
    // Posts
    // ----------------------------------------------------------------

    public ThreadPost AddPost(string threadId, string author, string text)
    {
        lock (_sync)
        {
            ThreadPost post = new()
            {
                ThreadId = threadId,
                Author = string.IsNullOrWhiteSpace(author) ? "You" : author.Trim(),
                Text = text.Trim()
            };

            _posts.Add(post);

            MatchThread? thread = _threads.FirstOrDefault(item => item.Id == threadId);

            if (thread is not null)
            {
                thread.PostCount = _posts.Count(item => item.ThreadId == threadId);
            }

            Log("post.created", post.Text.Length > 48 ? post.Text[..48] : post.Text);
            Persist();
            return post;
        }
    }

    public IReadOnlyList<ThreadPost> GetPosts(string threadId)
    {
        lock (_sync)
        {
            return _posts
                .Where(post => post.ThreadId == threadId)
                .OrderByDescending(post => post.CreatedAtUtc)
                .ToList();
        }
    }

    public ThreadPost? ToggleReaction(string postId, string user)
    {
        lock (_sync)
        {
            ThreadPost? post = _posts.FirstOrDefault(item => item.Id == postId);

            if (post is null)
            {
                return null;
            }

            if (post.ReactedBy.Contains(user, StringComparer.OrdinalIgnoreCase))
            {
                post.ReactedBy.RemoveAll(name => string.Equals(name, user, StringComparison.OrdinalIgnoreCase));
                Log("reaction.removed", post.Text.Length > 32 ? post.Text[..32] : post.Text);
            }
            else
            {
                post.ReactedBy.Add(user);
                Log("reaction.added", post.Text.Length > 32 ? post.Text[..32] : post.Text);
            }

            Persist();
            return post;
        }
    }

    // ----------------------------------------------------------------
    // Comments
    // ----------------------------------------------------------------

    public ThreadComment AddComment(string postId, string author, string text)
    {
        lock (_sync)
        {
            ThreadComment comment = new()
            {
                PostId = postId,
                Author = string.IsNullOrWhiteSpace(author) ? "You" : author.Trim(),
                Text = text.Trim()
            };

            ThreadPost? post = _posts.FirstOrDefault(item => item.Id == postId);
            post?.Comments.Add(comment);

            Log("comment.added", comment.Text.Length > 48 ? comment.Text[..48] : comment.Text);
            Persist();
            return comment;
        }
    }

    // ----------------------------------------------------------------
    // Follows / blocks
    // ----------------------------------------------------------------

    public bool ToggleFollowTeam(string team)
    {
        lock (_sync)
        {
            string clean = team.Trim();

            if (string.IsNullOrWhiteSpace(clean))
            {
                return false;
            }

            bool nowFollowing;

            int index = _follows.FindIndex(item =>
                string.Equals(item, clean, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                _follows.RemoveAt(index);
                nowFollowing = false;
                Log("team.unfollowed", clean);
            }
            else
            {
                _follows.Add(clean);
                nowFollowing = true;
                Log("team.followed", clean);
            }

            Persist();
            return nowFollowing;
        }
    }

    public bool IsFollowing(string team)
    {
        lock (_sync)
        {
            return _follows.Contains(team, StringComparer.OrdinalIgnoreCase);
        }
    }

    public IReadOnlyList<string> GetFollowedTeams()
    {
        lock (_sync)
        {
            return _follows.ToList();
        }
    }

    public bool ToggleBlock(string author)
    {
        lock (_sync)
        {
            string clean = author.Trim();

            if (string.IsNullOrWhiteSpace(clean))
            {
                return false;
            }

            bool nowBlocked;
            int index = _blocks.FindIndex(item =>
                string.Equals(item, clean, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                _blocks.RemoveAt(index);
                nowBlocked = false;
                Log("author.unblocked", clean);
            }
            else
            {
                _blocks.Add(clean);
                nowBlocked = true;
                Log("author.blocked", clean);
            }

            Persist();
            return nowBlocked;
        }
    }

    public IReadOnlyList<string> GetBlockedAuthors()
    {
        lock (_sync)
        {
            return _blocks.ToList();
        }
    }

    // ----------------------------------------------------------------
    // Reports / moderation
    // ----------------------------------------------------------------

    public CommunityReport AddReport(string targetType, string targetId, string reason, string reporter, string preview)
    {
        lock (_sync)
        {
            CommunityReport report = new()
            {
                TargetType = targetType,
                TargetId = targetId,
                Reason = string.IsNullOrWhiteSpace(reason) ? "Unspecified" : reason.Trim(),
                Reporter = string.IsNullOrWhiteSpace(reporter) ? "You" : reporter.Trim(),
                TargetPreview = preview.Length > 90 ? preview[..90] : preview
            };

            _reports.Insert(0, report);
            Log("report.filed", $"{targetType} · {report.Reason}");
            Persist();
            return report;
        }
    }

    public IReadOnlyList<CommunityReport> GetReports(bool openOnly = false)
    {
        lock (_sync)
        {
            return _reports
                .Where(report => !openOnly || report.IsOpen)
                .OrderByDescending(report => report.CreatedAtUtc)
                .ToList();
        }
    }

    public bool ResolveReport(string reportId, bool upheld, string? note)
    {
        lock (_sync)
        {
            CommunityReport? report = _reports.FirstOrDefault(item => item.Id == reportId);

            if (report is null)
            {
                return false;
            }

            report.Status = upheld ? "resolved" : "dismissed";
            report.Resolution = string.IsNullOrWhiteSpace(note)
                ? (upheld ? "Actioned by moderator" : "No action needed")
                : note!.Trim();
            report.ResolvedAtUtc = DateTime.UtcNow;

            Log("report." + report.Status, report.Reason);
            Persist();
            return true;
        }
    }

    // ----------------------------------------------------------------
    // Records / export
    // ----------------------------------------------------------------

    public IReadOnlyList<CommunityRecord> GetRecords(int limit = 60)
    {
        lock (_sync)
        {
            return _records
                .OrderByDescending(record => record.AtUtc)
                .Take(limit)
                .ToList();
        }
    }

    public CommunitySnapshot CreateSnapshot()
    {
        lock (_sync)
        {
            return new CommunitySnapshot
            {
                Threads = _threads.ToList(),
                Posts = _posts.ToList(),
                Reports = _reports.ToList(),
                Records = _records.ToList(),
                FollowedTeams = _follows.ToList(),
                BlockedAuthors = _blocks.ToList()
            };
        }
    }

    /// <summary>Writes the snapshot to a JSON file and returns its full path.</summary>
    public async Task<string> ExportAsync(CancellationToken cancellationToken = default)
    {
        CommunitySnapshot snapshot = CreateSnapshot();

        string json = JsonSerializer.Serialize(
            snapshot,
            MarkUptvJsonContext.Default.CommunitySnapshot);

        string path = Path.Combine(
            FileSystem.AppDataDirectory,
            $"markuptv-community-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);

        Log("records.exported", Path.GetFileName(path));

        _logger.LogInformation("Community snapshot exported to {Path}", path);

        return path;
    }

    // ----------------------------------------------------------------
    // Internals
    // ----------------------------------------------------------------

    private void Log(string action, string detail)
    {
        _records.Insert(0, new CommunityRecord
        {
            Action = action,
            Detail = detail
        });

        if (_records.Count > MaxRecords)
        {
            _records.RemoveRange(MaxRecords, _records.Count - MaxRecords);
        }
    }

    private void Persist()
    {
        try
        {
            Preferences.Set(ThreadsKey, JsonSerializer.Serialize(_threads, MarkUptvJsonContext.Default.ListMatchThread));
            Preferences.Set(PostsKey, JsonSerializer.Serialize(_posts, MarkUptvJsonContext.Default.ListThreadPost));
            Preferences.Set(ReportsKey, JsonSerializer.Serialize(_reports, MarkUptvJsonContext.Default.ListCommunityReport));
            Preferences.Set(RecordsKey, JsonSerializer.Serialize(_records, MarkUptvJsonContext.Default.ListCommunityRecord));
            Preferences.Set(FollowsKey, JsonSerializer.Serialize(_follows, MarkUptvJsonContext.Default.ListString));
            Preferences.Set(BlocksKey, JsonSerializer.Serialize(_blocks, MarkUptvJsonContext.Default.ListString));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Community store could not persist state");
        }

        Changed?.Invoke();
    }

    private List<T> Load<T>(
        string key,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<List<T>> typeInfo)
    {
        try
        {
            string? raw = Preferences.Get(key, null);

            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<T>();
            }

            return JsonSerializer.Deserialize(raw, typeInfo) ?? new List<T>();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Community list {Key} could not be read", key);
            return new List<T>();
        }
    }
}
