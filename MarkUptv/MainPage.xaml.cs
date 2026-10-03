using MarkUptv.Models;
using MarkUptv.Services;
using MarkUptv.ViewModels;
using Microsoft.Maui.Controls;
using System.ComponentModel;
using System.Windows.Input;

namespace MarkUptv;

public partial class MainPage : ContentPage
{
    private readonly MainPageViewModel _viewModel;
    private readonly AdMobService _adMobService;

    private bool _entranceAnimationCompleted;
    private bool _entranceAnimationRunning;

    private CancellationTokenSource?
        _skyAnimationCancellation;

    public MainPage(
        MainPageViewModel viewModel,
        AdMobService adMobService)
    {
        Services.StartupTrace.Mark("MainPage ctor enter");

        InitializeComponent();

        Services.StartupTrace.Mark("MainPage.InitializeComponent done");

        _viewModel = viewModel
            ?? throw new ArgumentNullException(
                nameof(viewModel));

        _adMobService = adMobService
            ?? throw new ArgumentNullException(
                nameof(adMobService));

        BindingContext =
            _viewModel;

        // The AI chat modal is a ~565-line subtree that used to be part of the
        // eager visual tree even though it stays invisible until the user opens
        // chat. It now inflates once, on first open, so InitializeComponent
        // skips its whole construction cost.
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private bool _aiChatModalInflated;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainPageViewModel.IsChatOpen))
        {
            EnsureAiChatModal();
        }
    }

    /// <summary>
    /// Inflates the AI chat modal into its host exactly once, the first time
    /// chat opens. Safe to call repeatedly; each extra call is a no-op.
    /// </summary>
    private void EnsureAiChatModal()
    {
        if (_aiChatModalInflated)
        {
            return;
        }

        _aiChatModalInflated = true;

        try
        {
            if (Resources["AiChatModalTemplate"] is DataTemplate template &&
                template.CreateContent() is View modal)
            {
                AiChatModalHost.Content = modal;
                Services.StartupTrace.Mark("AI chat modal inflated on demand");
            }
            else
            {
                Services.StartupTrace.Mark("AI chat modal template missing or wrong type");
            }
        }
        catch (Exception ex)
        {
            Services.StartupTrace.Mark(
                $"AI chat modal inflate failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ============================================================
    // TEMPLATE TAP DISPATCH
    // BindableLayout templates cannot resolve commands on the page
    // BindingContext through x:Reference, so taps are wired to these
    // code-behind handlers that execute the matching view-model command.
    // ============================================================

    private static void ExecuteViewModelCommand(
        ICommand command,
        object? parameter)
    {
        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }

    private void OnDashboardFeatureCardTapped(
        object? sender,
        TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext
                is DashboardFeature feature)
        {
            ExecuteViewModelCommand(
                _viewModel.OpenDashboardFeatureCommand,
                feature);
        }
    }

    private void OnCategoryFeatureTapped(
        object? sender,
        TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext
                is DashboardFeature feature)
        {
            ExecuteViewModelCommand(
                _viewModel.OpenDashboardFeatureCommand,
                feature);
        }
    }

    private void OnAiHighlightCardTapped(
        object? sender,
        TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext
                is AiHighlightItem item)
        {
            ExecuteViewModelCommand(
                _viewModel.OpenAiHighlightCommand,
                item);
        }
    }

    private void OnTrendingChannelTapped(
        object? sender,
        TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext
                is TvChannel channel)
        {
            ExecuteViewModelCommand(
                _viewModel.PlayChannelCommand,
                channel);
        }
    }

    private void OnRecentChannelTapped(
        object? sender,
        TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext
                is TvChannel channel)
        {
            ExecuteViewModelCommand(
                _viewModel.PlayRecentCommand,
                channel);
        }
    }

    private void OnSocialPostTapped(
        object? sender,
        TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext
                is SocialPost post)
        {
            ExecuteViewModelCommand(
                _viewModel.OpenPostCommand,
                post);
        }
    }

    private void OnNewsItemTapped(
        object? sender,
        TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext
                is NewsArticle article)
        {
            ExecuteViewModelCommand(
                _viewModel.OpenNewsItemCommand,
                article);
        }
    }

    private void OnAiRecommendedChannelTapped(
        object? sender,
        TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext
                is AiRecommendedChannel channel)
        {
            ExecuteViewModelCommand(
                _viewModel.PlayAiRecommendedChannelCommand,
                channel);
        }
    }

    private void OnQuickChipTapped(
        object? sender,
        TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext
                is string chip)
        {
            ExecuteViewModelCommand(
                _viewModel.SendQuickChipCommand,
                chip);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            // Prefetch the modal once the page is on screen so the first
            // open never pays the inflation cost mid-interaction.
            Dispatcher.Dispatch(EnsureAiChatModal);

            Task loadingTask =
                _viewModel
                    .InitializeCommand
                    .ExecuteAsync(null);

            if (!_entranceAnimationCompleted &&
                !_entranceAnimationRunning)
            {
                await PlayEntranceAnimationAsync();
            }

            StartSkyAnimation();

            // The banner is a third-party view that performs network + Play
            // Services work when it is created. Building it inline stalls the
            // very first frame (and can trigger Android's ANR watchdog on cold
            // start), so it is queued after the shell has painted and is
            // created off the critical path.
            Dispatcher.Dispatch(
                TryShowBanner);

            // Below-the-fold sections are realised one per UI-thread pass (see the
            // DashboardChunk1..4 DataTemplates in MainPage.xaml). Inflating them
            // together with the page is a single multi-second block that trips
            // Android's input watchdog on cold start; a pass per section keeps every
            // block short enough for input to interleave.
            ScheduleDeferredDashboardChunks();

            await loadingTask;
        }
        catch (OperationCanceledException)
        {
            // Normal when the page disappears.
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[MainPage] Appearance failure: {exception}");
        }
    }

    protected override void OnDisappearing()
    {
        _viewModel.OnPageDisappearing();

        StopSkyAnimation();

        ContentStack.CancelAnimations();
        HeaderPanel.CancelAnimations();
        SearchPanel.CancelAnimations();
        HeroPanel.CancelAnimations();
        FloatingDiamondOne.CancelAnimations();
        FloatingDiamondTwo.CancelAnimations();

        base.OnDisappearing();
    }

    // ============================================================
    // ADVERTISEMENT
    // ============================================================

    private void TryShowBanner()
    {
        try
        {
            _adMobService.ShowBanner(
                BannerContainer);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[MainPage] Advertisement failure: " +
                $"{exception.Message}");
        }
    }

    // ============================================================
    // ENTRANCE ANIMATION
    // ============================================================

    //  One section per pass: the passes are sized by what they cost, not by what they
    //  contain — a channel or news card realizes off its collection, which is far more
    //  expensive than inflating a plain element, so each data-backed section gets its
    //  own pass. Order matches the visual order of the dashboard.
    private static readonly string[] DeferredDashboardChunks =
    {
        "DashboardChunk1",   // AI hub: header + recommendation zone
        "DashboardChunk1b",  // AI hub: ask-AI + smart-highlights zones
        "DashboardChunk2",   // live match
        "DashboardChunk3",   // trending channels
        "DashboardChunk4",   // backend categories
        "DashboardChunk5",   // continue watching
        "DashboardChunk6",   // community preview
        "DashboardChunk7",   // news
        "DashboardChunk8",   // general error
    };

    private int _nextDeferredChunk;
    private bool _deferredChunkPumpActive;

    /// <summary>
    /// Starts realising the dashboard's below-the-fold sections. Each pass builds one
    /// section on the UI thread and then re-queues itself a frame later, so input is
    /// serviced between passes and no single block can approach the input watchdog.
    /// The markup stays in MainPage.xaml as DataTemplates, so nothing here is built
    /// until its pass runs.
    /// </summary>
    private void ScheduleDeferredDashboardChunks()
    {
        if (_deferredChunkPumpActive ||
            _nextDeferredChunk >= DeferredDashboardChunks.Length)
        {
            return;
        }

        _deferredChunkPumpActive = true;

        // Give the eager chrome (header, search, hero) its first frames first.
        Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(140),
            PumpDeferredDashboardChunk);
    }

    private void PumpDeferredDashboardChunk()
    {
        if (_nextDeferredChunk >= DeferredDashboardChunks.Length)
        {
            _deferredChunkPumpActive = false;
            return;
        }

        string key = DeferredDashboardChunks[_nextDeferredChunk];
        long started = System.Diagnostics.Stopwatch.GetTimestamp();

        try
        {
            if (DashboardSectionsHost.Resources.TryGetValue(key, out object? value) &&
                value is DataTemplate template &&
                template.CreateContent() is View section)
            {
                DashboardChunkStack.Children.Add(section);

                Services.StartupTrace.Mark(
                    $"dashboard {key} inflated in " +
                    $"{System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms " +
                    $"({_nextDeferredChunk + 1}/{DeferredDashboardChunks.Length})");
            }
            else
            {
                Services.StartupTrace.Mark($"dashboard {key} template not found");
            }
        }
        catch (Exception ex)
        {
            Services.StartupTrace.Mark($"dashboard {key} failed: {ex.GetType().Name}: {ex.Message}");
        }

        _nextDeferredChunk++;

        if (_nextDeferredChunk < DeferredDashboardChunks.Length)
        {
            Dispatcher.DispatchDelayed(
                TimeSpan.FromMilliseconds(16),
                PumpDeferredDashboardChunk);
        }
        else
        {
            _deferredChunkPumpActive = false;
            Services.StartupTrace.Mark("dashboard deferred sections complete");
        }
    }

    /// <summary>
    /// Opens the Shell flyout from the dashboard's menu button. The dashboard
    /// hides the Shell nav bar (it draws its own header), which also hides the
    /// platform's drawer button, so this is the only deliberate way in — an
    /// edge swipe is taken by Android's own "back" gesture.
    /// </summary>
    private void OnOpenMenuTapped(
        object? sender,
        TappedEventArgs e)
    {
        if (Shell.Current is { } shell)
        {
            shell.FlyoutIsPresented = true;
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
            ContentStack.CancelAnimations();
            HeaderPanel.CancelAnimations();
            SearchPanel.CancelAnimations();
            HeroPanel.CancelAnimations();

            HeaderPanel.Opacity = 0;
            HeaderPanel.TranslationY = -14;

            SearchPanel.Opacity = 0;
            SearchPanel.Scale = 0.97;

            ContentStack.Opacity = 0;
            ContentStack.TranslationY = 18;

            HeroPanel.Opacity = 0;
            HeroPanel.Scale = 0.98;

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
                SearchPanel.FadeToAsync(
                    1,
                    250,
                    Easing.CubicOut),

                SearchPanel.ScaleToAsync(
                    1,
                    250,
                    Easing.SpringOut),

                ContentStack.FadeToAsync(
                    1,
                    320,
                    Easing.CubicOut),

                ContentStack.TranslateToAsync(
                    0,
                    0,
                    320,
                    Easing.CubicOut));

            await Task.WhenAll(
                HeroPanel.FadeToAsync(
                    1,
                    260,
                    Easing.CubicOut),

                HeroPanel.ScaleToAsync(
                    1,
                    280,
                    Easing.SpringOut));

            _entranceAnimationCompleted = true;
        }
        catch (OperationCanceledException)
        {
            // Normal during page navigation.
        }
        finally
        {
            HeaderPanel.Opacity = 1;
            HeaderPanel.TranslationY = 0;

            SearchPanel.Opacity = 1;
            SearchPanel.Scale = 1;

            ContentStack.Opacity = 1;
            ContentStack.TranslationY = 0;

            HeroPanel.Opacity = 1;
            HeroPanel.Scale = 1;

            _entranceAnimationRunning = false;
        }
    }

    // ============================================================
    // BUTTON ANIMATION
    // ============================================================

    private async void OnAnimatedButtonClicked(
        object? sender,
        EventArgs e)
    {
        if (sender is not VisualElement element)
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
        catch (OperationCanceledException)
        {
            // Navigation can cancel the animation.
        }
        finally
        {
            element.Scale = 1;
        }
    }

    // ============================================================
    // SKY ANIMATION
    // ============================================================

    private void StartSkyAnimation()
    {
        StopSkyAnimation();

        var cancellation =
            new CancellationTokenSource();

        _skyAnimationCancellation =
            cancellation;

        _ = AnimateSkyAsync(
            cancellation.Token);
    }

    private void StopSkyAnimation()
    {
        CancellationTokenSource? cancellation =
            Interlocked.Exchange(
                ref _skyAnimationCancellation,
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
        FloatingDiamondOne.Opacity = 1;

        FloatingDiamondTwo.TranslationY = 0;
        FloatingDiamondTwo.Rotation = 0;
        FloatingDiamondTwo.Opacity = 1;
    }

    // The floating diamonds breathe for a few beats on entry, then settle.
    // A never-ending animation keeps the platform UI permanently "busy":
    // it burns battery/GPU, blocks accessibility tooling (uiautomator can never
    // reach an idle state) and keeps the Android window from going quiet.
    private const int SkyAnimationBeats = 3;

    private async Task AnimateSkyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            for (var beat = 0;
                 beat < SkyAnimationBeats &&
                 !cancellationToken.IsCancellationRequested;
                 beat++)
            {
                await Task.WhenAll(
                    FloatingDiamondOne.TranslateToAsync(
                        0,
                        -8,
                        1_200,
                        Easing.SinInOut),

                    FloatingDiamondOne.RotateToAsync(
                        14,
                        1_200,
                        Easing.SinInOut),

                    FloatingDiamondTwo.TranslateToAsync(
                        0,
                        7,
                        1_450,
                        Easing.SinInOut),

                    FloatingDiamondTwo.RotateToAsync(
                        -13,
                        1_450,
                        Easing.SinInOut));

                cancellationToken.ThrowIfCancellationRequested();

                await Task.WhenAll(
                    FloatingDiamondOne.TranslateToAsync(
                        0,
                        0,
                        1_200,
                        Easing.SinInOut),

                    FloatingDiamondOne.RotateToAsync(
                        0,
                        1_200,
                        Easing.SinInOut),

                    FloatingDiamondTwo.TranslateToAsync(
                        0,
                        0,
                        1_450,
                        Easing.SinInOut),

                    FloatingDiamondTwo.RotateToAsync(
                        0,
                        1_450,
                        Easing.SinInOut));

                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                // Leave the decorative shapes at their resting position so the
                // layout is deterministic (and screenshot-stable) after the
                // entrance animation has played out.
                FloatingDiamondOne.TranslationY = 0;
                FloatingDiamondOne.Rotation = 0;
                FloatingDiamondTwo.TranslationY = 0;
                FloatingDiamondTwo.Rotation = 0;
            }
        }
        catch (OperationCanceledException)
        {
            // Normal when the page disappears.
        }
        catch (ObjectDisposedException)
        {
            // The cancellation source was disposed during shutdown.
        }
    }
}