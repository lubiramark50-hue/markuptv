using MarkUptv.Helpers;

namespace MarkUptv.Pages;

public partial class MoviesPage : ContentPage
{
    private bool _entranceAnimationCompleted;

    private CancellationTokenSource? _backgroundAnimationCancellation;

    public MoviesPage(
        ViewModels.MoviesViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));

        MarkUptv.Helpers.PlaybackCoordinator.Register(ChannelPlayer);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            if (!_entranceAnimationCompleted)
            {
                await PlayEntranceAnimationAsync();
            }

            StartBackgroundAnimation();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"MoviesPage animation failed: {exception}");
        }
    }

    protected override void OnDisappearing()
    {
        StopBackgroundAnimation();
        CancelPageAnimations();
        MarkUptv.Helpers.PlaybackCoordinator.Release(ChannelPlayer);

        base.OnDisappearing();
    }

    private async Task PlayEntranceAnimationAsync()
    {
        HeaderPanel.Opacity = 0;
        HeaderPanel.TranslationY = -13;

        PlayerPanel.Opacity = 0;
        PlayerPanel.Scale = 0.975;

        ChannelPanel.Opacity = 0;
        ChannelPanel.TranslationX = 18;

        try
        {
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
                    290,
                    Easing.CubicOut),

                PlayerPanel.ScaleToAsync(
                    1,
                    290,
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
        finally
        {
            HeaderPanel.Opacity = 1;
            HeaderPanel.TranslationY = 0;

            PlayerPanel.Opacity = 1;
            PlayerPanel.Scale = 1;

            ChannelPanel.Opacity = 1;
            ChannelPanel.TranslationX = 0;
        }
    }

    private async void OnAnimatedButtonClicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            if (sender is Microsoft.Maui.Controls.VisualElement element)
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
            if (sender is not Microsoft.Maui.Controls.VisualElement element ||
                element.BindingContext is not Models.TvChannel channel ||
                BindingContext is not ViewModels.MoviesViewModel viewModel)
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
        Microsoft.Maui.Controls.VisualElement element)
    {
        element.CancelAnimations();

        try
        {
            await Task.WhenAll(
                element.ScaleToAsync(
                    0.96,
                    75,
                    Easing.CubicOut),

                element.FadeToAsync(
                    0.88,
                    75,
                    Easing.CubicOut));

            await Task.WhenAll(
                element.ScaleToAsync(
                    1,
                    150,
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
        SearchBorder.CancelAnimations();

        SearchBorder.Stroke =
            Color.FromArgb("#80FFD95A");

        try
        {
            await SearchBorder.ScaleToAsync(
                1.008,
                130,
                Easing.CubicOut);
        }
        finally
        {
            SearchBorder.Scale = 1.008;
        }
    }

    private async void OnSearchUnfocused(
        object? sender,
        FocusEventArgs e)
    {
        SearchBorder.CancelAnimations();

        try
        {
            await SearchBorder.ScaleToAsync(
                1,
                120,
                Easing.CubicOut);
        }
        finally
        {
            SearchBorder.Scale = 1;

            SearchBorder.Stroke =
                Color.FromArgb("#35FFFFFF");
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

    private void CancelPageAnimations()
    {
        HeaderPanel.CancelAnimations();
        PlayerPanel.CancelAnimations();
        ChannelPanel.CancelAnimations();
        SearchBorder.CancelAnimations();
        FloatingDiamondOne.CancelAnimations();
        FloatingDiamondTwo.CancelAnimations();
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
                        1_350,
                        Easing.SinInOut),

                    FloatingDiamondOne.RotateToAsync(
                        15,
                        1_350,
                        Easing.SinInOut),

                    FloatingDiamondTwo.TranslateToAsync(
                        0,
                        8,
                        1_550,
                        Easing.SinInOut),

                    FloatingDiamondTwo.RotateToAsync(
                        -14,
                        1_550,
                        Easing.SinInOut));

                cancellationToken.ThrowIfCancellationRequested();

                await Task.WhenAll(
                    FloatingDiamondOne.TranslateToAsync(
                        0,
                        0,
                        1_350,
                        Easing.SinInOut),

                    FloatingDiamondOne.RotateToAsync(
                        0,
                        1_350,
                        Easing.SinInOut),

                    FloatingDiamondTwo.TranslateToAsync(
                        0,
                        0,
                        1_550,
                        Easing.SinInOut),

                    FloatingDiamondTwo.RotateToAsync(
                        0,
                        1_550,
                        Easing.SinInOut));
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Normal cancellation when the page disappears.
        }
        finally
        {
            FloatingDiamondOne.TranslationY = 0;
            FloatingDiamondOne.Rotation = 0;

            FloatingDiamondTwo.TranslationY = 0;
            FloatingDiamondTwo.Rotation = 0;
        }
    }
}