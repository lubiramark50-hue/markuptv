namespace MarkUptv.Models;

public enum AiChatMessageRole
{
    User,
    Assistant
}

public sealed class AiChatMessage
{
    public AiChatMessageRole Role { get; set; }

    public string Text { get; set; } = string.Empty;

    public bool IsTyping { get; set; }

    public List<AiRecommendedChannel>? Recommendations { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class AiRecommendedChannel
{
    public int ChannelId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? LogoUrl { get; set; }

    public string Category { get; set; } = string.Empty;

    public string? CurrentProgramme { get; set; }

    public string Reason { get; set; } = string.Empty;

    public TvChannel? SourceChannel { get; set; }
}

public sealed class AiHighlightItem
{
    public string Icon { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    public string AccentColor { get; set; } = "#E8B54A";

    public string? TargetCategory { get; set; }

    public TvChannel? TargetChannel { get; set; }
}
