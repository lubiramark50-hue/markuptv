using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace MarkUptv.ViewModels;

/// <summary>
/// Controls community-post composition, validation,
/// publishing status and navigation.
/// </summary>
public partial class ComposePostViewModel : BaseViewModel
{
    private const int MaximumPostLength = 2_000;
    private const int MaximumAuthorLength = 80;

    private readonly SocialService _socialService;
    private readonly IDeviceService _deviceService;
    private readonly ILogger<ComposePostViewModel> _logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private string _authorName = "MarkUp Viewer";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    [NotifyPropertyChangedFor(nameof(RemainingCharacters))]
    [NotifyPropertyChangedFor(nameof(CharacterCountText))]
    [NotifyPropertyChangedFor(nameof(ContentProgress))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private string _content = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    [NotifyPropertyChangedFor(nameof(IsNotPosting))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _isPosting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isStatusSuccess;

    /// <summary>Topic chips shown in the composer. null tag = general post.</summary>
    public IReadOnlyList<string> TopicTags { get; } =
        ["General", "Sports", "News", "Movies"];

    [ObservableProperty]
    private string _selectedTag = "General";

    public bool IsNotPosting =>
        !IsPosting;

    public bool HasStatusMessage =>
        !string.IsNullOrWhiteSpace(StatusMessage);

    public int RemainingCharacters =>
        Math.Max(
            0,
            MaximumPostLength - (Content?.Length ?? 0));

    public string CharacterCountText =>
        $"{Content?.Length ?? 0:N0} / {MaximumPostLength:N0}";

    public double ContentProgress =>
        Math.Clamp(
            (double)(Content?.Length ?? 0) / MaximumPostLength,
            0,
            1);

    public bool CanPublish
    {
        get
        {
            string content = Content?.Trim() ?? string.Empty;
            string author = AuthorName?.Trim() ?? string.Empty;

            return
                !IsPosting &&
                content.Length > 0 &&
                content.Length <= MaximumPostLength &&
                author.Length <= MaximumAuthorLength;
        }
    }

    public ComposePostViewModel(
        SocialService socialService,
        IDeviceService deviceService,
        ILogger<ComposePostViewModel> logger)
    {
        _socialService = socialService
            ?? throw new ArgumentNullException(nameof(socialService));

        _deviceService = deviceService
            ?? throw new ArgumentNullException(nameof(deviceService));

        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));
    }

    partial void OnContentChanged(string value)
    {
        ClearPreviousStatus();
    }

    partial void OnAuthorNameChanged(string value)
    {
        ClearPreviousStatus();
    }

    [RelayCommand(
        AllowConcurrentExecutions = false,
        CanExecute = nameof(CanPublish))]
    private async Task PublishAsync(
        CancellationToken cancellationToken)
    {
        string normalizedContent =
            Content?.Trim() ?? string.Empty;

        string normalizedAuthor =
            NormalizeAuthorName(AuthorName);

        if (string.IsNullOrWhiteSpace(normalizedContent))
        {
            SetStatus(
                "Write something before publishing.",
                success: false);

            return;
        }

        if (normalizedContent.Length > MaximumPostLength)
        {
            SetStatus(
                $"Your post cannot exceed {MaximumPostLength:N0} characters.",
                success: false);

            return;
        }

        if (normalizedAuthor.Length > MaximumAuthorLength)
        {
            SetStatus(
                $"Your display name cannot exceed {MaximumAuthorLength} characters.",
                success: false);

            return;
        }

        IsPosting = true;
        ClearError();
        SetStatus(string.Empty, success: false);

        try
        {
            string deviceId =
                GetSafeDeviceId();

            _logger.LogInformation(
                "Publishing community post. Author: {Author}; Characters: {Length}",
                normalizedAuthor,
                normalizedContent.Length);

            var post =
                await _socialService.CreatePostAsync(
                    deviceId,
                    normalizedAuthor,
                    normalizedContent,
                    mediaUrl: null,
                    channelTag: NormalizeChannelTag(SelectedTag));

            cancellationToken.ThrowIfCancellationRequested();

            if (post is null)
            {
                SetStatus(
                    "The post was not accepted. Please try again.",
                    success: false);

                return;
            }

            Content = string.Empty;

            SetStatus(
                "Your post was published successfully.",
                success: true);

            _logger.LogInformation(
                "Community post published successfully.");

            await Task.Delay(
                TimeSpan.FromMilliseconds(450),
                cancellationToken);

            await SafeNavigateAsync(
                "..",
                parameters: null,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Community-post publishing was cancelled.");
        }
        catch (Exception exception)
        {
            SetError(
                "Your post could not be published.");

            SetStatus(
                "Check your connection and try again.",
                success: false);

            _logger.LogError(
                exception,
                "Community-post publishing failed.");
        }
        finally
        {
            IsPosting = false;
        }
    }

    [RelayCommand(
        AllowConcurrentExecutions = false)]
    private async Task GoBackAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await SafeNavigateAsync(
                "..",
                parameters: null,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Compose-page back navigation was cancelled.");
        }
        catch (Exception exception)
        {
            SetError(
                "The previous page could not be opened.");

            _logger.LogError(
                exception,
                "Compose-page back navigation failed.");
        }
    }

    [RelayCommand]
    private void SelectTopic(string? tag)
    {
        string normalized = tag?.Trim() ?? string.Empty;

        if (normalized.Length == 0 ||
            normalized.Equals(SelectedTag, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SelectedTag = normalized;
        ClearPreviousStatus();
    }

    [RelayCommand]
    private void ClearPost()
    {
        if (IsPosting)
        {
            return;
        }

        Content = string.Empty;
        SelectedTag = "General";
        ClearError();
        SetStatus(string.Empty, success: false);
    }

    public void OnPageDisappearing()
    {
        PublishCommand.Cancel();
        GoBackCommand.Cancel();
    }

    private void ClearPreviousStatus()
    {
        if (IsPosting)
        {
            return;
        }

        if (HasStatusMessage)
        {
            StatusMessage = string.Empty;
            IsStatusSuccess = false;
        }

        if (HasError)
        {
            ClearError();
        }
    }

    private void SetStatus(
        string message,
        bool success)
    {
        IsStatusSuccess = success;
        StatusMessage = message;
    }

    private string GetSafeDeviceId()
    {
        try
        {
            string? deviceId =
                _deviceService.GetDeviceId();

            return string.IsNullOrWhiteSpace(deviceId)
                ? "anonymous"
                : deviceId.Trim();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Device identifier could not be read. Anonymous mode will be used.");

            return "anonymous";
        }
    }

    private static string NormalizeAuthorName(
        string? authorName)
    {
        return string.IsNullOrWhiteSpace(authorName)
            ? "MarkUp Viewer"
            : authorName.Trim();
    }

    /// <summary>
    /// Maps the composer chip to the backend topic tag. "General" posts get
    /// no tag; anything else lowercases to match the feed filter values.
    /// </summary>
    private static string? NormalizeChannelTag(string? tag)
    {
        string trimmed = tag?.Trim() ?? string.Empty;

        if (trimmed.Length == 0 ||
            trimmed.Equals("General", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed.ToLowerInvariant();
    }
}