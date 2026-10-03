using MarkUptv.Models;

namespace MarkUptv.Services;

/// <summary>
/// Supplies the free-source catalogue document. The app implementation reads it
/// from the packaged assets; tests can supply the same document from disk,
/// which keeps the resolution pipeline verifiable without a device.
/// </summary>
public interface ILiveCatalogProvider
{
    Task<LiveCatalog> LoadAsync(CancellationToken cancellationToken = default);
}
