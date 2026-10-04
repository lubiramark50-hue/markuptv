#pragma warning disable IL2026 
#pragma warning disable IL3050 

using MarkUptv.Services;
using MarkUptv.Pages;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MarkUptv;

public partial class App : Application
{
    private static readonly string LogFile = Path.Combine(FileSystem.AppDataDirectory, "startup_log.txt");
    private static readonly object _logLock = new object();
    private static readonly DateTime AppStartUtc = DateTime.UtcNow;

    private readonly PaymentService _paymentService;
    private readonly IServiceProvider _services;

    /// <summary>
    /// Completed once the Shell has been attached to the window. Startup boots
    /// with the lightweight LoadingPage and inflates the Shell afterwards; every
    /// navigation awaits this gate so nothing races the deferral.
    /// </summary>
    private readonly TaskCompletionSource _shellReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Raised when the login handshake has finished and the app is about to
    /// navigate — the point at which building the Shell is actually needed.
    /// </summary>
    private readonly TaskCompletionSource _shellRequested =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public App(
        PaymentService paymentService,
        IServiceProvider services)
    {
        _paymentService = paymentService;
        _services = services;

        RegisterGlobalExceptionHandling();

        try
        {
            WriteLog("App constructor started");
            InitializeComponent();
            Services.StartupTrace.Mark("App.InitializeComponent done");
        }
        catch (Exception ex)
        {
            WriteLog($"App constructor error: {ex}");
            throw;
        }
    }

