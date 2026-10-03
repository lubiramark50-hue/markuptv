using System.Globalization;

namespace MarkUptv.Converters;

/// <summary>
/// Turns a channel's EPG programme title into a friendly row
/// subtitle. Stations without a real programme (the common case
/// for always-on music television) get an inviting live copy.
/// </summary>
public sealed class MusicProgrammeSubtitleConverter :
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
            return "24/7 music television — always on air";
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
