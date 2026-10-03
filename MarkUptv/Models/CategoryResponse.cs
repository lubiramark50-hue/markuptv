namespace MarkUptv.Models;

/// <summary>
/// Response returned when loading a TV category.
/// </summary>
public sealed class CategoryResponse
{
    public string Category { get; set; } = string.Empty;

    public List<string> Groups { get; set; } = [];

    public List<TvChannel> Channels { get; set; } = [];

    /// <summary>
    /// Set by the server when this category is a paid shelf and the device has
    /// no pass. The channel list is empty in that case, so the page renders the
    /// day-pass offer instead of an empty grid.
    /// </summary>
    public bool PassRequired { get; set; }

    public decimal PassPrice { get; set; }

    public string? PassCurrency { get; set; }

    public int PassHours { get; set; } = 24;

    /// <summary>Server-owned price text for the offer card.</summary>
    public string PassPriceText => PassPrice > 0
        ? $"{PassPrice:N0} {(!string.IsNullOrWhiteSpace(PassCurrency) ? PassCurrency : "UGX")}"
        : "1,000 UGX";
}