using System.Collections.Generic;
using System.Linq;

namespace MarkUptv.Models;

/// <summary>
/// One league section on the live-match board: a display name plus the
/// matches currently scheduled/underway in that competition.
/// </summary>
public class LeagueGroup
{
    public string League { get; set; } = string.Empty;

    public List<LiveMatch> Matches { get; set; } = [];

    public int MatchCount => Matches.Count;

    public int LiveCount =>
        Matches.Count(m => m.IsLive);

    public bool HasLive => LiveCount > 0;
}