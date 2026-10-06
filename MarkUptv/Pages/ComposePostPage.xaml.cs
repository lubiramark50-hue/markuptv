using MarkUptv.ViewModels;
using Microsoft.Maui.Controls;

using MarkUptv.Helpers;

namespace MarkUptv.Pages;

/// <summary>
/// Presentation layer for composing posts.
///
/// This file contains only page lifecycle handling,
/// focus effects and decorative animations.
/// </summary>
public partial class ComposePostPage : ContentPage
{
    private readonly ComposePostViewModel _viewModel;

    private bool _entranceAnimationCompleted;
    private bool _entranceAnimationRunning;

    private CancellationTokenSource? _diamondAnimationCancellation;

    public ComposePostPage(
        ComposePostViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));

        BindingContext = viewModel;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        UpdateTopicChipVisuals();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            if (!_entranceAnimationCompleted &&
                !_entranceAnimationRunning)
            {
                await PlayEntranceAnimationAsync();
            }

            StartDiamondAnimation();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ComposePostPage] Appearance animation failed: {exception}");
        }
    }

    protected override void OnDisappearing()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.OnPageDisappearing();

        StopDiamondAnimation();

        if (AuthorEntry.IsFocused)
        {
            AuthorEntry.Unfocus();
        }

        if (ContentEditor.IsFocused)
        {
            ContentEditor.Unfocus();
        }

        HeaderPanel.CancelAnimations();
        HeaderDiamond.CancelAnimations();
        ContentStack.CancelAnimations();
        HeroPanel.CancelAnimations();
        ComposerCard.CancelAnimations();
        GuidelinesCard.CancelAnimations();
        AuthorInputBorder.CancelAnimations();
        EditorInputBorder.CancelAnimations();
        PublishButton.CancelAnimations();

        base.OnDisappearing();
    }

    // ============================================================
    // ENTRANCE ANIMATION
    // ============================================================

    private async Task PlayEntranceAnimationAsync()
    {
        if (_entranceAnimationRunning ||
            _entranceAnimationCompleted)
        {
            return;
        }

        _entranceAnimationRunning = true;

        try
        {
            HeaderPanel.Opacity = 0;
            HeaderPanel.TranslationY = -14;

            HeaderDiamond.Scale = 0.72;
            HeaderDiamond.Rotation = -22;

            HeroPanel.Opacity = 0;
            HeroPanel.Scale = 0.97;

            ComposerCard.Opacity = 0;
            ComposerCard.TranslationY = 20;

            GuidelinesCard.Opacity = 0;
            GuidelinesCard.TranslationY = 14;

            await Task.WhenAll(
                HeaderPanel.FadeToAsync(
                    1,
                    220,
                    Easing.CubicOut),

                HeaderPanel.TranslateToAsync(
                    0,
                    0,
                    220,
                    Easing.CubicOut),

                HeaderDiamond.ScaleToAsync(
                    1,
                    300,
                    Easing.SpringOut),

                HeaderDiamond.RotateToAsync(
                    0,
                    300,
                    Easing.CubicOut));

            await Task.WhenAll(
                HeroPanel.FadeToAsync(
                    1,
                    260,
                    Easing.CubicOut),

                HeroPanel.ScaleToAsync(
                    1,
                    270,
                    Easing.SpringOut));

            await Task.WhenAll(
                ComposerCard.FadeToAsync(
                    1,
                    290,
                    Easing.CubicOut),

                ComposerCard.TranslateToAsync(
                    0,
                    0,
                    290,
                    Easing.CubicOut));

            await Task.WhenAll(
                GuidelinesCard.FadeToAsync(
                    1,
                    240,
                    Easing.CubicOut),

                GuidelinesCard.TranslateToAsync(
                    0,
                    0,
                    240,
                    Easing.CubicOut));

            _entranceAnimationCompleted = true;
        }
        finally
        {
            HeaderPanel.Opacity = 1;
            HeaderPanel.TranslationY = 0;

            HeaderDiamond.Scale = 1;
            HeaderDiamond.Rotation = 0;

            HeroPanel.Opacity = 1;
            HeroPanel.Scale = 1;

            ComposerCard.Opacity = 1;
            ComposerCard.TranslationY = 0;

            GuidelinesCard.Opacity = 1;
            GuidelinesCard.TranslationY = 0;

            _entranceAnimationRunning = false;
        }
    }

    // ============================================================
    // TOPIC CHIP SELECTION
    // ============================================================

    private void OnViewModelPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName ==
            nameof(ComposePostViewModel.SelectedTag))
        {
            UpdateTopicChipVisuals();
        }
    }

    /// <summary>
    /// Reflects the selected topic chip: the active chip gets the brand
    /// accent; inactive chips stay muted glass.
    /// </summary>
    private void UpdateTopicChipVisuals()
    {
        if (TopicChipRow is null)
        {
            return;
        }

        string selected = _viewModel.SelectedTag;

        foreach (var child in TopicChipRow)
        {
            if (child is not Border chip ||
                chip.Content is not Label label)
            {
                continue;
            }

            bool isActive = label.Text?.Equals(
                selected,
                StringComparison.OrdinalIgnoreCase) == true;

            chip.BackgroundColor = isActive
                ? Color.FromArgb("#16E8B54A")
                : Color.FromArgb("#14FFFFFF");

            chip.Stroke = isActive
                ? Color.FromArgb("#8AE8B54A")
                : Color.FromArgb("#26FFFFFF");

            label.TextColor = isActive
                ? Colors.White
                : Color.FromArgb("#93A0BF");
        }
    }

    // ============================================================
    // BUTTON ANIMATION
    // ============================================================

    private async void OnAnimatedButtonClicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            if (sender is not VisualElement element)
            {
                return;
            }

            await AnimateButtonAsync(element);
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnAnimatedButtonClicked: " + exception.Message);
        }
    }

    private static async Task AnimateButtonAsync(
        VisualElement element)
    {
        element.CancelAnimations();

        try
        {
            await Task.WhenAll(
                element.ScaleToAsync(
                    0.92,
                    65,
                    Easing.CubicOut),

                element.FadeToAsync(
                    0.86,
                    65,
                    Easing.CubicOut));

            await Task.WhenAll(
                element.ScaleToAsync(
                    1,
                    145,
                    Easing.SpringOut),

                element.FadeToAsync(
                    1,
                    130,
                    Easing.CubicOut));
        }
        finally
        {
            element.Scale = 1;
            element.Opacity = 1;
        }
    }

    // ============================================================
    // INPUT FOCUS ANIMATIONS
    // ============================================================

    private async void OnAuthorEntryFocused(
        object? sender,
        FocusEventArgs e)
    {
        try
        {
            await AnimateInputFocusedAsync(
                AuthorInputBorder);
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnAuthorEntryFocused: " + exception.Message);
        }
    }

    private async void OnAuthorEntryUnfocused(
        object? sender,
        FocusEventArgs e)
    {
        try
        {
            await AnimateInputUnfocusedAsync(
                AuthorInputBorder);
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnAuthorEntryUnfocused: " + exception.Message);
        }
    }

    private async void OnContentEditorFocused(
        object? sender,
        FocusEventArgs e)
    {
        try
        {
            await AnimateInputFocusedAsync(
                EditorInputBorder);
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnContentEditorFocused: " + exception.Message);
        }
    }

    private async void OnContentEditorUnfocused(
        object? sender,
        FocusEventArgs e)
    {
        try
        {
            await AnimateInputUnfocusedAsync(
                EditorInputBorder);
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnContentEditorUnfocused: " + exception.Message);
        }
    }

    private static async Task AnimateInputFocusedAsync(
        Border border)
    {
        border.CancelAnimations();

        border.Stroke =
            Color.FromArgb("#70E8B54A");

        try
        {
            await border.ScaleToAsync(
                1.008,
                130,
                Easing.CubicOut);
        }
        finally
        {
            border.Scale = 1.008;
        }
    }

    private static async Task AnimateInputUnfocusedAsync(
        Border border)
    {
        border.CancelAnimations();

        try
        {
            await border.ScaleToAsync(
                1,
                120,
                Easing.CubicOut);
        }
        finally
        {
            border.Scale = 1;

            border.Stroke =
                Color.FromArgb("#35FFFFFF");
        }
    }

    // ============================================================
    // FLOATING DIAMONDS
    // ============================================================

    private void StartDiamondAnimation()
    {
        StopDiamondAnimation();

        var cancellation =
            new CancellationTokenSource();

        _diamondAnimationCancellation =
            cancellation;

        _ = RunDiamondAnimationAsync(
            cancellation.Token);
    }

    private void StopDiamondAnimation()
    {
        CancellationTokenSource? cancellation =
            Interlocked.Exchange(
                ref _diamondAnimationCancellation,
                null);

        if (cancellation is not null)
        {
            try
            {
                cancellation.Cancel();
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        FloatingDiamondOne.CancelAnimations();
        FloatingDiamondTwo.CancelAnimations();

        FloatingDiamondOne.TranslationY = 0;
        FloatingDiamondOne.Rotation = 0;

        FloatingDiamondTwo.TranslationY = 0;
        FloatingDiamondTwo.Rotation = 0;
    }

    private async Task RunDiamondAnimationAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            // Bounded decorative flourish - see AmbientAnimation.
            var beat = 0;

            while (AmbientAnimation.ShouldContinue(
                       beat++,
                       cancellationToken))
            {
                await Task.WhenAll(
                    FloatingDiamondOne.TranslateToAsync(
                        0,
                        -9,
                        1_250,
                        Easing.SinInOut),

                    FloatingDiamondOne.RotateToAsync(
                        15,
                        1_250,
                        Easing.SinInOut),

                    FloatingDiamondTwo.TranslateToAsync(
                        0,
                        8,
                        1_500,
                        Easing.SinInOut),

                    FloatingDiamondTwo.RotateToAsync(
                        -14,
                        1_500,
                        Easing.SinInOut));

                cancellationToken.ThrowIfCancellationRequested();

                await Task.WhenAll(
                    FloatingDiamondOne.TranslateToAsync(
                        0,
                        0,
                        1_250,
                        Easing.SinInOut),

                    FloatingDiamondOne.RotateToAsync(
                        0,
                        1_250,
                        Easing.SinInOut),

                    FloatingDiamondTwo.TranslateToAsync(
                        0,
                        0,
                        1_500,
                        Easing.SinInOut),

                    FloatingDiamondTwo.RotateToAsync(
                        0,
                        1_500,
                        Easing.SinInOut));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal when the page disappears.
        }
    }
}