using System;
using System.Globalization;

namespace MarkUptv.Converters;

/// <summary>
/// Returns <c>true</c> when the bound value is a number greater than zero.
/// Used to show collection sections only when they hold at least one item.
/// </summary>
public class CountGreaterThanZeroConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue > 0;
        }

        if (value is long longValue)
        {
            return longValue > 0;
        }

        if (value is double doubleValue)
        {
            return doubleValue > 0;
        }

        return false;
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
        => throw new NotSupportedException(
            "CountGreaterThanZeroConverter is one-way only.");
}
