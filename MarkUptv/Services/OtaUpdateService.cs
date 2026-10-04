using System.Net.Http.Json;
using System.Security.Cryptography;
#if ANDROID
using Android.App;
using Android.Content;
using Android.Content.PM;
#endif

namespace MarkUptv.Services;

public sealed class OtaUpdateService
{
    private const string ManifestUrl = "https://raw.githubusercontent.com/lubiramark50-hue/markuptv/main/MarkUptv/update/latest.json";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task CheckAndOfferAsync(CancellationToken cancellationToken = default)
    {
#if ANDROID
        try
        {
            var manifest = await Http.GetFromJsonAsync<OtaManifest>(ManifestUrl, cancellationToken).ConfigureAwait(false);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.ApkUrl) || manifest.VersionCode <= CurrentVersionCode()) return;
            var apk = await DownloadAndVerifyAsync(manifest, cancellationToken).ConfigureAwait(false);
            if (apk is null) return;
            await MainThread.InvokeOnMainThreadAsync(() => Install(apk));
        }
        catch { }
#endif
    }

#if ANDROID
    private static int CurrentVersionCode()
    {
#pragma warning disable CA1422
        var info = Application.Context.PackageManager?.GetPackageInfo(Application.Context.PackageName!, PackageInfoFlags.MetaData);
#pragma warning restore CA1422
        return (int)(info?.LongVersionCode ?? 0);
    }

    private static async Task<string?> DownloadAndVerifyAsync(OtaManifest manifest, CancellationToken ct)
    {
        var file = Path.Combine(FileSystem.CacheDirectory, "markuptv-update.apk");
        await using var input = await Http.GetStreamAsync(manifest.ApkUrl, ct);
        await using var output = File.Create(file);
        await input.CopyToAsync(output, ct);
        await output.FlushAsync(ct);
        if (!string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            await using var verify = File.OpenRead(file);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(verify, ct));
            if (!hash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase)) { File.Delete(file); return null; }
        }
        return file;
    }

    private static void Install(string apkPath)
    {
        var context = Application.Context;
        var installer = context.PackageManager!.PackageInstaller;
        var parameters = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
        parameters.SetAppPackageName(context.PackageName);
        var sessionId = installer.CreateSession(parameters);
        using var session = installer.OpenSession(sessionId);
        using (var input = File.OpenRead(apkPath))
        using (var output = session.OpenWrite("base.apk", 0, input.Length)) input.CopyTo(output);
        var intent = new Intent(context, typeof(OtaInstallReceiver));
        var pending = PendingIntent.GetBroadcast(context, sessionId, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        session.Commit(pending.IntentSender);
    }
#endif

    private sealed record OtaManifest(int VersionCode, string? VersionName, string ApkUrl, string? Sha256, bool Mandatory);
}

#if ANDROID
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class OtaInstallReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent) { }
}
#endif
