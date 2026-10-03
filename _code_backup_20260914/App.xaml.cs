#pragma warning disable IL2026 
#pragma warning disable IL3050 

using MarkUptv.Services;
using MarkUptv.Pages;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using Microsoft.Maui.ApplicationModel;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MarkUptv;

public partial class App : Application
{
    private static readonly string LogFile = Path.Combine(FileSystem.AppDataDirectory, "startup_log.txt");
    private static readonly object _logLock = new object();

    private readonly PaymentService _paymentService;
    private readonly AppShell _appShell;

    public App(PaymentService paymentService, AppShell appShell)
    {
        _paymentService = paymentService;
        _appShell = appShell;

        try
        {
            WriteLog("App constructor started");
            InitializeComponent();
        }
        catch (Exception ex)
        {
            WriteLog($"App constructor error: {ex}");
            throw;
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        WriteLog("CreateWindow executed");
        var window = new Window(_appShell);

        // 🛡️ WAIT FOR WINDOWS NATIVE UI ENGINE TO ATTACH BEFORE FETCHING DATA
        window.Created += (s, e) =>
        {
            WriteLog("Native Window Created successfully. Running Lifecycle.");
            Task.Run(async () => await RunAppInitializationLifecycleAsync());
        };

        return window;
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

    private async Task NavigateSafelyAsync(string absoluteRoute)
    {
        if (Shell.Current is null) return;

        // Verify we aren't already on this exact page to avoid native rendering crashes
        var currentState = Shell.Current.CurrentState?.Location?.OriginalString ?? string.Empty;
        if (currentState.Contains(absoluteRoute.Replace("///", ""))) return;

        // 🛡️ Give the WinUI pipeline a safe buffer to clear the LoadingPage
        await Task.Delay(150);

        if (MainThread.IsMainThread)
        {
            await Shell.Current.GoToAsync(absoluteRoute);
        }
        else
        {
            await MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(absoluteRoute));
        }
    }

    private static void WriteLog(string message)
    {
        var log = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}{Environment.NewLine}";
        System.Diagnostics.Debug.WriteLine(log);
        lock (_logLock) { try { File.AppendAllText(LogFile, log); } catch { } }
    }
}