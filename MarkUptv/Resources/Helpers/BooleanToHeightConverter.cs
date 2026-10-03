using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace MarkUptv.Resources.Helpers;

public class BooleanToHeightConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isExpanded && isExpanded)
            return 120;   // expanded height
        return 0;         // collapsed height
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BooleanToTextConverter : IValueConverter
{
    public string TrueStr { get; set; } = "True";
    public string FalseStr { get; set; } = "False";

    // FIX: Added '?' to arguments to correctly follow standard IValueConverter implementations
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool booleanValue)
        {
            return booleanValue ? TrueStr : FalseStr;
        }

        return FalseStr;
    }

    // FIX: Added '?' to arguments to match nullable reference checks
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}