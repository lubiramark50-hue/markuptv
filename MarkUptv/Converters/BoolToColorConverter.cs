using System;
using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MarkUptv.Converters
{
    /// <summary>
    /// Converts a boolean value to one of two Colors.
    /// Usage:
    ///   converters:BoolToColorConverter with TrueColor and FalseColor set in XAML.
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        /// <summary>
        /// The color to return when the bound value is <c>true</c>.
        /// </summary>
        public Color TrueColor { get; set; } = Colors.Transparent;

        /// <summary>
        /// The color to return when the bound value is <c>false</c>.
        /// </summary>
        public Color FalseColor { get; set; } = Colors.Transparent;

        /// <summary>
        /// Modifies the source data before passing it to the target for display in the UI.
        /// </summary>
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return boolValue ? TrueColor : FalseColor;

            return FalseColor;
        }

        /// <summary>
        /// Modifies the target data before passing it to the source object. This is not supported for this converter.
        /// </summary>
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException("Two-way binding is not supported by BoolToColorConverter.");
        }
    }
}
