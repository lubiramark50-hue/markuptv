using CommunityToolkit.Maui;

using MarkUptv.Handlers;
using MarkUptv.Models;
using MarkUptv.Pages;
using MarkUptv.Services;
using MarkUptv.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Maui;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;

namespace MarkUptv;

public static class MauiProgram
{
    private const int DevelopmentBackendPort = 5293;

    /*
     * The backend currently runs on this PC (MarkUpTvServer, port 5293).
     * - Android devices on the same Wi-Fi reach it through the PC's LAN IP.
     * - The Android emulator reaches the host loopback through 10.0.2.2.
     * - The Windows build runs on the same machine as the server, so it
     *   uses localhost.
     * When the backend is hosted publicly, replace these with the real
     * domain (https://…).
     *
     * The LAN address below changes whenever the router hands out a new
     * DHCP lease, so it is only the *starting* candidate: at startup the app
     * probes this address, the machine's mDNS name and the last address that
     * worked, then remembers the winner (see BackendEndpointProvider). Look up
     * the current address with `ipconfig | findstr IPv4` on the PC.
     */
    private const string LanAndroidApiUrl =
        "http://192.168.1.75:5293/";

    /*
     * The PC's Windows computer name, used to build the mDNS (".local") and
     * plain-host fallback candidates. Change it if the machine is renamed:
     * run `hostname` on the PC to read it.
     */
    private const string DevelopmentHostName =
        "mrmark";

    private const string ProductionApiUrl =
        "https://api.markuptv.example/";

    public static MauiApp CreateMauiApp()
    {
        // Cold-start timeline anchor: everything below is measured relative to
        // the first line of managed startup.
        Services.StartupTrace.Mark("CreateMauiApp enter");

        MauiAppBuilder builder =
            MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitMediaElement()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont(
                    "OpenSans-Regular.ttf",
                    "OpenSansRegular");

                fonts.AddFont(
                    "OpenSans-Semibold.ttf",
                    "OpenSansSemibold");
            });

#if DEBUG
        /*
         * Keep AddConsole because it was already available in your
         * original project. Do not use AddDebug unless the corresponding
         * logging provider package is installed.
         */
        builder.Logging.AddConsole();
