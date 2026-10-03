using System.Globalization;

namespace MarkUptv.Helpers;

/// <summary>
/// Maps a boolean state onto one of two numbers, for example the height of the
/// player stage when it is idle versus when it is playing.
///
/// Bindable so a page can declare the sizes inline:
/// <c>HeightRequest="{Binding IsPlayerVisible, Converter={StaticResource BoolToNumber}, TrueValue=232, FalseValue=196}"</c>
/// </summary>
public sealed class BoolToNumberConverter : IValueConverter
{
    /// <summary>Value used when the binding is true.</summary>
    public double TrueValue { get; set; } = 1;

    /// <summary>Value used when the binding is false.</summary>
    public double FalseValue { get; set; }

    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        bool flag = value is bool boolValue && boolValue;

        return flag ? TrueValue : FalseValue;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
