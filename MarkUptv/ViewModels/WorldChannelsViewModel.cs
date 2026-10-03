using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Models;
using MarkUptv.Serialization;
using MarkUptv.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;

namespace MarkUptv.ViewModels;

/// <summary>
/// Free sports channels from around the world, grouped by country. Every entry
/// is an official free broadcaster or a publicly free service; entries that
/// cannot be embedded legally open on their official page instead.
/// </summary>
public partial class WorldChannelsViewModel : BaseViewModel
{
    private readonly LiveCatalogService _catalog;
    private readonly LiveResolutionService _resolver;
    private readonly LiveSessionService _session;
    private readonly ILogger<WorldChannelsViewModel> _logger;

    private List<LiveSource> _allSources = new();

    public WorldChannelsViewModel(
        LiveCatalogService catalog,
        LiveResolutionService resolver,
        LiveSessionService session,
        ILogger<WorldChannelsViewModel> logger)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ObservableCollection<WorldChannelGroup> Groups { get; } = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _summaryText = "Loading free channels…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoChannels))]
    private bool _hasChannels;

    public bool HasNoChannels => !HasChannels;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public override Task RefreshAsync(CancellationToken cancellationToken = default)
        => LoadAsync(cancellationToken);

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_allSources.Count == 0)
        {
            IsLoading = true;

            try
            {
                _allSources = (await _catalog.GetWorldChannelsAsync(cancellationToken)).ToList();
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "World channels could not be loaded");
                SetError("Free channel list could not be loaded.");
            }
            finally
            {
                IsLoading = false;
            }
        }

        ApplyFilter();
    }

    [RelayCommand]
    private async Task SelfTestAsync()
    {
        if (Shell.Current is null)
        {
            return;
        }

        try
        {
            LiveResolution resolution = await _resolver.BuildSelfTestAsync();

            _session.SetPending(new PendingLivePlayback
            {
                Title = "Playback self-test",
                League = "Diagnostics",
                Candidates = resolution.Candidates,
                IsSelfTest = true
            });

            await MainThread.InvokeOnMainThreadAsync(
                () => Shell.Current!.GoToAsync(nameof(Pages.PlayerPage)));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Playback self-test could not start");
            SetError("The playback self-test could not start.");
        }
    }

    /// <summary>Opens (or plays) one catalogue entry.</summary>
    public async Task OpenChannelAsync(LiveSource? source)
    {
        if (source is null || Shell.Current is null)
        {
            return;
        }

        try
        {
            if (source.IsDirectlyPlayable || source.IsEmbeddable)
            {
                LiveResolution resolution = await _resolver.ResolveChannelAsync(
                    source.Label,
                    source.Url,
                    source.Kind);

                _session.SetPending(new PendingLivePlayback
                {
                    Title = source.Label,
                    League = source.League,
                    Candidates = resolution.Candidates
                });

                await MainThread.InvokeOnMainThreadAsync(
                    () => Shell.Current!.GoToAsync(nameof(Pages.PlayerPage)));

                return;
            }

            string page = string.IsNullOrWhiteSpace(source.OfficialPage)
                ? source.Url ?? string.Empty
                : source.OfficialPage!;

            if (string.IsNullOrWhiteSpace(page))
            {
                return;
            }

            string webRoute =
                $"{nameof(Pages.WebViewPage)}?pageTitle={Uri.EscapeDataString(source.Label)}" +
                $"&url={Uri.EscapeDataString(page)}";

            await MainThread.InvokeOnMainThreadAsync(
                () => Shell.Current!.GoToAsync(webRoute));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Opening channel {Channel} failed", source.Channel);
            SetError("That channel could not be opened.");
        }
    }

    private void ApplyFilter()
    {
        string needle = SearchText?.Trim() ?? string.Empty;

        IEnumerable<LiveSource> filtered = _allSources;

        if (needle.Length > 0)
        {
            filtered = filtered.Where(source =>
                Contains(source.Label, needle) ||
                Contains(source.Channel, needle) ||
                Contains(source.Country, needle) ||
                Contains(source.Language, needle) ||
                Contains(source.League, needle));
        }

        List<WorldChannelGroup> groups = filtered
            .GroupBy(source => string.IsNullOrWhiteSpace(source.Country) ? "Other" : source.Country!)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new WorldChannelGroup(group.Key, group.OrderBy(source => source.Priority)))
            .ToList();

        Groups.Clear();

        foreach (WorldChannelGroup group in groups)
        {
            Groups.Add(group);
        }

        int total = filtered.Count();
        HasChannels = total > 0;

        SummaryText = total == 0
            ? "No free channels match that search."
            : $"{total} free source{(total == 1 ? string.Empty : "s")} from {groups.Count} " +
              $"{(groups.Count == 1 ? "country" : "countries")} · official and free to watch";
    }

    private static bool Contains(string? value, string needle)
        => !string.IsNullOrWhiteSpace(value) &&
           value!.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
