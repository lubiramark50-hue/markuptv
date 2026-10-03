using MarkUptv.Pages;
using MarkUptv.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;

namespace MarkUptv;

/// <summary>
/// Main MarkUpTV Shell.
/// Code-behind contains only route registration and decorative
/// user-interface animations.
/// </summary>
public partial class AppShell : Shell
{
    private static readonly object RouteRegistrationLock =
        new();

    private static bool _routesRegistered;

    private readonly ILogger<AppShell> _logger;

    private CancellationTokenSource? _skyAnimationCts;
    private bool _isLoaded;

    public AppShell(
        AppShellViewModel viewModel,
        ILogger<AppShell> logger)
    {
        _logger = logger
            ?? throw new ArgumentNullException(
                nameof(logger));

        InitializeComponent();

        BindingContext = viewModel
            ?? throw new ArgumentNullException(
                nameof(viewModel));

        RegisterAppRoutes();

        Loaded += OnShellLoaded;
        Unloaded += OnShellUnloaded;
        PropertyChanged += OnShellPropertyChanged;
    }

    private void RegisterAppRoutes()
    {
        lock (RouteRegistrationLock)
        {
            if (_routesRegistered)
            {
                return;
            }

            try
            {
                _logger.LogInformation(
                    "Registering MarkUpTV dynamic routes.");

                /*
                 * Pages declared as ShellContent in AppShell.xaml must
                 * not be registered again here.
                 */

                Routing.RegisterRoute(
                    nameof(CategoryChannelPage),
                    typeof(CategoryChannelPage));

                Routing.RegisterRoute(
                    nameof(PostDetailPage),
                    typeof(PostDetailPage));

                Routing.RegisterRoute(
                    nameof(ComposePostPage),
                    typeof(ComposePostPage));

                Routing.RegisterRoute(
                    nameof(MorePage),
                    typeof(MorePage));

                Routing.RegisterRoute(
                    nameof(NotificationsPage),
                    typeof(NotificationsPage));

                Routing.RegisterRoute(
                    nameof(WebViewPage),
                    typeof(WebViewPage));

                Routing.RegisterRoute(
                    nameof(MainNewsWebViewPage),
                    typeof(MainNewsWebViewPage));

                Routing.RegisterRoute(
                    nameof(PaymentWebViewPage),
                    typeof(PaymentWebViewPage));

                _routesRegistered = true;

                _logger.LogInformation(
                    "MarkUpTV routes registered successfully.");
            }
            catch (Exception exception)
            {
                _logger.LogCritical(
                    exception,
                    "Fatal error while registering MarkUpTV routes.");

                throw;
            }
        }
    }

    private void OnShellLoaded(
        object? sender,
        EventArgs e)
    {
        _isLoaded = true;

        if (FlyoutIsPresented)
        {
            StartSkyAnimation();
        }
    }

    private void OnShellUnloaded(
        object? sender,
        EventArgs e)
    {
        _isLoaded = false;
        StopSkyAnimation();
    }

