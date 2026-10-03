using System.Globalization;

namespace MarkUptv.Converters;

/// <summary>
/// Turns a channel's EPG programme title into a friendly row
/// subtitle. Channels whose EPG has no real entry (or report the
/// generic "Program unavailable" placeholder) get an inviting live
/// copy supplied through ConverterParameter.
/// </summary>
public sealed class ChannelSubtitleConverter :
    IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        string? title = value as string;

        if (string.IsNullOrWhiteSpace(title) ||
            string.Equals(
                title,
                "Program unavailable",
                StringComparison.OrdinalIgnoreCase))
        {
            string fallback =
                parameter as string ??
                string.Empty;

            return string.IsNullOrWhiteSpace(fallback)
                ? "Live — 24/7"
                : fallback;
        }

        return title;
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
        => throw new NotSupportedException();
}
