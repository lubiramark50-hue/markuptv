using Microsoft.Maui.Controls;

using MarkUptv.Helpers;

namespace MarkUptv.Pages;

public partial class News : ContentPage
{
    private bool _entranceAnimationCompleted;

    private CancellationTokenSource?
        _backgroundAnimationCancellation;

    public News(
        ViewModels.NewsViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));

        MarkUptv.Helpers.PlaybackCoordinator.Register(ChannelPlayer);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_entranceAnimationCompleted)
        {
            await PlayEntranceAnimationAsync();
        }

        StartBackgroundAnimation();
    }

    protected override void OnDisappearing()
    {
        StopBackgroundAnimation();
        MarkUptv.Helpers.PlaybackCoordinator.Release(ChannelPlayer);

        HeaderPanel.CancelAnimations();
        WorkspaceGrid.CancelAnimations();
        PlayerPanel.CancelAnimations();
        ChannelPanel.CancelAnimations();
        LiveIndicator.CancelAnimations();
        SearchBorder.CancelAnimations();

        base.OnDisappearing();
    }

    private async Task PlayEntranceAnimationAsync()
    {
        HeaderPanel.Opacity = 0;
        HeaderPanel.TranslationY = -13;

        PlayerPanel.Opacity = 0;
        PlayerPanel.TranslationX = -20;

        ChannelPanel.Opacity = 0;
        ChannelPanel.TranslationX = 20;

        await Task.WhenAll(
            HeaderPanel.FadeToAsync(
                1,
                220,
                Easing.CubicOut),

            HeaderPanel.TranslateToAsync(
                0,
                0,
                220,
                Easing.CubicOut));

        await Task.WhenAll(
            PlayerPanel.FadeToAsync(
                1,
                280,
                Easing.CubicOut),

            PlayerPanel.TranslateToAsync(
                0,
                0,
                280,
                Easing.CubicOut),

            ChannelPanel.FadeToAsync(
                1,
                300,
                Easing.CubicOut),

            ChannelPanel.TranslateToAsync(
                0,
                0,
                300,
                Easing.CubicOut));

        _entranceAnimationCompleted = true;
    }

    private async void OnAnimatedButtonClicked(
        object? sender,
        EventArgs e)
    {
        if (sender is VisualElement element)
        {
            await AnimateElementAsync(element);
        }
    }

    private async void OnChannelCardTapped(
        object? sender,
        TappedEventArgs e)
    {
        if (sender is not VisualElement element ||
            element.BindingContext is not Models.TvChannel channel ||
            BindingContext is not ViewModels.NewsViewModel viewModel)
        {
            return;
        }

        await AnimateElementAsync(element);

        viewModel.PlayChannelCommand.Execute(channel);
    }

    private static async Task AnimateElementAsync(
        VisualElement element)
    {
        element.CancelAnimations();

        try
        {
            await Task.WhenAll(
                element.ScaleToAsync(
                    0.965,
                    70,
                    Easing.CubicOut),

                element.FadeToAsync(
                    0.88,
                    70,
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

    private async void OnSearchFocused(
        object? sender,
        FocusEventArgs e)
    {
        SearchBorder.Stroke =
            Color.FromArgb("#70E8B54A");

        await SearchBorder.ScaleToAsync(
            1.008,
            130,
            Easing.CubicOut);
    }

    private async void OnSearchUnfocused(
        object? sender,
        FocusEventArgs e)
    {
        await SearchBorder.ScaleToAsync(
            1,
            120,
            Easing.CubicOut);

        SearchBorder.Stroke =
            Color.FromArgb("#35FFFFFF");
    }

    private void StartBackgroundAnimation()
    {
        StopBackgroundAnimation();

        var cancellation =
            new CancellationTokenSource();

        _backgroundAnimationCancellation =
            cancellation;

        _ = AnimateBackgroundAsync(
            cancellation.Token);
    }

    private void StopBackgroundAnimation()
    {
        CancellationTokenSource? cancellation =
            Interlocked.Exchange(
                ref _backgroundAnimationCancellation,
                null);

        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        FloatingDiamondOne.CancelAnimations();
        FloatingDiamondTwo.CancelAnimations();
        LiveIndicator.CancelAnimations();

        FloatingDiamondOne.TranslationY = 0;
        FloatingDiamondOne.Rotation = 0;

        FloatingDiamondTwo.TranslationY = 0;
        FloatingDiamondTwo.Rotation = 0;

        LiveIndicator.Opacity = 1;
    }

    private async Task AnimateBackgroundAsync(
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
                        -8,
                        1_300,
                        Easing.SinInOut),

                    FloatingDiamondOne.RotateToAsync(
                        13,
                        1_300,
                        Easing.SinInOut),

                    FloatingDiamondTwo.TranslateToAsync(
                        0,
                        8,
                        1_550,
                        Easing.SinInOut),

                    FloatingDiamondTwo.RotateToAsync(
                        -14,
                        1_550,
                        Easing.SinInOut),

                    LiveIndicator.FadeToAsync(
                        0.58,
                        850,
                        Easing.SinInOut));

                cancellationToken.ThrowIfCancellationRequested();

                await Task.WhenAll(
                    FloatingDiamondOne.TranslateToAsync(
                        0,
                        0,
                        1_300,
                        Easing.SinInOut),

                    FloatingDiamondOne.RotateToAsync(
                        0,
                        1_300,
                        Easing.SinInOut),

                    FloatingDiamondTwo.TranslateToAsync(
                        0,
                        0,
                        1_550,
                        Easing.SinInOut),

                    FloatingDiamondTwo.RotateToAsync(
                        0,
                        1_550,
                        Easing.SinInOut),

                    LiveIndicator.FadeToAsync(
                        1,
                        850,
                        Easing.SinInOut));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal when the page disappears.
        }
    }
}