    private void RegisterGlobalExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            WriteLog($"[AppDomain UnhandledException] IsTerminating={e.IsTerminating}: {ex}");
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            WriteLog($"[TaskScheduler UnobservedTaskException]: {e.Exception}");
            e.SetObserved(); 
        };
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        WriteLog("CreateWindow executed");

        // Boot with a static, 23-line page. Inflating the Shell here (flyout +
        // 26 ShellContents) costs ~4.6s of main-thread time on the emulator plus
        // its first layout, and that is what pushed the process past Android's
        // startup deadline. The Shell is attached as soon as this frame has
        // painted instead (AttachShellDeferred).
        var window = new Window(new LoadingPage());

        // Adaptivity starts with knowing the viewport. Measuring on the window
        // (not just the display) means desktop users who resize, and TVs that
        // report a different panel size, get a real re-layout.
        MarkUptv.Services.AdaptiveMetrics.Refresh();

        window.SizeChanged += (s, e) =>
        {
            MarkUptv.Services.AdaptiveMetrics.Refresh();
            MarkUptv.Helpers.AdaptiveLayoutHost.ApplyToCurrentPage();
        };

        // 🛡️ WAIT FOR WINDOWS NATIVE UI ENGINE TO ATTACH BEFORE FETCHING DATA
        window.Created += (s, e) =>
        {
            WriteLog("Native Window Created successfully. Running Lifecycle.");

            _ = Task.Run(async () =>
            {
                var newer = await _services.GetRequiredService<AppUpdateChecker>().GetNewVersionAsync();
                if (!string.IsNullOrWhiteSpace(newer)) WriteLog($"A newer MarkUptv build is available: {newer}");
            });

            try
            {
                double elapsedMs = (DateTime.UtcNow - ResolveProcessStartUtc()).TotalMilliseconds;
                WriteLog($"COLD START ready in {elapsedMs:F0} ms");
            }
            catch (Exception timingException)
            {
                WriteLog($"Cold start timing failed: {timingException.Message}");
            }

            // Diagnostics mode: exercise the real pipeline and exit with a status
            // code. Only runs when MARKUPTV_SELFCHECK=1, so normal launches are
            // unaffected.
            if (Services.AppSelfCheck.IsRequested)
            {
                // Diagnostics navigate headlessly, so the Shell has to exist
                // before the check starts; build it inline here.
                BuildAndAttachShell(window);

                Task.Run(async () =>
                {
                    int exitCode = Services.AppSelfCheck.IsPlayerCheckRequested
                        ? await Services.AppSelfCheck.RunPlayerAsync(_services)
                        : Services.AppSelfCheck.IsFixtureCheckRequested
                            ? await Services.AppSelfCheck.RunFixturesAsync(_services)
                            : Services.AppSelfCheck.IsSoakCheckRequested
                                ? await Services.AppSelfCheck.RunSoakAsync(_services)
                                : Services.AppSelfCheck.IsCommunityCheckRequested
                                    ? await Services.AppSelfCheck.RunCommunityAsync(_services)
                                    : Services.AppSelfCheck.IsNavigationSoakRequested
                                        ? await Services.AppSelfCheck.RunNavigationSoakAsync(_services)
                                        : Services.AppSelfCheck.IsAssistantCheckRequested
                                            ? await Services.AppSelfCheck.RunAssistantAsync(_services)
                                            : await Services.AppSelfCheck.RunAsync(_services);
                    WriteLog($"Self-check finished with exit code {exitCode}");
                    Environment.Exit(exitCode);
                });

                return;
            }

            AttachShellDeferred(window);

            Task.Run(async () => await RunAppInitializationLifecycleAsync());
        };

        return window;
    }

    /// <summary>
    /// Builds the Shell off the startup critical path and swaps it in as the
    /// window's page. Runs on a background thread for the delay, then on the UI
    /// thread for the swap (Shell handlers must be created there), and always
    /// completes <see cref="_shellReady"/> so a failure can never wedge
    /// navigation.
    /// </summary>
    private void AttachShellDeferred(Window window)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Android posts a focus event to the window the instant it is
                // created. Inflating the Shell inside that handshake is what
                // tripped "Waited 5014ms for FocusEvent" in the input
                // dispatcher, so wait for the login handshake to ask for the
                // Shell (or 6s, whichever lands first) and build it while the
                // main thread is otherwise idle.
                await Task.WhenAny(
                        _shellRequested.Task,
                        Task.Delay(TimeSpan.FromSeconds(6)))
                    .ConfigureAwait(false);

                await MainThread.InvokeOnMainThreadAsync(
                    () => BuildAndAttachShell(window))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                WriteLog($"Deferred shell attach crashed: {ex}");
                _shellReady.TrySetResult();
            }
        });
    }

    /// <summary>
    /// Resolves the Shell, swaps it in as the window's page and releases the
    /// navigation gate. Must run on the UI thread (Shell handlers are created
    /// there) and never throws.
    /// </summary>
    private bool BuildAndAttachShell(Window window)
    {
        try
        {
            Services.StartupTrace.Mark("AppShell resolve (deferred)");

            AppShell shell = _services.GetRequiredService<AppShell>();

            Services.StartupTrace.Mark("AppShell built (deferred)");

            window.Page = shell;

            Services.StartupTrace.Mark("Shell attached to window");

            return true;
        }
        catch (Exception ex)
        {
            WriteLog($"Deferred shell attach failed: {ex}");
            return false;
        }
        finally
        {
            _shellReady.TrySetResult();
        }
    }

    /// <summary>
    /// Best-effort process start time for cold-start measurement; falls back to
    /// the moment this type was first loaded.
    /// </summary>
    private static DateTime ResolveProcessStartUtc()
    {
        try
        {
            return System.Diagnostics.Process
                .GetCurrentProcess()
                .StartTime
                .ToUniversalTime();
        }
        catch
        {
            return AppStartUtc;
        }
    }

    protected override void OnStart()
    {
        base.OnStart();
        WriteLog("OnStart triggered.");
        // Intentionally empty. Logic moved to window.Created hook above to prevent 0xc0000005.
    }

    protected override void OnResume()
    {
        base.OnResume();
        WriteLog("OnResume lifecycle triggered. Re-verifying account status...");
        Task.Run(async () => await RunAppInitializationLifecycleAsync());
    }

    private async Task RunAppInitializationLifecycleAsync()
    {
        try
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                WriteLog("No internet connection detected.");
                return;
            }

            bool isRegistered = false;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                WriteLog($"Device registration attempt {attempt}...");
                isRegistered = await _paymentService.RegisterDeviceAsync();
                if (isRegistered) break;
                if (attempt < 3) await Task.Delay(50 * attempt);
            }

            if (!isRegistered)
            {
                await NavigateSafelyAsync("///PaymentRequiredPage");
                return;
            }

            var status = await _paymentService.GetStatusAsync();
            if (status == null || !status.CanWatch)
            {
                await NavigateSafelyAsync("///PaymentRequiredPage");
                return;
            }

            WriteLog("User status valid. Access granted.");
            await NavigateSafelyAsync("///MainPage");
        }
        catch (Exception ex)
        {
            WriteLog($"Lifecycle error: {ex.Message}");
            await NavigateSafelyAsync("///PaymentRequiredPage");
        }
    }

    protected override async void OnAppLinkRequestReceived(Uri uri)
    {
        base.OnAppLinkRequestReceived(uri);
        if (uri.Scheme == "markuptv" && uri.Host == "payment-success")
        {
            var status = await _paymentService.GetStatusAsync();
            if (status?.CanWatch == true)
                await NavigateSafelyAsync("///MainPage");
            else
                await RunAppInitializationLifecycleAsync();
        }
    }

