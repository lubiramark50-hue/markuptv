using System.Globalization;

namespace MarkUptv.Converters;

/// <summary>
/// Takes a semicolon-separated group value (for example
/// "Culture;Entertainment;Music") and returns the tags other
/// than the page's own "Music" tag, joined with a dot, so rows
/// only show genuinely distinguishing information.
/// </summary>
public sealed class MusicExtraTagsConverter :
    IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        string? group = value as string;

        if (string.IsNullOrWhiteSpace(group))
        {
            return string.Empty;
        }

        var tags = group
            .Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(tag =>
                !string.Equals(
                    tag,
                    "Music",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    tag,
                    "All",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return tags.Length == 0
            ? string.Empty
            : string.Join("  •  ", tags);
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
        => throw new NotSupportedException();
}