    private void OnShellPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_isLoaded ||
            !string.Equals(
                e.PropertyName,
                nameof(FlyoutIsPresented),
                StringComparison.Ordinal))
        {
            return;
        }

        if (FlyoutIsPresented)
        {
            StartSkyAnimation();
        }
        else
        {
            StopSkyAnimation();
        }
    }

    private void StartSkyAnimation()
    {
        StopSkyAnimation();

        var cancellation =
            new CancellationTokenSource();

        _skyAnimationCts =
            cancellation;

        _ = RunSkyAnimationAsync(
            cancellation.Token);
    }

    private void StopSkyAnimation()
    {
        CancellationTokenSource? cancellation =
            Interlocked.Exchange(
                ref _skyAnimationCts,
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

        DiamondOne.CancelAnimations();
        DiamondTwo.CancelAnimations();
        DiamondThree.CancelAnimations();
        GalaxyHalo.CancelAnimations();
        BrandDiamond.CancelAnimations();
        SearchPanel.CancelAnimations();
        LiveStatusDot.CancelAnimations();

        DiamondOne.TranslationY = 0;
        DiamondOne.Rotation = 0;
        DiamondOne.Opacity = 0.82;

        DiamondTwo.TranslationY = 0;
        DiamondTwo.Rotation = 0;
        DiamondTwo.Opacity = 0.84;

        DiamondThree.TranslationY = 0;
        DiamondThree.Rotation = 0;
        DiamondThree.Opacity = 0.66;

        GalaxyHalo.Scale = 1;
        GalaxyHalo.Rotation = 0;
        BrandDiamond.Scale = 1;
        BrandDiamond.Rotation = 0;
        SearchPanel.Scale = 1;
        LiveStatusDot.Opacity = 1;
    }

    private async Task RunSkyAnimationAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            SearchPanel.Opacity = 0;
            SearchPanel.TranslationY = 8;

            await Task.WhenAll(
                SearchPanel.FadeToAsync(
                    1,
                    240,
                    Easing.CubicOut),

                SearchPanel.TranslateToAsync(
                    0,
                    0,
                    240,
                    Easing.CubicOut),

                BrandDiamond.ScaleToAsync(
                    1.08,
                    250,
                    Easing.CubicOut));

            await BrandDiamond.ScaleToAsync(
                1,
                180,
                Easing.SpringOut);

            while (!cancellationToken.IsCancellationRequested &&
                   FlyoutIsPresented)
            {
                await Task.WhenAll(
                    DiamondOne.TranslateToAsync(
                        0,
                        -8,
                        900,
                        Easing.SinInOut),

                    DiamondOne.RotateToAsync(
                        15,
                        900,
                        Easing.SinInOut),

                    DiamondOne.FadeToAsync(
                        0.48,
                        900,
                        Easing.SinInOut),

                    DiamondTwo.TranslateToAsync(
                        0,
                        7,
                        1_100,
                        Easing.SinInOut),

                    DiamondTwo.RotateToAsync(
                        -18,
                        1_100,
                        Easing.SinInOut),

                    DiamondThree.TranslateToAsync(
                        0,
                        -6,
                        1_000,
                        Easing.SinInOut),

                    DiamondThree.FadeToAsync(
                        0.94,
                        1_000,
                        Easing.SinInOut),

                    GalaxyHalo.ScaleToAsync(
                        1.06,
                        1_400,
                        Easing.SinInOut),

                    GalaxyHalo.RotateToAsync(
                        4,
                        1_400,
                        Easing.SinInOut),

                    LiveStatusDot.FadeToAsync(
                        0.35,
                        700,
                        Easing.SinInOut),

                    BrandDiamond.RotateToAsync(
                        7,
                        1_000,
                        Easing.SinInOut));

                cancellationToken.ThrowIfCancellationRequested();

                await Task.WhenAll(
                    DiamondOne.TranslateToAsync(
                        0,
                        0,
                        900,
                        Easing.SinInOut),

                    DiamondOne.RotateToAsync(
                        0,
                        900,
                        Easing.SinInOut),

                    DiamondOne.FadeToAsync(
                        0.82,
                        900,
                        Easing.SinInOut),

                    DiamondTwo.TranslateToAsync(
                        0,
                        0,
                        1_100,
                        Easing.SinInOut),

                    DiamondTwo.RotateToAsync(
                        0,
                        1_100,
                        Easing.SinInOut),

                    DiamondThree.TranslateToAsync(
                        0,
                        0,
                        1_000,
                        Easing.SinInOut),

                    DiamondThree.FadeToAsync(
                        0.66,
                        1_000,
                        Easing.SinInOut),

                    GalaxyHalo.ScaleToAsync(
                        1,
                        1_400,
                        Easing.SinInOut),

                    GalaxyHalo.RotateToAsync(
                        0,
                        1_400,
                        Easing.SinInOut),

                    LiveStatusDot.FadeToAsync(
                        1,
                        700,
                        Easing.SinInOut),

                    BrandDiamond.RotateToAsync(
                        0,
                        1_000,
                        Easing.SinInOut));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal when the flyout closes.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The Shell sky animation stopped unexpectedly.");
        }
    }
}