// Android-only, and only in debug builds: the signature uses the platform
// Intent type, so it must not be compiled for Windows/iOS targets.
#if DEBUG && ANDROID
    /// <summary>
    /// UI-audit navigator: markuptv://goto/&lt;route&gt; jumps straight to any
    /// registered Shell route, so automated screenshot tooling can reach every
    /// page deterministically instead of driving the flyout by fragile taps.
    /// Called from MainActivity.OnNewIntent / cold-start OnCreate.
    /// </summary>
    public void HandleAndroidIntent(Android.Content.Intent? intent)
    {
        try
        {
            var data = intent?.Data;
            if (data is null || data.Scheme != "markuptv" || data.Host != "goto")
                return;

            var route = data.LastPathSegment;
            if (string.IsNullOrWhiteSpace(route))
                return;

            MainThread.BeginInvokeOnMainThread(async () =>
                await NavigateSafelyAsync($"///{route}"));
        }
        catch (Exception ex)
        {
            WriteLog($"goto intent failed: {ex.Message}");
        }
    }
#endif

    /// <summary>
    /// Asks the deferred attach to run and waits (bounded) for the Shell to
    /// become the window's page.
    /// </summary>
    private async Task WaitForShellAsync()
    {
        _shellRequested.TrySetResult();

        try
        {
            await _shellReady.Task
                .WaitAsync(TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            WriteLog("Waited 30s for the Shell; continuing anyway.");
        }
    }

    private async Task NavigateSafelyAsync(string absoluteRoute)
    {
        // Navigation needs the Shell; make sure the deferred attach ran.
        await WaitForShellAsync();

        if (Shell.Current is null)
        {
            WriteLog($"Navigate {absoluteRoute} skipped: Shell.Current is null");
            return;
        }

        // Verify we aren't already on this exact page to avoid native rendering crashes
        var currentState = Shell.Current.CurrentState?.Location?.OriginalString ?? string.Empty;
        if (currentState.Contains(absoluteRoute.Replace("///", ""))) return;

        // 🛡️ Give the WinUI pipeline a safe buffer to clear the LoadingPage
        await Task.Delay(150);

        try
        {
            if (MainThread.IsMainThread)
            {
                await Shell.Current.GoToAsync(absoluteRoute);
            }
            else
            {
                await MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(absoluteRoute));
            }

            WriteLog($"Navigate {absoluteRoute} done. Now at: {Shell.Current.CurrentState?.Location?.OriginalString}");
        }
        catch (Exception navEx)
        {
            // A stale or unknown route (e.g. an old deep link) must never take
            // the app down: the jump is best-effort, so log and stay put.
            WriteLog($"Navigate {absoluteRoute} FAILED: {navEx.Message}");
        }
    }

    private static void WriteLog(string message)
    {
        var log = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}{Environment.NewLine}";
        System.Diagnostics.Debug.WriteLine(log);
        lock (_logLock) { try { File.AppendAllText(LogFile, log); } catch { } }
    }
}
