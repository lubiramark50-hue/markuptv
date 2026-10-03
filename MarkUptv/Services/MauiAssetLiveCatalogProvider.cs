using System.Text.Json;
using MarkUptv.Models;
using MarkUptv.Serialization;
using Microsoft.Maui.Storage;

namespace MarkUptv.Services;

/// <summary>
/// Reads <c>live-sources.json</c> from the app package using the trimming-safe
/// source-generated JSON context.
/// </summary>
public sealed class MauiAssetLiveCatalogProvider : ILiveCatalogProvider
{
    private const string CatalogAssetName = "live-sources.json";

    public async Task<LiveCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        using Stream stream = await FileSystem
            .OpenAppPackageFileAsync(CatalogAssetName)
            .ConfigureAwait(false);

        LiveCatalog? catalog = await JsonSerializer
            .DeserializeAsync(
                stream,
                MarkUptvJsonContext.Default.LiveCatalog,
                cancellationToken)
            .ConfigureAwait(false);

        return catalog ?? new LiveCatalog();
    }
}
