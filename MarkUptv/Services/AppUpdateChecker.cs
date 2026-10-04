using System.Net.Http.Json;

namespace MarkUptv.Services;

public sealed class AppUpdateChecker
{
    private static readonly Uri ManifestUri = new("https://raw.githubusercontent.com/lubiramark50-hue/markuptv/main/MarkUptv/update/version.json");
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    public async Task<string?> GetNewVersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var remote = await _http.GetFromJsonAsync<VersionManifest>(ManifestUri, cancellationToken);
            var local = AppInfo.Current.VersionString;
            if (remote?.Version is { Length: > 0 } && !string.Equals(remote.Version, local, StringComparison.OrdinalIgnoreCase))
                return remote.Version;
        }
        catch { }
        return null;
    }
    private sealed record VersionManifest(string Version, string UpdatedAt, string Channel);
}