#endif

        Uri backendUri =
            ResolveBackendUri();

        RegisterOptions(
            builder.Services,
            backendUri);

        RegisterCoreServices(
            builder.Services);

        RegisterHttpClients(
            builder.Services,
            backendUri);

        RegisterViewModels(
            builder.Services);

        RegisterPages(
            builder.Services);

        RegisterMediaElementHandlerMapping();

        Services.StartupTrace.Mark("service graph registered");

        MauiApp app = builder.Build();

        Services.StartupTrace.Mark("builder.Build() returned");

        return app;
    }

    /// <summary>
    /// Header injection hook: when any page assigns MediaElement.Source, run
    /// AFTER the toolkit's own MapSource and hand the native player the
    /// Referer / User-Agent staged by StreamHeaderProvider (set by the
    /// view model immediately before a repaired/scraped stream starts).
    /// No headers staged → no-op, so plain IPTV playback is untouched.
    /// </summary>
    private static void RegisterMediaElementHandlerMapping()
    {
        CommunityToolkit.Maui.Core.Handlers.MediaElementHandler
            .PropertyMapper
            .AppendToMapping(
                nameof(CommunityToolkit.Maui.Views.MediaElement.Source),
                (handler, _) =>
                    Handlers.StreamPlayerHeaderHandler.Apply(handler));
    }

    private static Uri ResolveBackendUri()
    {
#if ANDROID
        string address =
            DeviceInfo.DeviceType == DeviceType.Virtual
                ? $"http://10.0.2.2:{DevelopmentBackendPort}/"
                : LanAndroidApiUrl;
#else
        string address =
            $"http://localhost:{DevelopmentBackendPort}/";
#endif

        if (!address.EndsWith(
                "/",
                StringComparison.Ordinal))
        {
            address += "/";
        }

        if (!Uri.TryCreate(
                address,
                UriKind.Absolute,
                out Uri? uri))
        {
            throw new InvalidOperationException(
                $"The backend URL is invalid: '{address}'.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp &&
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "The backend URL must use HTTP or HTTPS.");
        }

        return uri;
    }

    private static void RegisterOptions(
        IServiceCollection services,
        Uri backendUri)
    {
        services.Configure<TvApiSettings>(options =>
        {
            options.BaseUrl =
                backendUri.AbsoluteUri;

            options.ChannelsPathTemplate =
                "api/tv/{category}";

            options.ProgrammesPathTemplate =
                "api/epg/channels/{channelId}/programmes";

            options.ReportFailurePath =
                "api/tv/report-failure";

            options.EnableEpg = true;
            options.RequestTimeoutSeconds = 30;
            options.RetryCount = 2;
            options.EpgLookBehindMinutes = 30;
            options.EpgLookAheadHours = 12;
            options.MaxConcurrentEpgRequests = 4;
        });

        services.Configure<FootballApiSettings>(options =>
        {
            options.ChannelsPath =
                "api/football";

            options.LiveMatchesPath =
                "api/football/live";

            // The backend resolves a watchable stream per live match on its
            // first /live request after a cache gap; that cold response can
            // take longer than a plain channel fetch, so allow for it here.
            options.TimeoutSeconds = 75;
        });

        services.Configure<PaymentSettings>(options =>
        {
            options.BaseUrl =
                backendUri.AbsoluteUri;
        });

        services.Configure<NewsApiSettings>(options =>
        {
            options.BaseUrl =
                new Uri(
                    backendUri,
                    "api/news/")
                .AbsoluteUri;
        });

        services.Configure<SearchApiSettings>(options =>
        {
            options.BaseUrl =
                backendUri.AbsoluteUri;
        });

        services.Configure<SocialApiSettings>(options =>
        {
            options.BaseUrl =
                backendUri.AbsoluteUri;
        });
    }

    private static void RegisterCoreServices(
        IServiceCollection services)
    {
        services.AddSingleton<IConnectivity>(
            Connectivity.Current);

        services.AddSingleton<DeviceService>();

        services.AddSingleton<IDeviceService>(
            serviceProvider =>
                serviceProvider
                    .GetRequiredService<DeviceService>());

        services.AddSingleton<
            ICacheService,
            MemoryCacheService>();

        services.AddSingleton<
            INotificationService,
            LocalNotificationService>();

        services.AddSingleton<StatusCacheService>();
        services.AddSingleton<NewsCacheService>();
        services.AddSingleton<ConnectivityService>();
        services.AddSingleton<AdMobService>();
        services.AddSingleton<RecentlyWatchedService>();
        services.AddSingleton<ChannelCacheService>();
        services.AddSingleton<ILiveCatalogProvider, MauiAssetLiveCatalogProvider>();
        services.AddSingleton<LiveCatalogService>();
        services.AddSingleton<StreamProbeService>();
        services.AddSingleton<LiveResolutionService>();
        services.AddSingleton<YouTubeLiveResolver>();
        services.AddSingleton<CommunityStore>();
        services.AddSingleton<LiveSessionService>();
        services.AddSingleton<PublicFixturesProvider>();
        services.AddSingleton<AssistantBrain>();

        // AI companion used by the dashboard (AI pick, highlights, chat).
        // MainPageViewModel cannot be activated without this registration.
        services.AddSingleton<
            IAiTvService,
            AiTvService>();
    }

    private static void RegisterHttpClients(
        IServiceCollection services,
        Uri backendUri)
    {
        // Endpoint discovery: the app probes the compiled-in address, this
        // machine's mDNS name and the last address that worked, then routes
        // every backend request to whichever answered. Without this a router
        // handing out a new DHCP lease silently broke live football, because
        // the board is fetched from the backend and has no offline cache.
        services.AddSingleton(provider => new BackendEndpointProvider(
            backendUri,
            DevelopmentHostName,
            provider.GetRequiredService<ILogger<BackendEndpointProvider>>()));

        services.AddTransient<BackendRoutingHandler>();

        services.AddHttpClient<TvApiService>(
            (serviceProvider, client) =>
            {
                TvApiSettings settings =
                    serviceProvider
                        .GetRequiredService<
                            IOptions<TvApiSettings>>()
                        .Value;

                if (!Uri.TryCreate(
                        settings.BaseUrl,
                        UriKind.Absolute,
                        out Uri? baseUri))
                {
                    throw new InvalidOperationException(
                        $"Invalid TV API URL: " +
                        $"'{settings.BaseUrl}'.");
                }

                client.BaseAddress = baseUri;
                client.Timeout = Timeout.InfiniteTimeSpan;

                ConfigureHeaders(
                    client,
                    "MarkUptv-TV/1.0");

                // The TV controller gates the adult shelf per device
                // (X-Device-Id or ?deviceId=). Without this default header
                // every channel request reads as anonymous and the 18+ shelf
                // renders empty even for a pass-holding device.
                client.DefaultRequestHeaders.Remove("X-Device-Id");
                client.DefaultRequestHeaders.Add(
                    "X-Device-Id",
                    serviceProvider
                        .GetRequiredService<IDeviceService>()
                        .GetDeviceId());
            })
            .AddHttpMessageHandler<BackendRoutingHandler>();

        services.AddHttpClient<FootballApiService>(
            (serviceProvider, client) =>
            {
                FootballApiSettings settings =
                    serviceProvider
                        .GetRequiredService<
                            IOptions<FootballApiSettings>>()
                        .Value;

                client.BaseAddress =
                    backendUri;

                client.Timeout =
                    TimeSpan.FromSeconds(
                        Math.Clamp(
                            settings.TimeoutSeconds,
                            5,
                            120));

                ConfigureHeaders(
                    client,
                    "MarkUptv-Football/1.0");
            })
            .AddHttpMessageHandler<BackendRoutingHandler>();

        services.AddHttpClient<NewsApiService>(
            client =>
            {
                client.BaseAddress =
                    new Uri(
                        backendUri,
                        "api/news/");

                client.Timeout =
                    TimeSpan.FromSeconds(30);

                ConfigureHeaders(
                    client,
                    "MarkUptv-News/1.0");
            })
            .AddHttpMessageHandler<BackendRoutingHandler>();

        services.AddHttpClient<SearchApiService>(
            client =>
            {
                client.BaseAddress = backendUri;
                client.Timeout = TimeSpan.FromSeconds(30);

                ConfigureHeaders(
                    client,
                    "MarkUptv-Search/1.0");
            })
            .AddHttpMessageHandler<BackendRoutingHandler>();

        services.AddHttpClient<SocialService>(
            client =>
            {
                client.BaseAddress = backendUri;
                client.Timeout = TimeSpan.FromSeconds(30);

                ConfigureHeaders(
                    client,
                    "MarkUptv-Social/1.0");
            })
            .AddHttpMessageHandler<BackendRoutingHandler>();

        // Catalogue metadata: small JSON, quick timeouts.
        services.AddHttpClient<MovieCatalogService>(
            client =>
            {
                client.BaseAddress = backendUri;
                client.Timeout = TimeSpan.FromSeconds(45);

                ConfigureHeaders(
                    client,
                    "MarkUptv-Movies/1.0");
            })
            .AddHttpMessageHandler<BackendRoutingHandler>();

        // The download manager streams whole films (often 1–3 GB) and owns the
        // offline library index, so it is a SINGLETON with its own no-timeout
        // HttpClient — a transient service here would lose download state and
        // the library between pages.
        services.AddSingleton(serviceProvider =>
        {
            var downloadClient = new HttpClient
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan
            };

            ConfigureHeaders(
                downloadClient,
                "MarkUptv-Movies/1.0");

            return new MovieDownloadService(
                downloadClient,
                serviceProvider.GetRequiredService<ILogger<MovieDownloadService>>());
        });

        // Several view models (PostDetail, SocialBase, Feed) take the
        // ISocialService abstraction while the newer ones take the concrete
        // client. Without this mapping DI throws "Unable to resolve service
        // for type 'MarkUptv.Services.ISocialService'" at navigation time,
        // which silently killed every post-detail open from the feed.
        services.AddTransient<ISocialService>(
            provider => provider.GetRequiredService<SocialService>());

        services.AddHttpClient<PaymentService>(
            client =>
            {
                client.BaseAddress = backendUri;
                client.Timeout = TimeSpan.FromSeconds(60);

                ConfigureHeaders(
                    client,
                    "MarkUptv-Payments/1.0");
            })
            .AddHttpMessageHandler<BackendRoutingHandler>();

        services.AddHttpClient<DonationService>(
            client =>
            {
                client.BaseAddress =
                    new Uri(
                        backendUri,
                        "api/donation/");

                client.Timeout =
                    TimeSpan.FromSeconds(60);

                ConfigureHeaders(
                    client,
                    "MarkUptv-Donations/1.0");
            })
            .AddHttpMessageHandler<BackendRoutingHandler>();
    }

    private static void ConfigureHeaders(
        HttpClient client,
        string userAgent)
    {
        client.DefaultRequestHeaders.UserAgent.Clear();

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            userAgent);

        client.DefaultRequestHeaders.Accept.Clear();

        client.DefaultRequestHeaders.Accept.ParseAdd(
            "application/json");
    }

    private static void RegisterViewModels(
        IServiceCollection services)
    {
        services.AddSingleton<AppShellViewModel>();

        services.AddTransient<MainPageViewModel>();
        services.AddTransient<RecentlyWatchedViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<DonationViewModel>();
        services.AddTransient<PaymentRequiredViewModel>();
        services.AddTransient<PaymentWebViewViewModel>();
        services.AddTransient<SocialViewModel>();
        services.AddTransient<NewsViewModel>();
        services.AddTransient<SportsViewModel>();
        services.AddTransient<MoviesViewModel>();

        // Free film catalogue (stream + offline library)
        services.AddTransient<MovieCatalogViewModel>();
        services.AddTransient<MovieDetailViewModel>();
        services.AddTransient<MovieDownloadsViewModel>();
        services.AddTransient<MusicViewModel>();
        services.AddTransient<FootballViewModel>();
        services.AddTransient<CartoonViewModel>();
        services.AddTransient<DiscoveryViewModel>();
        services.AddTransient<FashionViewModel>();
        services.AddTransient<GospelViewModel>();
        services.AddTransient<LifestyleViewModel>();
        services.AddTransient<ReligiousTvViewModel>();
        services.AddTransient<LocalViewModel>();
        services.AddTransient<EuropeanSportsViewModel>();
        services.AddTransient<WildLifeViewModel>();
        services.AddTransient<CategoryChannelViewModel>();

        services.AddTransient<PlayerViewModel>();
        services.AddTransient<WorldChannelsViewModel>();
        services.AddTransient<MatchThreadViewModel>();
        services.AddTransient<CommunityHubViewModel>();
        services.AddTransient<PostDetailViewModel>();
        services.AddTransient<ComposePostViewModel>();
        services.AddTransient<NotificationsViewModel>();
    }

    private static void RegisterPages(
        IServiceCollection services)
    {
        services.AddSingleton<AppShell>();

        services.AddTransient<MainPage>();
        services.AddTransient<SearchPage>();
        services.AddTransient<DonationPage>();
        services.AddTransient<WebViewPage>();
        services.AddTransient<RecentlyWatchedPage>();
        services.AddTransient<PaymentRequiredPage>();
        services.AddTransient<PaymentWebViewPage>();
        services.AddTransient<SocialPage>();
        services.AddTransient<News>();
        services.AddTransient<Sports>();
        services.AddTransient<MoviesPage>();
        services.AddTransient<MusicPage>();
        services.AddTransient<FootballPage>();
        services.AddTransient<Cartoonspage>();
        services.AddTransient<DiscoveryPage>();
        services.AddTransient<FashionPage>();
        services.AddTransient<GospelPage>();
        services.AddTransient<Lifestyle>();
        services.AddTransient<ReligiousTvPage>();
        services.AddTransient<LocalPage>();
        services.AddTransient<EuropeanSportsPage>();
        services.AddTransient<WildlifePage>();
        services.AddTransient<CategoryChannelPage>();

        services.AddTransient<MovieCatalogPage>();
        services.AddTransient<MovieDetailPage>();
        services.AddTransient<MovieDownloadsPage>();

        services.AddTransient<PlayerPage>();
        services.AddTransient<WorldChannelsPage>();
        services.AddTransient<MatchThreadPage>();
        services.AddTransient<CommunityHubPage>();
        services.AddTransient<PostDetailPage>();
        services.AddTransient<ComposePostPage>();
        services.AddTransient<NotificationsPage>();
        services.AddTransient<MorePage>();
    }
}
