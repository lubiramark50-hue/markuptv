using MarkUptv.Pages;
using MarkUptv.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;

using MarkUptv.Converters;
using MarkUptv.Helpers;
using Microsoft.Maui.Controls.Shapes;
using System.Globalization;

// System.IO is an implicit using here, so the vector shape needs an alias.
using MenuIconPath = Microsoft.Maui.Controls.Shapes.Path;

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
    private readonly IServiceProvider _services;

    private CancellationTokenSource? _skyAnimationCts;
    private bool _isLoaded;

    public AppShell(
        AppShellViewModel viewModel,
        IServiceProvider services,
        ILogger<AppShell> logger)
    {
        _logger = logger
            ?? throw new ArgumentNullException(
                nameof(logger));

        _services = services
            ?? throw new ArgumentNullException(
                nameof(services));

        Services.StartupTrace.Mark("AppShell ctor enter");

        InitializeComponent();

        Services.StartupTrace.Mark("AppShell.InitializeComponent done");

        BindingContext = viewModel
            ?? throw new ArgumentNullException(
                nameof(viewModel));

        RegisterAppRoutes();

        Services.StartupTrace.Mark("AppShell routes registered");

        Loaded += OnShellLoaded;
        Unloaded += OnShellUnloaded;
        PropertyChanged += OnShellPropertyChanged;
        Navigated += OnShellNavigated;
    }

    /// <summary>
    /// Pages that already ship their own back control, so must not be given a
    /// second one by the nav bar.
    /// </summary>
    private static readonly HashSet<string> PagesWithTheirOwnBackButton =
        new(StringComparer.Ordinal)
        {
            nameof(SearchPage),
            nameof(MovieDetailPage),
            nameof(MovieDownloadsPage),
            nameof(ComposePostPage),
            nameof(MainNewsWebViewPage),
            nameof(WebViewPage),
            nameof(AdultsPage),
        };

    /// <summary>
    /// Gives every page a way out: the platform nav bar carries the drawer
    /// button on a root page and a back arrow on a pushed one.
    ///
    /// Nearly every page hides the nav bar (they draw their own headers), which
    /// hid the drawer button with it — so the flyout was unreachable from all of
    /// them and a pushed page had no back arrow at all. The dashboard supplies
    /// its own menu button, and the pages listed above supply their own back
    /// control, so those are left as they are.
    /// </summary>
    private void OnShellNavigated(
        object? sender,
        ShellNavigatedEventArgs e)
    {
        if (CurrentPage is not Page page)
        {
            return;
        }

        // Every page gets the adaptive rules - including the dashboard, and
        // before the nav-bar decision below returns early. Tall content gains a
        // scroll host and wide viewports get a readable centred column.
        AdaptiveLayoutHost.Apply(page);

        if (page is MainPage ||
            PagesWithTheirOwnBackButton.Contains(page.GetType().Name))
        {
            return;
        }

        Shell.SetNavBarIsVisible(page, true);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Flyout menu
    // ═══════════════════════════════════════════════════════════════════════

    // Shell draws its own flyout rows from plain MAUI Labels. Those labels pick
    // up the app's implicit Label style —
    // {AppThemeBinding Light=Black, Dark=White} — and this app runs in light
    // mode, so the drawer painted black text on its navy background: measured
    // at 1.07:1, which is why the menu read as empty. FlyoutContent replaces
    // those rows with the tree built here, where every colour is set locally
    // and cannot be overridden by any style.

    private static readonly Color MenuText = Color.FromArgb("#EAF0FB");
    private static readonly Color MenuMuted = Color.FromArgb("#9AA5C4");
    private static readonly Color MenuGold = Color.FromArgb("#E8B54A");
    private static readonly Color MenuSelectedFill = Color.FromArgb("#26E8B54A");
    private static readonly Color MenuSelectedStroke = Color.FromArgb("#5CE8B54A");

    private static readonly FeatureIconConverter MenuIcons = new();

    /// <summary>
    /// The menu, in the order a viewer looks for things. Titles are matched
    /// against the Shell's own items, so a renamed route cannot leave a dead
    /// entry behind, and any visible item not named here is still listed under
    /// a final section rather than becoming unreachable.
    /// </summary>
    private static readonly (string Section, string[] Titles)[] MenuSections =
    {
        ("MAIN", ["Dashboard"]),
        ("LIVE & SPORT", ["Football", "World Channels", "European Football"]),
        ("ENTERTAINMENT", ["Cartoons", "Discovery", "Fashion", "Lifestyle", "Gospel", "Religious", "Local TV", "Wildlife"]),
        ("COMMUNITY", ["Community Hub", "Community", "Recently Watched"]),
        ("FILMS", ["Movies & Films"]),
        ("ACCOUNT", ["Adults 18+", "Support"]),
    };

    /// <summary>
    /// The category shortcuts, as the same <c>category|title|accent</c>
    /// triples the Shell's menu items used to declare. They live here now
    /// because FlyoutContent replaces the flyout's own item list, so a Shell
    /// MenuItem would no longer be drawn anywhere.
    /// </summary>
    private static readonly (string Key, string Title, string Accent, string Icon)[] MenuCategories =
    {
        ("business", "Business", "#A8771F", "business.png"),
        ("classic", "Classic TV", "#E8B54A", "classic.png"),
        ("comedy", "Comedy", "#E8B54A", "comedy.png"),
        ("cooking", "Cooking", "#0E7A99", "cooking.png"),
        ("culture", "Culture", "#F7E7A6", "culture.png"),
        ("documentary", "Documentary", "#F7E7A6", "documentary.png"),
        ("education", "Education", "#A8771F", "education.png"),
        ("entertainment", "Entertainment", "#E8B54A", "entertainment.png"),
        ("family", "Family", "#6DFF8F", "family.png"),
        ("general", "General TV", "#F7E7A6", "general.png"),
        ("legislative", "Legislative", "#A8771F", "legislative.png"),
        ("public", "Public TV", "#A8771F", "publictv.png"),
        ("relax", "Relax", "#6DFF8F", "relax.png"),
        ("science", "Science", "#F7E7A6", "science.png"),
        ("series", "Series", "#E8B54A", "series.png"),
        ("shop", "Shop", "#E8B54A", "shop.png"),
        ("travel", "Travel", "#6DFF8F", "travel.png"),
        ("weather", "Weather", "#A8771F", "weather.png"),
    };

    private readonly List<(BaseShellItem Target, Border Row, Label Label, MenuIconPath Icon)> _menuRows = new();

    private bool _flyoutMenuBuilt;

    /// <summary>
    /// Fills the flyout body. Runs a few rows per dispatch so the drawer is
    /// never the reason a frame is late, and never twice.
    /// </summary>
    private async Task BuildFlyoutMenuAsync()
    {
        if (_flyoutMenuBuilt)
        {
            return;
        }

        _flyoutMenuBuilt = true;

        try
        {
            Dictionary<string, BaseShellItem> items = Items
                .OfType<BaseShellItem>()
                .Where(item =>
                    item.FlyoutItemIsVisible &&
                    !string.IsNullOrWhiteSpace(item.Title))
                .GroupBy(item => item.Title, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.Ordinal);

            var placed = new HashSet<string>(StringComparer.Ordinal);

            foreach ((string section, string[] titles) in MenuSections)
            {
                List<BaseShellItem> members = titles
                    .Where(items.ContainsKey)
                    .Select(title => items[title])
                    .ToList();

                if (members.Count == 0)
                {
                    continue;
                }

                FlyoutMenu.Add(BuildSectionHeader(section));

                foreach (BaseShellItem item in members)
                {
                    placed.Add(item.Title);
                    FlyoutMenu.Add(BuildMenuRow(item));
                    await Task.Yield();
                }
            }

            BaseShellItem[] leftover = items.Values
                .Where(item => !placed.Contains(item.Title))
                .OrderBy(item => item.Title, StringComparer.Ordinal)
                .ToArray();

            if (leftover.Length > 0)
            {
                FlyoutMenu.Add(BuildSectionHeader("MORE"));

                foreach (BaseShellItem item in leftover)
                {
                    FlyoutMenu.Add(BuildMenuRow(item));
                    await Task.Yield();
                }
            }

            await BuildCategoryGridAsync();

            UpdateFlyoutSelection();

            Services.StartupTrace.Mark("Flyout menu built");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The flyout menu could not be built.");
        }
    }

    private static Label BuildSectionHeader(string text) =>
        new()
        {
            Text = text,
            TextColor = MenuMuted,
            FontSize = 10,
            FontAttributes = FontAttributes.Bold,
            CharacterSpacing = 1.6,
            Margin = new Thickness(11, 12, 11, 1),
            InputTransparent = true,
        };

    /// <summary>
    /// One menu row: the same flat vector mark the dashboard uses, the title,
    /// and an 18+ badge where it matters.
    /// </summary>
    private View BuildMenuRow(BaseShellItem item)
    {
        var icon = new MenuIconPath
        {
            Data = (Geometry)MenuIcons.Convert(
                item.Title,
                typeof(Geometry),
                null,
                CultureInfo.InvariantCulture),
            Aspect = Stretch.Uniform,
            WidthRequest = 20,
            HeightRequest = 20,
            Fill = MenuGold,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
        };

        var label = new Label
        {
            Text = item.Title,
            TextColor = MenuText,
            FontSize = 14.5,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
        };

        var layout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 14,
        };

        layout.Add(icon, 0);
        layout.Add(label, 1);

        if (item.Title.Contains("18+", StringComparison.Ordinal))
        {
            layout.Add(
                new Border
                {
                    Padding = new Thickness(7, 2),
                    StrokeThickness = 1,
                    Stroke = Color.FromArgb("#66FF4D8D"),
                    BackgroundColor = Color.FromArgb("#1FFF4D8D"),
                    StrokeShape = new RoundRectangle { CornerRadius = 7 },
                    VerticalOptions = LayoutOptions.Center,
                    InputTransparent = true,
                    Content = new Label
                    {
                        Text = "18+",
                        TextColor = Color.FromArgb("#FF9EC2"),
                        FontSize = 10,
                        FontAttributes = FontAttributes.Bold,
                        InputTransparent = true,
                    },
                },
                2);
        }

        var row = new Border
        {
            Content = layout,
            HeightRequest = 46,
            Padding = new Thickness(12, 0),
            StrokeThickness = 1,
            Stroke = Colors.Transparent,
            BackgroundColor = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => ActivateShellItem(item);
        row.GestureRecognizers.Add(tap);
        row.Behaviors.Add(new TapScaleBehavior());

        _menuRows.Add((item, row, label, icon));

        return row;
    }

    /// <summary>
    /// The category shortcuts, taken straight from the Shell's own menu items
    /// so the command and its parameter stay declared in one place.
    /// </summary>
    private async Task BuildCategoryGridAsync()
    {
        if (MenuCategories.Length == 0)
        {
            return;
        }

        FlyoutMenu.Add(BuildSectionHeader("CATEGORIES"));

        for (int index = 0; index < MenuCategories.Length; index += 2)
        {
            var line = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Star),
                },
                ColumnSpacing = 8,
            };

            line.Add(BuildCategoryChip(MenuCategories[index]), 0);

            if (index + 1 < MenuCategories.Length)
            {
                line.Add(BuildCategoryChip(MenuCategories[index + 1]), 1);
            }

            FlyoutMenu.Add(line);

            await Task.Yield();
        }
    }

    private View BuildCategoryChip(
        (string Key, string Title, string Accent, string Icon) category)
    {
        var layout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 9,
            Padding = new Thickness(10, 0),
        };

        layout.Add(
            new Image
            {
                Source = category.Icon,
                Aspect = Aspect.AspectFit,
                WidthRequest = 20,
                HeightRequest = 20,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true,
            },
            0);

        layout.Add(
            new Label
            {
                Text = category.Title,
                TextColor = MenuText,
                FontSize = 12,
                LineBreakMode = LineBreakMode.TailTruncation,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true,
            },
            1);

        var chip = new Border
        {
            Content = layout,
            HeightRequest = 42,
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#26FFFFFF"),
            BackgroundColor = Color.FromArgb("#14FFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => ActivateCategory(
            $"{category.Key}|{category.Title}|{category.Accent}",
            category.Title);
        chip.GestureRecognizers.Add(tap);
        chip.Behaviors.Add(new TapScaleBehavior { PressedScale = 0.97 });

        return chip;
    }

    private void ActivateCategory(string payload, string title)
    {
        try
        {
            FlyoutIsPresented = false;

            if (BindingContext is AppShellViewModel viewModel &&
                viewModel.OpenCategoryCommand.CanExecute(payload))
            {
                viewModel.OpenCategoryCommand.Execute(payload);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The {Category} category could not be opened.",
                title);
        }
    }

    /// <summary>
    /// Navigates on the Shell's own object model rather than a route string:
    /// the items here are declared without explicit routes, so
    /// <c>GoToAsync</c> has no stable absolute path to reach them.
    /// </summary>
    private void ActivateShellItem(BaseShellItem item)
    {
        try
        {
            FlyoutIsPresented = false;

            switch (item)
            {
                case ShellContent content
                    when content.Parent is ShellSection section &&
                         section.Parent is ShellItem owner:

                    CurrentItem = owner;
                    owner.CurrentItem = section;
                    section.CurrentItem = content;
                    break;

                case ShellSection section
                    when section.Parent is ShellItem owner:

                    CurrentItem = owner;
                    owner.CurrentItem = section;
                    break;

                case ShellItem owner:

                    CurrentItem = owner;
                    break;
            }

            UpdateFlyoutSelection();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Flyout navigation to {Title} failed.",
                item.Title);
        }
    }

    /// <summary>
    /// Marks the row for the section currently on screen.
    /// </summary>
    private void UpdateFlyoutSelection()
    {
        ShellItem? current = CurrentItem;

        foreach ((BaseShellItem target, Border row, Label label, MenuIconPath icon) in _menuRows)
        {
            bool selected = ReferenceEquals(ResolveOwner(target), current);

            row.BackgroundColor = selected ? MenuSelectedFill : Colors.Transparent;
            row.Stroke = selected ? MenuSelectedStroke : Colors.Transparent;
            label.TextColor = selected ? MenuGold : MenuText;
            icon.Opacity = selected ? 1 : 0.9;
        }
    }

    private static ShellItem? ResolveOwner(BaseShellItem item) =>
        item switch
        {
            ShellItem shellItem => shellItem,
            { Parent: ShellItem owner } => owner,
            { Parent: ShellSection { Parent: ShellItem owner } } => owner,
            _ => null,
        };

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

                // The player is a pushed route (not a ShellContent) so that
                // opening it is a normal push and back returns to the board.
                Routing.RegisterRoute(
                    nameof(PlayerPage),
                    typeof(PlayerPage));
                Routing.RegisterRoute(
                    nameof(CategoryChannelPage),
                    typeof(CategoryChannelPage));

                Routing.RegisterRoute(
                    nameof(PostDetailPage),
                    typeof(PostDetailPage));

                // Free film catalogue: pushed from the Movies flyout entry.
                Routing.RegisterRoute(
                    nameof(MovieDetailPage),
                    typeof(MovieDetailPage));

                Routing.RegisterRoute(
                    nameof(MovieDownloadsPage),
                    typeof(MovieDownloadsPage));

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

        // Fill the drawer just after the shell paints, a few rows per
        // dispatch, so opening the menu is never the first time that work
        // runs and never lands as one long frame.
        Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(500),
            () => _ = BuildFlyoutMenuAsync());

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

            if (!_flyoutMenuBuilt)
            {
                _ = BuildFlyoutMenuAsync();
            }

            UpdateFlyoutSelection();
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

            // Ambient motion is a short flourish, never a permanent loop: a
            // never-ending animation keeps the UI permanently busy (battery,
            // GPU, accessibility tooling) and competes with real interactions.
            var beat = 0;

            while (AmbientAnimation.ShouldContinue(
                       beat++,
                       cancellationToken) &&
                   FlyoutIsPresented)
            {
                await Task.WhenAll(
                    DiamondOne.TranslateToAsync(
                        0,
                        -8,
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

                    DiamondThree.TranslateToAsync(
                        0,
                        -6,
                        1_000,
                        Easing.SinInOut),

                    DiamondThree.FadeToAsync(
                        0.94,
                        1_000,
                        Easing.SinInOut),

                    // The halo and the brand tile used to scale and rotate here.
                    // Scaling a 210 dp rounded border (clipped, with a blurred
                    // shadow behind it) re-renders that whole subtree every
                    // frame, which is what made the sparkles look uneven on
                    // Android. Opacity and a few dp of travel read the same and
                    // cost a composited layer instead.
                    LiveStatusDot.FadeToAsync(
                        0.35,
                        700,
                        Easing.SinInOut));

                cancellationToken.ThrowIfCancellationRequested();

                await Task.WhenAll(
                    DiamondOne.TranslateToAsync(
                        0,
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

                    DiamondThree.TranslateToAsync(
                        0,
                        0,
                        1_000,
                        Easing.SinInOut),

                    DiamondThree.FadeToAsync(
                        0.66,
                        1_000,
                        Easing.SinInOut),

                    LiveStatusDot.FadeToAsync(
                        1,
                        700,
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
