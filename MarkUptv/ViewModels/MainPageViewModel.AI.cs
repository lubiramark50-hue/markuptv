using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Models;
using MarkUptv.Pages;
using MarkUptv.Services;

namespace MarkUptv.ViewModels;

public partial class MainPageViewModel
{
    private AiRecommendedChannel? _cachedAiRecommendation;

    public ObservableCollection<AiChatMessage> AiChatMessages { get; } = [];
    public ObservableCollection<AiHighlightItem> AiHighlights { get; } = [];
    public ObservableCollection<string> AiQuickChips { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AiRecommendationWithChannel))]
    private AiRecommendedChannel? _aiRecommendation;

    [ObservableProperty]
    private bool _isAiRecommendationLoading;

    [ObservableProperty]
    private bool _isChatOpen;

    [ObservableProperty]
    private string _chatInputText = string.Empty;

    [ObservableProperty]
    private bool _isAiProcessingResponse;

    [ObservableProperty]
    private bool _isAiPanelVisible = true;

    public TvChannel? AiRecommendationWithChannel =>
        AiRecommendation?.SourceChannel;

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RefreshAiRecommendationAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            IsAiRecommendationLoading = true;

            await Task.Delay(120, cancellationToken);

            await RunOnMainThreadAsync(() =>
            {
                LiveMatch? match = BuildLiveMatchSnapshot();
                AiRecommendation = _aiTvService.PickRecommendation(
                    TrendingChannels,
                    RecentlyWatchedChannels,
                    match,
                    forceRefresh: true);
            });
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsAiRecommendationLoading = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task PlayAiRecommendationAsync(
        CancellationToken cancellationToken)
    {
        if (AiRecommendation?.SourceChannel is TvChannel channel)
        {
            await PlayChannelCoreAsync(channel, cancellationToken);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task ToggleChatAsync(
        CancellationToken cancellationToken)
    {
        IsChatOpen = !IsChatOpen;
        return Task.CompletedTask;
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task CloseChatAsync(
        CancellationToken cancellationToken)
    {
        IsChatOpen = false;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ClearChat()
    {
        AiChatMessages.Clear();
        // Add initial greeting back if desired
        AiChatMessages.Add(new AiChatMessage
        {
            Role = AiChatMessageRole.Assistant,
            Text = "Hello! I am your MarkUpTV Assistant. What would you like to watch today?",
            IsTyping = false
        });
    }

    [RelayCommand]
    private async Task CopyMessageAsync(string messageText)
    {
        if (!string.IsNullOrWhiteSpace(messageText))
        {
            await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(messageText);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SendChatMessageAsync(
        CancellationToken cancellationToken)
    {
        string message = ChatInputText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        await RunOnMainThreadAsync(() =>
        {
            AiChatMessages.Add(new AiChatMessage
            {
                Role = AiChatMessageRole.User,
                Text = message
            });
            ChatInputText = string.Empty;
        });

        await GenerateAiResponseAsync(message, cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SendQuickChipAsync(
        string? chip,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(chip))
        {
            return;
        }

        await RunOnMainThreadAsync(() =>
        {
            AiChatMessages.Add(new AiChatMessage
            {
                Role = AiChatMessageRole.User,
                Text = chip
            });
        });

        await GenerateAiResponseAsync(chip!, cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenAiHighlightAsync(
        AiHighlightItem? item,
        CancellationToken cancellationToken)
    {
        if (item is null)
        {
            return;
        }

        if (item.TargetChannel is TvChannel channel &&
            !string.IsNullOrWhiteSpace(GetPlayableStreamUrl(channel)))
        {
            await PlayChannelCoreAsync(channel, cancellationToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(item.TargetCategory))
        {
            string route;
            string accent = "#E8B54A";
            string title = item.Title;
            string cat = item.TargetCategory;

            switch (cat.ToLowerInvariant())
            {
                case "football":
                    await NavigateRootAsync(nameof(FootballPage), cancellationToken);
                    return;
                case "sports":
                    await NavigateRootAsync("SportsPage", cancellationToken);
                    return;
                case "news":
                    await NavigateRootAsync("NewsPage", cancellationToken);
                    return;
                case "movies":
                    await NavigateRootAsync(nameof(MoviesPage), cancellationToken);
                    return;
                default:
                    await NavigateCategoryAsync(cat, title, accent, cancellationToken);
                    return;
            }
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task PlayAiRecommendedChannelAsync(
        AiRecommendedChannel? rec,
        CancellationToken cancellationToken)
    {
        if (rec?.SourceChannel is TvChannel channel)
        {
            await PlayChannelCoreAsync(channel, cancellationToken);
        }
    }

    private async Task GenerateAiResponseAsync(
        string userMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            IsAiProcessingResponse = true;

            AiChatMessage? typingMsg = null;
            await RunOnMainThreadAsync(() =>
            {
                typingMsg = new AiChatMessage
                {
                    Role = AiChatMessageRole.Assistant,
                    IsTyping = true,
                    Text = "Thinking…"
                };
                AiChatMessages.Add(typingMsg);
            });

            int delayMs = 400 + (userMessage?.Length ?? 0) * 6;
            await Task.Delay(Math.Clamp(delayMs, 400, 1200), cancellationToken);

            await RunOnMainThreadAsync(() =>
            {
                LiveMatch? match = BuildLiveMatchSnapshot();
                List<TvChannel> allAvailable = new();
                foreach (TvChannel c in TrendingChannels) allAvailable.Add(c);
                foreach (TvChannel c in RecentlyWatchedChannels)
                {
                    if (!allAvailable.Any(x => x.Id == c.Id))
                    {
                        allAvailable.Add(c);
                    }
                }

                List<AiChatMessage> responses = _aiTvService.RespondToUser(
                    userMessage,
                    allAvailable,
                    TrendingChannels,
                    RecentlyWatchedChannels,
                    match);

                if (typingMsg is not null)
                {
                    AiChatMessages.Remove(typingMsg);
                }

                foreach (AiChatMessage msg in responses)
                {
                    AiChatMessages.Add(msg);
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsAiProcessingResponse = false;
        }
    }

    private void InitializeAi()
    {
        List<string> chips = _aiTvService.GetQuickSuggestionChips();
        foreach (string chip in chips)
        {
            AiQuickChips.Add(chip);
        }

        AiChatMessages.Add(new AiChatMessage
        {
            Role = AiChatMessageRole.Assistant,
            Text = "Hi, I'm the MarkUpTV assistant.\n\nI can help you find something to watch. Try asking \"What's on now?\" or tap one of the quick chips below."
        });
    }

    private void RefreshAiHighlightsAndRecommendation()
    {
        LiveMatch? match = BuildLiveMatchSnapshot();

        List<NewsArticle> topNews = NewsItems.Take(3).ToList();

        List<AiHighlightItem> highlights = _aiTvService.BuildHighlights(
            TrendingChannels,
            match,
            topNews);

        AiHighlights.Clear();
        foreach (AiHighlightItem h in highlights)
        {
            AiHighlights.Add(h);
        }

        _cachedAiRecommendation = _aiTvService.PickRecommendation(
            TrendingChannels,
            RecentlyWatchedChannels,
            match,
            forceRefresh: false);
        AiRecommendation = _cachedAiRecommendation;
    }

    private LiveMatch? BuildLiveMatchSnapshot()
    {
        if (!HasLiveMatch)
        {
            return null;
        }

        return new LiveMatch
        {
            HomeTeam = HomeTeamName,
            AwayTeam = AwayTeamName,
            Score = $"{HomeTeamScore}-{AwayTeamScore}",
            Minute = LiveMatchMinute
        };
    }
}
