using MarkUptv.ViewModels;

using MarkUptv.Helpers;

namespace MarkUptv.Pages;

public partial class Cartoonspage : ContentPage
{
    private readonly CartoonViewModel _viewModel;

    private bool _entranceAnimationCompleted;
    private bool _entranceAnimationRunning;

    private CancellationTokenSource? _equalizerCts;

    public Cartoonspage(
        CartoonViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(
            viewModel);

        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = viewModel;

        MarkUptv.Helpers.PlaybackCoordinator.Register(CartoonPlayer);

        _viewModel.PropertyChanged +=
            OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            Task loadingTask =
                _viewModel
                    .InitializePageCommand
                    .ExecuteAsync(null);

            if (!_entranceAnimationCompleted &&
                !_entranceAnimationRunning)
            {
                await PlayEntranceAnimationAsync();
            }

            await loadingTask;
        }
        catch (OperationCanceledException)
        {
            // Normal page navigation cancellation.
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Cartoonspage] Page initialization failed: " +
                $"{exception}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        _viewModel.OnPageDisappearing();

        MarkUptv.Helpers.PlaybackCoordinator.Release(CartoonPlayer);

        StopEqualizer();

        try
        {
            CartoonPlayer.Stop();
        }
        catch
        {
            // MediaElement may already be disposed.
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(
                e.PropertyName,
                nameof(CartoonViewModel.IsStreaming),
                StringComparison.Ordinal))
        {
            if (_viewModel.IsStreaming)
            {
                StartEqualizer();
            }
            else
            {
                StopEqualizer();
            }
        }
    }

    private void StartEqualizer()
    {
        StopEqualizer();

        var cancellation =
            new CancellationTokenSource();

        _equalizerCts = cancellation;

        _ = AnimateEqualizerAsync(
            cancellation.Token);
    }

    private void StopEqualizer()
    {
        CancellationTokenSource? cancellation =
            Interlocked.Exchange(
                ref _equalizerCts,
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

        LiveEqBarOne.CancelAnimations();
        LiveEqBarTwo.CancelAnimations();
        LiveEqBarThree.CancelAnimations();

        LiveEqBarOne.ScaleY = 1;
        LiveEqBarTwo.ScaleY = 1;
        LiveEqBarThree.ScaleY = 1;
    }

    private async Task AnimateEqualizerAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            // Bounded decorative flourish - see AmbientAnimation for why this
            // must not loop forever on a real device.
            var beat = 0;

            while (AmbientAnimation.ShouldContinue(
                       beat++,
                       cancellationToken))
            {
                await Task.WhenAll(
                    LiveEqBarOne.ScaleYToAsync(
                        2.4,
                        140,
                        Easing.SinInOut),

                    LiveEqBarTwo.ScaleYToAsync(
                        0.55,
                        160,
                        Easing.SinInOut),

                    LiveEqBarThree.ScaleYToAsync(
                        1.8,
                        130,
                        Easing.SinInOut));

                cancellationToken.ThrowIfCancellationRequested();

                await Task.WhenAll(
                    LiveEqBarOne.ScaleYToAsync(
                        0.6,
                        150,
                        Easing.SinInOut),

                    LiveEqBarTwo.ScaleYToAsync(
                        1.9,
                        140,
                        Easing.SinInOut),

                    LiveEqBarThree.ScaleYToAsync(
                        0.8,
                        160,
                        Easing.SinInOut));

                cancellationToken.ThrowIfCancellationRequested();

                await Task.WhenAll(
                    LiveEqBarOne.ScaleYToAsync(
                        1.7,
                        130,
                        Easing.SinInOut),

                    LiveEqBarTwo.ScaleYToAsync(
                        0.9,
                        150,
                        Easing.SinInOut),

                    LiveEqBarThree.ScaleYToAsync(
                        2.2,
                        140,
                        Easing.SinInOut));

                cancellationToken.ThrowIfCancellationRequested();
            }

            // The equaliser is decorative: once the flourish is over the bars
            // settle so the header stops consuming frames.
            LiveEqBarOne.ScaleY = 1;
            LiveEqBarTwo.ScaleY = 1;
            LiveEqBarThree.ScaleY = 1;
        }
        catch (OperationCanceledException)
        {
            // Normal when the stream stops or the page disappears.
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Cartoonspage] Equalizer stopped: {exception.Message}");
        }
        finally
        {
            LiveEqBarOne.ScaleY = 1;
            LiveEqBarTwo.ScaleY = 1;
            LiveEqBarThree.ScaleY = 1;
        }
    }

    private void OnMediaOpened(
        object? sender,
        EventArgs e)
    {
        if (_viewModel.MediaReadyCommand.CanExecute(null))
        {
            _viewModel.MediaReadyCommand.Execute(null);
        }
    }

    private void OnMediaFailed(
        object? sender,
        EventArgs e)
    {
        if (_viewModel.MediaFailedCommand.CanExecute(null))
        {
            _viewModel.MediaFailedCommand.Execute(null);
        }
    }

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
            HeaderPanel.CancelAnimations();
            PlayerPanel.CancelAnimations();
            ChannelPanel.CancelAnimations();

            HeaderPanel.Opacity = 0;
            HeaderPanel.TranslationY = -15;

            PlayerPanel.Opacity = 0;
            PlayerPanel.TranslationX = -18;

            ChannelPanel.Opacity = 0;
            ChannelPanel.TranslationX = 18;

            await Task.WhenAll(
                HeaderPanel.FadeToAsync(
                    1,
                    230,
                    Easing.CubicOut),

                HeaderPanel.TranslateToAsync(
                    0,
                    0,
                    230,
                    Easing.CubicOut));

            await Task.WhenAll(
                PlayerPanel.FadeToAsync(
                    1,
                    290,
                    Easing.CubicOut),

                PlayerPanel.TranslateToAsync(
                    0,
                    0,
                    290,
                    Easing.CubicOut),

                ChannelPanel.FadeToAsync(
                    1,
                    320,
                    Easing.CubicOut),

                ChannelPanel.TranslateToAsync(
                    0,
                    0,
                    320,
                    Easing.CubicOut));

            _entranceAnimationCompleted = true;
        }
        finally
        {
            HeaderPanel.Opacity = 1;
            HeaderPanel.TranslationY = 0;

            PlayerPanel.Opacity = 1;
            PlayerPanel.TranslationX = 0;

            ChannelPanel.Opacity = 1;
            ChannelPanel.TranslationX = 0;

            _entranceAnimationRunning = false;
        }
    }

    private async void OnAnimatedButtonClicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            if (sender is VisualElement element)
            {
                await AnimatePressAsync(element);
            }
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnAnimatedButtonClicked: " + exception.Message);
        }
    }

    private async void OnCartoonCardTapped(
        object? sender,
        TappedEventArgs e)
    {
        try
        {
            if (sender is not VisualElement card ||
                card.BindingContext is not Models.TvChannel channel)
            {
                return;
            }

            await AnimateCardAsync(card);

            _viewModel.PlayChannelCommand.Execute(channel);
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnCartoonCardTapped: " + exception.Message);
        }
    }

    private async void OnMascotTapped(
        object? sender,
        TappedEventArgs e)
    {
        try
        {
            await AnimateBounceAsync(
                MascotButton);
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnMascotTapped: " + exception.Message);
        }
    }

    private async void OnCrownTapped(
        object? sender,
        TappedEventArgs e)
    {
        try
        {
            await AnimateSpinAsync(
                CrownButton);
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnCrownTapped: " + exception.Message);
        }
    }

    private async void OnEmptyPlayerTapped(
        object? sender,
        TappedEventArgs e)
    {
        try
        {
            await AnimateBounceAsync(
                EmptyPlayerPlayButton);
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnEmptyPlayerTapped: " + exception.Message);
        }
    }

    private static async Task AnimatePressAsync(
        VisualElement element)
    {
        element.CancelAnimations();

        try
        {
            await element.ScaleToAsync(
                0.91,
                70,
                Easing.CubicOut);

            await element.ScaleToAsync(
                1,
                130,
                Easing.SpringOut);
        }
        finally
        {
            element.Scale = 1;
        }
    }

    private static async Task AnimateCardAsync(
        VisualElement element)
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
                    0.86,
                    75,
                    Easing.CubicOut));

            await Task.WhenAll(
                element.ScaleToAsync(
                    1,
                    145,
                    Easing.SpringOut),

                element.FadeToAsync(
                    1,
                    145,
                    Easing.CubicOut));
        }
        finally
        {
            element.Scale = 1;
            element.Opacity = 1;
        }
    }

    private static async Task AnimateBounceAsync(
        VisualElement element)
    {
        element.CancelAnimations();

        try
        {
            await element.ScaleToAsync(
                1.13,
                90,
                Easing.CubicOut);

            await element.ScaleToAsync(
                0.96,
                80,
                Easing.CubicInOut);

            await element.ScaleToAsync(
                1,
                140,
                Easing.SpringOut);
        }
        finally
        {
            element.Scale = 1;
        }
    }

    private static async Task AnimateSpinAsync(
        VisualElement element)
    {
        element.CancelAnimations();

        try
        {
            await Task.WhenAll(
                element.RotateToAsync(
                    15,
                    90,
                    Easing.CubicOut),

                element.ScaleToAsync(
                    1.1,
                    90,
                    Easing.CubicOut));

            await element.RotateToAsync(
                -10,
                85,
                Easing.CubicInOut);

            await Task.WhenAll(
                element.RotateToAsync(
                    0,
                    140,
                    Easing.SpringOut),

                element.ScaleToAsync(
                    1,
                    140,
                    Easing.SpringOut));
        }
        finally
        {
            element.Rotation = 0;
            element.Scale = 1;
        }
    }
}