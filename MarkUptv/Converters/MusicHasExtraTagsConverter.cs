using System.Globalization;

namespace MarkUptv.Converters;

/// <summary>
/// Returns true when a group value contains at least one tag
/// other than the page's "Music" tag (used to reveal the extra
/// tag label on a row).
/// </summary>
public sealed class MusicHasExtraTagsConverter :
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
            return false;
        }

        return group
            .Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Any(tag =>
                !string.Equals(
                    tag,
                    "Music",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    tag,
                    "All",
                    StringComparison.OrdinalIgnoreCase));
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
        => throw new NotSupportedException();
}
