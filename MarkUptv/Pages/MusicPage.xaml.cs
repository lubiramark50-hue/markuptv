

using MarkUptv.Helpers;

namespace MarkUptv.Pages;

public partial class MusicPage : ContentPage
{
    private bool _entranceAnimationCompleted;

    private CancellationTokenSource?
        _backgroundAnimationCancellation;

    public MusicPage(
        ViewModels.MusicViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));

        MarkUptv.Helpers.PlaybackCoordinator.Register(ChannelPlayer);
    }

    protected override async void OnAppearing()
    {
        try
        {
            base.OnAppearing();

            if (!_entranceAnimationCompleted)
            {
                await PlayEntranceAnimationAsync();
            }

            StartBackgroundAnimation();
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnAppearing: " + exception.Message);
        }
    }

    protected override void OnDisappearing()
    {
        StopBackgroundAnimation();
        MarkUptv.Helpers.PlaybackCoordinator.Release(ChannelPlayer);

        HeaderPanel.CancelAnimations();
        PlayerPanel.CancelAnimations();
        ChannelPanel.CancelAnimations();
        SearchBorder.CancelAnimations();
        MusicOrb.CancelAnimations();

        base.OnDisappearing();
    }

    private async Task PlayEntranceAnimationAsync()
    {
        HeaderPanel.Opacity = 0;
        HeaderPanel.TranslationY = -13;

        PlayerPanel.Opacity = 0;
        PlayerPanel.Scale = 0.97;

        ChannelPanel.Opacity = 0;
        ChannelPanel.TranslationX = 18;

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
                285,
                Easing.CubicOut),

            PlayerPanel.ScaleToAsync(
                1,
                285,
                Easing.SpringOut),

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
        try
        {
            if (sender is VisualElement element)
            {
                await AnimateElementAsync(element);
            }
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnAnimatedButtonClicked: " + exception.Message);
        }
    }

    private async void OnChannelCardTapped(
        object? sender,
        TappedEventArgs e)
    {
        try
        {
            if (sender is not VisualElement element ||
                element.BindingContext is not Models.TvChannel channel ||
                BindingContext is not ViewModels.MusicViewModel viewModel)
            {
                return;
            }

            await AnimateElementAsync(element);

            viewModel.PlayChannelCommand.Execute(channel);
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnChannelCardTapped: " + exception.Message);
        }
    }

    private static async Task AnimateElementAsync(
        VisualElement element)
    {
        element.CancelAnimations();

        try
        {
            await Task.WhenAll(
                element.ScaleToAsync(
                    0.96,
                    70,
                    Easing.CubicOut),

                element.FadeToAsync(
                    0.88,
                    70,
                    Easing.CubicOut));

            await Task.WhenAll(
                element.ScaleToAsync(
                    1,
                    150,
                    Easing.SpringOut),

                element.FadeToAsync(
                    1,
                    135,
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
        try
        {
            SearchBorder.Stroke =
                Color.FromArgb("#80A63CFF");

            await SearchBorder.ScaleToAsync(
                1.008,
                130,
                Easing.CubicOut);
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnSearchFocused: " + exception.Message);
        }
    }

    private async void OnSearchUnfocused(
        object? sender,
        FocusEventArgs e)
    {
        try
        {
            await SearchBorder.ScaleToAsync(
                1,
                120,
                Easing.CubicOut);

            SearchBorder.Stroke =
                Color.FromArgb("#35FFFFFF");
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnSearchUnfocused: " + exception.Message);
        }
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
        MusicOrb.CancelAnimations();

        FloatingDiamondOne.TranslationY = 0;
        FloatingDiamondOne.Rotation = 0;

        FloatingDiamondTwo.TranslationY = 0;
        FloatingDiamondTwo.Rotation = 0;

        MusicOrb.Scale = 1;
        MusicOrb.Rotation = 0;
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
                        -9,
                        1_250,
                        Easing.SinInOut),

                    FloatingDiamondOne.RotateToAsync(
                        12,
                        1_250,
                        Easing.SinInOut),

                    FloatingDiamondTwo.TranslateToAsync(
                        0,
                        8,
                        1_500,
                        Easing.SinInOut),

                    FloatingDiamondTwo.RotateToAsync(
                        -13,
                        1_500,
                        Easing.SinInOut),

                    MusicOrb.ScaleToAsync(
                        1.06,
                        1_100,
                        Easing.SinInOut),

                    MusicOrb.RotateToAsync(
                        7,
                        1_100,
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
                        Easing.SinInOut),

                    MusicOrb.ScaleToAsync(
                        1,
                        1_100,
                        Easing.SinInOut),

                    MusicOrb.RotateToAsync(
                        0,
                        1_100,
                        Easing.SinInOut));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal when leaving the page.
        }
    }
}