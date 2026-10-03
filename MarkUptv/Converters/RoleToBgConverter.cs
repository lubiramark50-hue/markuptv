using System;
using System.Globalization;
using MarkUptv.Models;
using Microsoft.Maui.Graphics;

namespace MarkUptv.Converters;

/// <summary>
/// Returns the chat-bubble background for a message based on its role.
/// </summary>
public class RoleToBgConverter : IValueConverter
{
    private static readonly Color AssistantBubble =
        Color.FromArgb("#1B2348");

    private static readonly Color UserBubble =
        Color.FromArgb("#3B2A85");

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return value is AiChatMessage message
            && message.Role == AiChatMessageRole.User
                ? UserBubble
                : AssistantBubble;
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
        => throw new NotSupportedException(
            "RoleToBgConverter is one-way only.");
}
