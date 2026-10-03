using System;
using System.Globalization;
using MarkUptv.Models;

namespace MarkUptv.Converters;

/// <summary>
/// Shows/hides an AI-chat element based on the message role.
/// Usage: IsVisible="{Binding Converter={StaticResource RoleToVisibleConverter},
///                              ConverterParameter=Assistant}"
/// </summary>
public class RoleToVisibleConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (value is not AiChatMessage message ||
            parameter is null)
        {
            return false;
        }

        string roleName = parameter.ToString() ?? string.Empty;

        return Enum.TryParse<AiChatMessageRole>(
            roleName,
            ignoreCase: true,
            out AiChatMessageRole expected)
            && message.Role == expected;
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
        => throw new NotSupportedException(
            "RoleToVisibleConverter is one-way only.");
}
