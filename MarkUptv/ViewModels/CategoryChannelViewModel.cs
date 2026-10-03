using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Helpers;
using MarkUptv.Models;
using MarkUptv.Services;
using Microsoft.Maui.Controls;

namespace MarkUptv.ViewModels;

public partial class CategoryChannelViewModel :
    BaseChannelViewModel,
    IQueryAttributable
{
    private string _category = "general";
    private string _pageTitle = "Live Channels";
    private string _accentColor = "#00D6C9";

    protected override string Category => _category;

    public string PageTitle
    {
        get => _pageTitle;

        private set
        {
            if (string.Equals(
                    _pageTitle,
                    value,
                    StringComparison.Ordinal))
            {
                return;
            }

            _pageTitle = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(NowPlayingText));
        }
    }

    public string AccentColor
    {
        get => _accentColor;

        private set
        {
            if (string.Equals(
                    _accentColor,
                    value,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _accentColor = value;
            OnPropertyChanged();
        }
    }

    public override string NowPlayingText =>
        SelectedChannel is not null
            ? SelectedChannel.Name
            : $"Select a {PageTitle.ToLowerInvariant()} stream";

    public CategoryChannelViewModel(
        TvApiService tvApi,
        RecentlyWatchedService recentlyWatched,
        PaymentService paymentService,
        ChannelCacheService cacheService)
        : base(
            tvApi,
            recentlyWatched,
            paymentService,
            cacheService)
    {
    }

    public void Configure(
        string? category,
        string? title,
        string? accent = null)
    {
        string cleanCategory =
            string.IsNullOrWhiteSpace(category)
                ? "general"
                : category.Trim()
                    .ToLowerInvariant();

        _category = cleanCategory;

        PageTitle =
            string.IsNullOrWhiteSpace(title)
                ? ToTitle(cleanCategory)
                : title.Trim();

        AccentColor =
            string.IsNullOrWhiteSpace(accent)
                ? PickAccent(cleanCategory)
                : accent.Trim();

        SearchText = string.Empty;
        SelectedGroup = "All";
        SelectedChannel = null;

        Channels.Clear();
        Groups.Clear();

        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(NowPlayingText));
    }

    public async Task EnsureLoadedAsync(
        CancellationToken cancellationToken = default)
    {
        // Enforcement guard: the adult category can only load when the
        // 18+ gate was passed in this app session, even if a deep link
        // or stale navigation tries to open it directly.
        if (_category == "adult" &&
            !AdultsAccess.IsGranted)
        {
            return;
        }

        // The 18+ shelf is a paid product: ask the server what this device is
        // entitled to before showing the grid, so a locked viewer sees the
        // offer instead of a silently empty list.
        if (_category == "adult")
        {
            await RefreshPassStateAsync();
        }

        if (Channels.Count > 0 || IsLoading)
        {
            return;
        }

        await LoadChannelsAsync(
            cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────
    // DAY PASS — the 18+ shelf
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>True when the server reports no active pass for this device.</summary>
    [ObservableProperty]
    private bool _passRequired;

    [ObservableProperty]
    private string _passPriceText = "1,000 UGX";

    [ObservableProperty]
    private string _passDurationText = "24 hours";

    [ObservableProperty]
    private string _passEmail = string.Empty;

    [ObservableProperty]
    private bool _isBuyingPass;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPassMessage))]
    private string _passMessage = string.Empty;

    public bool HasPassMessage => PassMessage.Length > 0;

    /// <summary>
    /// Mirrors the server's verdict. Enforcement lives in the API (the
    /// channels are withheld there); this only chooses between the offer card
    /// and the ordinary empty state.
    /// </summary>
    private async Task RefreshPassStateAsync()
    {
        try
        {
            DeviceStatusResponse? status =
                await PaymentService.GetFreshStatusAsync().ConfigureAwait(true);

            PassRequired = status is null || !status.PassActive;
            PassPriceText = status?.PassPriceText ?? PassPriceText;
            PassDurationText = status?.PassDurationText ?? PassDurationText;
        }
        catch (Exception)
        {
            // Unknown entitlement: show the offer rather than silently acting
            // as if the paid shelf were free.
            PassRequired = true;
        }
    }

    [RelayCommand]
    private async Task BuyPassAsync()
    {
        if (IsBuyingPass)
        {
            return;
        }

        PassMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(PassEmail) || !PassEmail.Contains('@'))
        {
            PassMessage = "Enter an email address for the receipt.";
            return;
        }

        IsBuyingPass = true;

        try
        {
            PaymentInitiationResult result = await PaymentService
                .InitiatePaymentAsync(new PaymentDetails
                {
                    Email = PassEmail.Trim(),
                    Product = PaymentDetails.DayPassProduct
                })
                .ConfigureAwait(true);

            if (!result.Success || string.IsNullOrWhiteSpace(result.RedirectUrl))
            {
                PassMessage = result.Error ?? "The payment gateway could not be reached.";
                return;
            }

            PaymentWebViewCallbackHolder.SuccessCallback = async () =>
            {
                PassMessage = "Payment received — unlocking…";
                await RefreshPassStateAsync().ConfigureAwait(true);

                if (!PassRequired)
                {
                    // The shelf was refused a moment ago; fetch it again.
                    LoadChannelsCommand.Execute(null);
                }
            };

            PaymentWebViewCallbackHolder.FailureCallback = () =>
            {
                PassMessage = "That payment did not complete. You have not been charged.";
                return Task.CompletedTask;
            };

            await Shell.Current.GoToAsync(
                $"PaymentWebViewPage?url={Uri.EscapeDataString(result.RedirectUrl)}");
        }
        catch (Exception)
        {
            PassMessage = "Checkout could not be started. Please try again.";
        }
        finally
        {
            IsBuyingPass = false;
        }
    }

    public void ApplyQueryAttributes(
        IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        string? category =
            ReadQueryValue(
                query,
                "category");

        string? title =
            ReadQueryValue(
                query,
                "title");

        string? accent =
            ReadQueryValue(
                query,
                "accent");

        Configure(
            category,
            title,
            accent);

        /*
         * IQueryAttributable is synchronous, so loading must be started
         * without awaiting. The helper catches failures to prevent an
         * unobserved task exception.
         */
        _ = EnsureLoadedSafelyAsync();
    }

    private async Task EnsureLoadedSafelyAsync()
    {
        try
        {
            await EnsureLoadedAsync(
                CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Normal navigation or application shutdown.
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[CategoryChannelViewModel] " +
                $"Channel loading failed: {exception}");
        }
    }

    private static string? ReadQueryValue(
        IDictionary<string, object> query,
        string key)
    {
        if (!query.TryGetValue(
                key,
                out object? value))
        {
            return null;
        }

        string encodedValue =
            value?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(encodedValue))
        {
            return null;
        }

        try
        {
            return Uri.UnescapeDataString(
                encodedValue);
        }
        catch (UriFormatException)
        {
            /*
             * Return the original value if the Shell query parameter
             * contains malformed percent encoding.
             */
            return encodedValue;
        }
    }

    private static string ToTitle(
        string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return "Live Channels";
        }

        string[] parts =
            category.Split(
                ['-', '_'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            return "Live Channels";
        }

        return string.Join(
            " ",
            parts.Select(part =>
                part.Length == 1
                    ? part.ToUpperInvariant()
                    : char.ToUpperInvariant(part[0]) +
                      part[1..].ToLowerInvariant()));
    }

    private static string PickAccent(
        string category)
    {
        return category switch
        {
            "sports" or
            "football" or
            "european-sports"
                => "#FF8A3D",

            "news" or
            "business" or
            "public" or
            "legislative"
                => "#3A86FF",

            "music" or
            "gospel" or
            "religious"
                => "#FFBE0B",

            "movies" or
            "series" or
            "entertainment"
                => "#FF3D6E",

            "science" or
            "education" or
            "documentary" or
            "discovery"
                => "#00D6C9",

            "wildlife" or
            "travel" or
            "weather"
                => "#6DFF8F",

            _ => "#00D6C9"
        };
    }
}