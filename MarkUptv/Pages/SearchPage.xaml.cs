using MarkUptv.ViewModels;
using MauiVisualElement =
    Microsoft.Maui.Controls.VisualElement;

using MarkUptv.Helpers;

namespace MarkUptv.Pages;

public partial class SearchPage :
    ContentPage,
    IQueryAttributable
{
    private readonly SearchViewModel _viewModel;

    private bool _entranceAnimationCompleted;
    private bool _entranceAnimationRunning;

    private CancellationTokenSource?
        _diamondAnimationCancellation;

    public SearchPage(
        SearchViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));

        BindingContext = viewModel;
    }

    public void ApplyQueryAttributes(
        IDictionary<string, object> query)
    {
        _viewModel.ApplyQueryAttributes(query);
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

            await Task.Delay(120);

            if (!SearchEntry.IsFocused)
            {
                SearchEntry.Focus();
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[SearchPage] Appearance failed: {exception}");
        }
    }

    protected override void OnDisappearing()
    {
        if (SearchEntry.IsFocused)
        {
            SearchEntry.Unfocus();
        }

        _viewModel.OnPageDisappearing();

        StopDiamondAnimation();

        HeaderPanel.CancelAnimations();
        SearchPanel.CancelAnimations();
        TabsPanel.CancelAnimations();
        ResultsPanel.CancelAnimations();
        HeaderDiamond.CancelAnimations();
        SearchEntryBorder.CancelAnimations();

        base.OnDisappearing();
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
            HeaderPanel.Opacity = 0;
            HeaderPanel.TranslationY = -14;

            SearchPanel.Opacity = 0;
            SearchPanel.Scale = 0.97;

            TabsPanel.Opacity = 0;
            TabsPanel.TranslationY = 12;

            ResultsPanel.Opacity = 0;
            ResultsPanel.TranslationY = 16;

            HeaderDiamond.Scale = 0.75;
            HeaderDiamond.Rotation = -18;

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
                SearchPanel.FadeToAsync(
                    1,
                    260,
                    Easing.CubicOut),

                SearchPanel.ScaleToAsync(
                    1,
                    260,
                    Easing.SpringOut));

            await Task.WhenAll(
                TabsPanel.FadeToAsync(
                    1,
                    230,
                    Easing.CubicOut),

                TabsPanel.TranslateToAsync(
                    0,
                    0,
                    230,
                    Easing.CubicOut),

                ResultsPanel.FadeToAsync(
                    1,
                    280,
                    Easing.CubicOut),

                ResultsPanel.TranslateToAsync(
                    0,
                    0,
                    280,
                    Easing.CubicOut));

            _entranceAnimationCompleted = true;
        }
        finally
        {
            HeaderPanel.Opacity = 1;
            HeaderPanel.TranslationY = 0;

            SearchPanel.Opacity = 1;
            SearchPanel.Scale = 1;

            TabsPanel.Opacity = 1;
            TabsPanel.TranslationY = 0;

            ResultsPanel.Opacity = 1;
            ResultsPanel.TranslationY = 0;

            HeaderDiamond.Scale = 1;
            HeaderDiamond.Rotation = 0;

            _entranceAnimationRunning = false;
        }
    }

    private async void OnResultTapped(
        object? sender,
        TappedEventArgs e)
    {
        try
        {
            if (sender is not MauiVisualElement resultCard ||
                resultCard.BindingContext is not Models.SearchResult result)
            {
                return;
            }

            await AnimateResultCardAsync(
                resultCard);

            _viewModel.OpenResultCommand.Execute(result);
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnResultTapped: " + exception.Message);
        }
    }

    private static async Task AnimateResultCardAsync(
        MauiVisualElement element)
    {
        element.CancelAnimations();

        try
        {
            await Task.WhenAll(
                element.ScaleToAsync(
                    0.972,
                    70,
                    Easing.CubicOut),

                element.FadeToAsync(
                    0.86,
                    70,
                    Easing.CubicOut));

            await Task.WhenAll(
                element.ScaleToAsync(
                    1,
                    155,
                    Easing.SpringOut),

                element.FadeToAsync(
                    1,
                    140,
                    Easing.CubicOut));
        }
        finally
        {
            element.Scale = 1;
            element.Opacity = 1;
        }
    }

    private async void OnAnimatedButtonClicked(
        object? sender,
        EventArgs e)
    {
        if (sender is not MauiVisualElement element)
        {
            return;
        }

        element.CancelAnimations();

        try
        {
            await element.ScaleToAsync(
                0.91,
                65,
                Easing.CubicOut);

            await element.ScaleToAsync(
                1,
                135,
                Easing.SpringOut);
        }
        finally
        {
            element.Scale = 1;
        }
    }

    private async void OnSearchEntryFocused(
        object? sender,
        FocusEventArgs e)
    {
        SearchEntryBorder.CancelAnimations();

        SearchEntryBorder.Stroke =
            Color.FromArgb("#8054B7FF");

        await SearchEntryBorder.ScaleToAsync(
            1.012,
            130,
            Easing.CubicOut);
    }

    private async void OnSearchEntryUnfocused(
        object? sender,
        FocusEventArgs e)
    {
        SearchEntryBorder.CancelAnimations();

        try
        {
            await SearchEntryBorder.ScaleToAsync(
                1,
                120,
                Easing.CubicOut);
        }
        finally
        {
            SearchEntryBorder.Scale = 1;

            SearchEntryBorder.Stroke =
                Color.FromArgb("#4CFFFFFF");
        }
    }

    private void StartDiamondAnimation()
    {
        StopDiamondAnimation();

        var cancellation =
            new CancellationTokenSource();

        _diamondAnimationCancellation =
            cancellation;

        _ = AnimateDiamondsAsync(
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
            cancellation.Cancel();
            cancellation.Dispose();
        }

        FloatingDiamondOne.CancelAnimations();
        FloatingDiamondTwo.CancelAnimations();

        FloatingDiamondOne.TranslationY = 0;
        FloatingDiamondOne.Rotation = 0;

        FloatingDiamondTwo.TranslationY = 0;
        FloatingDiamondTwo.Rotation = 0;
    }

    private async Task AnimateDiamondsAsync(
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
                        12,
                        1_300,
                        Easing.SinInOut),

                    FloatingDiamondTwo.TranslateToAsync(
                        0,
                        8,
                        1_550,
                        Easing.SinInOut),

                    FloatingDiamondTwo.RotateToAsync(
                        -13,
                        1_550,
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
                        Easing.SinInOut));
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Normal when leaving the page.
        }
    }
}