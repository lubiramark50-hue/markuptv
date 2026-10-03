using System;
using System.Globalization;
using MarkUptv.Models;

namespace MarkUptv.Converters;

/// <summary>
/// Aligns assistant chat bubbles to the start and user bubbles to the end.
/// </summary>
public class RoleToAlignmentConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return value is AiChatMessage message
            && message.Role == AiChatMessageRole.User
                ? LayoutOptions.End
                : LayoutOptions.Start;
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
        => throw new NotSupportedException(
            "RoleToAlignmentConverter is one-way only.");
}
