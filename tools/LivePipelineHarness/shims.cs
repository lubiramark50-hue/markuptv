using System.Text.Json;
using Microsoft.Extensions.Logging;
using MarkUptv.Models;
using MarkUptv.Services;

namespace MarkUptv.LivePipelineHarness;

/// <summary>
/// Minimal console logger so the production services log verbatim during a run.
/// </summary>
public sealed class ConsoleLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        Console.WriteLine($"    [{logLevel}] {formatter(state, exception)}");
    }
}

/// <summary>
/// Supplies the real catalogue document from the repository, so the harness can
/// run the production resolution code without a device or an app package.
/// </summary>
public sealed class RepositoryCatalogProvider : ILiveCatalogProvider
{
    public string CatalogDirectory { get; set; } = string.Empty;

    public async Task<LiveCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(CatalogDirectory, "live-sources.json");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Catalogue not found at {path}");
        }

        await using FileStream stream = File.OpenRead(path);

        LiveCatalog? catalog = await JsonSerializer.DeserializeAsync<LiveCatalog>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        return catalog ?? new LiveCatalog();
    }
}
