using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace MarkUptv.Converters
{
    public class IntToBoolConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int selectedIndex && parameter is string paramStr &&
                int.TryParse(paramStr, out int targetIndex))
            {
                return selectedIndex == targetIndex;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}