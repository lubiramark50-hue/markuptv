using MarkUptv.Models;

namespace MarkUptv.Services;

public interface IAiTvService
{
    AiRecommendedChannel PickRecommendation(
        IEnumerable<TvChannel> trending,
        IEnumerable<TvChannel> recentlyWatched,
        LiveMatch? liveMatch,
        bool forceRefresh = false);

    List<AiHighlightItem> BuildHighlights(
        IEnumerable<TvChannel> trending,
        LiveMatch? liveMatch,
        IEnumerable<NewsArticle>? topNews);

    List<AiChatMessage> RespondToUser(
        string userMessage,
        IEnumerable<TvChannel> allAvailableChannels,
        IEnumerable<TvChannel> trending,
        IEnumerable<TvChannel> recentlyWatched,
        LiveMatch? liveMatch);

    List<string> GetQuickSuggestionChips();
}

public sealed class AiTvService : IAiTvService
{
    private int _recommendationIndex;
    private readonly Random _rng = new();

    public AiRecommendedChannel PickRecommendation(
        IEnumerable<TvChannel> trending,
        IEnumerable<TvChannel> recentlyWatched,
        LiveMatch? liveMatch,
        bool forceRefresh = false)
    {
        if (forceRefresh)
        {
            _recommendationIndex++;
        }

        List<TvChannel> trendList = trending?.OfType<TvChannel>().ToList() ?? new();
        List<TvChannel> recentList = recentlyWatched?.OfType<TvChannel>().ToList() ?? new();

        if (liveMatch is not null &&
            (_recommendationIndex % 4 == 0 || trendList.Count == 0))
        {
            TvChannel? matchChannel = trendList
                .FirstOrDefault(c =>
                    c.Category?.Contains("sport", StringComparison.OrdinalIgnoreCase) == true ||
                    c.Group?.Contains("sport", StringComparison.OrdinalIgnoreCase) == true);

            if (matchChannel is not null)
            {
                return new AiRecommendedChannel
                {
                    ChannelId = matchChannel.Id,
                    Name = matchChannel.Name,
                    LogoUrl = matchChannel.LogoUrl,
                    Category = matchChannel.Category,
                    CurrentProgramme = matchChannel.CurrentProgrammeTitle,
                    Reason = $"⚽ {liveMatch.HomeTeam} vs {liveMatch.AwayTeam} is live now!",
                    SourceChannel = matchChannel
                };
            }
        }

        TvChannel? pick = null;
        string reason = string.Empty;

        int hour = DateTime.Now.Hour;

        if (recentList.Count > 0 && _recommendationIndex % 3 == 0)
        {
            pick = recentList[_rng.Next(recentList.Count)];
            reason = "Based on what you've been watching lately ✨";
        }
        else if (trendList.Count > 0)
        {
            if (hour >= 6 && hour < 12)
            {
                pick = trendList.FirstOrDefault(c =>
                    c.Category?.Contains("news", StringComparison.OrdinalIgnoreCase) == true) ??
                       trendList.FirstOrDefault(c =>
                           c.Group?.Contains("news", StringComparison.OrdinalIgnoreCase) == true) ??
                       trendList[_recommendationIndex % Math.Max(1, trendList.Count)];
                reason = "Perfect for your morning viewing ☀️";
            }
            else if (hour >= 12 && hour < 17)
            {
                pick = trendList.FirstOrDefault(c =>
                    c.Category?.Contains("sport", StringComparison.OrdinalIgnoreCase) == true) ??
                       trendList.FirstOrDefault(c =>
                           c.Group?.Contains("music", StringComparison.OrdinalIgnoreCase) == true) ??
                       trendList[_recommendationIndex % Math.Max(1, trendList.Count)];
                reason = "Great for afternoon entertainment 🎬";
            }
            else if (hour >= 17 && hour < 21)
            {
                pick = trendList.FirstOrDefault(c =>
                    c.Category?.Contains("movie", StringComparison.OrdinalIgnoreCase) == true) ??
                       trendList.FirstOrDefault(c =>
                           c.Category?.Contains("entertainment", StringComparison.OrdinalIgnoreCase) == true) ??
                       trendList.FirstOrDefault(c =>
                           c.Group?.Contains("series", StringComparison.OrdinalIgnoreCase) == true) ??
                       trendList[_recommendationIndex % Math.Max(1, trendList.Count)];
                reason = "Prime time pick for tonight 🌆";
            }
            else
            {
                pick = trendList.FirstOrDefault(c =>
                    c.Category?.Contains("music", StringComparison.OrdinalIgnoreCase) == true) ??
                       trendList.FirstOrDefault(c =>
                           c.Group?.Contains("relax", StringComparison.OrdinalIgnoreCase) == true) ??
                       trendList[_recommendationIndex % Math.Max(1, trendList.Count)];
                reason = "Wind down with this tonight 🌙";
            }
        }

        pick ??= trendList.FirstOrDefault() ?? recentList.FirstOrDefault() ?? new TvChannel
        {
            Id = 0,
            Name = "MarkUpTV Live",
            Category = "General",
            CurrentProgrammeTitle = "Live programming"
        };

        return new AiRecommendedChannel
        {
            ChannelId = pick.Id,
            Name = pick.Name,
            LogoUrl = pick.LogoUrl,
            Category = pick.Category,
            CurrentProgramme = pick.CurrentProgrammeTitle,
            Reason = reason,
            SourceChannel = pick
        };
    }

