using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Pages;
using Microsoft.Extensions.Logging;

namespace MarkUptv.ViewModels;

/// <summary>
/// Controls MarkUpTV Shell search, flyout state and
/// dynamic category navigation.
/// </summary>
public partial class AppShellViewModel :
    BaseViewModel
{
    private const string DefaultAccent =
        "#FF3E91";

    private readonly ILogger<AppShellViewModel> _logger;

    [ObservableProperty]
    private string _flyoutSearchText =
        string.Empty;

    [ObservableProperty]
    private bool _isFlyoutOpen;

    [ObservableProperty]
    private string _currentSectionTitle =
        "Home";

    public AppShellViewModel(
        ILogger<AppShellViewModel> logger)
    {
        _logger = logger
            ?? throw new ArgumentNullException(
                nameof(logger));
    }

    partial void OnIsFlyoutOpenChanged(
        bool value)
    {
        _logger.LogDebug(
            "Shell flyout state changed. Open: {IsOpen}",
            value);
    }

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private async Task ExecuteFlyoutSearchAsync(
        CancellationToken cancellationToken)
    {
        string query =
            FlyoutSearchText.Trim();

        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        try
        {
            _logger.LogInformation(
                "Global Shell search initiated: {Query}",
                query);

            IsFlyoutOpen = false;
            FlyoutSearchText = string.Empty;
            CurrentSectionTitle = "Search";

            var parameters =
                new Dictionary<string, object>
                {
                    ["query"] = query
                };

            await SafeNavigateAsync(
                "//SearchPage",
                parameters,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Shell search navigation was cancelled.");
        }
        catch (Exception exception)
        {
            SetError(
                "Search could not be opened.");

            _logger.LogError(
                exception,
                "Failed to navigate to SearchPage.");
        }
    }

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private async Task OpenCategoryAsync(
        string? payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            _logger.LogWarning(
                "Category navigation was skipped because the payload was empty.");

            return;
        }

        try
        {
            CategoryNavigationRequest request =
                ParseCategoryPayload(payload);

            IsFlyoutOpen = false;
            CurrentSectionTitle = request.Title;

            var parameters =
                new Dictionary<string, object>
                {
                    ["category"] = request.Category,
                    ["title"] = request.Title,
                    ["accent"] = request.Accent
                };

            _logger.LogInformation(
                "Opening category {Category} with title {Title}.",
                request.Category,
                request.Title);

            await SafeNavigateAsync(
                nameof(CategoryChannelPage),
                parameters,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Category navigation was cancelled.");
        }
        catch (Exception exception)
        {
            SetError(
                "The selected category could not be opened.");

            _logger.LogError(
                exception,
                "Failed to navigate to a category page.");
        }
    }

    [RelayCommand]
    private void CloseFlyout()
    {
        IsFlyoutOpen = false;
    }

    private static CategoryNavigationRequest
        ParseCategoryPayload(string payload)
    {
        string[] parts =
            payload.Split(
                '|',
                3,
                StringSplitOptions.TrimEntries);

        string category =
            parts.Length > 0
                ? parts[0].Trim()
                    .ToLowerInvariant()
                : string.Empty;

        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException(
                "The category key is missing.",
                nameof(payload));
        }

        string title =
            parts.Length > 1 &&
            !string.IsNullOrWhiteSpace(parts[1])
                ? parts[1].Trim()
                : CreateTitle(category);

        string accent =
            parts.Length > 2
                ? NormalizeAccent(parts[2])
                : DefaultAccent;

        return new CategoryNavigationRequest(
            category,
            title,
            accent);
    }

    private static string NormalizeAccent(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultAccent;
        }

        string accent =
            value.Trim();

        bool validLength =
            accent.Length is 7 or 9;

        bool validCharacters =
            accent.StartsWith(
                "#",
                StringComparison.Ordinal) &&
            accent
                .Skip(1)
                .All(Uri.IsHexDigit);

        return validLength &&
               validCharacters
            ? accent.ToUpperInvariant()
            : DefaultAccent;
    }

    private static string CreateTitle(
        string category)
    {
        string[] words =
            category.Split(
                ['-', '_'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        if (words.Length == 0)
        {
            return "Live Channels";
        }

        return string.Join(
            " ",
            words.Select(word =>
                word.Length == 1
                    ? word.ToUpperInvariant()
                    : char.ToUpperInvariant(word[0]) +
                      word[1..].ToLowerInvariant()));
    }

    private sealed record CategoryNavigationRequest(
        string Category,
        string Title,
        string Accent);
}