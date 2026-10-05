using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace MarkUptv
{
    // MarkUpTV.Theme inherits Maui.SplashTheme and only overrides the text and
    // control colours; Android paints the Shell flyout rows with the theme's
    // text colours, which MAUI's stock attributes resolve too dark to read on
    // the navy flyout. See Platforms/Android/Resources/values/styles.xml.
    [Activity(
        Theme = "@style/MarkUpTV.Theme",
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    // Deep links (markuptv://payment-success / markuptv://payment-failed) used by
    // the payment flow. Declared here so the merged manifest only ever contains
    // REAL activity classes — hand-written <activity> entries in the manifest with
    // relative ".MainActivity" names expand to a phantom class that breaks launch.
    [IntentFilter(
        new[] { Intent.ActionMain },
        Categories = new[]
        {
            Intent.CategoryLauncher,
            "android.intent.category.LEANBACK_LAUNCHER"
        })]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[]
        {
            Intent.CategoryDefault,
            Intent.CategoryBrowsable
        },
        DataScheme = "markuptv",
        DataHost = "payment-success")]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[]
        {
            Intent.CategoryDefault,
            Intent.CategoryBrowsable
        },
        DataScheme = "markuptv",
        DataHost = "payment-failed")]
#if DEBUG
    // UI-audit deep link host (see App.HandleAndroidIntent).
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[]
        {
            Intent.CategoryDefault,
            Intent.CategoryBrowsable
        },
        DataScheme = "markuptv",
        DataHost = "goto")]
#endif
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Cold-start deep links: MAUI does not route custom-view intents for
            // us on every version, so deliver once the shell has booted.
#if DEBUG
            new Handler(Looper.MainLooper!).PostDelayed(
                () => (App.Current as App)?.HandleAndroidIntent(Intent), 6000);
#endif
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);

            // Warm delivery — the reliable path for markuptv://goto/<route>.
#if DEBUG
            (App.Current as App)?.HandleAndroidIntent(intent);
#endif
        }

        /// <summary>
        /// Closing the app used to kill the process with "FATAL EXCEPTION: main"
        /// (ObjectDisposedException: IServiceProvider) — i.e. MarkUpTV "stopped"
        /// every time it was finished or swiped away.
        ///
        /// Cause: MAUI's ShellFragmentContainer.OnDestroy asks the DI container
        /// for the dispatcher, but the MauiContext's service provider has already
        /// been disposed by the time this activity tears down. The exception is
        /// raised inside a native callback, so Android treats it as a crash.
        ///
        /// Nothing of ours is left to release at that point, so the framework's
        /// teardown ordering race is absorbed here instead of taking the process
        /// down with it.
        /// </summary>
        protected override void OnDestroy()
        {
            try
            {
                base.OnDestroy();
            }
            catch (ObjectDisposedException exception)
            {
                Android.Util.Log.Warn(
                    "MarkUpTV",
                    "Shell teardown ran after the service provider was disposed: " +
                    exception.Message);
            }
        }
    }
}