    public List<AiHighlightItem> BuildHighlights(
        IEnumerable<TvChannel> trending,
        LiveMatch? liveMatch,
        IEnumerable<NewsArticle>? topNews)
    {
        List<TvChannel> trendList = trending?.OfType<TvChannel>().ToList() ?? new List<TvChannel>();
        List<AiHighlightItem> highlights = new(3);

        if (liveMatch is not null)
        {
            string homeScore = "0";
            string awayScore = "0";
            if (!string.IsNullOrWhiteSpace(liveMatch.Score))
            {
                string[] parts = liveMatch.Score.Split('-', 2, StringSplitOptions.TrimEntries);
                if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0])) homeScore = parts[0];
                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])) awayScore = parts[1];
            }

            highlights.Add(new AiHighlightItem
            {
                Icon = "⚽",
                Title = string.IsNullOrWhiteSpace(liveMatch.Score)
                    ? $"{liveMatch.HomeTeam} vs {liveMatch.AwayTeam}"
                    : $"{liveMatch.HomeTeam} {homeScore}  –  {awayScore} {liveMatch.AwayTeam}",
                Subtitle = liveMatch.Minute ?? "LIVE NOW",
                AccentColor = "#FF315F",
                TargetCategory = "football"
            });
        }
        else
        {
            TvChannel? sports = trendList.FirstOrDefault(c =>
                c.Category?.Contains("sport", StringComparison.OrdinalIgnoreCase) == true);
            highlights.Add(new AiHighlightItem
            {
                Icon = "🏆",
                Title = sports?.Name ?? "Sports Live",
                Subtitle = sports?.CurrentProgrammeTitle ?? "Live sports channels",
                AccentColor = "#FF8A3D",
                TargetCategory = "sports",
                TargetChannel = sports
            });
        }

        TvChannel? movie = trendList.FirstOrDefault(c =>
            c.Category?.Contains("movie", StringComparison.OrdinalIgnoreCase) == true);
        highlights.Add(new AiHighlightItem
        {
            Icon = "🎬",
            Title = movie?.Name ?? "Movie Night",
            Subtitle = movie?.CurrentProgrammeTitle ?? "Featured cinema picks",
            AccentColor = "#FF3E91",
            TargetCategory = "movies",
            TargetChannel = movie
        });

        NewsArticle? news = topNews?.FirstOrDefault();
        if (news is not null)
        {
            highlights.Add(new AiHighlightItem
            {
                Icon = "📰",
                Title = news.Title.Length > 38 ? news.Title[..38] + "…" : news.Title,
                Subtitle = news.Source ?? "Breaking news",
                AccentColor = "#3A86FF",
                TargetCategory = "news"
            });
        }
        else
        {
            TvChannel? newsChannel = trendList.FirstOrDefault(c =>
                c.Category?.Contains("news", StringComparison.OrdinalIgnoreCase) == true);
            highlights.Add(new AiHighlightItem
            {
                Icon = "📺",
                Title = newsChannel?.Name ?? "Live News",
                Subtitle = newsChannel?.CurrentProgrammeTitle ?? "Top stories now",
                AccentColor = "#3A86FF",
                TargetCategory = "news",
                TargetChannel = newsChannel
            });
        }

        return highlights;
    }

    public List<AiChatMessage> RespondToUser(
        string userMessage,
        IEnumerable<TvChannel> allAvailableChannels,
        IEnumerable<TvChannel> trending,
        IEnumerable<TvChannel> recentlyWatched,
        LiveMatch? liveMatch)
    {
        List<TvChannel> allChannels = allAvailableChannels?.OfType<TvChannel>().ToList() ?? new List<TvChannel>();
        List<TvChannel> trendList = trending?.OfType<TvChannel>().ToList() ?? new List<TvChannel>();
        List<TvChannel> recentList = recentlyWatched?.OfType<TvChannel>().ToList() ?? new List<TvChannel>();

        string q = (userMessage ?? string.Empty).Trim().ToLowerInvariant();

        List<AiChatMessage> result = new();
        AiChatMessage typing = new()
        {
            Role = AiChatMessageRole.Assistant,
            IsTyping = true
        };
        result.Add(typing);

        AiChatMessage reply = new()
        {
            Role = AiChatMessageRole.Assistant
        };

        List<TvChannel> recommendations = new();
        string replyText;

        if (string.IsNullOrWhiteSpace(q))
        {
            replyText = "Hey there! I'm your MarkUpTV AI assistant ✨ Ask me what's on, or try: \"What's on now?\", \"Recommend a movie\", or \"Show me live sports\".";
        }
        else if (q.Contains("sport") || q.Contains("football") || q.Contains("live match") || q.Contains("soccer") || q.Contains("game"))
        {
            if (liveMatch is not null)
            {
                replyText = $"There's a live match happening right now! ⚽\n\n" +
                           $"**{liveMatch.HomeTeam}** vs **{liveMatch.AwayTeam}**\n" +
                           $"Score: {liveMatch.Score} • {liveMatch.Minute ?? "LIVE"}\n\n" +
                           $"Here are sports channels where you can catch the action:";
                recommendations.AddRange(allChannels
                    .Where(c =>
                        c.Category?.Contains("sport", StringComparison.OrdinalIgnoreCase) == true ||
                        c.Group?.Contains("sport", StringComparison.OrdinalIgnoreCase) == true)
                    .Take(3));
                if (!recommendations.Any())
                {
                    recommendations.AddRange(trendList.Take(2));
                }
            }
            else
            {
                replyText = "Here are the top sports channels right now. Catch all the live action! 🏆";
                recommendations.AddRange(allChannels
                    .Where(c =>
                        c.Category?.Contains("sport", StringComparison.OrdinalIgnoreCase) == true ||
                        c.Group?.Contains("sport", StringComparison.OrdinalIgnoreCase) == true)
                    .Take(3));
                if (!recommendations.Any())
                {
                    recommendations.AddRange(trendList.Take(2));
                }
            }
        }
        else if (q.Contains("movie") || q.Contains("film") || q.Contains("cinema"))
        {
            replyText = "Movie night sorted! 🎬🍿 Here are my top picks for you right now:";
            recommendations.AddRange(allChannels
                .Where(c =>
                    c.Category?.Contains("movi", StringComparison.OrdinalIgnoreCase) == true ||
                    c.Group?.Contains("cinema", StringComparison.OrdinalIgnoreCase) == true)
                .Take(3));
            if (!recommendations.Any())
            {
                recommendations.AddRange(trendList
                    .Where(c => c.Category?.Contains("entertainment", StringComparison.OrdinalIgnoreCase) == true)
                    .Take(3));
            }
            if (!recommendations.Any())
            {
                recommendations.AddRange(trendList.Take(3));
            }
        }
        else if (q.Contains("news") || q.Contains("headline") || q.Contains("breaking") || q.Contains("story"))
        {
            replyText = "Here are the news channels bringing you the latest stories from around the world 📰";
            recommendations.AddRange(allChannels
                .Where(c =>
                    c.Category?.Contains("news", StringComparison.OrdinalIgnoreCase) == true ||
                    c.Group?.Contains("news", StringComparison.OrdinalIgnoreCase) == true)
                .Take(3));
            if (!recommendations.Any())
            {
                recommendations.AddRange(trendList.Take(3));
            }
        }
        else if (q.Contains("music") || q.Contains("song") || q.Contains("listen"))
        {
            replyText = "Turn up the volume! 🎵🎶 Here are the best music channels for your mood:";
            recommendations.AddRange(allChannels
                .Where(c =>
                    c.Category?.Contains("music", StringComparison.OrdinalIgnoreCase) == true ||
                    c.Group?.Contains("music", StringComparison.OrdinalIgnoreCase) == true)
                .Take(3));
            if (!recommendations.Any())
            {
                recommendations.AddRange(trendList.Take(3));
            }
        }
        else if (q.Contains("kids") || q.Contains("cartoon") || q.Contains("children") || q.Contains("family"))
        {
            replyText = "Family-friendly picks incoming! 👨‍👩‍👧‍👦 Enjoy these together:";
            recommendations.AddRange(allChannels
                .Where(c =>
                    c.Category?.Contains("cartoon", StringComparison.OrdinalIgnoreCase) == true ||
                    c.Category?.Contains("family", StringComparison.OrdinalIgnoreCase) == true ||
                    c.Group?.Contains("kids", StringComparison.OrdinalIgnoreCase) == true)
                .Take(3));
            if (!recommendations.Any())
            {
                recommendations.AddRange(trendList.Take(3));
            }
        }
        else if (q.Contains("recommend") || q.Contains("suggest") || q.Contains("what should") || q.Contains("pick") || q.Contains("what to watch"))
        {
            AiRecommendedChannel top = PickRecommendation(trendList, recentList, liveMatch, forceRefresh: true);
            replyText = $"Based on {DateTime.Now.Hour:00}:{DateTime.Now.Minute:00} and trending picks, I think you'll love:\n\n" +
                       $"**✨ {top.Name}**\n{top.Reason}";
            if (top.SourceChannel is not null)
            {
                recommendations.Add(top.SourceChannel);
            }
            recommendations.AddRange(trendList
                .Where(c => c.Id != top.ChannelId)
                .Take(2));
        }
        else if (q.Contains("now") || q.Contains("on tv") || q.Contains("on air") || q.Contains("playing"))
        {
            replyText = "Here's what's trending right now across the MarkUpTV universe 🌌:";
            recommendations.AddRange(trendList.Take(4));
        }
        else if (q.Contains("hello") || q.Contains("hi") || q.Contains("hey"))
        {
            replyText = $"Hey! Welcome to MarkUpTV ✨\n\nI can help you find:\n" +
                       "• What's live right now\n" +
                       "• Sports, movies, music, news\n" +
                       "• Personalised recommendations\n\n" +
                       "Try asking \"What's on now?\"";
        }
        else if (q.Contains("recent") || q.Contains("history") || q.Contains("watched lately"))
        {
            if (recentList.Any())
            {
                replyText = "Here's what you've been enjoying lately. Tap any to resume! ▶";
                recommendations.AddRange(recentList.Take(4));
            }
            else
            {
                replyText = "You don't have a watch history yet! Start exploring — try the trending channels below 👇";
                recommendations.AddRange(trendList.Take(3));
            }
        }
        else if (q.Contains("help") || q.Contains("what can you do"))
        {
            replyText = "I'm your AI TV assistant! Here's how I can help:\n\n" +
                       "🎯 **\"Recommend something\"** — Personalised pick\n" +
                       "⚽ **\"What football is on?\"** — Live matches\n" +
                       "🎬 **\"Find a movie\"** — Cinema channels\n" +
                       "📰 **\"Show me news\"** — Latest headlines\n" +
                       "🎵 **\"Play music\"** — Music channels\n\n" +
                       "Or just tap one of the quick chips below!";
        }
        else
        {
            TvChannel? keywordMatch = allChannels
                .FirstOrDefault(c =>
                    (!string.IsNullOrWhiteSpace(c.Name) &&
                    c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(c.Category) &&
                    c.Category?.Contains(q, StringComparison.OrdinalIgnoreCase) == true));

            if (keywordMatch is not null)
            {
                replyText = $"Found something you might like! 🎯";
                recommendations.Add(keywordMatch);
                recommendations.AddRange(trendList
                    .Where(c => c.Id != keywordMatch.Id)
                    .Take(2));
            }
            else
            {
                replyText = "Hmm, I couldn't find exactly that. Here are the most popular channels right now — tap any to watch! 👇";
                recommendations.AddRange(trendList.Take(4));
            }
        }

        reply.Text = StripMarkdown(replyText);

        if (recommendations.Any())
        {
            reply.Recommendations = recommendations
                .DistinctBy(c => c.Id)
                .Select(c => new AiRecommendedChannel
                {
                    ChannelId = c.Id,
                    Name = c.Name,
                    LogoUrl = c.LogoUrl,
                    Category = c.Category,
                    CurrentProgramme = c.CurrentProgrammeTitle,
                    Reason = "Tap to watch ▶",
                    SourceChannel = c
                })
                .ToList();
        }

        result.Add(reply);
        return result;
    }

    private static string StripMarkdown(string text)
        => (text ?? string.Empty).Replace("**", string.Empty, StringComparison.Ordinal);

    public List<string> GetQuickSuggestionChips()
    {
        return new List<string>
        {
            "What's on now?",
            "Recommend a movie",
            "Show sports",
            "Latest news",
            "Play music",
            "Continue watching"
        };
    }
}

