using System;
using System.Globalization;
using MarkUptv.Models;
using Microsoft.Maui.Graphics;

namespace MarkUptv.Converters;

/// <summary>
/// Returns the chat-bubble border color for a message based on its role.
/// </summary>
public class RoleToStrokeConverter : IValueConverter
{
    private static readonly Color AssistantStroke =
        Color.FromArgb("#38FFFFFF");

    private static readonly Color UserStroke =
        Color.FromArgb("#66FFFFFF");

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return value is AiChatMessage message
            && message.Role == AiChatMessageRole.User
                ? UserStroke
                : AssistantStroke;
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
        => throw new NotSupportedException(
            "RoleToStrokeConverter is one-way only.");
}
