using MarkUptv.Models;

namespace MarkUptv.Services;

/// <summary>A resolved playback request waiting for the player to pick it up.</summary>
public sealed class PendingLivePlayback
{
    public string Title { get; init; } = "Live Player";

    public string? League { get; init; }

    public IReadOnlyList<StreamCandidate> Candidates { get; init; } = Array.Empty<StreamCandidate>();

    public bool IsSelfTest { get; init; }
}

/// <summary>
/// Carries a resolved playback plan from the calling screen to the player.
///
/// Navigation query strings are not a reliable carrier for a multi-kilobyte
/// candidate plan (and were observed to arrive empty in this app), so the plan
/// is handed over in-process instead. The player drains the request once, which
/// keeps deep links and back-navigation predictable.
/// </summary>
public sealed class LiveSessionService
{
    private readonly object _sync = new();
    private PendingLivePlayback? _pending;

    /// <summary>Queues a plan for the next player page.</summary>
    public void SetPending(PendingLivePlayback playback)
    {
        ArgumentNullException.ThrowIfNull(playback);

        lock (_sync)
        {
            _pending = playback;
        }
    }

    /// <summary>Takes the queued plan, if any. The queue is emptied.</summary>
    public bool TryTake(out PendingLivePlayback? playback)
    {
        lock (_sync)
        {
            playback = _pending;
            _pending = null;
            return playback is not null;
        }
    }

    /// <summary>True when a plan is waiting (diagnostics use).</summary>
    public bool HasPending
    {
        get
        {
            lock (_sync)
            {
                return _pending is not null;
            }
        }
    }
}
