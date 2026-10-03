using System;
using System.Globalization;
using Microsoft.Maui.Controls;
using Markdig;

namespace MarkUptv.Converters;

public class MarkdownConverter : IValueConverter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string markdown || string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        // Convert Markdown to HTML
        string html = Markdown.ToHtml(markdown, Pipeline);
        
        // Wrap in styled div to ensure text color is white as per our app theme
        return $"<div style=\"color: white; font-family: sans-serif; font-size: 13px;\">{html}</div>";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
