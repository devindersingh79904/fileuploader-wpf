using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FileUploader.ViewModels
{
    /// <summary>
    /// Returns Visible when value is NOT null/empty. If ConverterParameter == "Invert",
    /// it returns Visible when value IS null/empty. Works for string and collections.
    /// </summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isEmpty = value switch
            {
                null => true,
                string s => string.IsNullOrWhiteSpace(s),
                System.Collections.ICollection c => c.Count == 0,
                _ => false
            };

            bool invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);

            if (invert)
                return isEmpty ? Visibility.Visible : Visibility.Collapsed;

            return isEmpty ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
