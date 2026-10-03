// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

using System;
using System.IO;
using System.Threading.Tasks;

namespace MarkUptv.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : MauiWinUIApplication
    {
        private static readonly object CrashLogLock = new();
        private static readonly string CrashLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarkUptv",
            "logs",
            "winui-crash.log");

        /// <summary>
        /// Initializes the singleton application object. This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            RegisterCrashDiagnostics();

            try
            {
                WriteCrashLog("WinUI App constructor started.");
                this.InitializeComponent();
                WriteCrashLog("WinUI InitializeComponent completed.");
            }
            catch (Exception ex)
            {
                WriteCrashLog("WinUI InitializeComponent failed.", ex);
                throw;
            }
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        private void RegisterCrashDiagnostics()
        {
            UnhandledException += OnWinUIUnhandledException;

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                WriteCrashLog(
                    $"AppDomain unhandled exception. IsTerminating={e.IsTerminating}",
                    e.ExceptionObject as Exception);
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                WriteCrashLog("Unobserved task exception.", e.Exception);
                e.SetObserved();
            };
        }

        private static void OnWinUIUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            string senderType = sender?.GetType().FullName ?? "<null>";
            WriteCrashLog(
                $"WinUI unhandled exception. Sender={senderType}; Message={e.Message}; HandledBefore={e.Handled}",
                e.Exception);

            e.Handled = true;
        }

        private static void WriteCrashLog(string message, Exception? exception = null)
        {
            try
            {
                string? directory = Path.GetDirectoryName(CrashLogPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string logEntry =
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} - {message}{Environment.NewLine}" +
                    (exception is null ? string.Empty : $"{exception}{Environment.NewLine}");

                System.Diagnostics.Debug.WriteLine(logEntry);

                lock (CrashLogLock)
                {
                    File.AppendAllText(CrashLogPath, logEntry);
                }
            }
            catch
            {
                // Logging must never become the crash source.
            }
        }
    }
}
